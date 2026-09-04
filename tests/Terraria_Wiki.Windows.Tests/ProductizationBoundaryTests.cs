using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Reader;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ProductizationBoundaryTests
{
    [Fact]
    public void VelopackHooksRunBeforeAvaloniaStarts()
    {
        var root = FindSourceRoot();
        var program = File.ReadAllText(Path.Combine(root, "Program.cs"));
        var project = File.ReadAllText(Path.Combine(root, "Terraria_Wiki.Windows.csproj"));

        Assert.Contains("Velopack\" Version=\"1.2.0", project, StringComparison.Ordinal);
        var hook = program.IndexOf("VelopackApp.Build().Run();", StringComparison.Ordinal);
        var avalonia = program.IndexOf(
            "BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);",
            StringComparison.Ordinal);

        Assert.True(hook >= 0, "The Velopack hook dispatcher must be present.");
        Assert.True(avalonia > hook, "Velopack hooks must run before Avalonia starts.");
    }

    [Fact]
    public void WebView2RecoveryUsesOnlyTheMicrosoftOfficialDownloadPage()
    {
        var root = FindSourceRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "MainWindow.cs"));
        var probe = File.ReadAllText(Path.Combine(root, "Hosting", "WebView2RuntimeProbe.cs"));

        Assert.Contains(
            "https://developer.microsoft.com/microsoft-edge/webview2/",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductDiagnosticsAreBoundedAndContainNoArbitraryUserPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "terraria-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var diagnostics = new ProductDiagnostics(root, maxLogBytes: 512);
            for (var index = 0; index < 20; index++)
                diagnostics.Write(ProductDiagnosticEvent.StartupFailed, "webview2-missing/secret value");

            Assert.NotNull(diagnostics.LogPath);
            Assert.NotNull(diagnostics.PreviousLogPath);
            Assert.True(File.Exists(diagnostics.LogPath));
            Assert.True(File.Exists(diagnostics.PreviousLogPath));
            Assert.True(new FileInfo(diagnostics.LogPath!).Length <= 512);
            Assert.True(new FileInfo(diagnostics.PreviousLogPath!).Length <= 512);
            var content = File.ReadAllText(diagnostics.LogPath!)
                + File.ReadAllText(diagnostics.PreviousLogPath!);
            Assert.DoesNotContain("secret value", content, StringComparison.Ordinal);
            Assert.DoesNotContain("/", content, StringComparison.Ordinal);
            Assert.Contains("webview2-missingsecretvalue", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WebView2UserDataIsOutsideTheApplicationPayload()
    {
        var userData = ReaderView.GetDefaultUserDataFolder();

        Assert.True(Path.IsPathFullyQualified(userData));
        Assert.Contains("Terraria AI Wiki", userData, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            userData.StartsWith(
                Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase),
            "WebView2 mutable data must not be stored under the application payload.");

        var reader = File.ReadAllText(Path.Combine(
            FindSourceRoot(),
            "Presentation",
            "Reader",
            "ReaderView.axaml.cs"));
        Assert.Contains("EnvironmentRequested +=", reader, StringComparison.Ordinal);
        Assert.Contains("webView2.UserDataFolder =", reader, StringComparison.Ordinal);
    }

    private static string FindSourceRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(source)!,
            "..",
            "..",
            "src",
            "Terraria_Wiki.Windows"));
}
