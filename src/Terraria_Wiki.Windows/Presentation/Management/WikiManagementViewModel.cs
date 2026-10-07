using System.Collections.ObjectModel;
using System.Globalization;
using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.Presentation.Management;

internal enum ManagementConfirmation
{
    None,
    SwitchWiki,
    ClearFailures,
    DeleteResources,
    DeleteWikiData,
    ReplaceWithFullDownload,
    ReplaceWithLightDownload,
    ImportDatabase,
}

internal sealed class WikiDataChangedEventArgs(bool requiresViewerRebind) : EventArgs
{
    public bool RequiresViewerRebind { get; } = requiresViewerRebind;
}

internal sealed record class WikiManagementActions
{
    public bool SwitchIncludesViewerRebind { get; init; }
    public Func<CancellationToken, Task<IReadOnlyList<WikiBookSnapshot>>> ListWikis { get; init; } =
        _ => Unavailable<IReadOnlyList<WikiBookSnapshot>>();
    public Func<CancellationToken, Task<WikiBookSnapshot>> GetActiveWiki { get; init; } =
        _ => Unavailable<WikiBookSnapshot>();
    public Func<int, CancellationToken, Task<WikiBookSnapshot>> SwitchWiki { get; init; } =
        (_, _) => Unavailable<WikiBookSnapshot>();
    public Func<CancellationToken, Task<WikiBookSnapshot>> GetStatistics { get; init; } =
        _ => Unavailable<WikiBookSnapshot>();
    public Func<CancellationToken, Task<bool>> HasFailures { get; init; } =
        _ => Unavailable<bool>();
    public Func<bool, IProgress<WikiProgress>?, CancellationToken, Task<WikiOperationSummary>> Download { get; init; } =
        (_, _, _) => Unavailable<WikiOperationSummary>();
    public Func<IProgress<WikiProgress>?, CancellationToken, Task<WikiOperationSummary>> DownloadResources { get; init; } =
        (_, _) => Unavailable<WikiOperationSummary>();
    public Func<bool, WikiUpdateMode, IProgress<WikiProgress>?, CancellationToken, Task<WikiOperationSummary>> Update { get; init; } =
        (_, _, _, _) => Unavailable<WikiOperationSummary>();
    public Func<IProgress<WikiProgress>?, CancellationToken, Task<WikiOperationSummary>> RetryFailures { get; init; } =
        (_, _) => Unavailable<WikiOperationSummary>();
    public Func<CancellationToken, Task> ClearFailures { get; init; } =
        _ => Unavailable();
    public Func<CancellationToken, Task> Vacuum { get; init; } =
        _ => Unavailable();
    public Func<CancellationToken, Task<WikiOperationSummary>> DeleteResources { get; init; } =
        _ => Unavailable<WikiOperationSummary>();
    public Func<CancellationToken, Task<WikiOperationSummary>> DeleteWikiData { get; init; } =
        _ => Unavailable<WikiOperationSummary>();
    public Func<CancellationToken, Task<string?>> PickImportDatabase { get; init; } =
        _ => Task.FromResult<string?>(null);
    public Func<string, CancellationToken, Task<WikiDatabaseInspection>> InspectDatabase { get; init; } =
        (_, _) => Unavailable<WikiDatabaseInspection>();
    public Func<string, string, CancellationToken, Task<WikiDatabaseImportResponse>> ImportDatabase { get; init; } =
        (_, _, _) => Unavailable<WikiDatabaseImportResponse>();
    public Func<WikiBookSnapshot, CancellationToken, Task<WikiDatabaseExportResponse?>> ExportDatabase { get; init; } =
        (_, _) => Task.FromResult<WikiDatabaseExportResponse?>(null);

    public static WikiManagementActions From(
        WikiApplication application,
        Func<CancellationToken, Task<string?>> pickImportDatabase,
        Func<WikiBookSnapshot, CancellationToken, Task<WikiDatabaseExportResponse?>> exportDatabase,
        Func<int, CancellationToken, Task<WikiBookSnapshot>>? switchWiki = null,
        Func<string, CancellationToken, Task<bool>>? confirmFullScanAsync = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new WikiManagementActions
        {
            ListWikis = application.ActiveWiki.ListAsync,
            GetActiveWiki = application.ActiveWiki.GetActiveAsync,
            SwitchWiki = switchWiki ?? application.ActiveWiki.SwitchAsync,
            SwitchIncludesViewerRebind = switchWiki is not null,
            GetStatistics = application.Data.GetStatisticsAsync,
            HasFailures = application.Data.HasFailuresAsync,
            Download = application.Data.DownloadAsync,
            DownloadResources = application.Data.DownloadResourcesAsync,
            Update = (resources, mode, progress, token) => application.Data.UpdateAsync(resources, progress, token, confirmFullScanAsync, mode),
            RetryFailures = (progress, token) => application.Data.RetryFailuresAsync(progress, token, confirmFullScanAsync),
            ClearFailures = application.Data.ClearFailuresAsync,
            Vacuum = application.Data.VacuumAsync,
            DeleteResources = application.Data.DeleteResourcesAsync,
            DeleteWikiData = application.Data.DeleteWikiDataAsync,
            PickImportDatabase = pickImportDatabase,
            InspectDatabase = application.Data.InspectDatabaseAsync,
            ImportDatabase = application.Data.ImportDatabaseAsync,
            ExportDatabase = exportDatabase,
        };
    }

    private static Task<T> Unavailable<T>() =>
        Task.FromException<T>(new InvalidOperationException("Wiki management is not attached."));

    private static Task Unavailable() =>
        Task.FromException(new InvalidOperationException("Wiki management is not attached."));
}

