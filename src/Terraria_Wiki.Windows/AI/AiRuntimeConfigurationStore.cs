using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terraria_Wiki.Windows.AI;

internal sealed record AiRuntimeConfigurationDraft(
    string PythonExecutable,
    AiProviderConfiguration Provider,
    string ApiKey,
    string ConfigurationPath,
    bool Exists)
{
    public override string ToString() =>
        $"AiRuntimeConfigurationDraft {{ PythonExecutable = {PythonExecutable}, Provider = {Provider}, " +
        $"ApiKey = [REDACTED], ConfigurationPath = {ConfigurationPath}, Exists = {Exists} }}";
}

internal sealed class AiRuntimeConfigurationStore
{
    internal const string DeepSeekBaseUrl = "https://api.deepseek.com";
    internal const string DeepSeekFlashModel = "deepseek-v4-flash";
    internal const string DeepSeekProModel = "deepseek-v4-pro";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        WriteIndented = true,
    };

    public AiRuntimeConfigurationStore(string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(configurationPath) || !Path.IsPathFullyQualified(configurationPath))
            throw new AiConfigurationException("ai-config-path-invalid", "AI 配置路径无效。");
        ConfigurationPath = Path.GetFullPath(configurationPath);
    }

    public string ConfigurationPath { get; }

    public async Task<AiRuntimeConfigurationDraft> LoadDraftAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigurationPath))
        {
            return new AiRuntimeConfigurationDraft(
                string.Empty,
                new AiProviderConfiguration(DeepSeekBaseUrl, DeepSeekFlashModel, "responses", null),
                string.Empty,
                ConfigurationPath,
                Exists: false);
        }

        var document = await ReadDocumentAsync(cancellationToken).ConfigureAwait(false);
        if (document.Provider is null)
            throw new AiConfigurationException("ai-config-invalid", "AI 配置缺少 provider。");
        return new AiRuntimeConfigurationDraft(
            document.PythonExecutable ?? string.Empty,
            new AiProviderConfiguration(
                document.Provider.BaseUrl ?? string.Empty,
                document.Provider.Model ?? string.Empty,
                document.Provider.Api ?? string.Empty,
                document.Provider.ReasoningEffort),
            document.ApiKey ?? string.Empty,
            ConfigurationPath,
            Exists: true);
    }

    public async Task<AiRuntimeConfiguration> LoadRequiredAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ConfigurationPath))
            throw new AiConfigurationException("ai-config-missing", "AI 配置不存在。");
        var draft = await LoadDraftAsync(cancellationToken).ConfigureAwait(false);
        return AiRuntimeConfiguration.Create(
            ConfigurationPath,
            draft.PythonExecutable,
            draft.Provider,
            draft.ApiKey);
    }

    public async Task<AiRuntimeConfiguration> SaveAsync(
        AiRuntimeConfigurationDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var configuration = AiRuntimeConfiguration.Create(
            ConfigurationPath,
            draft.PythonExecutable,
            draft.Provider,
            draft.ApiKey);
        var document = new AiRuntimeConfigurationFile
        {
            Version = 2,
            PythonExecutable = configuration.PythonExecutable,
            ApiKey = configuration.ApiKey,
            Provider = new AiProviderConfigurationFile
            {
                BaseUrl = configuration.Provider.BaseUrl,
                Model = configuration.Provider.Model,
                Api = configuration.Provider.Api,
                ReasoningEffort = configuration.Provider.ReasoningEffort,
            },
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
            return configuration;
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

    private async Task<AiRuntimeConfigurationFile> ReadDocumentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                ConfigurationPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<AiRuntimeConfigurationFile>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (document is null || document.Version != 2)
                throw new AiConfigurationException("ai-config-invalid", "AI 配置版本无效。");
            return document;
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

    private sealed record AiRuntimeConfigurationFile
    {
        public int Version { get; init; }
        public string? PythonExecutable { get; init; }
        public string? ApiKey { get; init; }
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
