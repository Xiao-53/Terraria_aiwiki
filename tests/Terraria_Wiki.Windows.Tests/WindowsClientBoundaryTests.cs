using System.Reflection;
using Terraria_Wiki.Application;

namespace Terraria_Wiki.Windows.Tests;

public sealed class WindowsClientBoundaryTests
{
    [Fact]
    public void ProjectReferencesApplicationAndPinnedAvaloniaWithoutMauiRazorOrSqlitePackages()
    {
        var root = FindSourceRoot();
        var project = File.ReadAllText(System.IO.Path.Combine(root, "Terraria_Wiki.Windows.csproj"));

        Assert.Contains("Terraria_Wiki.Application.csproj", project, StringComparison.Ordinal);
        Assert.Contains("Terraria_Wiki.WikiTools.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Terraria_Wiki.Core.csproj", project, StringComparison.Ordinal);
        Assert.Contains("Avalonia\" Version=\"12.1.1", project, StringComparison.Ordinal);
        Assert.Contains("Avalonia.Controls.WebView\" Version=\"12.0.1", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Maui", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Razor", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductProjectsAndBuildTargetsDoNotDependOnReferenceRepository()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectFiles = Directory.GetFiles(
                System.IO.Path.Combine(repositoryRoot, "src"),
                "*.csproj",
                SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(
                System.IO.Path.Combine(repositoryRoot, "tests"),
                "*.csproj",
                SearchOption.AllDirectories))
            .Append(System.IO.Path.Combine(repositoryRoot, "Terraria_Wiki.Core.slnx"));
        var buildDefinition = string.Join("\n", projectFiles.Select(File.ReadAllText));
        var normalizedBuildDefinition = buildDefinition.Replace('\\', '/');

        Assert.DoesNotContain("terraria-wiki-offline", buildDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("../../Terraria_Wiki", normalizedBuildDefinition, StringComparison.Ordinal);
        Assert.DoesNotContain(@"..\..\Terraria_Wiki", buildDefinition, StringComparison.Ordinal);
        Assert.Contains("assets/windows/Viewer/Web", normalizedBuildDefinition, StringComparison.Ordinal);
        Assert.Contains("assets/windows/Viewer/_common/iframe-bridge.js", normalizedBuildDefinition, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductSourceAndTestsDoNotReadLegacyProjectAssets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var boundaryTest = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(repositoryRoot, "tests", "Terraria_Wiki.Windows.Tests", "WindowsClientBoundaryTests.cs"));
        var productFiles = new[] { "src", "tests", "agent" }
            .SelectMany(directory => Directory.GetFiles(
                System.IO.Path.Combine(repositoryRoot, directory),
                "*",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(System.IO.Path.GetFullPath(path), boundaryTest, StringComparison.OrdinalIgnoreCase))
            .Where(path => new[] { ".cs", ".csproj", ".props", ".targets", ".py", ".toml" }
                .Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

        foreach (var file in productFiles)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("terraria-wiki-offline", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("\\\"Terraria_Wiki\\\"\\s*,\\s*\\\"Resources\\\"", content);
            Assert.DoesNotMatch("\\\"Terraria_Wiki\\\"\\s*,\\s*\\\"wwwroot\\\"", content);
        }
    }

    [Fact]
    public void PresentationAndViewerDoNotAccessPersistenceOrGlobalAppState()
    {
        var root = FindSourceRoot();
        var files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("using SQLite", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SQLiteAsyncConnection", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SQLiteConnection", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DatabaseService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ContentDbService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ManagerDbService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("App.App", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Maui", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WindowsAssemblyAddsNoApplicationPortsOrPersistentPublicModels()
    {
        var assembly = typeof(Terraria_Wiki.Windows.App).Assembly;
        var publicTypes = assembly.GetExportedTypes();

        Assert.DoesNotContain(publicTypes, type => type.IsInterface && typeof(IApplicationSettingsStore).IsAssignableFrom(type));
        Assert.DoesNotContain(publicTypes, type => type.Namespace?.Contains("Models", StringComparison.Ordinal) == true);
    }

    private static string FindSourceRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "..", "..", "src", "Terraria_Wiki.Windows"));

    private static string FindRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "..", ".."));
}