internal sealed class WikiBookItemViewModel : ObservableObject
{
    private readonly Action<WikiBookSnapshot> _select;

    public WikiBookItemViewModel(WikiBookSnapshot wiki, bool isActive, Action<WikiBookSnapshot> select)
    {
        Wiki = wiki;
        IsActive = isActive;
        _select = select;
        SelectCommand = new RelayCommand(_ => _select(Wiki));
    }

    public WikiBookSnapshot Wiki { get; }
    public int Id => Wiki.Id;
    public string Title => Wiki.Title;
    public string Description => Wiki.Description;
    public bool IsActive { get; }
    public string InstallStatus => Text.Render(Wiki.IsPageDownloaded ? UiTextCatalog.Message("Installed") : UiTextCatalog.Message("NotDownloaded"));
    public RelayCommand SelectCommand { get; }
}

internal sealed class WikiManagementViewModel : ObservableObject, IDisposable
{
    protected override void OnTextCatalogChanged()
    {
        foreach (var item in Wikis) item.UseTextCatalog(Text);
    }
    private readonly Func<CancellationToken, Task<string?>> _pickImportDatabase;
    private readonly Func<WikiBookSnapshot, CancellationToken, Task<WikiDatabaseExportResponse?>> _exportDatabase;
    private readonly ProductDiagnostics _diagnostics;
    private WikiManagementActions? _actions;
    private CancellationTokenSource? _operationCancellation;
    private WikiBookSnapshot? _activeWiki;
    private ManagementConfirmation _confirmation;
    private WikiBookSnapshot? _pendingSwitch;
    private WikiDatabaseInspection? _pendingImport;
    private string _statusMessage = UiTextCatalog.Message("PreparingDataManagement");
    private bool _isAvailable;
    private bool _isBusy;
    private bool _hasFailures;
    private double _progressValue;
    private double _progressMaximum = 1;
    private bool _isProgressIndeterminate = true;
    private bool _disposed;

