using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.Tests;

public sealed class DataRootResolverTests
{
    [Fact]
    public async Task ExplicitArgumentWinsAndIsSaved()
    {
        using var temp = new TestDirectory();
        var explicitRoot = temp.CreateDataRoot("explicit");
        var savedRoot = temp.CreateDataRoot("saved");
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        await store.SaveDataRootAsync(savedRoot);
        var resolver = new DataRootResolver(store);

        var result = await resolver.ResolveAsync(["--data-root", explicitRoot]);

        Assert.Equal(DataRootSource.CommandLine, result.Source);
        Assert.Equal(explicitRoot, result.Path);
        Assert.Equal(explicitRoot, await store.LoadDataRootAsync());
    }

    [Fact]
    public async Task ValidSavedRootWinsWithoutPicker()
    {
        using var temp = new TestDirectory();
        var savedRoot = temp.CreateDataRoot();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        await store.SaveDataRootAsync(savedRoot);

        var result = await new DataRootResolver(store).ResolveAsync([]);

        Assert.Equal(DataRootSource.SavedSettings, result.Source);
        Assert.Equal(savedRoot, result.Path);
    }

    [Fact]
    public async Task InvalidSavedRootRequiresExplicitRecoveryAndIsNotOverwritten()
    {
        using var temp = new TestDirectory();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        var invalidSavedRoot = System.IO.Path.Combine(temp.Path, "missing");
        await store.SaveDataRootAsync(invalidSavedRoot);

        var error = await Assert.ThrowsAsync<DataRootResolutionException>(() =>
            new DataRootResolver(store).ResolveAsync([]));

        Assert.Equal("data-root-required", error.Code);
        Assert.Equal(invalidSavedRoot, await store.LoadDataRootAsync());
    }

    [Fact]
    public async Task FirstStartupCreatesAndPersistsManagedDefaultDataRoot()
    {
        using var temp = new TestDirectory();
        var settingsDirectory = System.IO.Path.Combine(temp.Path, "settings");
        var store = new HostSettingsStore(System.IO.Path.Combine(settingsDirectory, "host-settings.json"));

        var result = await new DataRootResolver(store).ResolveAsync([]);

        var expected = System.IO.Path.Combine(settingsDirectory, "WikiData");
        Assert.Equal(DataRootSource.ManagedDefault, result.Source);
        Assert.Equal(expected, result.Path);
        Assert.True(Directory.Exists(expected));
        Assert.Equal(expected, await store.LoadDataRootAsync());
    }

    [Fact]
    public async Task ManagedDefaultIsInitializedByExistingApplicationCore()
    {
        using var temp = new TestDirectory();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "settings", "host-settings.json"));
        var resolution = await new DataRootResolver(store).ResolveAsync([]);

        await using var application = new WikiApplication(
            resolution.Path,
            store,
            new NoopConsumerCoordinator(),
            new RejectingWikiTransport());
        var active = await application.ActiveWiki.InitializeAsync();

        Assert.Equal(1, active.Id);
        Assert.True(File.Exists(System.IO.Path.Combine(resolution.Path, "Manager.db")));
        Assert.True(File.Exists(System.IO.Path.Combine(resolution.Path, "Terraria_Wiki_zh", "data.db")));
    }

    [Fact]
    public async Task ExplicitEmptyFolderIsAcceptedAsANewDataRoot()
    {
        using var temp = new TestDirectory();
        var emptyRoot = System.IO.Path.Combine(temp.Path, "new-data");
        Directory.CreateDirectory(emptyRoot);
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "settings", "host-settings.json"));

        var result = await new DataRootResolver(store).ResolveAsync(["--data-root", emptyRoot]);

        Assert.Equal(DataRootSource.CommandLine, result.Source);
        Assert.Equal(emptyRoot, result.Path);
        Assert.Equal(emptyRoot, await store.LoadDataRootAsync());
    }

    [Fact]
    public void NonEmptyFolderWithoutManagerDatabaseIsRejectedForInitialization()
    {
        using var temp = new TestDirectory();
        var root = System.IO.Path.Combine(temp.Path, "not-wiki-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(System.IO.Path.Combine(root, "unrelated.txt"), "keep");

        var valid = DataRootResolver.TryPrepareForInitialization(root, out _, out var error);

        Assert.False(valid);
        Assert.Contains("Manager.db", error, StringComparison.Ordinal);
        Assert.True(File.Exists(System.IO.Path.Combine(root, "unrelated.txt")));
    }

    [Fact]
    public async Task ExplicitInvalidRootDoesNotSilentlyFallBack()
    {
        using var temp = new TestDirectory();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));

        var error = await Assert.ThrowsAsync<DataRootResolutionException>(() =>
            new DataRootResolver(store).ResolveAsync(["--data-root=relative"]));

        Assert.Equal("invalid-explicit-data-root", error.Code);
    }

    [Fact]
    public void MissingAndDuplicateArgumentsAreRejected()
    {
        var missing = Assert.Throws<DataRootResolutionException>(() => DataRootResolver.ParseExplicitPath(["--data-root"]));
        var duplicate = Assert.Throws<DataRootResolutionException>(() =>
            DataRootResolver.ParseExplicitPath(["--data-root=C:\\one", "--data-root", "C:\\two"]));

        Assert.Equal("missing-data-root-argument", missing.Code);
        Assert.Equal("duplicate-data-root-argument", duplicate.Code);
    }

    [Fact]
    public void DiagnosticHostAndSmokePathsRequireSingleAbsoluteValues()
    {
        Assert.Equal(
            @"C:\temp\host.json",
            WindowsClientRuntime.ParseHostSettingsPath(["--host-settings", @"C:\temp\host.json"]));
        Assert.Equal(
            @"C:\temp\smoke.json",
            MainWindow.ParseOption(["--smoke-log", @"C:\temp\smoke.json"], "--smoke-log"));
        Assert.Equal(
            @"C:\temp\favorites.json",
            MainWindow.ParseOption(["--chat-favorites", @"C:\temp\favorites.json"], "--chat-favorites"));
        Assert.Equal(
            @"C:\temp\logs",
            MainWindow.ParseOption(["--diagnostics-directory", @"C:\temp\logs"], "--diagnostics-directory"));
        Assert.Throws<WindowsClientStartupException>(() =>
            WindowsClientRuntime.ParseHostSettingsPath(["--host-settings", "relative.json"]));
        Assert.Throws<ArgumentException>(() =>
            MainWindow.ParseOption(["--smoke-log", "relative.json"], "--smoke-log"));
        Assert.Equal(
            "天顶剑怎么合成？",
            MainWindow.ParseTextOption(
                ["--ai-smoke-question", " 天顶剑怎么合成？ "],
                "--ai-smoke-question"));
        Assert.True(MainWindow.HasFlag(["--ai-smoke-cancel-first"], "--ai-smoke-cancel-first"));
        Assert.Throws<ArgumentException>(() =>
            MainWindow.HasFlag(
                ["--ai-smoke-cancel-first", "--ai-smoke-cancel-first"],
                "--ai-smoke-cancel-first"));
    }

    [Fact]
    public void ReselectionReplacesEveryPreviousDataRootAndPreservesOtherOptions()
    {
        var result = MainWindow.WithDataRoot(
            ["--smoke-log", @"C:\temp\smoke.json", "--data-root=old", "--data-root", @"C:\older"],
            @"C:\WikiData\");

        Assert.Equal(
            ["--smoke-log", @"C:\temp\smoke.json", "--data-root", @"C:\WikiData"],
            result);
    }
}
