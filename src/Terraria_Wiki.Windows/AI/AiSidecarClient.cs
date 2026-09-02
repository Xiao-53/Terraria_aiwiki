using System.Text;

namespace Terraria_Wiki.Windows.AI;

internal enum AiRunEventKind
{
    RunStarted,
    ToolStarted,
    ToolFinished,
    AnswerDelta,
}

internal sealed record AiRunEvent(
    AiRunEventKind Kind,
    string? Name = null,
    string? Status = null,
    string? Delta = null);

internal sealed record AiSidecarShutdownOptions(
    TimeSpan GracefulTimeout,
    TimeSpan ForcedExitTimeout,
    TimeSpan ConnectionDisposeTimeout)
{
    public static AiSidecarShutdownOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(500));
}

internal sealed class AiSidecarClient : IAsyncDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);

    private readonly IAiSidecarConnection _connection;
    private readonly AiSidecarShutdownOptions _shutdownOptions;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private readonly HashSet<string> _controlRequestIds = new(StringComparer.Ordinal);
    private readonly Task _exitMonitor;
    private string? _activeRequestId;
    private string? _cancelSentFor;
    private TaskCompletionSource? _activeCompletion;
    private int _expectedExit;
    private int _shutdown;
    private int _faulted;
    private int _disposed;

    private AiSidecarClient(
        IAiSidecarConnection connection,
        AiSidecarShutdownOptions shutdownOptions)
    {
        _connection = connection;
        _shutdownOptions = shutdownOptions;
        _exitMonitor = MonitorExitAsync();
    }

    public event EventHandler<AiRuntimeException>? Faulted;
    public bool IsFaulted => Volatile.Read(ref _faulted) != 0;

    public static async Task<AiSidecarClient> StartAsync(
        AiRuntimeConfiguration configuration,
        Uri wikiToolEndpoint,
        string wikiToolBearerToken,
        IAiSidecarConnectionFactory? factory = null,
        CancellationToken cancellationToken = default,
        AiSidecarShutdownOptions? shutdownOptions = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(wikiToolEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(wikiToolBearerToken);
        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
            throw new AiRuntimeException("model_authentication");

        var launcher = Path.Combine(AppContext.BaseDirectory, "AgentRuntime", "sidecar_launcher.py");
        factory ??= new ProcessAiSidecarConnectionFactory();
        var connection = await factory.StartAsync(configuration, launcher, cancellationToken)
            .ConfigureAwait(false);
        var requestId = NewRequestId();
        try
        {
            await connection.WriteAsync(
                AiSidecarProtocol.CreateInitialize(
                    requestId,
                    configuration,
                    wikiToolEndpoint,
                    wikiToolBearerToken),
                cancellationToken).ConfigureAwait(false);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(HandshakeTimeout);
            var line = await connection.ReadLineAsync(handshake.Token).ConfigureAwait(false)
                ?? throw new AiRuntimeException("runtime_crashed");
            var ready = AiSidecarProtocol.Parse(line);
            if (ready.Kind == AiSidecarEventKind.Fatal && ready.RequestId == requestId)
                throw new AiRuntimeException(ready.Error?.Code ?? "unknown");
            ValidateReady(ready, requestId);
            return new AiSidecarClient(connection, shutdownOptions ?? AiSidecarShutdownOptions.Default);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            connection.KillProcessTree();
            await DisposeFailedConnectionAsync(connection).ConfigureAwait(false);
            throw new AiRuntimeException("timeout");
        }
        catch
        {
            connection.KillProcessTree();
            await DisposeFailedConnectionAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AiAgentAnswer> AnswerAsync(
        string question,
        Func<AiRunEvent, ValueTask>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > 32_768)
            throw new AiRuntimeException("invalid_input");
        ThrowIfUnavailable();

        var requestId = NewRequestId();
        TaskCompletionSource completion;
        lock (_sync)
        {
            if (_activeRequestId is not null)
                throw new AiRuntimeException("runtime_busy");
            _activeRequestId = requestId;
            _cancelSentFor = null;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeCompletion = completion;
        }

        var cancelledByCaller = 0;
        var callbackFailed = false;
        using var registration = cancellationToken.Register(() =>
        {
            Interlocked.Exchange(ref cancelledByCaller, 1);
            _ = CancelActiveAsync();
        });
        var markdown = new StringBuilder();
        var toolCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var runStarted = false;
        try
        {
            await _connection.WriteAsync(
                AiSidecarProtocol.CreateAnswer(requestId, question),
                _lifetime.Token).ConfigureAwait(false);
            while (true)
            {
                var line = await _connection.ReadLineAsync(_lifetime.Token).ConfigureAwait(false)
                    ?? throw new AiRuntimeException("runtime_crashed");
                var message = AiSidecarProtocol.Parse(line);
                if (message.RequestId != requestId)
                {
                    if (message.Kind == AiSidecarEventKind.Failed && RemoveControlId(message.RequestId))
                        continue;
                    throw new AiRuntimeException("protocol_violation");
                }

                if (message.Kind == AiSidecarEventKind.Fatal)
                {
                    var error = new AiRuntimeException(message.Error?.Code ?? "unknown");
                    MarkFaulted(error);
                    _connection.KillProcessTree();
                    throw error;
                }
                if (message.Kind == AiSidecarEventKind.Failed)
                    throw new AiRuntimeException(message.Error?.Code ?? "unknown");
                if (message.Kind == AiSidecarEventKind.Cancelled)
                {
                    if (callbackFailed)
                        throw new AiRuntimeException("unknown");
                    if (Volatile.Read(ref cancelledByCaller) != 0 || cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    throw new AiRuntimeException("cancelled");
                }

                switch (message.Kind)
                {
                    case AiSidecarEventKind.RunStarted:
                        if (runStarted)
                            throw new AiRuntimeException("protocol_violation");
                        runStarted = true;
                        callbackFailed |= !await InvokeEventAsync(
                            onEvent,
                            new AiRunEvent(AiRunEventKind.RunStarted)).ConfigureAwait(false);
                        break;
                    case AiSidecarEventKind.ToolStarted:
                        RequireToolPhase(runStarted, markdown);
                        var startedName = message.Name!;
                        toolCounts[startedName] = toolCounts.GetValueOrDefault(startedName) + 1;
                        callbackFailed |= !await InvokeEventAsync(
                            onEvent,
                            new AiRunEvent(AiRunEventKind.ToolStarted, Name: startedName)).ConfigureAwait(false);
                        break;
                    case AiSidecarEventKind.ToolFinished:
                        RequireToolPhase(runStarted, markdown);
                        var finishedName = message.Name!;
                        if (toolCounts.GetValueOrDefault(finishedName) <= 0)
                            throw new AiRuntimeException("protocol_violation");
                        toolCounts[finishedName]--;
                        callbackFailed |= !await InvokeEventAsync(
                            onEvent,
                            new AiRunEvent(
                                AiRunEventKind.ToolFinished,
                                finishedName,
                                message.Status)).ConfigureAwait(false);
                        break;
                    case AiSidecarEventKind.AnswerDelta:
                        RequireRunStarted(runStarted);
                        if (toolCounts.Values.Any(count => count != 0))
                            throw new AiRuntimeException("protocol_violation");
                        markdown.Append(message.Delta);
                        if (markdown.Length > 32_768)
                            throw new AiRuntimeException("protocol_violation");
                        callbackFailed |= !await InvokeEventAsync(
                            onEvent,
                            new AiRunEvent(AiRunEventKind.AnswerDelta, Delta: message.Delta))
                            .ConfigureAwait(false);
                        break;
                    case AiSidecarEventKind.Completed:
                        RequireRunStarted(runStarted);
                        if (toolCounts.Values.Any(count => count != 0)
                            || message.Answer is null
                            || message.Answer.Markdown != markdown.ToString())
                        {
                            throw new AiRuntimeException("protocol_violation");
                        }
                        if (_cancelSentFor == requestId)
                            await DrainRacedCancelAsync().ConfigureAwait(false);
                        if (callbackFailed)
                            throw new AiRuntimeException("unknown");
                        if (Volatile.Read(ref cancelledByCaller) != 0
                            || cancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(cancellationToken);
                        }
                        return message.Answer;
                    default:
                        throw new AiRuntimeException("protocol_violation");
                }

                if (callbackFailed)
                    await CancelActiveAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new AiRuntimeException("runtime_crashed");
        }
        catch (AiRuntimeException exception) when (exception.Code == "protocol_violation")
        {
            MarkFaulted(exception);
            _connection.KillProcessTree();
            throw;
        }
        finally
        {
            lock (_sync)
            {
                if (_activeRequestId == requestId)
                {
                    _activeRequestId = null;
                    _cancelSentFor = null;
                    _controlRequestIds.Clear();
                    _activeCompletion = null;
                }
            }
            completion.TrySetResult();
        }
    }

    public async Task CancelActiveAsync()
    {
        string? target;
        string cancelId;
        lock (_sync)
        {
            target = _activeRequestId;
            if (target is null || _cancelSentFor == target)
                return;
            _cancelSentFor = target;
            cancelId = NewRequestId();
            _controlRequestIds.Add(cancelId);
        }
        try
        {
            await _connection.WriteAsync(
                AiSidecarProtocol.CreateCancel(cancelId, target),
                _lifetime.Token).ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                _controlRequestIds.Remove(cancelId);
                if (_cancelSentFor == target)
                    _cancelSentFor = null;
            }
            throw;
        }
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdown, 1) != 0)
            return;
        Interlocked.Exchange(ref _expectedExit, 1);
        using var graceful = new CancellationTokenSource(_shutdownOptions.GracefulTimeout);
        try
        {
            Task? active;
            lock (_sync)
                active = _activeCompletion?.Task;
            if (active is not null)
            {
                try { await CancelActiveAsync().WaitAsync(graceful.Token).ConfigureAwait(false); } catch { }
                try { await active.WaitAsync(graceful.Token).ConfigureAwait(false); } catch { }
            }

            if (!_connection.HasExited && !graceful.IsCancellationRequested)
            {
                try
                {
                    await _connection.WriteAsync(
                            AiSidecarProtocol.CreateShutdown(NewRequestId()),
                            graceful.Token)
                        .AsTask()
                        .WaitAsync(graceful.Token)
                        .ConfigureAwait(false);
                    await _connection.WaitForExitAsync(graceful.Token)
                        .WaitAsync(graceful.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (!_connection.HasExited)
            {
                _connection.KillProcessTree();
                using var forced = new CancellationTokenSource(_shutdownOptions.ForcedExitTimeout);
                try
                {
                    await _connection.WaitForExitAsync(forced.Token)
                        .WaitAsync(forced.Token)
                        .ConfigureAwait(false);
                }
                catch { }
            }
        }
        finally
        {
            _lifetime.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await ShutdownAsync().ConfigureAwait(false);
        try
        {
            await _exitMonitor.WaitAsync(_shutdownOptions.ForcedExitTimeout).ConfigureAwait(false);
        }
        catch { }
        try
        {
            await _connection.DisposeAsync()
                .AsTask()
                .WaitAsync(_shutdownOptions.ConnectionDisposeTimeout)
                .ConfigureAwait(false);
        }
        catch { }
        _lifetime.Dispose();
    }

    private async Task MonitorExitAsync()
    {
        try
        {
            await _connection.WaitForExitAsync().ConfigureAwait(false);
            if (Volatile.Read(ref _expectedExit) == 0)
                MarkFaulted(new AiRuntimeException("runtime_crashed"));
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _expectedExit) == 0)
                MarkFaulted(new AiRuntimeException("runtime_crashed", exception));
        }
    }

    private void MarkFaulted(AiRuntimeException error)
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0)
            return;
        try { _lifetime.Cancel(); } catch { }
        try { Faulted?.Invoke(this, error); } catch { }
    }

    private void ThrowIfUnavailable()
    {
        if (Volatile.Read(ref _shutdown) != 0 || IsFaulted || _connection.HasExited)
            throw new AiRuntimeException("runtime_crashed");
    }

    private bool RemoveControlId(string requestId)
    {
        lock (_sync)
            return _controlRequestIds.Remove(requestId);
    }

    private async Task DrainRacedCancelAsync()
    {
        string? cancelId;
        lock (_sync)
            cancelId = _controlRequestIds.SingleOrDefault();
        if (cancelId is null)
            return;

        var line = await _connection.ReadLineAsync(_lifetime.Token).ConfigureAwait(false)
            ?? throw new AiRuntimeException("protocol_violation");
        var response = AiSidecarProtocol.Parse(line);
        if (response.RequestId != cancelId
            || response.Kind != AiSidecarEventKind.Failed
            || response.Error?.Code != "request_not_active"
            || !RemoveControlId(cancelId))
        {
            throw new AiRuntimeException("protocol_violation");
        }
    }

    private static async ValueTask<bool> InvokeEventAsync(
        Func<AiRunEvent, ValueTask>? callback,
        AiRunEvent value)
    {
        if (callback is null)
            return true;
        try
        {
            await callback(value).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void RequireRunStarted(bool runStarted)
    {
        if (!runStarted)
            throw new AiRuntimeException("protocol_violation");
    }

    private static void RequireToolPhase(bool runStarted, StringBuilder markdown)
    {
        if (!runStarted || markdown.Length != 0)
            throw new AiRuntimeException("protocol_violation");
    }

    private static void ValidateReady(AiSidecarEvent ready, string requestId)
    {
        if (ready.Kind != AiSidecarEventKind.Ready
            || ready.RequestId != requestId
            || ready.RuntimeVersion != "1"
            || ready.PydanticAiVersion != "2.31.0"
            || !System.Version.TryParse(ready.PythonVersion, out var python)
            || python.Major != 3
            || python.Minor is < 12 or > 14)
        {
            throw new AiRuntimeException("dependency_mismatch");
        }
    }

    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    private static async Task DisposeFailedConnectionAsync(IAiSidecarConnection connection)
    {
        try
        {
            await connection.DisposeAsync()
                .AsTask()
                .WaitAsync(AiSidecarShutdownOptions.Default.ConnectionDisposeTimeout)
                .ConfigureAwait(false);
        }
        catch { }
    }
}