    public WikiManagementViewModel(
        Func<CancellationToken, Task<string?>>? pickImportDatabase = null,
        Func<WikiBookSnapshot, CancellationToken, Task<WikiDatabaseExportResponse?>>? exportDatabase = null,
        ProductDiagnostics? diagnostics = null)
    {
        _pickImportDatabase = pickImportDatabase ?? (_ => Task.FromResult<string?>(null));
        _exportDatabase = exportDatabase ?? ((_, _) => Task.FromResult<WikiDatabaseExportResponse?>(null));
        _diagnostics = diagnostics ?? new ProductDiagnostics(null);
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(showRefreshStatus: true), _ => CanOperate);
        DownloadAllCommand = new AsyncCommand(
            _ => RequestOrStartDownloadAsync(includeResources: true),
            _ => CanOperate && SupportsOnlineDownload && (!HasPageData || !HasResources));
        DownloadPagesCommand = new AsyncCommand(
            _ => RequestOrStartDownloadAsync(includeResources: false),
            _ => CanOperate && SupportsOnlineDownload && (!HasPageData || HasResources));
        FullUpdateCommand = new AsyncCommand(_ => UpdateAsync(WikiUpdateMode.Full), _ => CanOperate && SupportsOnlineDownload && HasPageData);
        QuickUpdateCommand = new AsyncCommand(_ => UpdateAsync(WikiUpdateMode.Quick), _ => CanOperate && SupportsOnlineDownload && HasPageData);
        RetryFailuresCommand = new AsyncCommand(_ => RetryFailuresAsync(), _ => CanOperate && SupportsOnlineDownload && HasFailures);
        RequestClearFailuresCommand = new RelayCommand(
            _ => RequestClearFailures(),
            _ => CanOperate && HasFailures);
        VacuumCommand = new AsyncCommand(_ => VacuumAsync(), _ => CanOperate && HasPageData);
        RequestDeleteResourcesCommand = new RelayCommand(
            _ => RequestDeleteResources(),
            _ => CanOperate && HasResources);
        RequestDeleteWikiDataCommand = new RelayCommand(
            _ => RequestDeleteWikiData(),
            _ => CanOperate && HasPageData);
        ImportCommand = new AsyncCommand(_ => BeginImportAsync(), _ => CanOperate);
        ExportCommand = new AsyncCommand(_ => ExportAsync(), _ => CanOperate && HasPageData);
        CancelOperationCommand = new RelayCommand(_ => CancelOperation(), _ => IsBusy);
        CancelConfirmationCommand = new RelayCommand(
            _ => CancelConfirmation(),
            _ => IsConfirmationVisible && !IsBusy);
        ConfirmCommand = new AsyncCommand(
            _ => ConfirmAsync(),
            _ => IsConfirmationVisible && !IsBusy && IsAvailable);
    }

    public event EventHandler<WikiDataChangedEventArgs>? DataChanged;

    public ObservableCollection<WikiBookItemViewModel> Wikis { get; } = [];

    public WikiBookSnapshot? ActiveWiki
    {
        get => _activeWiki;
        private set
        {
            if (!SetProperty(ref _activeWiki, value))
                return;
            OnPropertyChanged(nameof(HasActiveWiki));
            OnPropertyChanged(nameof(ActiveWikiTitle));
            OnPropertyChanged(nameof(ActiveWikiDescription));
            OnPropertyChanged(nameof(HasPageData));
            OnPropertyChanged(nameof(HasResources));
            OnPropertyChanged(nameof(NeedsPageData));
            OnPropertyChanged(nameof(NeedsResources));
            OnPropertyChanged(nameof(HasFullDownload));
            OnPropertyChanged(nameof(HasLightDownload));
            OnPropertyChanged(nameof(SupportsOnlineDownload));
            OnPropertyChanged(nameof(ShowOnlineDownloadActions));
            OnPropertyChanged(nameof(ShowFullDownloadAction));
            OnPropertyChanged(nameof(ShowLightDownloadAction));
            OnPropertyChanged(nameof(PageCountText));
            OnPropertyChanged(nameof(RedirectCountText));
            OnPropertyChanged(nameof(ResourceCountText));
            OnPropertyChanged(nameof(DataSizeText));
            OnPropertyChanged(nameof(UpdateTimeText));
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

    public StatusNotification Notification { get; } = new();

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(CanOperate));
                OnPropertyChanged(nameof(ShowOnlineDownloadActions));
                RefreshCommands();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;
            Notification.SetBusy(value);
            OnPropertyChanged(nameof(CanOperate));
            OnPropertyChanged(nameof(IsIdle));
            CancelOperationCommand.NotifyCanExecuteChanged();
            RefreshCommands();
        }
    }

    public bool HasFailures
    {
        get => _hasFailures;
        private set
        {
            if (SetProperty(ref _hasFailures, value))
                RefreshCommands();
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public double ProgressMaximum
    {
        get => _progressMaximum;
        private set => SetProperty(ref _progressMaximum, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public bool HasActiveWiki => ActiveWiki is not null;
    public bool HasPageData => ActiveWiki?.IsPageDownloaded == true;
    public bool HasResources => ActiveWiki?.IsResourceDownloaded == true;
    public bool NeedsPageData => HasActiveWiki && !HasPageData;
    public bool NeedsResources => HasPageData && !HasResources;
    public bool HasFullDownload => HasPageData && HasResources;
    public bool HasLightDownload => HasPageData && !HasResources;
    public bool SupportsOnlineDownload => ActiveWiki is { } wiki && HasDownloadEntry(wiki);
    public bool ShowOnlineDownloadActions => IsAvailable && SupportsOnlineDownload;
    public bool ShowFullDownloadAction => SupportsOnlineDownload && (NeedsPageData || HasLightDownload);
    public bool ShowLightDownloadAction => SupportsOnlineDownload && (NeedsPageData || HasFullDownload);
    public bool CanOperate => IsAvailable && !IsBusy && HasActiveWiki;
    public bool IsIdle => !IsBusy;
    public string ActiveWikiTitle => Text.Render(ActiveWiki?.Title ?? UiTextCatalog.Message("NoWikiSelected"));
    public string ActiveWikiDescription => Text.Render(ActiveWiki?.Description ?? UiTextCatalog.Message("TheLocalWikiDataSourceIsUnavailable"));
    public string PageCountText => (ActiveWiki?.PageCount ?? 0).ToString("N0", Text.Culture);
    public string RedirectCountText => (ActiveWiki?.RedirectCount ?? 0).ToString("N0", Text.Culture);
    public string ResourceCountText => (ActiveWiki?.ResourceCount ?? 0).ToString("N0", Text.Culture);
    public string DataSizeText => FormatBytes(ActiveWiki?.DataSize ?? 0);
    public string UpdateTimeText => Text.Render(ActiveWiki is null || ActiveWiki.UpdateTime == default
        ? UiTextCatalog.Message("NotUpdatedYet")
        : ActiveWiki.UpdateTime.ToString("yyyy-MM-dd HH:mm", Text.Culture));

    public bool IsConfirmationVisible => _confirmation != ManagementConfirmation.None;
    public string ConfirmationTitle => Text.Render(_confirmation switch
    {
        ManagementConfirmation.SwitchWiki => UiTextCatalog.Message("SwitchActiveWiki"),
        ManagementConfirmation.ClearFailures => UiTextCatalog.Message("ClearFailureRecords"),
        ManagementConfirmation.DeleteResources => UiTextCatalog.Message("DeleteAllResources"),
        ManagementConfirmation.DeleteWikiData => UiTextCatalog.Message("DeleteCurrentWikiData"),
        ManagementConfirmation.ReplaceWithFullDownload => UiTextCatalog.Message("SwitchToFullData"),
        ManagementConfirmation.ReplaceWithLightDownload => UiTextCatalog.Message("SwitchToLightweightData"),
        ManagementConfirmation.ImportDatabase => UiTextCatalog.Message("ImportThisDatabase"),
        _ => string.Empty,
    });
    public string ConfirmationMessage => Text.Render(_confirmation switch
    {
        ManagementConfirmation.SwitchWiki => UiTextCatalog.Message("SwitchTheActiveDataSourceToTheViewer", _pendingSwitch?.Title),
        ManagementConfirmation.ClearFailures => UiTextCatalog.Message("OnlyPageAndResourceDownloadFailureListsWill"),
        ManagementConfirmation.DeleteResources => UiTextCatalog.Message("AllLocalImagesAndAudioForWillBe", ActiveWikiTitle),
        ManagementConfirmation.DeleteWikiData => UiTextCatalog.Message("PagesResourcesHistoryAndFavoritesForWillBe", ActiveWikiTitle),
        ManagementConfirmation.ReplaceWithFullDownload => UiTextCatalog.Message("TheOldDatabaseForWillBeDeletedThen", ActiveWikiTitle),
        ManagementConfirmation.ReplaceWithLightDownload => UiTextCatalog.Message("TheOldDatabaseForWillBeDeletedThen2", ActiveWikiTitle),
        ManagementConfirmation.ImportDatabase => BuildImportConfirmation(),
        _ => string.Empty,
    });
    public string ConfirmationActionText => Text.Render(_confirmation switch
    {
        ManagementConfirmation.SwitchWiki => UiTextCatalog.Message("ConfirmSwitch"),
        ManagementConfirmation.ClearFailures => UiTextCatalog.Message("ConfirmClearing"),
        ManagementConfirmation.DeleteResources => UiTextCatalog.Message("DeleteResources"),
        ManagementConfirmation.DeleteWikiData => UiTextCatalog.Message("DeleteAllData"),
        ManagementConfirmation.ReplaceWithFullDownload => UiTextCatalog.Message("DeleteAndDownloadFullData"),
        ManagementConfirmation.ReplaceWithLightDownload => UiTextCatalog.Message("DeleteAndDownloadLightweightData"),
        ManagementConfirmation.ImportDatabase => UiTextCatalog.Message("ConfirmImport"),
        _ => UiTextCatalog.Message("Confirm"),
    });
    public bool IsDangerousConfirmation => _confirmation is
        ManagementConfirmation.ClearFailures or
        ManagementConfirmation.DeleteResources or
        ManagementConfirmation.DeleteWikiData or
        ManagementConfirmation.ReplaceWithFullDownload or
        ManagementConfirmation.ReplaceWithLightDownload or
        ManagementConfirmation.ImportDatabase;
    public bool IsStandardConfirmation => IsConfirmationVisible && !IsDangerousConfirmation;

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand DownloadAllCommand { get; }
    public AsyncCommand DownloadPagesCommand { get; }
    public AsyncCommand FullUpdateCommand { get; }
    public AsyncCommand QuickUpdateCommand { get; }
    public AsyncCommand RetryFailuresCommand { get; }
    public RelayCommand RequestClearFailuresCommand { get; }
    public AsyncCommand VacuumCommand { get; }
    public RelayCommand RequestDeleteResourcesCommand { get; }
    public RelayCommand RequestDeleteWikiDataCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand ExportCommand { get; }
    public RelayCommand CancelOperationCommand { get; }
    public RelayCommand CancelConfirmationCommand { get; }
    public AsyncCommand ConfirmCommand { get; }
    private string EmptyWikiStatus => ActiveWiki is null || SupportsOnlineDownload
        ? UiTextCatalog.Message("ThisWikiHasNoPagesYetChooseFull")
        : UiTextCatalog.Message("OnlineDownloadsAreCurrentlyUnavailableForThisWiki");

    internal void ShowEmptyWikiOnboarding() => StatusMessage = EmptyWikiStatus;

    public void Attach(
        WikiApplication application,
        Func<int, CancellationToken, Task<WikiBookSnapshot>>? switchWiki = null)
    {
        Attach(WikiManagementActions.From(application, _pickImportDatabase, _exportDatabase, switchWiki, ConfirmFullScanAsync));
    }

    internal Func<string, CancellationToken, Task<bool>>? FullScanApproval { get; set; }

    internal Task<bool> ConfirmFullScanAsync(string reason, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return FullScanApproval?.Invoke(reason, token) ?? Task.FromResult(false);
    }

    internal void Attach(WikiManagementActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        IsAvailable = true;
        StatusMessage = UiTextCatalog.Message("ViewAndMaintainLocalWikiData");
    }

    public void Detach()
    {
        CancelOperation();
        _actions = null;
        IsAvailable = false;
        IsBusy = false;
        HasFailures = false;
        ActiveWiki = null;
        Wikis.Clear();
        CancelConfirmation();
        StatusMessage = UiTextCatalog.Message("TheWikiDatabaseIsDisconnectedRestoreTheDefault");
    }

    internal async Task RefreshAsync(bool showRefreshStatus = false)
    {
        if (_actions is null || IsBusy)
            return;
        var current = BeginOperation(showRefreshStatus ? UiTextCatalog.Message("RefreshingWikiDataStatus") : string.Empty);
        if (current is null)
            return;
        try
        {
            await ReloadAsync(current.Token);
            StatusMessage = NeedsPageData
                ? EmptyWikiStatus
                : showRefreshStatus ? UiTextCatalog.Message("WikiDataStatusRefreshed") : string.Empty;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("RefreshCancelled");
        }
        catch
        {
            StatusMessage = UiTextCatalog.Message("CouldNotReadWikiDataStatusLocalData");
        }
        finally
        {
            EndOperation(current);
        }
    }

    internal void RequestSwitch(WikiBookSnapshot wiki)
    {
        ArgumentNullException.ThrowIfNull(wiki);
        if (!CanOperate)
            return;
        if (wiki.Id == ActiveWiki!.Id)
        {
            StatusMessage = UiTextCatalog.Message("IsAlreadyTheCurrentWiki", wiki.Title);
            return;
        }
        _pendingSwitch = wiki;
        SetConfirmation(ManagementConfirmation.SwitchWiki);
    }

    internal void RequestClearFailures()
    {
        if (CanOperate && HasFailures)
            SetConfirmation(ManagementConfirmation.ClearFailures);
    }

    internal void RequestDeleteResources()
    {
        if (CanOperate && HasResources)
            SetConfirmation(ManagementConfirmation.DeleteResources);
    }

    internal void RequestDeleteWikiData()
    {
        if (CanOperate && HasPageData)
            SetConfirmation(ManagementConfirmation.DeleteWikiData);
    }

    internal async Task ConfirmAsync()
    {
        var confirmation = _confirmation;
        if (confirmation == ManagementConfirmation.None || IsBusy || _actions is null)
            return;
        ClearConfirmationState(keepPendingData: true);
        switch (confirmation)
        {
            case ManagementConfirmation.SwitchWiki:
                await SwitchWikiAsync();
                break;
            case ManagementConfirmation.ClearFailures:
                await ClearFailuresAsync();
                break;
            case ManagementConfirmation.DeleteResources:
                await DeleteResourcesAsync();
                break;
            case ManagementConfirmation.DeleteWikiData:
                await DeleteWikiDataAsync();
                break;
            case ManagementConfirmation.ReplaceWithFullDownload:
                await ReplaceDownloadModeAsync(includeResources: true);
                break;
            case ManagementConfirmation.ReplaceWithLightDownload:
                await ReplaceDownloadModeAsync(includeResources: false);
                break;
            case ManagementConfirmation.ImportDatabase:
                await ImportDatabaseAsync();
                break;
        }
    }

    internal void CancelConfirmation()
    {
        ClearConfirmationState(keepPendingData: false);
        StatusMessage = IsAvailable ? UiTextCatalog.Message("OperationCancelledLocalDataIsUnchanged") : StatusMessage;
    }

    internal async Task DownloadAsync(bool includeResources)
    {
        var label = includeResources ? UiTextCatalog.Message("PageAndResourceDownload") : UiTextCatalog.Message("PageDownload");
        await RunSummaryOperationAsync(
            label,
            (progress, token) => _actions!.Download(includeResources, progress, token),
            dataChanged: true,
            requiresViewerRebind: true);
    }

    internal async Task RequestOrStartDownloadAsync(bool includeResources)
    {
        if (!CanOperate || !SupportsOnlineDownload)
            return;
        if (!HasPageData)
        {
            await DownloadAsync(includeResources);
            return;
        }
        SetConfirmation(includeResources
            ? ManagementConfirmation.ReplaceWithFullDownload
            : ManagementConfirmation.ReplaceWithLightDownload);
    }

    internal async Task UpdateAsync(WikiUpdateMode mode = WikiUpdateMode.Automatic) =>
        await RunSummaryOperationAsync(
            mode == WikiUpdateMode.Full ? UiTextCatalog.Message("FullWikiUpdate") : mode == WikiUpdateMode.Quick ? UiTextCatalog.Message("QuickWikiUpdate") :
                HasResources ? UiTextCatalog.Message("PageAndResourceUpdate") : UiTextCatalog.Message("PageUpdate"),
            (progress, token) => _actions!.Update(HasResources, mode, progress, token),
            dataChanged: true,
            requiresViewerRebind: true);

    internal async Task RetryFailuresAsync() =>
        await RunSummaryOperationAsync(
            UiTextCatalog.Message("RetryFailedItems"),
            (progress, token) => _actions!.RetryFailures(progress, token),
            dataChanged: true,
            requiresViewerRebind: true);

    internal async Task VacuumAsync() =>
        await RunSimpleOperationAsync(
            UiTextCatalog.Message("DatabaseMaintenance"),
            token => _actions!.Vacuum(token),
            UiTextCatalog.Message("DatabaseMaintenanceComplete"),
            dataChanged: true,
            requiresViewerRebind: true);

    internal async Task BeginImportAsync()
    {
        if (!CanOperate || _actions is null)
            return;
        var current = BeginOperation(UiTextCatalog.Message("SelectTheDataDbDatabaseFileToImport"));
        if (current is null)
            return;
        try
        {
            var path = await _actions.PickImportDatabase(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = UiTextCatalog.Message("NoDatabaseSelectedLocalDataIsUnchanged");
                return;
            }
            StatusMessage = UiTextCatalog.Message("CheckingDatabaseIntegrity");
            _pendingImport = await _actions.InspectDatabase(path, current.Token);
            current.Token.ThrowIfCancellationRequested();
            SetConfirmation(ManagementConfirmation.ImportDatabase);
            StatusMessage = UiTextCatalog.Message("DatabaseCheckPassedCurrentWikiDataWillOnly");
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("ImportCheckCancelledLocalDataIsUnchanged");
        }
        catch
        {
            _pendingImport = null;
            StatusMessage = UiTextCatalog.Message("CouldNotCheckThisDatabaseLocalDataIs");
        }
        finally
        {
            EndOperation(current);
        }
    }

    internal async Task ExportAsync()
    {
        if (!CanOperate || _actions is null || ActiveWiki is null)
            return;
        var current = BeginOperation(UiTextCatalog.Message("ChooseWhereToSaveTheDatabase"));
        if (current is null)
            return;
        try
        {
            var result = await _actions.ExportDatabase(ActiveWiki, current.Token);
            current.Token.ThrowIfCancellationRequested();
            StatusMessage = result is null
                ? UiTextCatalog.Message("NoSaveLocationSelectedNoDatabaseFileWas")
                : UiTextCatalog.Message("ExportComplete", Path.GetFileName(result.DatabasePath), FormatBytes(result.DatabaseBytes));
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("ExportCancelledIncompleteTemporaryFilesWereRemoved");
        }
        catch
        {
            StatusMessage = UiTextCatalog.Message("ExportFailedOriginalDataAndAnyExistingDestination");
        }
        finally
        {
            EndOperation(current);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
        Notification.Dispose();
    }

    private async Task SwitchWikiAsync()
    {
        var target = _pendingSwitch;
        _pendingSwitch = null;
        if (target is null)
            return;
        await RunSimpleOperationAsync(
            UiTextCatalog.Message("WikiSwitch"),
            async token => { await _actions!.SwitchWiki(target.Id, token); },
            UiTextCatalog.Message("SwitchedTo", target.Title),
            dataChanged: true,
            requiresViewerRebind: !_actions!.SwitchIncludesViewerRebind);
    }

    private async Task ClearFailuresAsync() =>
        await RunSimpleOperationAsync(
            UiTextCatalog.Message("ClearFailureRecords2"),
            token => _actions!.ClearFailures(token),
            UiTextCatalog.Message("DownloadFailureRecordsClearedIncompleteSyncTasksWill"),
            dataChanged: true,
            requiresViewerRebind: false);

    private async Task DeleteResourcesAsync() =>
        await RunSummaryOperationAsync(
            UiTextCatalog.Message("ResourceDeletion"),
            (_, token) => _actions!.DeleteResources(token),
            dataChanged: true,
            requiresViewerRebind: true);

    private async Task DeleteWikiDataAsync() =>
        await RunSummaryOperationAsync(
            UiTextCatalog.Message("WikiDataDeletion"),
            (_, token) => _actions!.DeleteWikiData(token),
            dataChanged: true,
            requiresViewerRebind: true);

    private async Task ReplaceDownloadModeAsync(bool includeResources)
    {
        if (!CanOperate || _actions is null)
            return;
        var label = includeResources ? UiTextCatalog.Message("SwitchToFullData2") : UiTextCatalog.Message("SwitchToLightweightData2");
        var current = BeginOperation(UiTextCatalog.Message("DeletingTheOldDatabaseAndPreparing", (includeResources ? UiTextCatalog.Message("Full") : UiTextCatalog.Message("Lightweight"))));
        if (current is null)
            return;
        var operationId = Guid.NewGuid();
        var wikiId = ActiveWiki?.Id;
        _diagnostics.WriteWikiOperation(operationId, wikiId, "started", result: label);
        var progress = CreateOperationProgress(label, current, operationId, wikiId);
        var dataMayHaveChanged = false;
        try
        {
            dataMayHaveChanged = true;
            await _actions.DeleteWikiData(current.Token);
            current.Token.ThrowIfCancellationRequested();
            StatusMessage = includeResources ? UiTextCatalog.Message("OldDatabaseDeletedDownloadingFullData") : UiTextCatalog.Message("OldDatabaseDeletedDownloadingLightweightData");
            var summary = await _actions.Download(includeResources, progress, current.Token);
            current.Token.ThrowIfCancellationRequested();
            await ReloadAsync(current.Token);
            DataChanged?.Invoke(this, new WikiDataChangedEventArgs(requiresViewerRebind: true));
            StatusMessage = FormatSummary(label, summary);
            _diagnostics.WriteWikiOperation(operationId, wikiId, "completed", result: StatusMessage);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            await RefreshAfterReplacementFailureAsync(dataMayHaveChanged);
            StatusMessage = UiTextCatalog.Message("CancelledTheOldDatabaseMayHaveBeenDeleted", label);
            _diagnostics.WriteWikiOperation(operationId, wikiId, "cancelled", new WikiProgress(progress.Stage));
        }
        catch (Exception exception)
        {
            _diagnostics.WriteWikiOperation(operationId, wikiId, "failed", new WikiProgress(progress.Stage), exception);
            await RefreshAfterReplacementFailureAsync(dataMayHaveChanged);
            StatusMessage = UiTextCatalog.Message("FailedTheOldDatabaseMayHaveBeenDeleted", label, FailureDescription(exception));
        }
        finally
        {
            EndOperation(current);
        }
    }

    private async Task RefreshAfterReplacementFailureAsync(bool dataMayHaveChanged)
    {
        if (!dataMayHaveChanged)
            return;
        try { await ReloadAsync(CancellationToken.None); }
        catch { }
        DataChanged?.Invoke(this, new WikiDataChangedEventArgs(requiresViewerRebind: true));
    }

    private async Task ImportDatabaseAsync()
    {
        var inspection = _pendingImport;
        _pendingImport = null;
        if (inspection is null)
            return;
        await RunOperationAsync(
            UiTextCatalog.Message("DatabaseImport"),
            async (_, token) =>
            {
                var result = await _actions!.ImportDatabase(
                    inspection.DatabasePath,
                    inspection.InspectionToken,
                    token);
                return (UiTextCatalog.Message("DatabaseForImported", result.WikiBook.Title), true);
            },
            dataChanged: true,
            requiresViewerRebind: true,
            rebindPredicateFromResult: true);
    }

    private async Task RunSummaryOperationAsync(
        string label,
        Func<IProgress<WikiProgress>, CancellationToken, Task<WikiOperationSummary>> action,
        bool dataChanged,
        bool requiresViewerRebind = false)
    {
        await RunOperationAsync(
            label,
            async (progress, token) =>
            {
                var summary = await action(progress, token);
                return (FormatSummary(label, summary), true);
            },
            dataChanged,
            requiresViewerRebind);
    }

    private async Task RunSimpleOperationAsync(
        string label,
        Func<CancellationToken, Task> action,
        string successMessage,
        bool dataChanged,
        bool requiresViewerRebind = false)
    {
        await RunOperationAsync(
            label,
            async (_, token) =>
            {
                await action(token);
                return (successMessage, true);
            },
            dataChanged,
            requiresViewerRebind);
    }

    private async Task RunOperationAsync(
        string label,
        Func<IProgress<WikiProgress>, CancellationToken, Task<(string Message, bool Rebind)>> action,
        bool dataChanged,
        bool requiresViewerRebind,
        bool rebindPredicateFromResult = false)
    {
        if (!CanOperate || _actions is null)
            return;
        var current = BeginOperation(UiTextCatalog.Message("Running2", label));
        if (current is null)
            return;
        var operationId = Guid.NewGuid();
        var wikiId = ActiveWiki?.Id;
        _diagnostics.WriteWikiOperation(operationId, wikiId, "started", result: label);
        var progress = CreateOperationProgress(label, current, operationId, wikiId);
        try
        {
            var result = await action(progress, current.Token);
            current.Token.ThrowIfCancellationRequested();
            var refreshFailed = false;
            if (dataChanged)
            {
                try { await ReloadAsync(current.Token); }
                catch { refreshFailed = true; }
                DataChanged?.Invoke(
                    this,
                    new WikiDataChangedEventArgs(
                        requiresViewerRebind && (!rebindPredicateFromResult || result.Rebind)));
            }
            StatusMessage = refreshFailed
                ? UiTextCatalog.Join("", result.Message, UiTextCatalog.Message("DataStatusHasNotRefreshedYetTryAgain"))
                : result.Message;
            _diagnostics.WriteWikiOperation(operationId, wikiId, "completed", result: StatusMessage);
        }
        catch (WikiOperationException exception) when (exception.Code == "full-scan-declined")
        {
            StatusMessage = UiTextCatalog.Message("FullScanDeclined");
            _diagnostics.WriteWikiOperation(operationId, wikiId, "cancelled", new WikiProgress(progress.Stage));
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("CancelledCompletedSafeStepsArePreserved", label);
            _diagnostics.WriteWikiOperation(operationId, wikiId, "cancelled", new WikiProgress(progress.Stage));
            if (dataChanged) await RefreshAfterFailureAsync();
        }
        catch (Exception exception)
        {
            _diagnostics.WriteWikiOperation(operationId, wikiId, "failed", new WikiProgress(progress.Stage), exception);
            StatusMessage = UiTextCatalog.Message("FailedDetailsWereRecordedInWikiSyncLog", label, FailureDescription(exception));
            if (dataChanged) await RefreshAfterFailureAsync();
        }
        finally
        {
            EndOperation(current);
        }
    }

    private OperationProgress CreateOperationProgress(string label, CancellationTokenSource current, Guid operationId, int? wikiId)
    {
        var lastStage = "";
        var lastLog = DateTimeOffset.MinValue;
        var gate = new object();
        IProgress<WikiProgress> ui = new Progress<WikiProgress>(value =>
        {
            if (ReferenceEquals(current, _operationCancellation)) ApplyProgress(label, value);
        });
        return new OperationProgress(value =>
        {
            lock (gate)
            {
                var routine = value.Code is "page-download" or "page-skipped" or "resource-downloaded" or "resource-skipped";
                var stage = routine ? (value.Code.StartsWith("page", StringComparison.Ordinal) ? "pages" : "media") : value.Code;
                if (stage != lastStage || !routine || (DateTimeOffset.UtcNow - lastLog).TotalSeconds >= 10)
                {
                    _diagnostics.WriteWikiOperation(operationId, wikiId, "progress", value);
                    lastLog = DateTimeOffset.UtcNow;
                }
                lastStage = stage;
            }
            ui.Report(value);
        });
    }

    private sealed class OperationProgress(Action<WikiProgress> report) : IProgress<WikiProgress>
    {
        public string Stage { get; private set; } = "";
        public void Report(WikiProgress value) { Stage = value.Code; report(value); }
    }

    private async Task RefreshAfterFailureAsync()
    {
        try { await ReloadAsync(CancellationToken.None); }
        catch { }
        DataChanged?.Invoke(this, new WikiDataChangedEventArgs(requiresViewerRebind: true));
    }

    private static string FailureDescription(Exception exception) => exception switch
    {
        Terraria_Wiki.Core.Net.WikiHttpBrowserVerificationException => UiTextCatalog.Message("TheWikiSiteRequiresBrowserVerificationDownloadsAre"),
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } => UiTextCatalog.Message("TheWikiSiteDeniedAccessHTTPCheckThe"),
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } => UiTextCatalog.Message("TheWikiSiteRequiresAuthorizationHTTP"),
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => UiTextCatalog.Message("TheWikiServerIsRateLimitingRequestsTry"),
        HttpRequestException { StatusCode: { } status } => UiTextCatalog.Message("WikiRequestReturnedHTTP", (int)status),
        WikiOperationException { Code: "quick-update-requires-full" } => UiTextCatalog.Message("QuickUpdateRequiresFull"),
        HttpRequestException => UiTextCatalog.Message("WikiNetworkConnectionFailed"),
        OperationCanceledException or TimeoutException => UiTextCatalog.Message("WikiRequestTimedOut"),
        IOException => UiTextCatalog.Message("FileOrDiskOperationFailed"),
        _ => UiTextCatalog.Message("AnErrorOccurredDuringSynchronization")
    };

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var actions = _actions ?? throw new InvalidOperationException("Wiki management is not attached.");
        var wikis = await actions.ListWikis(cancellationToken);
        var active = await actions.GetActiveWiki(cancellationToken);
        active = await actions.GetStatistics(cancellationToken);
        var failures = await actions.HasFailures(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ActiveWiki = active;
        HasFailures = failures;
        Wikis.Clear();
        foreach (var wiki in wikis)
        {
            var current = wiki.Id == active.Id ? active : wiki;
            // Keep existing/active offline data manageable, but hide unavailable download targets.
            if (!HasDownloadEntry(current) && !current.IsPageDownloaded
                && !current.IsResourceDownloaded && current.Id != active.Id)
                continue;
            Wikis.Add(new WikiBookItemViewModel(current, current.Id == active.Id, RequestSwitch).WithText(Text));
        }
        OnPropertyChanged(nameof(HasActiveWiki));
    }

    private static bool HasDownloadEntry(WikiBookSnapshot wiki) =>
        wiki.DataFolder is "Terraria_Wiki_zh" or "Terraria_Wiki_en";

    private CancellationTokenSource? BeginOperation(string status)
    {
        if (IsBusy || !IsAvailable)
            return null;
        var current = new CancellationTokenSource();
        _operationCancellation = current;
        IsBusy = true;
        ProgressValue = 0;
        ProgressMaximum = 1;
        IsProgressIndeterminate = true;
        StatusMessage = status;
        return current;
    }

    private void EndOperation(CancellationTokenSource current)
    {
        if (!ReferenceEquals(current, _operationCancellation))
            return;
        _operationCancellation = null;
        current.Dispose();
        IsBusy = false;
    }

    private void CancelOperation() => _operationCancellation?.Cancel();

    private void ApplyProgress(string label, WikiProgress progress)
    {
        if (!IsBusy)
            return;
        IsProgressIndeterminate = progress.Total <= 0;
        ProgressMaximum = Math.Max(1, progress.Total);
        ProgressValue = Math.Clamp(progress.Current, 0, (int)ProgressMaximum);
        label = progress.Code switch
        {
            "sync-discovery" => UiTextCatalog.Message("DiscoveringRemoteChanges"),
            "sync-full-scan-approval" => UiTextCatalog.Message("WaitingForFullScanApproval"),
            "sync-full-scan" => UiTextCatalog.Message("CheckingTheFullDirectory"),
            "sync-delete" => UiTextCatalog.Message("SynchronizingDeletionsAndRedirects"),
            "sync-index-maintenance" => UiTextCatalog.Message("MaintainingTheUpdatedSearchIndex"),
            "page-download" or "page-skipped" or "sync-pages-start" => UiTextCatalog.Message("ProcessingPages"),
            "resource-downloaded" or "resource-skipped" or "sync-media-start" => UiTextCatalog.Message("ProcessingMediaResources"),
            "http-retry" => UiTextCatalog.Message("RequestFailedWaitingToRetry"),
            "sync-page-failed" or "sync-media-failed" or "download-item-failed" => UiTextCatalog.Message("RecordingFailedItemsAndContinuing"),
            _ => label
        };
        StatusMessage = progress.Total > 0
            ? UiTextCatalog.Message("ProgressCounts", label, progress.Current, progress.Total)
            : UiTextCatalog.Message("InProgress", label);
        if (progress.Code == "sync-full-scan")
            StatusMessage = UiTextCatalog.Join("", _statusMessage, progress.Detail switch
            {
                "baseline-missing" => UiTextCatalog.Message("NoIncrementalBaselineYet"),
                "page-recovery" => UiTextCatalog.Message("RecoveringThePreviousIncompletePageSync"),
                "identity-changed" => UiTextCatalog.Message("WikiSyncConfigurationHasChanged"),
                "media-baseline-missing" => UiTextCatalog.Message("EstablishingAMediaSyncBaseline"),
                "periodic" => UiTextCatalog.Message("ThePeriodicCheckIsDue"),
                "anchor-unavailable" => UiTextCatalog.Message("TheRemoteLogAnchorHasExpired"),
                _ => UiTextCatalog.Message("LogCoverageCannotBeConfirmedRunningASafe")
            });
    }

    private void SetConfirmation(ManagementConfirmation confirmation)
    {
        _confirmation = confirmation;
        NotifyConfirmationChanged();
    }

    private void ClearConfirmationState(bool keepPendingData)
    {
        _confirmation = ManagementConfirmation.None;
        if (!keepPendingData)
        {
            _pendingSwitch = null;
            _pendingImport = null;
        }
        NotifyConfirmationChanged();
    }

    private void NotifyConfirmationChanged()
    {
        OnPropertyChanged(nameof(IsConfirmationVisible));
        OnPropertyChanged(nameof(ConfirmationTitle));
        OnPropertyChanged(nameof(ConfirmationMessage));
        OnPropertyChanged(nameof(ConfirmationActionText));
        OnPropertyChanged(nameof(IsDangerousConfirmation));
        OnPropertyChanged(nameof(IsStandardConfirmation));
        CancelConfirmationCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private string BuildImportConfirmation()
    {
        if (_pendingImport is null)
            return string.Empty;
        return UiTextCatalog.Message("ImportWillReplacePagesResourcesFavoritesAndSearch", Path.GetFileName(_pendingImport.DatabasePath), FormatBytes(_pendingImport.DatabaseBytes), ActiveWikiTitle);
    }

    private void RefreshCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        DownloadAllCommand.NotifyCanExecuteChanged();
        DownloadPagesCommand.NotifyCanExecuteChanged();
        FullUpdateCommand.NotifyCanExecuteChanged();
        QuickUpdateCommand.NotifyCanExecuteChanged();
        RetryFailuresCommand.NotifyCanExecuteChanged();
        RequestClearFailuresCommand.NotifyCanExecuteChanged();
        VacuumCommand.NotifyCanExecuteChanged();
        RequestDeleteResourcesCommand.NotifyCanExecuteChanged();
        RequestDeleteWikiDataCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private static string FormatSummary(string label, WikiOperationSummary summary) =>
        UiTextCatalog.Message("PagesDownloadedSkippedDeletedRedirectsResourcesFailures", label, (summary.Failed > 0 ? UiTextCatalog.Message("Incomplete") : UiTextCatalog.Message("Complete")), summary.Pages, summary.Skipped, summary.Deleted, summary.Redirects, summary.Resources, summary.Failed);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes:N0} B";
        var value = bytes / 1024d;
        if (value < 1024)
            return $"{value:N1} KB";
        value /= 1024;
        if (value < 1024)
            return $"{value:N1} MB";
        return $"{value / 1024:N2} GB";
    }
}
