using System.Reflection;
using System.Text.RegularExpressions;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Application.Tests;

public sealed class ApplicationBoundaryTests
{
    private static readonly HashSet<Type> ForbiddenPublicTypes =
    [
        typeof(DatabaseService),
        typeof(ManagerDbService),
        typeof(ContentDbService),
        typeof(WikiBook),
        typeof(WikiPage),
        typeof(WikiHistory),
        typeof(WikiFavorite),
        typeof(WikiAsset),
    ];

    [Fact]
    public void PublicSurfaceDoesNotExposePersistenceServicesOrTableModels()
    {
        var assembly = typeof(WikiApplication).Assembly;
        var leaks = new List<string>();
        foreach (var type in assembly.GetExportedTypes())
        {
            CheckType(type, type.FullName ?? type.Name, leaks);
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                    CheckType(parameter.ParameterType, $"{type.Name}.ctor({parameter.Name})", leaks);
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                CheckType(method.ReturnType, $"{type.Name}.{method.Name} return", leaks);
                foreach (var parameter in method.GetParameters())
                    CheckType(parameter.ParameterType, $"{type.Name}.{method.Name}({parameter.Name})", leaks);
            }
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                CheckType(property.PropertyType, $"{type.Name}.{property.Name}", leaks);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                CheckType(field.FieldType, $"{type.Name}.{field.Name}", leaks);
        }

        Assert.Empty(leaks);
    }

    [Fact]
    public void ApplicationDefinesExactlyTheTwoApprovedHostPortsAndFiveUseCaseGroups()
    {
        var assembly = typeof(WikiApplication).Assembly;
        var ports = assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(WikiApplication).Namespace)
            .Select(type => type.Name)
            .Order()
            .ToArray();
        var useCases = assembly.GetExportedTypes()
            .Where(type => type.IsClass && type.Name.EndsWith("UseCases", StringComparison.Ordinal))
            .Select(type => type.Name)
            .Order()
            .ToArray();

        Assert.Equal(
        [
            nameof(IApplicationSettingsStore),
            nameof(IWikiDataConsumerCoordinator),
        ], ports);
        Assert.Equal(
        [
            nameof(ActiveWikiUseCases),
            nameof(ApplicationSettingsUseCases),
            nameof(WikiDataUseCases),
            nameof(WikiLibraryUseCases),
            nameof(WikiQueryUseCases),
        ], useCases);
    }

    [Fact]
    public void ProjectHasOnlyCoreReferenceAndSourceHasNoForbiddenHostDependencies()
    {
        var repository = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repository, "src", "Terraria_Wiki.Application");
        var project = File.ReadAllText(Path.Combine(sourceRoot, "Terraria_Wiki.Application.csproj"));
        Assert.DoesNotContain("PackageReference", project, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(project, "<ProjectReference "));
        Assert.Contains("../Terraria_Wiki.Core/Terraria_Wiki.Core.csproj", project, StringComparison.Ordinal);

        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        foreach (var token in new[]
                 {
                     "Microsoft.Maui",
                     "Avalonia",
                     "Razor",
                     "WebView",
                     "FileSystem.",
                     "SQLite",
                     "#if",
                     "#elif",
                     "VectorDatabase",
                     "Embedding",
                 })
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\bApp\.", RegexOptions.CultureInvariant), source);
        Assert.DoesNotMatch(new Regex(@"\bPreferences\.Default\b", RegexOptions.CultureInvariant), source);
    }

    [Fact]
    public async Task UseCasesRequireExplicitInitialization()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(initialize: false);

        var query = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.Query.ReadAsync("Sword"));
        var library = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.Library.GetPagesAsync(new PageRequest(0, 20)));
        var data = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.Data.GetStatisticsAsync());

        Assert.Equal("not-initialized", query.Code);
        Assert.Equal("not-initialized", library.Code);
        Assert.Equal("not-initialized", data.Code);
    }

    private static void CheckType(Type type, string location, ICollection<string> leaks)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            CheckType(type.GetElementType()!, location, leaks);
            return;
        }
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                CheckType(argument, location, leaks);
        }
        if (ForbiddenPublicTypes.Contains(type)
            || type.FullName?.StartsWith("SQLite.", StringComparison.Ordinal) == true)
            leaks.Add($"{location}: {type.FullName}");
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
