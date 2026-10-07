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
    private readonly string _capabilityMessage;

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
        _statusMessage = UiTextCatalog.FromSafeMessage(service.Capabilities.StatusMessage);
        _capabilityMessage = _statusMessage;
        CheckCommand = new AsyncCommand(_ => CheckAsync(), _ => CanCheck);
        DownloadCommand = new AsyncCommand(_ => DownloadAsync(), _ => CanDownload);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => CanCancel);
        RestartCommand = new AsyncCommand(_ => RestartAsync(), _ => CanRestart);
        OpenReleasesCommand = new AsyncCommand(
            _ => OpenReleasesAsync(),
            _ => ReleasesUri is not null && State is not ProductUpdateState.Checking);
    }

    public string CurrentVersion => _service.Capabilities.CurrentVersion;
    public string CapabilityMessage => Text.Render(_capabilityMessage);
    public StatusNotification Notification { get; } = new();
    public string DistributionKind => Text.Render(_service.Capabilities.IsPortable ? "Portable" : UiTextCatalog.Message("Installed2"));
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
        get => Text.Render(_statusMessage);
        private set
        {
            SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value));
            Notification.Show(!string.IsNullOrWhiteSpace(value));
        }
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
            StatusMessage = UiTextCatalog.Message("CheckingGitHubReleases");
            AvailableVersion = null;
            var update = await _service.CheckAsync(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (update is null)
            {
                State = ProductUpdateState.UpToDate;
                StatusMessage = UiTextCatalog.Message("YouHaveTheLatestVersionOnTheStable");
            }
            else
            {
                AvailableVersion = update.Version;
                State = ProductUpdateState.Available;
                StatusMessage = UiTextCatalog.Message("VersionIsAvailableYouChooseWhenToRestart", update.Version);
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            State = ProductUpdateState.Cancelled;
            StatusMessage = UiTextCatalog.Message("UpdateCheckCancelledCurrentVersionIsUnaffected");
        }
        catch
        {
            State = ProductUpdateState.Failed;
            StatusMessage = UiTextCatalog.Message("CouldNotCheckGitHubReleasesOfflineFeaturesAnd");
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
            StatusMessage = UiTextCatalog.Message("DownloadingAndVerifyingTheFullUpdatePackage");
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
            StatusMessage = UiTextCatalog.Message("UpdateVerifiedItWillOnlyBeAppliedWhen");
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            State = ProductUpdateState.Cancelled;
            StatusMessage = UiTextCatalog.Message("DownloadCancelledCurrentVersionIsUnchanged");
        }
        catch
        {
            State = ProductUpdateState.Failed;
            StatusMessage = UiTextCatalog.Message("UpdateDownloadOrVerificationFailedCurrentVersionIs");
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
        Notification.SetBusy(true);
        try
        {
            StatusMessage = UiTextCatalog.Message("SafelyClosingLocalServicesAndApplyingTheUpdate");
            await _applyAndRestart();
        }
        catch
        {
            State = ProductUpdateState.Failed;
            StatusMessage = UiTextCatalog.Message("CouldNotStartTheUpdaterDownloadManuallyFrom");
        }
        finally
        {
            Notification.SetBusy(false);
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
            StatusMessage = UiTextCatalog.Message("CouldNotOpenTheBrowserVisitTheProject");
        }
    }

    private CancellationTokenSource BeginOperation()
    {
        var current = new CancellationTokenSource();
        _operation = current;
        Notification.SetBusy(true);
        RefreshCommands();
        return current;
    }

    private void EndOperation(CancellationTokenSource current)
    {
        if (!ReferenceEquals(current, _operation))
            return;
        _operation = null;
        current.Dispose();
        Notification.SetBusy(false);
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
        Notification.Dispose();
    }
}
