using Terraria_Wiki.Application;

namespace Terraria_Wiki.Windows.Viewer;

internal enum ViewerConsumerState
{
    Stopped,
    Starting,
    Ready,
    Paused,
    Failed,
}

internal sealed record ViewerConsumerStatus(ViewerConsumerState State, Uri? BaseUri, string? Message, Exception? Error = null);

internal sealed class ViewerConsumerCoordinator : IWikiDataConsumerCoordinator, IAsyncDisposable
{
    private readonly string _staticRoot;
    private readonly IViewerDataSource _dataSource;
    private readonly Func<string> _theme;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Func<CancellationToken, Task<WikiBookSnapshot>>? _activeWiki;
    private ViewerServer? _server;
    private bool _enabled;
    private bool _alive = true;
    private int _pauseCount;

    public ViewerConsumerCoordinator(string staticRoot, IViewerDataSource dataSource, Func<string> theme)
    {
        _staticRoot = Path.GetFullPath(staticRoot ?? throw new ArgumentNullException(nameof(staticRoot)));
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public event EventHandler<ViewerConsumerStatus>? StatusChanged;
    public ViewerConsumerStatus Status { get; private set; } = new(ViewerConsumerState.Stopped, null, "Viewer is stopped.");
    public int GetRequestCount(string path) => _server?.GetRequestCount(path) ?? 0;

    public void SetActiveWikiProvider(Func<CancellationToken, Task<WikiBookSnapshot>> provider) =>
        _activeWiki = provider ?? throw new ArgumentNullException(nameof(provider));

    public async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _enabled = true;
            if (_pauseCount == 0)
                await StartUnderLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IAsyncDisposable> PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _pauseCount++;
            if (_pauseCount == 1)
            {
                await StopServerUnderLockAsync().ConfigureAwait(false);
                SetStatus(new ViewerConsumerStatus(ViewerConsumerState.Paused, null, "Viewer is paused for a Wiki data operation."));
            }
            return new PauseLease(this);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RetryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_enabled || _pauseCount != 0)
                return;
            await StopServerUnderLockAsync().ConfigureAwait(false);
            await StartUnderLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_alive)
                return;
            _alive = false;
            _enabled = false;
            _lifetime.Cancel();
            await StopServerUnderLockAsync().ConfigureAwait(false);
            SetStatus(new ViewerConsumerStatus(ViewerConsumerState.Stopped, null, "Viewer is stopped."));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private async ValueTask ReleasePauseAsync()
    {
        if (!_alive)
            return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_alive || _pauseCount == 0)
                return;
            _pauseCount--;
            if (_pauseCount == 0 && _enabled)
            {
                try
                {
                    await StartUnderLockAsync(_lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    SetStatus(new ViewerConsumerStatus(ViewerConsumerState.Failed, null, "Viewer could not resume.", exception));
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartUnderLockAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
            return;
        if (_activeWiki is null)
            throw new InvalidOperationException("The active Wiki provider has not been configured.");

        SetStatus(new ViewerConsumerStatus(ViewerConsumerState.Starting, null, "Starting local Viewer…"));
        try
        {
            var active = await _activeWiki(cancellationToken).ConfigureAwait(false);
            var server = new ViewerServer(new ViewerServerOptions
            {
                StaticRoot = _staticRoot,
                WikiDataFolder = active.DataFolder,
                DataSource = _dataSource,
                Theme = NormalizeTheme(_theme()),
                ErrorSink = exception => SetStatus(new ViewerConsumerStatus(ViewerConsumerState.Failed, BaseUri(), "Viewer request failed.", exception)),
            });
            var started = await server.StartAsync(_lifetime.Token).ConfigureAwait(false);
            _server = server;
            SetStatus(new ViewerConsumerStatus(ViewerConsumerState.Ready, started.BaseUri, "Viewer is ready."));
        }
        catch (Exception exception)
        {
            SetStatus(new ViewerConsumerStatus(ViewerConsumerState.Failed, null, "Viewer could not start.", exception));
            throw;
        }
    }

    private async Task StopServerUnderLockAsync()
    {
        var server = _server;
        _server = null;
        if (server is not null)
            await server.DisposeAsync().ConfigureAwait(false);
    }

    private Uri? BaseUri() => _server?.BaseUri;

    private static string NormalizeTheme(string value) => value is "dark" or "original" ? value : "light";

    private void SetStatus(ViewerConsumerStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ThrowIfDisposed()
    {
        if (!_alive)
            throw new ObjectDisposedException(nameof(ViewerConsumerCoordinator));
    }

    private sealed class PauseLease : IAsyncDisposable
    {
        private ViewerConsumerCoordinator? _owner;
        public PauseLease(ViewerConsumerCoordinator owner) => _owner = owner;

        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            return owner is null ? ValueTask.CompletedTask : owner.ReleasePauseAsync();
        }
    }
}
