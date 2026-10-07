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
    private string _statusMessage = UiTextCatalog.Message("OpenThisPageToLoadAIConfiguration");
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
    private string _configurationError = string.Empty;

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
        Notification = new StatusNotification(notificationDelay);
        Notification.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StatusNotification.IsVisible)) OnPropertyChanged(nameof(HasStatusMessage));
            if (args.PropertyName == nameof(StatusNotification.Opacity)) OnPropertyChanged(nameof(NotificationOpacity));
        };
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
        get => Text.Render(_statusMessage);
        private set
        {
            SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value));
            Notification.Show(!string.IsNullOrWhiteSpace(value));
        }
    }

    public StatusNotification Notification { get; }
    public bool HasStatusMessage => Notification.IsVisible;
    public double NotificationOpacity => Notification.Opacity;
    public string ConfigurationError
    {
        get => Text.Render(_configurationError);
        private set => SetProperty(ref _configurationError, UiTextCatalog.FromSafeMessage(value));
    }

    public void SetPageActive(bool active) => Notification.SetPageActive(active);

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
                Notification.SetBusy(value);
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
    public string ApiKeyPlaceholder => Text.Render(HasStoredCredential ? string.Empty : UiTextCatalog.Message("EnterAPIKey"));
    public string CredentialStatus => Text.Render(HasStoredCredential
        ? UiTextCatalog.Message("SecurelySavedInWindowsCredentialManagerTheFixed")
        : UiTextCatalog.Message("NoKeySavedYetItWillBeStored"));

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
    public string ProviderDescription => Text.Render(IsDeepSeek
        ? UiTextCatalog.Message("UsesTheOfficialDeepSeekEndpointAndResponsesAPI")
        : UiTextCatalog.Message("ConnectToAnotherOpenAIResponsesOrChatCompletions"));
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
        Notification.Reset();
        IsAvailable = false;
        IsBusy = false;
        StatusMessage = string.Empty;
        ConfigurationError = UiTextCatalog.Message("AISettingsUnavailable");
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
            ConfigurationError = draft.Exists ? string.Empty : UiTextCatalog.Message("EnterAModelNameAndAPIKeyThen");
            _modelCatalog.Clear();
            foreach (var entry in catalog)
                _modelCatalog[entry.Key] = entry.Value;
            RestoreDiscoveredModelsFromCache();
            _hasLoaded = true;
            StatusMessage = draft.Exists
                ? string.Empty
                : UiTextCatalog.Message("NoAIConfigurationYetFetchOrEnterA");
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("LoadingAIConfigurationWasCancelled");
        }
        catch (AiConfigurationException exception)
        {
            ConfigurationError = UiTextCatalog.Join("", UiTextCatalog.FromSafeMessage(exception.Message), UiTextCatalog.Message("RepairTheConfigurationFileSwitchToGeneralSettings"));
            StatusMessage = UiTextCatalog.Message("CouldNotLoadAIConfiguration");
        }
        catch
        {
            ConfigurationError = UiTextCatalog.Message("CouldNotReadAIConfigurationSwitchToGeneral");
            StatusMessage = UiTextCatalog.Message("CouldNotReadAIConfiguration");
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
            StatusMessage = UiTextCatalog.Message("FetchingTheModelList");
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
                ? UiTextCatalog.Message("NoModelsReturnedYouCanEnterAModel")
                : UiTextCatalog.Message("FetchedModels", DiscoveredModels.Count);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("FetchingModelsWasCancelled");
        }
        catch (Exception exception) when (exception is AiConfigurationException or AiProviderConnectionException)
        {
            if (exception is AiConfigurationException) ConfigurationError = exception.Message;
            StatusMessage = exception.Message;
        }
        catch
        {
            StatusMessage = UiTextCatalog.Message("CouldNotFetchTheModelListCurrentConfiguration");
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
            StatusMessage = UiTextCatalog.Message("TestingConnection");
            await _testConfiguration(BuildDraft(), current.Token);
            current.Token.ThrowIfCancellationRequested();
            StatusMessage = UiTextCatalog.Message("ConnectionSuccessful");
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("ConnectionTestCancelled");
        }
        catch (Exception exception) when (exception is AiConfigurationException or AiProviderConnectionException)
        {
            if (exception is AiConfigurationException) ConfigurationError = exception.Message;
            StatusMessage = exception.Message;
        }
        catch
        {
            StatusMessage = UiTextCatalog.Message("ConnectionTestFailedCheckTheEndpointModelAnd");
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
            StatusMessage = UiTextCatalog.Message("SavingConfiguration");
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
                StatusMessage = UiTextCatalog.Message("ApplyingConfiguration");
                var status = await _restart(current.Token);
                current.Token.ThrowIfCancellationRequested();
                ConfigurationError = status.State == WindowsAiRuntimeState.Ready ? string.Empty : status.Message;
                StatusMessage = status.State == WindowsAiRuntimeState.Ready
                    ? UiTextCatalog.Message("AIConfigurationSaved")
                    : UiTextCatalog.Join("", UiTextCatalog.Message("AIConfigurationSavedButNotApplied"), UiTextCatalog.FromSafeMessage(status.Message));
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
                StatusMessage = UiTextCatalog.Message("AIConfigurationSavedApplicationToThisSessionWas");
            }
            catch
            {
                ConfigurationError = UiTextCatalog.Message("AIConfigurationHasNotBeenAppliedRetryLater");
                StatusMessage = UiTextCatalog.Message("AIConfigurationSavedButNotAppliedRetryLater");
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("SavingAIConfigurationWasCancelled");
        }
        catch (AiConfigurationException exception)
        {
            ConfigurationError = exception.Message;
            StatusMessage = exception.Code switch
            {
                "python-invalid" or "python-missing" or "provider-invalid" or "model_authentication" => exception.Message,
                _ => UiTextCatalog.Message("SavingAIConfigurationFailedThisSessionContinuesUsing"),
            };
        }
        catch
        {
            StatusMessage = UiTextCatalog.Message("SavingAIConfigurationFailedThisSessionContinuesUsing");
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
            StatusMessage = UiTextCatalog.Message("ClearingKey");
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
            ConfigurationError = UiTextCatalog.Message("NoKeySavedYetEnterAnAPIKey");
            StatusMessage = status.State == WindowsAiRuntimeState.Disabled
                ? UiTextCatalog.Message("APIKeyRemovedFromWindowsCredentialManagerAI")
                : UiTextCatalog.Message("APIKeyClearedAIStatusWillRefreshOn");
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            StatusMessage = UiTextCatalog.Message("ClearingTheAPIKeyWasCancelledAChange");
        }
        catch
        {
            StatusMessage = UiTextCatalog.Message("CouldNotClearWindowsCredentialsTheAIConfiguration");
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
