using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed class AiSettingsViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<AiRuntimeConfigurationDraft>> _load;
    private readonly Func<AiRuntimeConfigurationDraft, CancellationToken, Task<AiRuntimeConfiguration>> _save;
    private readonly Func<CancellationToken, Task<WindowsAiRuntimeStatus>> _restart;
    private readonly Func<CancellationToken, Task<string?>> _pickPythonExecutable;
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
    private int _deepSeekModelIndex;
    private string _customBaseUrl = string.Empty;
    private string _customModel = string.Empty;
    private int _customApiIndex;
    private int _reasoningEffortIndex;
    private bool _disposed;

    public AiSettingsViewModel(
        Func<CancellationToken, Task<AiRuntimeConfigurationDraft>> load,
        Func<AiRuntimeConfigurationDraft, CancellationToken, Task<AiRuntimeConfiguration>> save,
        Func<CancellationToken, Task<WindowsAiRuntimeStatus>> restart,
        Func<CancellationToken, Task<string?>> pickPythonExecutable)
    {
        _load = load ?? throw new ArgumentNullException(nameof(load));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _restart = restart ?? throw new ArgumentNullException(nameof(restart));
        _pickPythonExecutable = pickPythonExecutable ?? throw new ArgumentNullException(nameof(pickPythonExecutable));
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => IsAvailable && !IsBusy);
        SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => CanSave);
        BrowsePythonCommand = new AsyncCommand(_ => BrowsePythonAsync(), _ => IsAvailable && !IsBusy);
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

    public int ProviderIndex
    {
        get => _providerIndex;
        set
        {
            if (!SetProperty(ref _providerIndex, value))
                return;
            OnPropertyChanged(nameof(IsDeepSeek));
            OnPropertyChanged(nameof(IsCustomProvider));
            OnPropertyChanged(nameof(ProviderDescription));
            MarkDirty();
        }
    }

    public string PythonExecutable
    {
        get => _pythonExecutable;
        set { if (SetProperty(ref _pythonExecutable, value ?? string.Empty)) MarkDirty(); }
    }

    public string ApiKey
    {
        get => _apiKey;
        set { if (SetProperty(ref _apiKey, value ?? string.Empty)) MarkDirty(); }
    }

    public int DeepSeekModelIndex
    {
        get => _deepSeekModelIndex;
        set { if (SetProperty(ref _deepSeekModelIndex, value)) MarkDirty(); }
    }

    public string CustomBaseUrl
    {
        get => _customBaseUrl;
        set { if (SetProperty(ref _customBaseUrl, value ?? string.Empty)) MarkDirty(); }
    }

    public string CustomModel
    {
        get => _customModel;
        set { if (SetProperty(ref _customModel, value ?? string.Empty)) MarkDirty(); }
    }

    public int CustomApiIndex
    {
        get => _customApiIndex;
        set { if (SetProperty(ref _customApiIndex, value)) MarkDirty(); }
    }

    public int ReasoningEffortIndex
    {
        get => _reasoningEffortIndex;
        set { if (SetProperty(ref _reasoningEffortIndex, value)) MarkDirty(); }
    }

    public bool IsDeepSeek => ProviderIndex == 0;
    public bool IsCustomProvider => ProviderIndex == 1;
    public string ProviderDescription => IsDeepSeek
        ? "使用 DeepSeek 官方 OpenAI-compatible Responses API。"
        : "连接其他兼容 OpenAI Responses 或 Chat Completions 的服务。";
    public bool CanSave => IsAvailable && !IsBusy && IsDirty;

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand BrowsePythonCommand { get; }

    public void Attach()
    {
        IsAvailable = true;
        StatusMessage = _hasLoaded
            ? "调整后点击“保存并应用”统一验证并重启 AI。"
            : "打开此页后载入 AI 配置。";
    }

    public void Detach()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        IsAvailable = false;
        IsBusy = false;
        StatusMessage = "AI 设置不可用。";
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
            current.Token.ThrowIfCancellationRequested();
            Apply(draft);
            _hasLoaded = true;
            StatusMessage = draft.Exists
                ? "AI 配置已载入。保存后会重启当前 AI Runtime。"
                : "尚无 AI 配置；已准备 DeepSeek 官方 API 默认项，请补全 Python 与 API Key。";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "AI 配置载入已取消。";
        }
        catch (AiConfigurationException exception)
        {
            StatusMessage = exception.Message + " 请修复配置文件后重新载入。";
        }
        catch
        {
            StatusMessage = "无法读取 AI 配置；现有配置未被修改。";
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
            var saved = await _save(BuildDraft(), current.Token);
            current.Token.ThrowIfCancellationRequested();
            Apply(new AiRuntimeConfigurationDraft(
                saved.PythonExecutable,
                saved.Provider,
                saved.ApiKey,
                saved.ConfigurationPath,
                Exists: true));
            try
            {
                var status = await _restart(current.Token);
                current.Token.ThrowIfCancellationRequested();
                StatusMessage = status.State == WindowsAiRuntimeState.Ready
                    ? "AI 配置已保存并应用到当前 Windows 会话。"
                    : "AI 配置已保存；" + status.Message;
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
                StatusMessage = "AI 配置已保存；当前会话应用已因退出而取消。";
            }
            catch
            {
                StatusMessage = "AI 配置已保存；AI 重启失败，可稍后在问答页重试。";
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "AI 配置保存已取消。";
        }
        catch (AiConfigurationException exception)
        {
            StatusMessage = exception.Code switch
            {
                "python-invalid" or "python-missing" => exception.Message,
                "provider-invalid" => exception.Message,
                "model_authentication" => exception.Message,
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
    }

    private async Task BrowsePythonAsync()
    {
        if (!IsAvailable || IsBusy)
            return;
        var current = BeginOperation();
        try
        {
            var path = await _pickPythonExecutable(current.Token);
            current.Token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(path))
            {
                PythonExecutable = path;
                StatusMessage = "已选择 Python；保存时会检查路径和运行环境。";
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = "Python 选择已取消。";
        }
        catch
        {
            StatusMessage = "无法选择 Python；现有配置未被修改。";
        }
        finally
        {
            EndOperation(current);
        }
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

    private void Apply(AiRuntimeConfigurationDraft draft)
    {
        var isDeepSeek = IsDeepSeekPreset(draft.Provider);
        _providerIndex = isDeepSeek ? 0 : 1;
        _pythonExecutable = draft.PythonExecutable;
        _apiKey = draft.ApiKey;
        _deepSeekModelIndex = string.Equals(
            draft.Provider.Model,
            AiRuntimeConfigurationStore.DeepSeekProModel,
            StringComparison.Ordinal) ? 1 : 0;
        _customBaseUrl = isDeepSeek ? string.Empty : draft.Provider.BaseUrl;
        _customModel = isDeepSeek ? string.Empty : draft.Provider.Model;
        _customApiIndex = draft.Provider.Api == "chat_completions" ? 1 : 0;
        _reasoningEffortIndex = draft.Provider.ReasoningEffort switch
        {
            "none" => 1,
            "low" => 2,
            "medium" or "high" or "xhigh" => 3,
            "max" => 4,
            _ => 0,
        };
        OnPropertyChanged(nameof(ProviderIndex));
        OnPropertyChanged(nameof(IsDeepSeek));
        OnPropertyChanged(nameof(IsCustomProvider));
        OnPropertyChanged(nameof(ProviderDescription));
        OnPropertyChanged(nameof(PythonExecutable));
        OnPropertyChanged(nameof(ApiKey));
        OnPropertyChanged(nameof(DeepSeekModelIndex));
        OnPropertyChanged(nameof(CustomBaseUrl));
        OnPropertyChanged(nameof(CustomModel));
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
                DeepSeekModelIndex == 1
                    ? AiRuntimeConfigurationStore.DeepSeekProModel
                    : AiRuntimeConfigurationStore.DeepSeekFlashModel,
                "responses",
                MapReasoningEffort())
            : new AiProviderConfiguration(
                CustomBaseUrl,
                CustomModel,
                CustomApiIndex == 1 ? "chat_completions" : "responses",
                MapReasoningEffort());
        return new AiRuntimeConfigurationDraft(
            PythonExecutable,
            provider,
            ApiKey,
            configurationPath ?? _savedDraft?.ConfigurationPath ?? string.Empty,
            exists);
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
        && provider.Api == "responses"
        && provider.Model is AiRuntimeConfigurationStore.DeepSeekFlashModel
            or AiRuntimeConfigurationStore.DeepSeekProModel;

    private void MarkDirty()
    {
        if (_savedDraft is not null)
            IsDirty = BuildDraft(_savedDraft.ConfigurationPath, _savedDraft.Exists) != _savedDraft;
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(CanSave));
        RefreshCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        BrowsePythonCommand.NotifyCanExecuteChanged();
    }
}
