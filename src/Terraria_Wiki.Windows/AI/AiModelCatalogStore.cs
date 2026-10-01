using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terraria_Wiki.Windows.AI;

internal sealed class AiModelCatalogStore
{
    private const int CurrentVersion = 1;
    private const int MaximumCatalogBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        WriteIndented = true,
    };

    public AiModelCatalogStore(string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(configurationPath) || !Path.IsPathFullyQualified(configurationPath))
            throw new AiConfigurationException("ai-model-cache-path-invalid", "AI 模型列表缓存路径无效。");
        var fullPath = Path.GetFullPath(configurationPath);
        CatalogPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "ai-models-cache.json");
    }

    public string CatalogPath { get; }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(CatalogPath))
            return EmptyCatalog();
        try
        {
            var information = new FileInfo(CatalogPath);
            if (information.Length <= 0 || information.Length > MaximumCatalogBytes)
                throw new JsonException();
            var payload = await File.ReadAllBytesAsync(CatalogPath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<CatalogFile>(payload, JsonOptions);
            if (document is null || document.Version != CurrentVersion || document.Services is null)
                throw new JsonException();
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var service in document.Services)
            {
                if (service.BaseUrl is null || service.Api is null || service.Models is null)
                    throw new JsonException();
                var provider = new AiProviderConfiguration(service.BaseUrl, "cache-validation", service.Api, null);
                AiRuntimeConfiguration.ValidateProvider(provider);
                result[ServiceKey(provider)] = NormalizeModels(service.Models);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or AiConfigurationException)
        {
            throw new AiConfigurationException("ai-model-cache-invalid", "已保存的 AI 模型列表无法读取。", exception);
        }
    }

    public async Task SaveAsync(
        AiProviderConfiguration provider,
        IReadOnlyList<string> models,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(models);
        AiRuntimeConfiguration.ValidateProvider(provider with { Model = "cache-validation" });
        Dictionary<string, CatalogService> services;
        try
        {
            var existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
            services = existing.ToDictionary(
                pair => pair.Key,
                pair => FromEntry(pair.Key, pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (AiConfigurationException exception) when (exception.Code == "ai-model-cache-invalid")
        {
            services = new Dictionary<string, CatalogService>(StringComparer.OrdinalIgnoreCase);
        }

        services[ServiceKey(provider)] = new CatalogService
        {
            BaseUrl = provider.BaseUrl.TrimEnd('/'),
            Api = provider.Api,
            Models = NormalizeModels(models).ToArray(),
        };
        var document = new CatalogFile
        {
            Version = CurrentVersion,
            Services = services.Values
                .OrderBy(service => service.BaseUrl, StringComparer.OrdinalIgnoreCase)
                .ThenBy(service => service.Api, StringComparer.Ordinal)
                .ToArray(),
        };
        var directory = Path.GetDirectoryName(CatalogPath)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(CatalogPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (payload.Length > MaximumCatalogBytes)
                throw new AiConfigurationException("ai-model-cache-write-failed", "AI 模型列表过大，无法保存。");
            await File.WriteAllBytesAsync(temporaryPath, payload, cancellationToken).ConfigureAwait(false);
            if (File.Exists(CatalogPath))
                File.Replace(temporaryPath, CatalogPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, CatalogPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AiConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AiConfigurationException("ai-model-cache-write-failed", "AI 模型列表无法保存。", exception);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    internal static string ServiceKey(AiProviderConfiguration provider) =>
        provider.Api + "\n" + provider.BaseUrl.TrimEnd('/');

    private static IReadOnlyList<string> NormalizeModels(IEnumerable<string> models) =>
        models
            .Where(model => !string.IsNullOrWhiteSpace(model)
                && model == model.Trim()
                && model.Length <= 256
                && !model.Any(char.IsControl))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(model => model, StringComparer.Ordinal)
            .ToArray();

    private static CatalogService FromEntry(string key, IReadOnlyList<string> models)
    {
        var separator = key.IndexOf('\n');
        if (separator <= 0 || separator == key.Length - 1)
            throw new AiConfigurationException("ai-model-cache-invalid", "已保存的 AI 模型列表无法读取。");
        return new CatalogService
        {
            Api = key[..separator],
            BaseUrl = key[(separator + 1)..],
            Models = models.ToArray(),
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyCatalog() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

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

    private sealed record CatalogFile
    {
        public int Version { get; init; }
        public CatalogService[]? Services { get; init; }
    }

    private sealed record CatalogService
    {
        public string? BaseUrl { get; init; }
        public string? Api { get; init; }
        public string[]? Models { get; init; }
    }
}
