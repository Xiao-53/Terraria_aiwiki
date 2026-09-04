using Microsoft.Win32;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed record WebView2RuntimeStatus(bool Available, string? Version, string? Source, string? Error);

internal static class WebView2RuntimeProbe
{
    public static WebView2RuntimeStatus Probe()
    {
        try
        {
            var configured = Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var status = FromExecutable(Path.Combine(configured, "msedgewebview2.exe"), "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
                if (status.Available)
                    return status;
            }

            var programRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var root in programRoots)
            {
                var applicationRoot = Path.Combine(root, "Microsoft", "EdgeWebView", "Application");
                if (!Directory.Exists(applicationRoot))
                    continue;
                foreach (var versionDirectory in Directory.EnumerateDirectories(applicationRoot).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var status = FromExecutable(Path.Combine(versionDirectory, "msedgewebview2.exe"), "EdgeWebView application directory");
                    if (status.Available)
                        return status;
                }
            }

            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var clients = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients");
                if (clients is null)
                    continue;
                foreach (var name in clients.GetSubKeyNames())
                {
                    using var client = clients.OpenSubKey(name);
                    var displayName = client?.GetValue("name") as string;
                    var version = client?.GetValue("pv") as string;
                    if (displayName?.Contains("WebView2 Runtime", StringComparison.OrdinalIgnoreCase) == true
                        && !string.IsNullOrWhiteSpace(version))
                        return new WebView2RuntimeStatus(true, version, $"{hive}/{view} registry", null);
                }
            }

            return new WebView2RuntimeStatus(
                false,
                null,
                null,
                "Microsoft Edge WebView2 Runtime was not found. Install the Evergreen Runtime and retry.");
        }
        catch (Exception exception)
        {
            return new WebView2RuntimeStatus(
                false,
                null,
                null,
                $"WebView2 Runtime detection failed ({exception.GetType().Name}).");
        }
    }

    private static WebView2RuntimeStatus FromExecutable(string path, string source)
    {
        if (!File.Exists(path))
            return new WebView2RuntimeStatus(false, null, null, null);
        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
        return new WebView2RuntimeStatus(true, version, source, null);
    }
}
