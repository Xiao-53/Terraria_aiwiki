using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terraria_Wiki.Windows.AI;

internal sealed record AiRuntimeConfigurationDraft(
    string PythonExecutable,
    AiProviderConfiguration Provider,
    string ApiKey,
    string ConfigurationPath,
    bool Exists,
    bool HasStoredCredential)
{
    public override string ToString() =>
        $"AiRuntimeConfigurationDraft {{ PythonExecutable = {PythonExecutable}, Provider = {Provider}, " +
        $"ApiKey = [REDACTED], ConfigurationPath = {ConfigurationPath}, Exists = {Exists}, " +
        $"HasStoredCredential = {HasStoredCredential} }}";
}

internal sealed class AiRuntimeConfigurationStore
{
    internal const string DeepSeekBaseUrl = "https://api.deepseek.com";
    internal const string DeepSeekFlashModel = "deepseek-v4-flash";
    internal const string DeepSeekProModel = "deepseek-v4-pro";
    internal const int CurrentVersion = 3;
    private const int MaximumConfigurationBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        WriteIndented = true,
    };

    private readonly IAiCredentialStore _credentialStore;
    private readonly Func<string> _pythonExecutable;

    public AiRuntimeConfigurationStore(
        string configurationPath,
        IAiCredentialStore? credentialStore = null,
        Func<string>? pythonExecutable = null)
    {
        if (string.IsNullOrWhiteSpace(configurationPath) || !Path.IsPathFullyQualified(configurationPath))
            throw new AiConfigurationException("ai-config-path-invalid", "AI 配置路径无效。");
        ConfigurationPath = Path.GetFullPath(configurationPath);
        _credentialStore = credentialStore ?? new WindowsAiCredentialStore();
        _pythonExecutable = pythonExecutable ?? AiRuntimePaths.BundledPythonExecutable;
    }

    public string ConfigurationPath { get; }

    public async Task<AiRuntimeConfigurationDraft> LoadDraftAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigurationPath))
        {
            return new AiRuntimeConfigurationDraft(
                _pythonExecutable(),
                DefaultProvider(),
                string.Empty,
                ConfigurationPath,
                Exists: false,
                HasStoredCredential: HasStoredCredential());
        }

        var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (document.Version == 2)
            document = await MigrateVersionTwoAsync(document, cancellationToken).ConfigureAwait(false);
        var provider = ValidateProvider(document.Provider);
        return new AiRuntimeConfigurationDraft(
            _pythonExecutable(),
            provider,
            string.Empty,
            ConfigurationPath,
            Exists: true,
            HasStoredCredential: HasStoredCredential());
    }

    public async Task<AiRuntimeConfiguration> LoadRequiredAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigurationPath))
            throw new AiConfigurationException("ai-config-missing", "AI 配置不存在。");
        var draft = await LoadDraftAsync(cancellationToken).ConfigureAwait(false);
        var apiKey = ReadRequiredCredential();
        return AiRuntimeConfiguration.Create(
            ConfigurationPath,
            draft.PythonExecutable,
            draft.Provider,
            apiKey);
    }

    public async Task<AiRuntimeConfiguration> SaveAsync(
        AiRuntimeConfigurationDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var provider = ValidateProvider(draft.Provider);
        var previousCredential = _credentialStore.Read();
        var replacementCredential = string.IsNullOrWhiteSpace(draft.ApiKey)
            ? previousCredential
            : AiRuntimeConfiguration.ValidateApiKey(draft.ApiKey);
        if (string.IsNullOrWhiteSpace(replacementCredential))
        {
            throw new AiConfigurationException(
                "model_authentication",
                "请先输入 AI API Key；已有密钥不会显示在界面中。");
        }

        var credentialChanged = !string.Equals(
            previousCredential,
            replacementCredential,
            StringComparison.Ordinal);
        try
        {
            if (credentialChanged)
                _credentialStore.Write(replacementCredential);
            await WriteVersionThreeAsync(provider, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (credentialChanged)
                RestoreCredential(previousCredential);
            throw;
        }

        return AiRuntimeConfiguration.Create(
            ConfigurationPath,
            _pythonExecutable(),
            provider,
            replacementCredential);
    }

    public void DeleteCredential() => _credentialStore.Delete();

    private async Task<LoadedConfiguration> MigrateVersionTwoAsync(
        LoadedConfiguration document,
        CancellationToken cancellationToken)
    {
        var provider = ValidateProvider(document.Provider);
        var apiKey = AiRuntimeConfiguration.ValidateApiKey(document.ApiKey);
        var previousCredential = _credentialStore.Read();
        try
        {
            _credentialStore.Write(apiKey);
            await WriteVersionThreeAsync(provider, cancellationToken).ConfigureAwait(false);
            return new LoadedConfiguration(CurrentVersion, provider, null);
        }
        catch
        {
            RestoreCredential(previousCredential);
            throw;
        }
    }

    private bool HasStoredCredential()
    {
        var value = _credentialStore.Read();
        return !string.IsNullOrWhiteSpace(value);
    }

    private string ReadRequiredCredential()
    {
        var value = _credentialStore.Read();
        return AiRuntimeConfiguration.ValidateApiKey(value);
    }

    private void RestoreCredential(string? previousCredential)
    {
        try
        {
            if (previousCredential is null)
                _credentialStore.Delete();
            else
                _credentialStore.Write(previousCredential);
        }
        catch
        {
            throw new AiConfigurationException(
                "ai-credential-rollback-failed",
                "AI 配置保存失败，且 Windows 凭据无法恢复；请重新输入 API Key。");
        }
    }

    private async Task<LoadedConfiguration> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        try
        {
            var information = new FileInfo(ConfigurationPath);
            if (information.Length <= 0 || information.Length > MaximumConfigurationBytes)
                throw new AiConfigurationException("ai-config-invalid", "AI 配置大小无效。");
            var payload = await File.ReadAllBytesAsync(ConfigurationPath, cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
            if (!json.RootElement.TryGetProperty("version", out var versionElement)
                || !versionElement.TryGetInt32(out var version))
            {
                throw new AiConfigurationException("ai-config-invalid", "AI 配置版本无效。");
            }

            return version switch
            {
                2 => FromVersionTwo(JsonSerializer.Deserialize<VersionTwoFile>(payload, JsonOptions)),
                CurrentVersion => FromVersionThree(JsonSerializer.Deserialize<VersionThreeFile>(payload, JsonOptions)),
                _ => throw new AiConfigurationException("ai-config-invalid", "AI 配置版本无效。"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new AiConfigurationException("ai-config-invalid", "AI 配置无法读取。", exception);
        }
    }

    private async Task WriteVersionThreeAsync(
        AiProviderConfiguration provider,
        CancellationToken cancellationToken)
    {
        var document = new VersionThreeFile
        {
            Version = CurrentVersion,
            Provider = ToFile(provider),
        };
        var directory = Path.GetDirectoryName(ConfigurationPath)!;
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(ConfigurationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(ConfigurationPath))
                File.Replace(temporaryPath, ConfigurationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, ConfigurationPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AiConfigurationException("ai-config-write-failed", "AI 配置无法保存。", exception);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static LoadedConfiguration FromVersionTwo(VersionTwoFile? document)
    {
        if (document is null || document.Version != 2 || document.Provider is null)
            throw new AiConfigurationException("ai-config-invalid", "AI 配置版本无效。");
        return new LoadedConfiguration(2, FromFile(document.Provider), document.ApiKey);
    }

    private static LoadedConfiguration FromVersionThree(VersionThreeFile? document)
    {
        if (document is null || document.Version != CurrentVersion || document.Provider is null)
            throw new AiConfigurationException("ai-config-invalid", "AI 配置版本无效。");
        return new LoadedConfiguration(CurrentVersion, FromFile(document.Provider), null);
    }

    private static AiProviderConfiguration ValidateProvider(AiProviderConfiguration? provider)
    {
        if (provider is null)
            throw new AiConfigurationException("ai-config-invalid", "AI 配置缺少 provider。");
        return AiRuntimeConfiguration.ValidateProvider(provider);
    }

    private static AiProviderConfiguration DefaultProvider() =>
        new(DeepSeekBaseUrl, DeepSeekFlashModel, "responses", null);

    private static AiProviderConfiguration FromFile(AiProviderConfigurationFile document) =>
        new(
            document.BaseUrl ?? string.Empty,
            document.Model ?? string.Empty,
            document.Api ?? string.Empty,
            document.ReasoningEffort);

    private static AiProviderConfigurationFile ToFile(AiProviderConfiguration provider) =>
        new()
        {
            BaseUrl = provider.BaseUrl,
            Model = provider.Model,
            Api = provider.Api,
            ReasoningEffort = provider.ReasoningEffort,
        };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record LoadedConfiguration(
        int Version,
        AiProviderConfiguration Provider,
        string? ApiKey);

    private sealed record VersionTwoFile
    {
        public int Version { get; init; }
        public string? PythonExecutable { get; init; }
        public string? ApiKey { get; init; }
        public AiProviderConfigurationFile? Provider { get; init; }
    }

    private sealed record VersionThreeFile
    {
        public int Version { get; init; }
        public AiProviderConfigurationFile? Provider { get; init; }
    }

    private sealed record AiProviderConfigurationFile
    {
        public string? BaseUrl { get; init; }
        public string? Model { get; init; }
        public string? Api { get; init; }
        public string? ReasoningEffort { get; init; }
    }
}
