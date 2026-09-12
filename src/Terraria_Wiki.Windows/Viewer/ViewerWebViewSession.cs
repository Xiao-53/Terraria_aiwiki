using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Terraria_Wiki.Windows.Viewer;

internal sealed class ViewerWebViewSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NativeWebView _webView;
    private readonly ViewerBridge _bridge;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ViewerEnvelope>> _pending = new(StringComparer.Ordinal);
    private readonly object _readyGate = new();
    private readonly object _identityGate = new();
    private TaskCompletionSource _ready = NewReadyCompletion();
    private Uri? _baseUri;
    private long _generation;
    private ViewerPageIdentity? _currentPage;
    private int _isReady;
    private int _receivedMessageCount;
    private int _validMessageCount;
    private string? _lastRequestMethod;
    private string? _lastCompletedMethod;
    private string? _lastBridgeErrorCode;
    private int _disposed;

    public ViewerWebViewSession(NativeWebView webView, ViewerBridge bridge)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _webView.WebMessageReceived += OnWebMessageReceived;
        _bridge.RequestCompleted += OnBridgeRequestCompleted;
        _bridge.PageCommitted += OnBridgePageCommitted;
        _bridge.HistoryChanged += OnBridgeHistoryChanged;
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<ViewerProtocolError>? ProtocolError;
    public event EventHandler<bool>? ReadyChanged;
    public event EventHandler<ViewerPageIdentity>? PageCommitted;
    public event EventHandler<bool>? HistoryChanged;
    public bool IsReady => Volatile.Read(ref _isReady) != 0;
    public int ReceivedMessageCount => Volatile.Read(ref _receivedMessageCount);
    public int ValidMessageCount => Volatile.Read(ref _validMessageCount);
    public string? LastRequestMethod => Volatile.Read(ref _lastRequestMethod);
    public string? LastCompletedMethod => Volatile.Read(ref _lastCompletedMethod);
    public string? LastBridgeErrorCode => Volatile.Read(ref _lastBridgeErrorCode);
    public ViewerPageIdentity? CurrentPage
    {
        get { lock (_identityGate) return _currentPage; }
    }
    public bool HasHistory => _bridge.HasHistory;

    public void Navigate(Uri baseUri, int wikiId, string wikiDisplayTitle, bool forceReload = false)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!forceReload && _baseUri == baseUri)
            return;
        _baseUri = baseUri;
        var generation = Interlocked.Increment(ref _generation);
        _bridge.BeginSession(wikiId, wikiDisplayTitle, generation);
        foreach (var pending in _pending.Values)
            pending.TrySetException(new ViewerProtocolException(
                "viewer-session-reset",
                "The Viewer session changed before the command completed."));
        _pending.Clear();
        lock (_identityGate)
            _currentPage = null;
        lock (_readyGate)
            _ready = NewReadyCompletion();
        Interlocked.Exchange(ref _isReady, 0);
        ReadyChanged?.Invoke(this, false);
        _webView.Source = baseUri;
    }

    public Task WaitUntilReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Task ready;
        lock (_readyGate)
            ready = _ready.Task;
        return ready.WaitAsync(timeout, cancellationToken);
    }

    public Task OpenPageAsync(string originalTitle, CancellationToken cancellationToken = default) =>
        SendCommandAsync("GotoPage", originalTitle, cancellationToken);

    public Task ChangeThemeAsync(bool isDark, CancellationToken cancellationToken = default) =>
        SendCommandAsync("ChangeTheme", isDark ? "True" : "False", cancellationToken);

    public async Task BackAsync(CancellationToken cancellationToken = default)
    {
        var command = _bridge.TryTakeBackCommand();
        if (command is null)
        {
            var error = new ViewerProtocolError("viewer-history-empty", "There is no previous Viewer page.");
            ProtocolError?.Invoke(this, error);
            throw new ViewerProtocolException(error.Code, error.Message);
        }
        try
        {
            await SendCommandAsync(command.Method, command.Data, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _bridge.RestoreBackCommand(command);
            throw;
        }
    }

    public async Task SendCommandAsync(
        string method,
        string data,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_baseUri is null || !IsReady)
            throw new InvalidOperationException("Viewer is not ready.");
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<ViewerEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new InvalidOperationException("Could not register a Viewer request.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(15));
        using var registration = linked.Token.Register(() => completion.TrySetCanceled(linked.Token));
        try
        {
            var request = JsonSerializer.Serialize(new { type = "req", id, method, data }, JsonOptions);
            await InvokeScriptOnUiThreadAsync($"window.terrariaWikiHost?.receive({request})").ConfigureAwait(false);
            var response = await completion.Task.ConfigureAwait(false);
            if (response.Error is not null)
                throw new ViewerProtocolException(response.Error.Code, response.Error.Message);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _webView.WebMessageReceived -= OnWebMessageReceived;
        _bridge.RequestCompleted -= OnBridgeRequestCompleted;
        _bridge.PageCommitted -= OnBridgePageCommitted;
        _bridge.HistoryChanged -= OnBridgeHistoryChanged;
        _lifetime.Cancel();
        lock (_readyGate)
            _ready.TrySetCanceled();
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();
        try
        {
            await InvokeScriptOnUiThreadAsync("window.stop()")
                .WaitAsync(TimeSpan.FromMilliseconds(500))
                .ConfigureAwait(false);
        }
        catch
        {
        }
        _lifetime.Dispose();
    }

    private void OnBridgeRequestCompleted(object? sender, ViewerBridgeRequestCompleted completed)
    {
        Volatile.Write(ref _lastCompletedMethod, completed.Method);
        Volatile.Write(ref _lastBridgeErrorCode, completed.Result.Error?.Code);
        if (completed.Method != "ViewerReady"
            || completed.Result.Error is not null
            || completed.Generation != Volatile.Read(ref _generation))
            return;
        if (Interlocked.Exchange(ref _isReady, 1) == 0)
        {
            lock (_readyGate)
                _ready.TrySetResult();
            ReadyChanged?.Invoke(this, true);
        }
    }

    private void OnBridgePageCommitted(object? sender, ViewerPageIdentity identity)
    {
        if (identity.Generation != Volatile.Read(ref _generation))
            return;
        lock (_identityGate)
            _currentPage = identity;
        PageCommitted?.Invoke(this, identity);
    }

    private void OnBridgeHistoryChanged(object? sender, bool hasHistory) =>
        HistoryChanged?.Invoke(this, hasHistory);

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs args)
    {
        Interlocked.Increment(ref _receivedMessageCount);
        _ = HandleWebMessageAsync(args.Body);
    }

    private async Task HandleWebMessageAsync(string? body)
    {
        var baseUri = _baseUri;
        if (baseUri is null || string.IsNullOrWhiteSpace(body))
            return;
        if (!ViewerProtocol.TryParseOuter(body, baseUri, out var envelope))
        {
            StatusChanged?.Invoke(this, "Rejected an invalid Viewer message.");
            return;
        }
        Interlocked.Increment(ref _validMessageCount);
        if (envelope!.Type == "req")
            Volatile.Write(ref _lastRequestMethod, envelope.Method);

        if (envelope.Type == "res")
        {
            if (_pending.TryGetValue(envelope.Id, out var pending))
                pending.TrySetResult(envelope);
            return;
        }

        var result = await _bridge.HandleOuterMessageAsync(body, baseUri, _lifetime.Token).ConfigureAwait(false);
        if (result is null)
            return;

        try
        {
            await InvokeScriptOnUiThreadAsync($"window.terrariaWikiHost?.receive({result.ResponseJson})").ConfigureAwait(false);
            if (result.Error is not null)
                ProtocolError?.Invoke(this, result.Error);
            if (result.Command is not null)
                await SendCommandAsync(result.Command.Method, result.Command.Data, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, $"Viewer bridge failed: {exception.Message}");
        }
    }

    private Task<string?> InvokeScriptOnUiThreadAsync(string script)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return _webView.InvokeScript(script);

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await _webView.InvokeScript(script));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private static TaskCompletionSource NewReadyCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ViewerProtocolException : IOException
{
    public ViewerProtocolException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
