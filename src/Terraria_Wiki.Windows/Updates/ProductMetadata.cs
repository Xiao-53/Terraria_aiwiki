using System.Reflection;

namespace Terraria_Wiki.Windows.Updates;

internal static class ProductMetadata
{
    internal const string StableChannel = "win-x64-stable";

    internal static string Version
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(ProductMetadata).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return string.IsNullOrWhiteSpace(informational)
                ? assembly.GetName().Version?.ToString(3) ?? "0.0.0"
                : informational.Split('+', 2)[0];
        }
    }

    internal static string? RepositoryUrl
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(ProductMetadata).Assembly;
            var value = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "RepositoryUrl")?
                .Value;
            return IsGitHubRepositoryUrl(value) ? value!.TrimEnd('/') : null;
        }
    }

    internal static Uri? ReleasesUri => RepositoryUrl is { } repository
        ? new Uri(repository + "/releases", UriKind.Absolute)
        : null;

    internal static bool IsGitHubRepositoryUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 && segments.All(segment =>
            segment.Length <= 100
            && segment.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.'));
    }
}
