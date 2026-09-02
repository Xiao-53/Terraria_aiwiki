using System.Reflection;
using System.Text.RegularExpressions;

namespace Terraria_Wiki.WikiTools.Tests;

public sealed class WikiToolBoundaryTests
{
    [Fact]
    public void AdapterProjectOnlyReferencesApplicationAndPinsTheOfficialTransportPackage()
    {
        var repository = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repository, "src", "Terraria_Wiki.WikiTools");
        var project = File.ReadAllText(Path.Combine(sourceRoot, "Terraria_Wiki.WikiTools.csproj"));

        Assert.Single(Regex.Matches(project, "<ProjectReference "));
        Assert.Contains("../Terraria_Wiki.Application/Terraria_Wiki.Application.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Terraria_Wiki.Core.csproj", project, StringComparison.Ordinal);
        Assert.Contains("ModelContextProtocol.AspNetCore\" Version=\"2.2.0\"", project, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(project, "<PackageReference "));
    }

    [Fact]
    public void AdapterSourceHasNoPersistenceUiPlatformPythonOrGlobalStateDependency()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Terraria_Wiki.WikiTools");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

        foreach (var token in new[]
                 {
                     "SQLite", "DatabaseService", "ContentDbService", "ManagerDbService", "Microsoft.Maui",
                     "Avalonia", "Razor", "WebView", "Windows.", "Python", "Pydantic", "WikiHistory",
                     "WikiFavorite", "WikiAsset", "VectorDatabase", "Embedding", "AddCors", "AllowAnyOrigin",
                 })
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\bApp\.", RegexOptions.CultureInvariant), source);
        Assert.DoesNotContain("Terraria_Wiki.Services", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicRuntimeSurfaceIsLimitedAndConnectionFormattingCannotRevealToken()
    {
        var assembly = typeof(WikiToolServer).Assembly;
        var exported = assembly.GetExportedTypes().Select(type => type.Name).Order().ToArray();

        Assert.Equal(
        [
            nameof(WikiToolConnection),
            nameof(WikiToolConsumerCoordinator),
            nameof(WikiToolServer),
        ], exported);
        var connection = new WikiToolConnection(new Uri("http://127.0.0.1:12345/mcp"), "top-secret-token");
        Assert.Equal("http://127.0.0.1:12345/mcp", connection.ToString());
        Assert.DoesNotContain(connection.BearerToken, connection.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsClientOwnsThePhaseSevenWikiToolRuntimeLifecycleWithoutCoreAccess()
    {
        var repository = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(repository, "src", "Terraria_Wiki.Windows", "Terraria_Wiki.Windows.csproj"));
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(Path.Combine(repository, "src", "Terraria_Wiki.Windows"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.Contains("Terraria_Wiki.WikiTools", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Terraria_Wiki.Core.csproj", project, StringComparison.Ordinal);
        Assert.Contains("WikiToolServer.StartAsync", source, StringComparison.Ordinal);
        Assert.Contains("_wikiTools.DisposeAsync", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Terraria_Wiki.Core.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Terraria_Wiki.Core.slnx.");
    }
}
