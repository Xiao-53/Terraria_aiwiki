using System.Collections.ObjectModel;
using System.Globalization;
using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Management;

internal enum ManagementConfirmation
{
    None,
    SwitchWiki,
    ClearFailures,
    DeleteResources,
    DeleteWikiData,
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
    public Func<bool, IProgress<WikiProgress>?, CancellationToken, Task<WikiOperationSummary>> Update { get; init; } =
        (_, _, _) => Unavailable<WikiOperationSummary>();
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
        Func<int, CancellationToken, Task<WikiBookSnapshot>>? switchWiki = null)
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
            Update = application.Data.UpdateAsync,
            RetryFailures = application.Data.RetryFailuresAsync,
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

internal sealed class WikiBookItemViewModel
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
    public string InstallStatus => Wiki.IsPageDownloaded ? "已安装" : "尚未下载";
    public RelayCommand SelectCommand { get; }
}

internal sealed class WikiManagementViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<string?>> _pickImportDatabase;
    private readonly Func<WikiBookSnapshot, CancellationToken, Task<WikiDatabaseExportResponse?>> _exportDatabase;
    private WikiManagementActions? _actions;
    private CancellationTokenSource? _operationCancellation;
    private WikiBookSnapshot? _activeWiki;
    private ManagementConfirmation _confirmation;
    private WikiBookSnapshot? _pendingSwitch;
    private WikiDatabaseInspection? _pendingImport;
    private string _statusMessage = "正在准备数据管理…";
    private bool _isAvailable;
    private bool _isBusy;
    private bool _hasFailures;
    private double _progressValue;
    private double _progressMaximum = 1;
    private bool _isProgressIndeterminate = true;
    private bool _disposed;

    public WikiManagementViewModel(
        Func<CancellationToken, Task<string?>>? pickImportDatabase = null,
        Func<WikiBookSnapshot, CancellationToken, Task<WikiDatabaseExportResponse?>>? exportDatabase = null)
    {
        _pickImportDatabase = pickImportDatabase ?? (_ => Task.FromResult<string?>(null));
        _exportDatabase = exportDatabase ?? ((_, _) => Task.FromResult<WikiDatabaseExportResponse?>(null));
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => CanOperate);
        DownloadAllCommand = new AsyncCommand(_ => DownloadAsync(includeResources: true), _ => CanOperate && !HasPageData);
        DownloadPagesCommand = new AsyncCommand(_ => DownloadAsync(includeResources: false), _ => CanOperate && !HasPageData);
        DownloadResourcesCommand = new AsyncCommand(_ => DownloadResourcesAsync(), _ => CanOperate && HasPageData && !HasResources);
        UpdateCommand = new AsyncCommand(_ => UpdateAsync(), _ => CanOperate && HasPageData);
        RetryFailuresCommand = new AsyncCommand(_ => RetryFailuresAsync(), _ => CanOperate && HasFailures);
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
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(CanOperate));
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
    public bool CanOperate => IsAvailable && !IsBusy && HasActiveWiki;
    public bool IsIdle => !IsBusy;
    public string ActiveWikiTitle => ActiveWiki?.Title ?? "未选择 Wiki";
    public string ActiveWikiDescription => ActiveWiki?.Description ?? "本地 Wiki 数据源不可用。";
    public string PageCountText => (ActiveWiki?.PageCount ?? 0).ToString("N0", CultureInfo.CurrentCulture);
    public string RedirectCountText => (ActiveWiki?.RedirectCount ?? 0).ToString("N0", CultureInfo.CurrentCulture);
    public string ResourceCountText => (ActiveWiki?.ResourceCount ?? 0).ToString("N0", CultureInfo.CurrentCulture);
    public string DataSizeText => FormatBytes(ActiveWiki?.DataSize ?? 0);
    public string UpdateTimeText => ActiveWiki is null || ActiveWiki.UpdateTime == default
        ? "尚未更新"
        : ActiveWiki.UpdateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public bool IsConfirmationVisible => _confirmation != ManagementConfirmation.None;
    public string ConfirmationTitle => _confirmation switch
    {
        ManagementConfirmation.SwitchWiki => "切换活动 Wiki？",
        ManagementConfirmation.ClearFailures => "清理失败记录？",
        ManagementConfirmation.DeleteResources => "删除全部资源？",
        ManagementConfirmation.DeleteWikiData => "删除当前 Wiki 数据？",
        ManagementConfirmation.ImportDatabase => "导入此数据库？",
        _ => string.Empty,
    };
    public string ConfirmationMessage => _confirmation switch
    {
        ManagementConfirmation.SwitchWiki => $"将活动数据源切换为“{_pendingSwitch?.Title}”。当前 Viewer、搜索与资料库会一起切换。",
        ManagementConfirmation.ClearFailures => "仅清理页面与资源下载失败列表；不会删除已经下载的 Wiki 内容。",
        ManagementConfirmation.DeleteResources => $"将删除“{ActiveWikiTitle}”的全部本地图片和音频资源，页面文字仍保留。",
        ManagementConfirmation.DeleteWikiData => $"将删除“{ActiveWikiTitle}”的页面、资源、历史与收藏数据。此操作不可撤销，可稍后重新下载或导入。",
        ManagementConfirmation.ImportDatabase => BuildImportConfirmation(),
        _ => string.Empty,
    };
    public string ConfirmationActionText => _confirmation switch
    {
        ManagementConfirmation.SwitchWiki => "确认切换",
        ManagementConfirmation.ClearFailures => "确认清理",
        ManagementConfirmation.DeleteResources => "删除资源",
        ManagementConfirmation.DeleteWikiData => "删除全部数据",
        ManagementConfirmation.ImportDatabase => "确认导入",
        _ => "确认",
    };
    public bool IsDangerousConfirmation => _confirmation is
        ManagementConfirmation.ClearFailures or
        ManagementConfirmation.DeleteResources or
        ManagementConfirmation.DeleteWikiData or
        ManagementConfirmation.ImportDatabase;
    public bool IsStandardConfirmation => IsConfirmationVisible && !IsDangerousConfirmation;

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand DownloadAllCommand { get; }
    public AsyncCommand DownloadPagesCommand { get; }
    public AsyncCommand DownloadResourcesCommand { get; }
    public AsyncCommand UpdateCommand { get; }
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
    internal void ShowEmptyWikiOnboarding() =>
        StatusMessage = "首次使用：当前 Wiki 尚无页面内容。请选择“下载页面与资源”或导入 data.db。";

    public void Attach(
        WikiApplication application,
        Func<int, CancellationToken, Task<WikiBookSnapshot>>? switchWiki = null)
    {
        Attach(WikiManagementActions.From(application, _pickImportDatabase, _exportDatabase, switchWiki));
    }

    internal void Attach(WikiManagementActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        IsAvailable = true;
        StatusMessage = "查看并维护本地 Wiki 数据。";
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
        StatusMessage = "当前 Wiki 数据库未连接；请恢复默认数据目录后重试。";
    }

    internal async Task RefreshAsync()
    {
        if (_actions is null || IsBusy)
            return;
        var current = BeginOperation("正在刷新 Wiki 数据状态…");
        if (current is null)
            return;
        try
        {
            await ReloadAsync(current.Token);
            StatusMessage = NeedsPageData
                ? "首次使用：当前 Wiki 尚无页面内容。请选择“下载页面与资源”或导入 data.db。"
                : "Wiki 数据状态已刷新。";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "刷新已取消。";
        }
        catch
        {
            StatusMessage = "无法读取 Wiki 数据状态；本地数据未被修改。";
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
            StatusMessage = $"“{wiki.Title}”已经是当前 Wiki。";
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
            case ManagementConfirmation.ImportDatabase:
                await ImportDatabaseAsync();
                break;
        }
    }

    internal void CancelConfirmation()
    {
        ClearConfirmationState(keepPendingData: false);
        StatusMessage = IsAvailable ? "操作已取消；本地数据未被修改。" : StatusMessage;
    }

    internal async Task DownloadAsync(bool includeResources)
    {
        var label = includeResources ? "页面与资源下载" : "页面下载";
        await RunSummaryOperationAsync(
            label,
            (progress, token) => _actions!.Download(includeResources, progress, token),
            dataChanged: true,
            requiresViewerRebind: true);
    }

    internal async Task DownloadResourcesAsync() =>
        await RunSummaryOperationAsync(
            "资源下载",
            (progress, token) => _actions!.DownloadResources(progress, token),
            dataChanged: true,
            requiresViewerRebind: true);

    internal async Task UpdateAsync() =>
        await RunSummaryOperationAsync(
            HasResources ? "页面与资源更新" : "页面更新",
            (progress, token) => _actions!.Update(HasResources, progress, token),
            dataChanged: true,
            requiresViewerRebind: true);

    internal async Task RetryFailuresAsync() =>
        await RunSummaryOperationAsync(
            "失败项重试",
            (progress, token) => _actions!.RetryFailures(progress, token),
            dataChanged: true,
            requiresViewerRebind: true);

    internal async Task VacuumAsync() =>
        await RunSimpleOperationAsync(
            "数据库维护",
            token => _actions!.Vacuum(token),
            "数据库整理完成。",
            dataChanged: true,
            requiresViewerRebind: true);

    internal async Task BeginImportAsync()
    {
        if (!CanOperate || _actions is null)
            return;
        var current = BeginOperation("请选择要导入的 data.db 数据库文件…");
        if (current is null)
            return;
        try
        {
            var path = await _actions.PickImportDatabase(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "未选择数据库；本地数据未被修改。";
                return;
            }
            StatusMessage = "正在检查数据库完整性…";
            _pendingImport = await _actions.InspectDatabase(path, current.Token);
            current.Token.ThrowIfCancellationRequested();
            SetConfirmation(ManagementConfirmation.ImportDatabase);
            StatusMessage = "数据库检查通过；确认后才会替换当前 Wiki 数据。";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "导入检查已取消；本地数据未被修改。";
        }
        catch
        {
            _pendingImport = null;
            StatusMessage = "无法检查此数据库；本地数据未被修改。";
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
        var current = BeginOperation("请选择数据库保存位置…");
        if (current is null)
            return;
        try
        {
            var result = await _actions.ExportDatabase(ActiveWiki, current.Token);
            current.Token.ThrowIfCancellationRequested();
            StatusMessage = result is null
                ? "未选择保存位置；没有创建数据库文件。"
                : $"导出完成：{Path.GetFileName(result.DatabasePath)}（{FormatBytes(result.DatabaseBytes)}）。";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "导出已取消；未完成的临时文件已清理。";
        }
        catch
        {
            StatusMessage = "导出失败；原有数据和已有目标文件未被修改。";
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
    }

    private async Task SwitchWikiAsync()
    {
        var target = _pendingSwitch;
        _pendingSwitch = null;
        if (target is null)
            return;
        await RunSimpleOperationAsync(
            "Wiki 切换",
            async token => { await _actions!.SwitchWiki(target.Id, token); },
            $"已切换到“{target.Title}”。",
            dataChanged: true,
            requiresViewerRebind: !_actions!.SwitchIncludesViewerRebind);
    }

    private async Task ClearFailuresAsync() =>
        await RunSimpleOperationAsync(
            "失败记录清理",
            token => _actions!.ClearFailures(token),
            "下载失败记录已清理。",
            dataChanged: true,
            requiresViewerRebind: false);

    private async Task DeleteResourcesAsync() =>
        await RunSummaryOperationAsync(
            "资源删除",
            (_, token) => _actions!.DeleteResources(token),
            dataChanged: true,
            requiresViewerRebind: true);

    private async Task DeleteWikiDataAsync() =>
        await RunSummaryOperationAsync(
            "Wiki 数据删除",
            (_, token) => _actions!.DeleteWikiData(token),
            dataChanged: true,
            requiresViewerRebind: true);

    private async Task ImportDatabaseAsync()
    {
        var inspection = _pendingImport;
        _pendingImport = null;
        if (inspection is null)
            return;
        await RunOperationAsync(
            "数据库导入",
            async (_, token) =>
            {
                var result = await _actions!.ImportDatabase(
                    inspection.DatabasePath,
                    inspection.InspectionToken,
                    token);
                return ($"已导入“{result.WikiBook.Title}”的数据库。", true);
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
        var current = BeginOperation($"正在执行{label}…");
        if (current is null)
            return;
        var progress = new Progress<WikiProgress>(value => ApplyProgress(label, value));
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
                ? result.Message + " 数据状态暂未刷新，请稍后重试。"
                : result.Message;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = $"{label}已取消；已完成的安全步骤会被保留。";
        }
        catch
        {
            StatusMessage = $"{label}失败；请确认网络、磁盘空间或数据库文件后重试。";
        }
        finally
        {
            EndOperation(current);
        }
    }

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
            Wikis.Add(new WikiBookItemViewModel(current, current.Id == active.Id, RequestSwitch));
        }
        OnPropertyChanged(nameof(HasActiveWiki));
    }

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
            "sync-discovery" => "发现远端变化",
            "sync-full-scan" => "核对完整目录",
            "sync-delete" => "同步删除与重定向",
            "sync-index-maintenance" => "整理更新后的搜索索引",
            "page-download" => "下载页面",
            "resource-downloaded" or "resource-skipped" => "核对媒体资源",
            _ => label
        };
        StatusMessage = progress.Total > 0
            ? $"{label}：{progress.Current:N0} / {progress.Total:N0}"
            : $"{label}正在进行…";
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
        return $"{Path.GetFileName(_pendingImport.DatabasePath)} · {FormatBytes(_pendingImport.DatabaseBytes)}。导入会替换“{ActiveWikiTitle}”的页面、资源、收藏与搜索数据；失败时会恢复原数据库。";
    }

    private void RefreshCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        DownloadAllCommand.NotifyCanExecuteChanged();
        DownloadPagesCommand.NotifyCanExecuteChanged();
        DownloadResourcesCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
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
        $"{label}{(summary.Failed > 0 ? "未完成" : "完成")}：下载页面 {summary.Pages:N0}、跳过 {summary.Skipped:N0}、删除 {summary.Deleted:N0}、重定向 {summary.Redirects:N0}、资源 {summary.Resources:N0}、失败 {summary.Failed:N0}。";

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
