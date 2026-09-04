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
    private int _languageIndex;
    private int _appThemeIndex;
    private int _contentThemeIndex;
    private bool _isSideButtonBackEnabled = true;
    private decimal _searchLimit = ApplicationPreferences.DefaultSearchLimit;
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
        IProductUpdateService? productUpdateService = null,
        Func<Task>? applyUpdateAndRestart = null,
        Func<Uri, Task>? openExternalUri = null,
        ProductDiagnostics? productDiagnostics = null)
    {
        _applyPreferences = applyPreferences ?? (_ => Task.CompletedTask);
        Ai = new AiSettingsViewModel(
            loadAiConfiguration ?? (_ => throw new InvalidOperationException("AI configuration is unavailable.")),
            saveAiConfiguration ?? ((_, _) => throw new InvalidOperationException("AI configuration is unavailable.")),
            restartAi ?? (_ => throw new InvalidOperationException("AI Runtime is unavailable.")),
            clearAiCredential ?? (_ => throw new InvalidOperationException("AI credential removal is unavailable.")));
        Updates = new ProductUpdateViewModel(
            productUpdateService ?? new VelopackProductUpdateService(),
            applyUpdateAndRestart ?? (() => Task.CompletedTask),
            openExternalUri ?? (_ => Task.CompletedTask));
        Diagnostics = new ProductDiagnosticsViewModel(
            productDiagnostics ?? ProductDiagnostics.CreateDefault(),
            openExternalUri ?? (_ => Task.CompletedTask));
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => IsAvailable && !IsBusy);
        SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => CanSave);
        SelectGeneralCommand = new RelayCommand(_ => SelectedPageIndex = 0);
        SelectAiCommand = new RelayCommand(_ => SelectedPageIndex = 1);
    }

    public AiSettingsViewModel Ai { get; }
    public ProductUpdateViewModel Updates { get; }
    public ProductDiagnosticsViewModel Diagnostics { get; }

    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        private set
        {
            if (!SetProperty(ref _selectedPageIndex, value))
                return;
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsAiSelected));
            if (IsAiSelected)
                _ = Ai.EnsureLoadedAsync();
        }
    }

    public bool IsGeneralSelected => SelectedPageIndex == 0;
    public bool IsAiSelected => SelectedPageIndex == 1;

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
                RefreshState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RefreshState();
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

    public int LanguageIndex
    {
        get => _languageIndex;
        set { if (SetProperty(ref _languageIndex, value)) MarkDirty(); }
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
        set { if (SetProperty(ref _searchLimit, value)) MarkDirty(); }
    }

    public decimal PageConcurrency
    {
        get => _pageConcurrency;
        set { if (SetProperty(ref _pageConcurrency, value)) MarkDirty(); }
    }

    public decimal ResourceConcurrency
    {
        get => _resourceConcurrency;
        set { if (SetProperty(ref _resourceConcurrency, value)) MarkDirty(); }
    }

    public decimal RetryAttempts
    {
        get => _retryAttempts;
        set { if (SetProperty(ref _retryAttempts, value)) MarkDirty(); }
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
        Ai.Attach();
        StatusMessage = "调整后点击“保存设置”统一验证并写入本机配置。";
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
        Ai.Detach();
        StatusMessage = "应用设置不可用。";
    }

    internal async Task RefreshAsync()
    {
        var load = _load;
        if (load is null || IsBusy)
            return;
        var current = BeginOperation();
        try
        {
            var preferences = await load(current.Token);
            current.Token.ThrowIfCancellationRequested();
            Apply(preferences);
            StatusMessage = "设置已载入。";
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
    }

    internal async Task SaveAsync()
    {
        var save = _save;
        if (save is null || !CanSave)
            return;
        var current = BeginOperation();
        try
        {
            var requested = BuildPreferences();
            var saved = await save(requested, current.Token);
            current.Token.ThrowIfCancellationRequested();
            Apply(saved);
            try
            {
                await _applyPreferences(saved);
                current.Token.ThrowIfCancellationRequested();
                StatusMessage = "设置已保存并应用到当前 Windows 会话。";
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
                StatusMessage = "设置已保存；当前会话应用已因退出而取消。";
            }
            catch
            {
                StatusMessage = "设置已保存；部分界面效果将在下次启动时生效。";
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "设置保存已取消。";
        }
        catch (ApplicationUseCaseException exception) when (exception.Code == "invalid-preferences")
        {
            StatusMessage = "设置值超出允许范围，请检查后重试。";
        }
        catch
        {
            StatusMessage = "设置保存失败；当前会话继续使用上次有效配置。";
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
        _languageIndex = preferences.Language == "en-us" ? 1 : 0;
        _appThemeIndex = preferences.AppTheme switch { "light" => 1, "dark" => 2, _ => 0 };
        _contentThemeIndex = preferences.ContentTheme == "original" ? 1 : 0;
        _isSideButtonBackEnabled = preferences.IsSideButtonBackEnabled;
        _searchLimit = preferences.SearchLimit;
        _pageConcurrency = preferences.PageConcurrency;
        _resourceConcurrency = preferences.ResourceConcurrency;
        _retryAttempts = preferences.RetryAttempts;
        OnPropertyChanged(nameof(LanguageIndex));
        OnPropertyChanged(nameof(AppThemeIndex));
        OnPropertyChanged(nameof(ContentThemeIndex));
        OnPropertyChanged(nameof(IsSideButtonBackEnabled));
        OnPropertyChanged(nameof(SideButtonStateText));
        OnPropertyChanged(nameof(SearchLimit));
        OnPropertyChanged(nameof(PageConcurrency));
        OnPropertyChanged(nameof(ResourceConcurrency));
        OnPropertyChanged(nameof(RetryAttempts));
        IsDirty = false;
    }

    private ApplicationPreferences BuildPreferences() => new()
    {
        Language = LanguageIndex == 1 ? "en-us" : "zh-cn",
        AppTheme = AppThemeIndex switch { 1 => "light", 2 => "dark", _ => "auto" },
        ContentTheme = ContentThemeIndex == 1 ? "original" : "auto",
        IsSideButtonBackEnabled = IsSideButtonBackEnabled,
        SearchLimit = Decimal.ToInt32(SearchLimit),
        PageConcurrency = Decimal.ToInt32(PageConcurrency),
        ResourceConcurrency = Decimal.ToInt32(ResourceConcurrency),
        RetryAttempts = Decimal.ToInt32(RetryAttempts),
    };

    private void MarkDirty()
    {
        if (_savedPreferences is not null)
            IsDirty = BuildPreferences() != _savedPreferences;
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanSave));
        RefreshCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }
}
