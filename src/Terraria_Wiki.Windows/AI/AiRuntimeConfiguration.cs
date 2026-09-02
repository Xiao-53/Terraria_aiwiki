using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terraria_Wiki.Windows.AI;

internal sealed record AiProviderConfiguration(
    string BaseUrl,
    string Model,
    string Api,
    string? ReasoningEffort);

internal sealed record AiRuntimeConfiguration(
    string PythonExecutable,
    AiProviderConfiguration Provider,
    string ApiKey,
    string ConfigurationPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    };

    public override string ToString() =>
        $"AiRuntimeConfiguration {{ PythonExecutable = {PythonExecutable}, Provider = {Provider}, " +
        $"ApiKey = [REDACTED], ConfigurationPath = {ConfigurationPath} }}";

    public static async Task<AiRuntimeConfiguration> LoadAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var path = ParseConfigurationPath(arguments) ?? DefaultConfigurationPath();
        if (!File.Exists(path))
            throw new AiConfigurationException("ai-config-missing", "AI 配置不存在。");

        AiRuntimeConfigurationFile? document;
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            document = await JsonSerializer.DeserializeAsync<AiRuntimeConfigurationFile>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new AiConfigurationException("ai-config-invalid", "AI 配置无法读取。", exception);
        }

        if (document is null || document.Version != 2)
            throw new AiConfigurationException("ai-config-invalid", "AI 配置版本无效。");

        var python = ValidatePythonExecutable(document.PythonExecutable);
        var provider = ValidateProvider(document.Provider);
        var apiKey = ValidateApiKey(document.ApiKey);
        return new AiRuntimeConfiguration(python, provider, apiKey, path);
    }

    internal static string? ParseConfigurationPath(IReadOnlyList<string> arguments)
    {
        string? value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (!string.Equals(arguments[index], "--ai-config", StringComparison.Ordinal))
                continue;
            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                throw new AiConfigurationException("ai-config-argument-missing", "--ai-config 需要绝对路径。");
            if (value is not null)
                throw new AiConfigurationException("ai-config-argument-duplicate", "--ai-config 只能指定一次。");
            if (!Path.IsPathFullyQualified(arguments[index]))
                throw new AiConfigurationException("ai-config-argument-invalid", "--ai-config 需要绝对路径。");
            value = Path.GetFullPath(arguments[index]);
        }
        return value;
    }

    internal static string DefaultConfigurationPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Terraria AI Wiki",
        "ai-runtime.json");

    private static string ValidatePythonExecutable(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || !Path.IsPathFullyQualified(value))
        {
            throw new AiConfigurationException("python-invalid", "Python 路径必须是绝对路径。");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AiConfigurationException("python-invalid", "Python 路径无效。");
        }
        if (!string.Equals(Path.GetFileName(fullPath), "python.exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(fullPath))
        {
            throw new AiConfigurationException("python-missing", "未找到配置的 python.exe。");
        }
        return fullPath;
    }

    private static AiProviderConfiguration ValidateProvider(AiProviderConfigurationFile? provider)
    {
        if (provider is null
            || string.IsNullOrWhiteSpace(provider.BaseUrl)
            || provider.BaseUrl != provider.BaseUrl.Trim()
            || !Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !IsSafeProviderUri(uri))
        {
            throw new AiConfigurationException("provider-invalid", "模型服务地址无效。");
        }
        if (string.IsNullOrWhiteSpace(provider.Model)
            || provider.Model != provider.Model.Trim()
            || provider.Model.Length > 256
            || provider.Model.Any(character => char.IsControl(character)))
        {
            throw new AiConfigurationException("provider-invalid", "模型名称无效。");
        }
        if (provider.Api is not ("responses" or "chat_completions"))
            throw new AiConfigurationException("provider-invalid", "模型 API 类型无效。");
        if (provider.ReasoningEffort is not null
            && provider.ReasoningEffort is not ("none" or "low" or "medium" or "high" or "xhigh" or "max"))
        {
            throw new AiConfigurationException("provider-invalid", "推理强度无效。");
        }

        return new AiProviderConfiguration(
            provider.BaseUrl,
            provider.Model,
            provider.Api,
            provider.ReasoningEffort);
    }

    private static string ValidateApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 4096
            || value.Any(char.IsControl))
        {
            throw new AiConfigurationException("model_authentication", "AI API Key 无效。");
        }
        return value;
    }

    private static bool IsSafeProviderUri(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;
        return uri.Scheme == Uri.UriSchemeHttp
            && IPAddress.TryParse(uri.Host, out var address)
            && IPAddress.IsLoopback(address);
    }

    private sealed record AiRuntimeConfigurationFile
    {
        public required int Version { get; init; }
        public required string PythonExecutable { get; init; }
        public required string ApiKey { get; init; }
        public required AiProviderConfigurationFile Provider { get; init; }
    }

    private sealed record AiProviderConfigurationFile
    {
        public required string BaseUrl { get; init; }
        public required string Model { get; init; }
        public required string Api { get; init; }
        public string? ReasoningEffort { get; init; }
    }
}

internal sealed class AiConfigurationException : IOException
{
    public AiConfigurationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;
    public string Code { get; }
}
