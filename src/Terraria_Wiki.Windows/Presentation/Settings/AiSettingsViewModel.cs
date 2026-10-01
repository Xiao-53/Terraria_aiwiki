using System.Collections.ObjectModel;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed class AiSettingsViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<AiRuntimeConfigurationDraft>> _load;
    private readonly Func<AiRuntimeConfigurationDraft, CancellationToken, Task<AiRuntimeConfiguration>> _save;
    private readonly Func<CancellationToken, Task<WindowsAiRuntimeStatus>> _restart;
    private readonly Func<CancellationToken, Task<WindowsAiRuntimeStatus>> _clearCredential;
    private readonly Func<AiRuntimeConfigurationDraft, CancellationToken, Task<IReadOnlyList<string>>> _discoverModels;
    private readonly Func<AiRuntimeConfigurationDraft, CancellationToken, Task> _testConfiguration;
    private readonly Func<CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>> _loadModelCatalog;
    private readonly Func<AiProviderConfiguration, IReadOnlyList<string>, CancellationToken, Task> _saveModelCatalog;
    private readonly Dictionary<string, IReadOnlyList<string>> _modelCatalog = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _operationCancellation;
    private AiRuntimeConfigurationDraft? _savedDraft;
    private string _statusMessage = "打开此页后载入 AI 配置。";
    private bool _isAvailable;
    private bool _isBusy;
    private bool _hasLoaded;
    private bool _isDirty;
    private int _providerIndex;
    private string _pythonExecutable = string.Empty;
    private string _apiKey = string.Empty;
    private bool _hasStoredCredential;
    private string _model = string.Empty;
    private string _customBaseUrl = string.Empty;
    private int _customApiIndex;
    private int _reasoningEffortIndex;
    private bool _disposed;
    private bool _pageActive = true;
    private bool _notificationVisible;
    private double _notificationOpacity = 1;
    private string _configurationError = string.Empty;
    private CancellationTokenSource? _notificationCancellation;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public AiSettingsViewModel(
        Func<CancellationToken, Task<AiRuntimeConfigurationDraft>> load,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task<AiRuntimeConfiguration>> save,
        Func<CancellationToken, Task<WindowsAiRuntimeStatus>> restart,
        Func<CancellationToken, Task<WindowsAiRuntimeStatus>>? clearCredential = null,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task<IReadOnlyList<string>>>? discoverModels = null,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task>? testConfiguration = null,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>>? loadModelCatalog = null,
        Func<AiProviderConfiguration, IReadOnlyList<string>, CancellationToken, Task>? saveModelCatalog = null,
        Func<TimeSpan, CancellationToken, Task>? notificationDelay = null)
    {
        _delay = notificationDelay ?? ((duration, token) => Task.Delay(duration, token));
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _restart = restart ?? throw new ArgumentNullException(nameof(restart));
        _clearCredential = clearCredential
            ?? (_ => throw new InvalidOperationException("AI credential removal is unavailable."));
        _discoverModels = discoverModels
            ?? ((_, _) => throw new InvalidOperationException("AI model discovery is unavailable."));
        _testConfiguration = testConfiguration
            ?? ((_, _) => throw new InvalidOperationException("AI configuration testing is unavailable."));
        _loadModelCatalog = loadModelCatalog
            ?? (_ => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>()));
        _saveModelCatalog = saveModelCatalog ?? ((_, _, _) => Task.CompletedTask);
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => IsAvailable && !IsBusy);
        SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => CanSave);
        ClearCredentialCommand = new AsyncCommand(_ => ClearCredentialAsync(), _ => CanClearCredential);
        DiscoverModelsCommand = new AsyncCommand(_ => DiscoverModelsAsync(), _ => CanUseNetworkActions);
        TestConfigurationCommand = new AsyncCommand(_ => TestConfigurationAsync(), _ => CanUseNetworkActions);
    }

    public ObservableCollection<string> DiscoveredModels { get; } = [];

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            CancelNotification();
            SetProperty(ref _statusMessage, value);
            NotificationOpacity = 1;
            _notificationVisible = _pageActive && !string.IsNullOrWhiteSpace(value);
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => _pageActive && _notificationVisible;
    public double NotificationOpacity
    {
        get => _notificationOpacity;
        private set => SetProperty(ref _notificationOpacity, value);
    }
    public string ConfigurationError
    {
        get => _configurationError;
        private set => SetProperty(ref _configurationError, value);
    }

    public void SetPageActive(bool active)
    {
        _pageActive = active;
        if (!active)
        {
            CancelNotification();
            _notificationVisible = false;
        }
        if (active && IsBusy) _notificationVisible = true;
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    private void CancelNotification()
    {
        _notificationCancellation?.Cancel();
        _notificationCancellation?.Dispose();
        _notificationCancellation = null;
    }

    private async Task ExpireNotificationAsync(CancellationTokenSource current)
    {
        var token = current.Token;
        try
        {
            await _delay(TimeSpan.FromSeconds(20), token);
            if (token.IsCancellationRequested || !ReferenceEquals(current, _notificationCancellation)) return;
            NotificationOpacity = 0;
            await _delay(TimeSpan.FromMilliseconds(180), token);
            if (token.IsCancellationRequested || !ReferenceEquals(current, _notificationCancellation)) return;
            _notificationVisible = false;
            OnPropertyChanged(nameof(HasStatusMessage));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(current, _notificationCancellation))
            {
                _notificationCancellation = null;
                current.Dispose();
            }
        }
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
            {
                OnPropertyChanged(nameof(HasStatusMessage));
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

    public int ProviderIndex
    {
        get => _providerIndex;
        set
        {
            if (!SetProperty(ref _providerIndex, value))
                return;
            RestoreDiscoveredModelsFromCache();
            OnPropertyChanged(nameof(IsDeepSeek));
            OnPropertyChanged(nameof(IsCustomProvider));
            OnPropertyChanged(nameof(ProviderDescription));
            MarkDirty();
        }
    }

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (!SetProperty(ref _apiKey, value ?? string.Empty))
                return;
            OnPropertyChanged(nameof(ShowStoredCredentialMask));
            OnPropertyChanged(nameof(ApiKeyPlaceholder));
            MarkDirty();
        }
    }

    public bool HasStoredCredential => _hasStoredCredential;
    public bool ShowStoredCredentialMask => HasStoredCredential && ApiKey.Length == 0;
    public string ApiKeyPlaceholder => HasStoredCredential ? string.Empty : "输入 API Key";
    public string CredentialStatus => HasStoredCredential
        ? "已安全保存在 Windows Credential Manager；输入框中的固定遮蔽标记不代表密钥长度，输入新值可替换。"
        : "尚未保存密钥；输入后将写入 Windows Credential Manager，不写入配置文件。";

    public string Model
    {
        get => _model;
        set
        {
            if (!SetProperty(ref _model, value ?? string.Empty))
                return;
            OnPropertyChanged(nameof(SelectedDiscoveredModel));
            MarkDirty();
        }
    }

    public string? SelectedDiscoveredModel
    {
        get => DiscoveredModels.Contains(Model) ? Model : null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Model = value;
        }
    }

    public string CustomBaseUrl
    {
        get => _customBaseUrl;
        set
        {
            if (!SetProperty(ref _customBaseUrl, value ?? string.Empty))
                return;
            RestoreDiscoveredModelsFromCache();
            MarkDirty();
        }
    }

    public int CustomApiIndex
    {
        get => _customApiIndex;
        set
        {
            if (!SetProperty(ref _customApiIndex, value))
                return;
            RestoreDiscoveredModelsFromCache();
            MarkDirty();
        }
    }

    public int ReasoningEffortIndex
    {
        get => _reasoningEffortIndex;
        set { if (SetProperty(ref _reasoningEffortIndex, value)) MarkDirty(); }
    }

    public bool IsDeepSeek => ProviderIndex == 0;
    public bool IsCustomProvider => ProviderIndex == 1;
    public string ProviderDescription => IsDeepSeek
        ? "使用 DeepSeek 官方地址和 Responses API；模型列表从服务实时获取。"
        : "连接其他兼容 OpenAI Responses 或 Chat Completions 的服务；不支持模型列表时可手填 model id。";
    public bool CanEdit => IsAvailable && !IsBusy;
    public bool CanSave => IsAvailable && !IsBusy && IsDirty;
    public bool CanClearCredential => IsAvailable && !IsBusy && HasStoredCredential;
    public bool CanUseNetworkActions => IsAvailable && !IsBusy;

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand ClearCredentialCommand { get; }
    public AsyncCommand DiscoverModelsCommand { get; }
    public AsyncCommand TestConfigurationCommand { get; }

    public void Attach()
    {
        IsAvailable = true;
        StatusMessage = string.Empty;
    }

    public void Detach()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        CancelNotification();
        IsAvailable = false;
        IsBusy = false;
        StatusMessage = string.Empty;
        ConfigurationError = "AI 设置不可用。";
    }

    public Task EnsureLoadedAsync() => _hasLoaded ? Task.CompletedTask : RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (!IsAvailable || IsBusy)
            return;
        var current = BeginOperation();
        try
        {
            var draft = await _load(current.Token);
            var catalog = await LoadModelCatalogSafelyAsync(current.Token);
            current.Token.ThrowIfCancellationRequested();
            Apply(draft);
            ConfigurationError = draft.Exists ? string.Empty : "请填写模型名称和 API Key 后保存配置。";
            _modelCatalog.Clear();
            foreach (var entry in catalog)
                _modelCatalog[entry.Key] = entry.Value;
            RestoreDiscoveredModelsFromCache();
            _hasLoaded = true;
            StatusMessage = draft.Exists
                ? string.Empty
                : "尚无 AI 配置；请先获取或手动填写模型，并输入 API Key。";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "AI 配置载入已取消。";
        }
        catch (AiConfigurationException exception)
        {
            ConfigurationError = exception.Message + " 请修复配置文件后切换到常规设置，再返回 AI 配置。";
            StatusMessage = "无法载入 AI 配置。";
        }
        catch
        {
            ConfigurationError = "无法读取 AI 配置，请切换到常规设置，再返回 AI 配置重试。";
            StatusMessage = "无法读取 AI 配置。";
        }
        finally
        {
            EndOperation(current);
        }
    }

    internal async Task DiscoverModelsAsync()
    {
        if (!CanUseNetworkActions)
            return;
        var current = BeginOperation();
        try
        {
            StatusMessage = "正在从模型服务获取列表…";
            var draft = BuildDraft();
            var models = await _discoverModels(draft, current.Token);
            current.Token.ThrowIfCancellationRequested();
            var normalizedModels = models
                .Distinct(StringComparer.Ordinal)
                .OrderBy(model => model, StringComparer.Ordinal)
                .ToArray();
            _modelCatalog[AiModelCatalogStore.ServiceKey(draft.Provider)] = normalizedModels;
            ApplyDiscoveredModels(normalizedModels);
            await _saveModelCatalog(draft.Provider, normalizedModels, current.Token);
            current.Token.ThrowIfCancellationRequested();
            var selectedFirstModel = Model.Length == 0 && DiscoveredModels.Count > 0;
            if (selectedFirstModel)
                Model = DiscoveredModels[0];
            StatusMessage = DiscoveredModels.Count == 0
                ? "未获取到模型，可手动输入模型名称"
                : $"已获取 {DiscoveredModels.Count} 个模型";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "获取模型已取消。";
        }
        catch (Exception exception) when (exception is AiConfigurationException or AiProviderConnectionException)
        {
            if (exception is AiConfigurationException) ConfigurationError = exception.Message;
            StatusMessage = exception.Message;
        }
        catch
        {
            StatusMessage = "无法获取模型列表；当前配置未被修改。";
        }
        finally
        {
            EndOperation(current);
        }
    }

    internal async Task TestConfigurationAsync()
    {
        if (!CanUseNetworkActions)
            return;
        var current = BeginOperation();
        try
        {
            StatusMessage = "正在测试连接…";
            await _testConfiguration(BuildDraft(), current.Token);
            current.Token.ThrowIfCancellationRequested();
            StatusMessage = "连接成功";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "测试连接已取消。";
        }
        catch (Exception exception) when (exception is AiConfigurationException or AiProviderConnectionException)
        {
            if (exception is AiConfigurationException) ConfigurationError = exception.Message;
            StatusMessage = exception.Message;
        }
        catch
        {
            StatusMessage = "测试连接失败，请检查服务地址、模型和密钥。";
        }
        finally
        {
            EndOperation(current);
        }
    }

    internal async Task SaveAsync()
    {
        if (!CanSave)
            return;
        var current = BeginOperation();
        try
        {
            StatusMessage = "正在保存配置…";
            var saved = await _save(BuildDraft(), current.Token);
            current.Token.ThrowIfCancellationRequested();
            Apply(new AiRuntimeConfigurationDraft(
                saved.PythonExecutable,
                saved.Provider,
                string.Empty,
                saved.ConfigurationPath,
                Exists: true,
                HasStoredCredential: true), resetDiscoveredModels: false);
            try
            {
                StatusMessage = "正在应用配置…";
                var status = await _restart(current.Token);
                current.Token.ThrowIfCancellationRequested();
                ConfigurationError = status.State == WindowsAiRuntimeState.Ready ? string.Empty : status.Message;
                StatusMessage = status.State == WindowsAiRuntimeState.Ready
                    ? "AI 配置已保存"
                    : "AI 配置已保存，暂未应用；" + status.Message;
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
                StatusMessage = "AI 配置已保存；当前会话应用已因退出而取消。";
            }
            catch
            {
                ConfigurationError = "AI 暂未应用配置，可稍后在问答页重试。";
                StatusMessage = "AI 配置已保存，暂未应用；可稍后在问答页重试。";
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "AI 配置保存已取消。";
        }
        catch (AiConfigurationException exception)
        {
            ConfigurationError = exception.Message;
            StatusMessage = exception.Code switch
            {
                "python-invalid" or "python-missing" or "provider-invalid" or "model_authentication" => exception.Message,
                _ => "AI 配置保存失败；当前会话继续使用上次有效配置。",
            };
        }
        catch
        {
            StatusMessage = "AI 配置保存失败；当前会话继续使用上次有效配置。";
        }
        finally
        {
            EndOperation(current);
        }
    }

    internal async Task ClearCredentialAsync()
    {
        if (!CanClearCredential)
            return;
        var current = BeginOperation();
        try
        {
            StatusMessage = "正在清除密钥…";
            var status = await _clearCredential(current.Token);
            current.Token.ThrowIfCancellationRequested();
            _apiKey = string.Empty;
            _hasStoredCredential = false;
            OnPropertyChanged(nameof(ApiKey));
            OnPropertyChanged(nameof(HasStoredCredential));
            OnPropertyChanged(nameof(ShowStoredCredentialMask));
            OnPropertyChanged(nameof(ApiKeyPlaceholder));
            OnPropertyChanged(nameof(CredentialStatus));
            _savedDraft = BuildDraft(_savedDraft?.ConfigurationPath, _savedDraft?.Exists ?? true);
            IsDirty = false;
            ConfigurationError = "尚未保存密钥，请输入 API Key 后保存配置。";
            StatusMessage = status.State == WindowsAiRuntimeState.Disabled
                ? "已从 Windows Credential Manager 清除 API Key；AI 已停用，离线功能不受影响。"
                : "已清除 API Key；AI Runtime 状态将在下次启动时刷新。";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "清除 API Key 已取消；现有凭据未确认改变。";
        }
        catch
        {
            StatusMessage = "无法清除 Windows 凭据；AI 配置文件未被修改。";
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

    private CancellationTokenSource BeginOperation()
    {
        var current = new CancellationTokenSource();
        _operationCancellation = current;
        CancelNotification();
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
        if (_pageActive && _notificationVisible && !_disposed)
        {
            var expiry = new CancellationTokenSource();
            _notificationCancellation = expiry;
            _ = ExpireNotificationAsync(expiry);
        }
    }

    private void Apply(AiRuntimeConfigurationDraft draft, bool resetDiscoveredModels = true)
    {
        var isDeepSeek = IsDeepSeekPreset(draft.Provider);
        _providerIndex = isDeepSeek ? 0 : 1;
        _pythonExecutable = draft.PythonExecutable;
        _apiKey = draft.ApiKey;
        _hasStoredCredential = draft.HasStoredCredential;
        _model = draft.Provider.Model;
        _customBaseUrl = isDeepSeek ? string.Empty : draft.Provider.BaseUrl;
        _customApiIndex = draft.Provider.Api == "chat_completions" ? 1 : 0;
        _reasoningEffortIndex = draft.Provider.ReasoningEffort switch
        {
            "none" => 1,
            "low" => 2,
            "medium" or "high" or "xhigh" => 3,
            "max" => 4,
            _ => 0,
        };
        if (resetDiscoveredModels)
            ClearDiscoveredModels();
        OnPropertyChanged(nameof(ProviderIndex));
        OnPropertyChanged(nameof(IsDeepSeek));
        OnPropertyChanged(nameof(IsCustomProvider));
        OnPropertyChanged(nameof(ProviderDescription));
        OnPropertyChanged(nameof(ApiKey));
        OnPropertyChanged(nameof(HasStoredCredential));
        OnPropertyChanged(nameof(ShowStoredCredentialMask));
        OnPropertyChanged(nameof(ApiKeyPlaceholder));
        OnPropertyChanged(nameof(CredentialStatus));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(SelectedDiscoveredModel));
        OnPropertyChanged(nameof(CustomBaseUrl));
        OnPropertyChanged(nameof(CustomApiIndex));
        OnPropertyChanged(nameof(ReasoningEffortIndex));
        _savedDraft = BuildDraft(draft.ConfigurationPath, draft.Exists);
        IsDirty = false;
    }

    private AiRuntimeConfigurationDraft BuildDraft(
        string? configurationPath = null,
        bool exists = true)
    {
        var provider = IsDeepSeek
            ? new AiProviderConfiguration(
                AiRuntimeConfigurationStore.DeepSeekBaseUrl,
                Model,
                "responses",
                MapReasoningEffort())
            : new AiProviderConfiguration(
                CustomBaseUrl,
                Model,
                CustomApiIndex == 1 ? "chat_completions" : "responses",
                MapReasoningEffort());
        return new AiRuntimeConfigurationDraft(
            _pythonExecutable,
            provider,
            ApiKey,
            configurationPath ?? _savedDraft?.ConfigurationPath ?? string.Empty,
            exists,
            _hasStoredCredential);
    }

    private string? MapReasoningEffort() => ReasoningEffortIndex switch
    {
        1 => "none",
        2 => "low",
        3 => "high",
        4 => "max",
        _ => null,
    };

    private static bool IsDeepSeekPreset(AiProviderConfiguration provider) =>
        string.Equals(
            provider.BaseUrl.TrimEnd('/'),
            AiRuntimeConfigurationStore.DeepSeekBaseUrl,
            StringComparison.OrdinalIgnoreCase)
        && provider.Api == "responses";

    private void ClearDiscoveredModels()
    {
        if (DiscoveredModels.Count > 0)
        {
            DiscoveredModels.Clear();
            OnPropertyChanged(nameof(SelectedDiscoveredModel));
        }
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadModelCatalogSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _loadModelCatalog(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }
    }

    private void RestoreDiscoveredModelsFromCache()
    {
        var provider = BuildDraft().Provider;
        ApplyDiscoveredModels(_modelCatalog.TryGetValue(AiModelCatalogStore.ServiceKey(provider), out var models)
            ? models
            : []);
    }

    private void ApplyDiscoveredModels(IEnumerable<string> models)
    {
        ClearDiscoveredModels();
        foreach (var model in models)
            DiscoveredModels.Add(model);
        OnPropertyChanged(nameof(SelectedDiscoveredModel));
    }

    private void MarkDirty()
    {
        if (_savedDraft is not null)
            IsDirty = BuildDraft(_savedDraft.ConfigurationPath, _savedDraft.Exists) != _savedDraft;
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanClearCredential));
        OnPropertyChanged(nameof(CanUseNetworkActions));
        RefreshCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        ClearCredentialCommand.NotifyCanExecuteChanged();
        DiscoverModelsCommand.NotifyCanExecuteChanged();
        TestConfigurationCommand.NotifyCanExecuteChanged();
    }
}
