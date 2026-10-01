using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Updates;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly Func<ApplicationPreferences, Task> _applyPreferences;
    private Func<CancellationToken, Task<ApplicationPreferences>>? _load;
    private Func<ApplicationPreferences, CancellationToken, Task<ApplicationPreferences>>? _save;
    private CancellationTokenSource? _operationCancellation;
    private ApplicationPreferences? _savedPreferences;
    private string _statusMessage = "正在准备设置…";
    private bool _isAvailable;
    private bool _isBusy;
    private int _editVersion;
    private Task? _saveTask;
    private int _appThemeIndex;
    private int _contentThemeIndex;
    private int _startupPageIndex;
    private bool _isInteractionFeedbackEnabled = true;
    private bool _isSideButtonBackEnabled = true;
    private decimal _searchLimit = ApplicationPreferences.DefaultSearchLimit;
    private decimal _aiSearchLimit = ApplicationPreferences.DefaultAiSearchLimit;
    private bool _isWikiFullScanEnabled = true;
    private decimal _wikiFullScanIntervalDays = ApplicationPreferences.DefaultWikiFullScanIntervalDays;
    private decimal _pageConcurrency = ApplicationPreferences.DefaultPageConcurrency;
    private decimal _resourceConcurrency = ApplicationPreferences.DefaultResourceConcurrency;
    private decimal _retryAttempts = ApplicationPreferences.DefaultRetryAttempts;
    private bool _isDirty;
    private int _selectedPageIndex;
    private bool _disposed;

    public SettingsViewModel(
        Func<ApplicationPreferences, Task>? applyPreferences = null,
        Func<CancellationToken, Task<AiRuntimeConfigurationDraft>>? loadAiConfiguration = null,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task<AiRuntimeConfiguration>>? saveAiConfiguration = null,
        Func<CancellationToken, Task<WindowsAiRuntimeStatus>>? restartAi = null,
        Func<CancellationToken, Task<WindowsAiRuntimeStatus>>? clearAiCredential = null,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task<IReadOnlyList<string>>>? discoverAiModels = null,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task>? testAiConfiguration = null,
        IProductUpdateService? productUpdateService = null,
        Func<Task>? applyUpdateAndRestart = null,
        Func<Uri, Task>? openExternalUri = null,
        ProductDiagnostics? productDiagnostics = null,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>? loadAiModelCatalog = null,
        Func<AiProviderConfiguration, IReadOnlyList<string>, CancellationToken, Task>? saveAiModelCatalog = null,
        Func<CancellationToken, Task<QuickAccessPreferences>>? loadQuickAccess = null,
        Func<QuickAccessPreferences, CancellationToken, Task<bool>>? saveQuickAccess = null,
        Func<bool, Task<bool>>? suspendQuickHotkeys = null)
    {
        _applyPreferences = applyPreferences ?? (_ => Task.CompletedTask);
        Ai = new AiSettingsViewModel(
            loadAiConfiguration ?? (_ => throw new InvalidOperationException("AI configuration is unavailable.")),
            saveAiConfiguration ?? ((_, _) => throw new InvalidOperationException("AI configuration is unavailable.")),
            restartAi ?? (_ => throw new InvalidOperationException("AI Runtime is unavailable.")),
            clearAiCredential ?? (_ => throw new InvalidOperationException("AI credential removal is unavailable.")),
            discoverAiModels,
            testAiConfiguration,
            loadAiModelCatalog,
            saveAiModelCatalog);
        Ai.Attach();
        Ai.SetPageActive(false);
        Updates = new ProductUpdateViewModel(
            productUpdateService ?? new VelopackProductUpdateService(),
            applyUpdateAndRestart ?? (() => Task.CompletedTask),
            openExternalUri ?? (_ => Task.CompletedTask));
        Diagnostics = new ProductDiagnosticsViewModel(
            productDiagnostics ?? ProductDiagnostics.CreateDefault(),
            openExternalUri ?? (_ => Task.CompletedTask));
        QuickAccess = new QuickAccessSettingsViewModel(
            loadQuickAccess ?? (_ => Task.FromResult(new QuickAccessPreferences())),
            saveQuickAccess ?? ((_, _) => Task.FromResult(false)),
            suspendQuickHotkeys);
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => IsAvailable && !IsBusy);
        SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => CanSave);
        SelectGeneralCommand = new RelayCommand(_ => SelectedPageIndex = 0);
        SelectAiCommand = new RelayCommand(_ => SelectedPageIndex = 1);
    }

    public AiSettingsViewModel Ai { get; }
    public ProductUpdateViewModel Updates { get; }
    public ProductDiagnosticsViewModel Diagnostics { get; }
    public QuickAccessSettingsViewModel QuickAccess { get; }

    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        private set
        {
            if (!SetProperty(ref _selectedPageIndex, value))
                return;
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsAiSelected));
            OnPropertyChanged(nameof(ShowGeneralStatus));
            Ai.SetPageActive(IsAiSelected);
            if (IsAiSelected)
                _ = Ai.EnsureLoadedAsync();
        }
    }

    public bool IsGeneralSelected => SelectedPageIndex == 0;
    public bool IsAiSelected => SelectedPageIndex == 1;
    public bool ShowGeneralStatus => IsGeneralSelected && !string.IsNullOrWhiteSpace(StatusMessage);
    public bool CanRetrySave => IsDirty && !IsBusy;
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
                OnPropertyChanged(nameof(ShowGeneralStatus));
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(CanEditWikiFullScanInterval));
                RefreshState();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowGeneralStatus));
                RefreshState();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                RefreshState();
        }
    }

    public int AppThemeIndex
    {
        get => _appThemeIndex;
        set { if (SetProperty(ref _appThemeIndex, value)) MarkDirty(); }
    }

    public int ContentThemeIndex
    {
        get => _contentThemeIndex;
        set { if (SetProperty(ref _contentThemeIndex, value)) MarkDirty(); }
    }

    public int StartupPageIndex
    {
        get => _startupPageIndex;
        set { if (SetProperty(ref _startupPageIndex, value)) MarkDirty(); }
    }

    public bool IsInteractionFeedbackEnabled
    {
        get => _isInteractionFeedbackEnabled;
        set
        {
            if (!SetProperty(ref _isInteractionFeedbackEnabled, value))
                return;
            OnPropertyChanged(nameof(InteractionFeedbackStateText));
            MarkDirty();
        }
    }

    public string InteractionFeedbackStateText => IsInteractionFeedbackEnabled ? "开" : "关";

    public bool IsSideButtonBackEnabled
    {
        get => _isSideButtonBackEnabled;
        set
        {
            if (!SetProperty(ref _isSideButtonBackEnabled, value))
                return;
            OnPropertyChanged(nameof(SideButtonStateText));
            MarkDirty();
        }
    }

    public string SideButtonStateText => IsSideButtonBackEnabled ? "开" : "关";

    public decimal SearchLimit
    {
        get => _searchLimit;
        set { if (value >= 1 && value <= 100 && value == decimal.Truncate(value) && SetProperty(ref _searchLimit, value)) MarkDirty(); }
    }

    public decimal AiSearchLimit
    {
        get => _aiSearchLimit;
        set { if (value >= 1 && value <= 100 && value == decimal.Truncate(value) && SetProperty(ref _aiSearchLimit, value)) MarkDirty(); }
    }

    public bool IsWikiFullScanEnabled
    {
        get => _isWikiFullScanEnabled;
        set
        {
            if (!SetProperty(ref _isWikiFullScanEnabled, value))
                return;
            OnPropertyChanged(nameof(CanEditWikiFullScanInterval));
            if (value && _wikiFullScanIntervalDays <= 0)
            {
                _wikiFullScanIntervalDays = ApplicationPreferences.DefaultWikiFullScanIntervalDays;
                OnPropertyChanged(nameof(WikiFullScanIntervalDays));
            }
            MarkDirty();
        }
    }

    public decimal WikiFullScanIntervalDays
    {
        get => _wikiFullScanIntervalDays;
        set { if (value >= 1 && value <= 2147483647 && value == decimal.Truncate(value) && SetProperty(ref _wikiFullScanIntervalDays, value)) MarkDirty(); }
    }

    public bool CanEditWikiFullScanInterval => IsAvailable && IsWikiFullScanEnabled;

    public decimal PageConcurrency
    {
        get => _pageConcurrency;
        set { if (value >= 1 && value <= 3 && value == decimal.Truncate(value) && SetProperty(ref _pageConcurrency, value)) MarkDirty(); }
    }

    public decimal ResourceConcurrency
    {
        get => _resourceConcurrency;
        set { if (value >= 1 && value <= 20 && value == decimal.Truncate(value) && SetProperty(ref _resourceConcurrency, value)) MarkDirty(); }
    }

    public decimal RetryAttempts
    {
        get => _retryAttempts;
        set { if (value >= 0 && value <= 20 && value == decimal.Truncate(value) && SetProperty(ref _retryAttempts, value)) MarkDirty(); }
    }

    public bool CanSave => IsAvailable && !IsBusy && IsDirty;
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public RelayCommand SelectGeneralCommand { get; }
    public RelayCommand SelectAiCommand { get; }

    public void Attach(ApplicationSettingsUseCases settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Attach(settings.GetAsync, settings.SaveAsync);
    }

    internal void Attach(
        Func<CancellationToken, Task<ApplicationPreferences>> load,
        Func<ApplicationPreferences, CancellationToken, Task<ApplicationPreferences>> save)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        IsAvailable = true;
        _ = QuickAccess.RefreshAsync();
        StatusMessage = string.Empty;
    }

    public void Detach()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _load = null;
        _save = null;
        _savedPreferences = null;
        IsAvailable = false;
        IsBusy = false;
        IsDirty = false;
        StatusMessage = "应用设置不可用。";
    }

    internal async Task RefreshAsync()
    {
        var load = _load;
        if (_saveTask is { } pending) await pending;
        if (load is null || IsBusy || !IsAvailable || _disposed)
            return;
        var current = BeginOperation();
        var version = _editVersion;
        try
        {
            var preferences = await load(current.Token);
            current.Token.ThrowIfCancellationRequested();
            _savedPreferences = preferences;
            if (version == _editVersion) Apply(preferences);
            else IsDirty = BuildPreferences() != preferences;
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "设置载入已取消。";
        }
        catch
        {
            StatusMessage = "无法读取设置；现有配置未被修改。";
        }
        finally
        {
            EndOperation(current);
        }
        if (IsDirty) await SaveAsync();
    }

    // One drain owns persistence and application. Edits made during an await are saved next.
    internal Task SaveAsync()
    {
        if (_saveTask is { } pending) return pending;
        if (_save is null || !IsDirty || IsBusy) return Task.CompletedTask;
        IsBusy = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _saveTask = completion.Task;
        _ = SavePendingAsync(completion);
        return completion.Task;
    }

    private async Task SavePendingAsync(TaskCompletionSource completion)
    {
        await Task.Yield();
        var current = new CancellationTokenSource();
        _operationCancellation = current;
        try
        {
            while (_save is { } save && IsDirty && !current.IsCancellationRequested)
            {
                var version = _editVersion;
                try
                {
                    var saved = await save(BuildPreferences(), current.Token);
                    current.Token.ThrowIfCancellationRequested();
                    _savedPreferences = saved;
                    // Saving does not rewrite editor fields; even an immediately completed save
                    // must leave a later edit intact. The Application boundary validates values.
                    IsDirty = BuildPreferences() != saved;
                    try
                    {
                        await _applyPreferences(saved);
                        current.Token.ThrowIfCancellationRequested();
                        StatusMessage = string.Empty;
                    }
                    catch (OperationCanceledException) when (current.IsCancellationRequested) { break; }
                    catch { StatusMessage = "设置已保存；部分界面效果将在下次启动时生效。"; }
                }
                catch (OperationCanceledException) when (current.IsCancellationRequested) { break; }
                catch (ApplicationUseCaseException exception) when (exception.Code == "invalid-preferences")
                {
                    StatusMessage = "设置值超出允许范围，请检查后重试。";
                    if (version == _editVersion) break;
                }
                catch
                {
                    StatusMessage = "设置保存失败；当前会话继续使用上次有效配置，可重试保存。";
                    if (version == _editVersion) break;
                }
            }
        }
        finally
        {
            _saveTask = null;
            EndOperation(current);
            completion.TrySetResult();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
        QuickAccess.Dispose();
        Ai.Dispose();
        Updates.Dispose();
    }

    private CancellationTokenSource BeginOperation()
    {
        var current = new CancellationTokenSource();
        _operationCancellation = current;
        IsBusy = true;
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

    private void Apply(ApplicationPreferences preferences)
    {
        _savedPreferences = preferences;
        _appThemeIndex = preferences.AppTheme switch { "light" => 1, "dark" => 2, _ => 0 };
        _contentThemeIndex = preferences.ContentTheme == "original" ? 1 : 0;
        _startupPageIndex = preferences.StartupPage == "wiki" ? 1 : 0;
        _isInteractionFeedbackEnabled = preferences.IsInteractionFeedbackEnabled;
        _isSideButtonBackEnabled = preferences.IsSideButtonBackEnabled;
        _searchLimit = preferences.SearchLimit;
        _aiSearchLimit = preferences.AiSearchLimit;
        _isWikiFullScanEnabled = preferences.WikiFullScanIntervalDays.HasValue;
        _wikiFullScanIntervalDays = preferences.WikiFullScanIntervalDays
            ?? ApplicationPreferences.DefaultWikiFullScanIntervalDays;
        _pageConcurrency = preferences.PageConcurrency;
        _resourceConcurrency = preferences.ResourceConcurrency;
        _retryAttempts = preferences.RetryAttempts;
        OnPropertyChanged(nameof(AppThemeIndex));
        OnPropertyChanged(nameof(ContentThemeIndex));
        OnPropertyChanged(nameof(StartupPageIndex));
        OnPropertyChanged(nameof(IsInteractionFeedbackEnabled));
        OnPropertyChanged(nameof(InteractionFeedbackStateText));
        OnPropertyChanged(nameof(IsSideButtonBackEnabled));
        OnPropertyChanged(nameof(SideButtonStateText));
        OnPropertyChanged(nameof(SearchLimit));
        OnPropertyChanged(nameof(AiSearchLimit));
        OnPropertyChanged(nameof(IsWikiFullScanEnabled));
        OnPropertyChanged(nameof(WikiFullScanIntervalDays));
        OnPropertyChanged(nameof(CanEditWikiFullScanInterval));
        OnPropertyChanged(nameof(PageConcurrency));
        OnPropertyChanged(nameof(ResourceConcurrency));
        OnPropertyChanged(nameof(RetryAttempts));
        IsDirty = false;
    }

    private ApplicationPreferences BuildPreferences() => (_savedPreferences ?? new ApplicationPreferences()) with
    {
        AppTheme = AppThemeIndex switch { 1 => "light", 2 => "dark", _ => "auto" },
        ContentTheme = ContentThemeIndex == 1 ? "original" : "auto",
        StartupPage = StartupPageIndex == 1 ? "wiki" : "chat",
        IsInteractionFeedbackEnabled = IsInteractionFeedbackEnabled,
        IsSideButtonBackEnabled = IsSideButtonBackEnabled,
        SearchLimit = Decimal.ToInt32(SearchLimit),
        AiSearchLimit = Decimal.ToInt32(AiSearchLimit),
        WikiFullScanIntervalDays = IsWikiFullScanEnabled
            ? Decimal.ToInt32(WikiFullScanIntervalDays)
            : null,
        PageConcurrency = Decimal.ToInt32(PageConcurrency),
        ResourceConcurrency = Decimal.ToInt32(ResourceConcurrency),
        RetryAttempts = Decimal.ToInt32(RetryAttempts),
    };

    private void MarkDirty()
    {
        ++_editVersion;
        if (_savedPreferences is not null)
        {
            IsDirty = BuildPreferences() != _savedPreferences;
            _ = SaveAsync();
        }
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanRetrySave));
        OnPropertyChanged(nameof(CanSave));
        RefreshCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }
}
