using Terraria_Wiki.Application;
using Terraria_Wiki.WikiTools;
using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.AI;

internal enum WindowsAiRuntimeState
{
    Disabled,
    Starting,
    Ready,
    Answering,
    Failed,
    Stopped,
}

internal sealed record WindowsAiRuntimeStatus(
    WindowsAiRuntimeState State,
    string Message,
    string? ErrorCode = null);

internal sealed class WindowsAiRuntime : IAsyncDisposable
{
    private readonly WikiToolServer _wikiTools;
    private readonly AiSidecarClient _sidecar;
    private int _disposed;

    private WindowsAiRuntime(WikiToolServer wikiTools, AiSidecarClient sidecar)
    {
        _wikiTools = wikiTools;
        _sidecar = sidecar;
        _sidecar.Faulted += OnSidecarFaulted;
        Status = new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Ready, "AI Runtime 已就绪。");
    }

    public event EventHandler<WindowsAiRuntimeStatus>? StatusChanged;
    public WindowsAiRuntimeStatus Status { get; private set; }

    public static async Task<WindowsAiRuntime> StartAsync(
        WikiQueryUseCases query,
        WikiToolConsumerCoordinator coordinator,
        AiRuntimeConfiguration configuration,
        IAiSidecarConnectionFactory? sidecarFactory = null,
        CancellationToken cancellationToken = default)
    {
        WikiToolServer? server = null;
        try
        {
            using (WindowsStartupInvestigation.Measure(StartupInvestigationPhase.WikiTools))
                server = await WikiToolServer.StartAsync(query, coordinator, cancellationToken)
                    .ConfigureAwait(false);
            var sidecar = await AiSidecarClient.StartAsync(
                configuration,
                server.Connection.Endpoint,
                server.Connection.BearerToken,
                sidecarFactory,
                cancellationToken).ConfigureAwait(false);
            return new WindowsAiRuntime(server, sidecar);
        }
        catch
        {
            if (server is not null)
            {
                try { await server.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            throw;
        }
    }

    public async Task<AiAgentAnswer> AnswerAsync(
        string question,
        IReadOnlyList<AiConversationHistoryTurn> history,
        Func<AiRunEvent, ValueTask>? onEvent = null,
        CancellationToken cancellationToken = default,
        string answerStyle = "detailed")
    {
        ThrowIfDisposed();
        if (Status.State is not WindowsAiRuntimeState.Ready)
            throw new AiRuntimeException(Status.State == WindowsAiRuntimeState.Answering
                ? "runtime_busy"
                : Status.ErrorCode ?? "runtime_crashed");
        SetStatus(new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Answering, "AI 正在查询本地 Wiki…"));
        try
        {
            var answer = await _sidecar.AnswerAsync(question, history, onEvent, cancellationToken, answerStyle)
                .ConfigureAwait(false);
            SetStatus(new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Ready, "AI Runtime 已就绪。"));
            return answer;
        }
        catch (OperationCanceledException)
        {
            if (!_sidecar.IsFaulted)
                SetStatus(new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Ready, "AI 回答已取消。"));
            throw;
        }
        catch (AiRuntimeException exception)
        {
            if (!_sidecar.IsFaulted)
                SetStatus(new WindowsAiRuntimeStatus(
                    WindowsAiRuntimeState.Ready,
                    exception.Message,
                    exception.Code));
            throw;
        }
    }

    public Task<AiAgentAnswer> AnswerAsync(
        string question,
        Func<AiRunEvent, ValueTask>? onEvent = null,
        CancellationToken cancellationToken = default) =>
        AnswerAsync(question, [], onEvent, cancellationToken);

    public Task CancelAsync() => _sidecar.CancelActiveAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _sidecar.Faulted -= OnSidecarFaulted;
        try
        {
            await _sidecar.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _wikiTools.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                SetStatus(new WindowsAiRuntimeStatus(WindowsAiRuntimeState.Stopped, "AI Runtime 已停止。"));
            }
        }
    }

    private void OnSidecarFaulted(object? sender, AiRuntimeException error) =>
        _ = HandleSidecarFailureAsync(error);

    private async Task HandleSidecarFailureAsync(AiRuntimeException error)
    {
        if (Volatile.Read(ref _disposed) != 0
            || Status.State is WindowsAiRuntimeState.Failed or WindowsAiRuntimeState.Stopped)
        {
            return;
        }
        try { await _wikiTools.StopAsync().ConfigureAwait(false); } catch { }
        if (Volatile.Read(ref _disposed) == 0)
            SetStatus(new WindowsAiRuntimeStatus(
                WindowsAiRuntimeState.Failed,
                "AI Runtime 已停止；离线搜索和阅读仍可使用。",
                error.Code));
    }

    private void SetStatus(WindowsAiRuntimeStatus status)
    {
        Status = status;
        try { StatusChanged?.Invoke(this, status); } catch { }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(WindowsAiRuntime));
    }
}
