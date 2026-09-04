using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Updates;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal enum ProductUpdateState
{
    Disabled,
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToRestart,
    Cancelled,
    Failed,
}

internal sealed class ProductUpdateViewModel : ObservableObject, IDisposable
{
    private readonly IProductUpdateService _service;
    private readonly Func<Task> _applyAndRestart;
    private readonly Func<Uri, Task> _openExternalUri;
    private CancellationTokenSource? _operation;
    private ProductUpdateState _state;
    private string _statusMessage;
    private string? _availableVersion;
    private int _downloadProgress;
    private bool _disposed;

    internal ProductUpdateViewModel(
        IProductUpdateService service,
        Func<Task> applyAndRestart,
        Func<Uri, Task> openExternalUri)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _applyAndRestart = applyAndRestart ?? throw new ArgumentNullException(nameof(applyAndRestart));
        _openExternalUri = openExternalUri ?? throw new ArgumentNullException(nameof(openExternalUri));
        _state = service.Capabilities.CanUpdate
            ? ProductUpdateState.Idle
            : ProductUpdateState.Disabled;
        _statusMessage = service.Capabilities.StatusMessage;
        CheckCommand = new AsyncCommand(_ => CheckAsync(), _ => CanCheck);
        DownloadCommand = new AsyncCommand(_ => DownloadAsync(), _ => CanDownload);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => CanCancel);
        RestartCommand = new AsyncCommand(_ => RestartAsync(), _ => CanRestart);
        OpenReleasesCommand = new AsyncCommand(
            _ => OpenReleasesAsync(),
            _ => ReleasesUri is not null && State is not ProductUpdateState.Checking);
    }

    public string CurrentVersion => _service.Capabilities.CurrentVersion;
    public string DistributionKind => _service.Capabilities.IsPortable ? "Portable" : "安装版";
    public Uri? ReleasesUri => _service.Capabilities.ReleasesUri;
    public ProductUpdateState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
                RefreshCommands();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? AvailableVersion
    {
        get => _availableVersion;
        private set
        {
            if (SetProperty(ref _availableVersion, value))
                OnPropertyChanged(nameof(AvailableVersionText));
        }
    }

    public string AvailableVersionText => AvailableVersion is null ? "—" : AvailableVersion;
    public int DownloadProgress
    {
        get => _downloadProgress;
        private set => SetProperty(ref _downloadProgress, Math.Clamp(value, 0, 100));
    }

    public bool IsProgressVisible => State == ProductUpdateState.Downloading;
    public bool CanCheck => _service.Capabilities.CanUpdate && State is not (
        ProductUpdateState.Checking or ProductUpdateState.Downloading or ProductUpdateState.ReadyToRestart);
    public bool CanDownload => State == ProductUpdateState.Available;
    public bool CanCancel => State is ProductUpdateState.Checking or ProductUpdateState.Downloading;
    public bool CanRestart => State == ProductUpdateState.ReadyToRestart;

    public AsyncCommand CheckCommand { get; }
    public AsyncCommand DownloadCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncCommand RestartCommand { get; }
    public AsyncCommand OpenReleasesCommand { get; }

    internal async Task CheckAsync()
    {
        if (!CanCheck)
            return;
        var current = BeginOperation();
        try
        {
            State = ProductUpdateState.Checking;
            StatusMessage = "正在检查 GitHub Releases…";
            AvailableVersion = null;
            var update = await _service.CheckAsync(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (update is null)
            {
                State = ProductUpdateState.UpToDate;
                StatusMessage = "当前已是 stable 渠道的最新版本。";
            }
            else
            {
                AvailableVersion = update.Version;
                State = ProductUpdateState.Available;
                StatusMessage = $"发现版本 {update.Version}；下载完成后由你确认重启。";
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            State = ProductUpdateState.Cancelled;
            StatusMessage = "检查更新已取消；当前版本不受影响。";
        }
        catch
        {
            State = ProductUpdateState.Failed;
            StatusMessage = "无法检查 GitHub Releases；离线功能和当前版本不受影响。";
        }
        finally
        {
            EndOperation(current);
        }
    }

    internal async Task DownloadAsync()
    {
        if (!CanDownload)
            return;
        var current = BeginOperation();
        try
        {
            State = ProductUpdateState.Downloading;
            StatusMessage = "正在下载并校验完整更新包…";
            DownloadProgress = 0;
            var progress = new Progress<int>(value =>
            {
                if (State == ProductUpdateState.Downloading)
                    DownloadProgress = Math.Max(DownloadProgress, value);
            });
            await _service.DownloadAsync(progress, current.Token);
            current.Token.ThrowIfCancellationRequested();
            DownloadProgress = 100;
            State = ProductUpdateState.ReadyToRestart;
            StatusMessage = "更新已校验，点击“重启并更新”后才会应用。";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            State = ProductUpdateState.Cancelled;
            StatusMessage = "下载已取消；当前版本未改变。";
        }
        catch
        {
            State = ProductUpdateState.Failed;
            StatusMessage = "更新下载或校验失败；当前版本未改变。";
        }
        finally
        {
            EndOperation(current);
        }
    }

    private async Task RestartAsync()
    {
        if (!CanRestart)
            return;
        try
        {
            StatusMessage = "正在安全关闭本地服务并应用更新…";
            await _applyAndRestart();
        }
        catch
        {
            State = ProductUpdateState.Failed;
            StatusMessage = "无法启动更新程序；请从 GitHub Releases 手动下载。";
        }
    }

    private async Task OpenReleasesAsync()
    {
        if (ReleasesUri is not { } uri)
            return;
        try
        {
            await _openExternalUri(uri);
        }
        catch
        {
            StatusMessage = "无法打开浏览器；请手动访问项目的 GitHub Releases 页面。";
        }
    }

    private CancellationTokenSource BeginOperation()
    {
        var current = new CancellationTokenSource();
        _operation = current;
        RefreshCommands();
        return current;
    }

    private void EndOperation(CancellationTokenSource current)
    {
        if (!ReferenceEquals(current, _operation))
            return;
        _operation = null;
        current.Dispose();
        RefreshCommands();
    }

    private void Cancel() => _operation?.Cancel();

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRestart));
        CheckCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        OpenReleasesCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = null;
    }
}
