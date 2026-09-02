using System.Net;

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
    public override string ToString() =>
        $"AiRuntimeConfiguration {{ PythonExecutable = {PythonExecutable}, Provider = {Provider}, " +
        $"ApiKey = [REDACTED], ConfigurationPath = {ConfigurationPath} }}";

    public static async Task<AiRuntimeConfiguration> LoadAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var path = ParseConfigurationPath(arguments) ?? DefaultConfigurationPath();
        return await new AiRuntimeConfigurationStore(path)
            .LoadRequiredAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    internal static AiRuntimeConfiguration Create(
        string configurationPath,
        string pythonExecutable,
        AiProviderConfiguration provider,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(configurationPath) || !Path.IsPathFullyQualified(configurationPath))
            throw new AiConfigurationException("ai-config-path-invalid", "AI 配置路径无效。");
        return new AiRuntimeConfiguration(
            ValidatePythonExecutable(pythonExecutable),
            ValidateProvider(provider),
            ValidateApiKey(apiKey),
            Path.GetFullPath(configurationPath));
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

    private static AiProviderConfiguration ValidateProvider(AiProviderConfiguration provider)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl)
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
}

internal sealed class AiConfigurationException : IOException
{
    public AiConfigurationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;
    public string Code { get; }
}
