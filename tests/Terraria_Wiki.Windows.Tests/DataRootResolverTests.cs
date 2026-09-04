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
        var picker = new TestDataRootPicker { Result = temp.CreateDataRoot("picked") };
        var resolver = new DataRootResolver(store, picker);

        var result = await resolver.ResolveAsync(["--data-root", explicitRoot]);

        Assert.Equal(DataRootSource.CommandLine, result.Source);
        Assert.Equal(explicitRoot, result.Path);
        Assert.Equal(explicitRoot, await store.LoadDataRootAsync());
        Assert.Equal(0, picker.Calls);
    }

    [Fact]
    public async Task ValidSavedRootWinsWithoutPicker()
    {
        using var temp = new TestDirectory();
        var savedRoot = temp.CreateDataRoot();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        await store.SaveDataRootAsync(savedRoot);
        var picker = new TestDataRootPicker();

        var result = await new DataRootResolver(store, picker).ResolveAsync([]);

        Assert.Equal(DataRootSource.SavedSettings, result.Source);
        Assert.Equal(savedRoot, result.Path);
        Assert.Equal(0, picker.Calls);
    }

    [Fact]
    public async Task InvalidSavedRootFallsBackToPickerAndPersistsSelection()
    {
        using var temp = new TestDirectory();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        await store.SaveDataRootAsync(System.IO.Path.Combine(temp.Path, "missing"));
        var selected = temp.CreateDataRoot("selected");
        var picker = new TestDataRootPicker { Result = selected };

        var result = await new DataRootResolver(store, picker).ResolveAsync([]);

        Assert.Equal(DataRootSource.FolderPicker, result.Source);
        Assert.Equal(selected, result.Path);
        Assert.Equal(selected, await store.LoadDataRootAsync());
        Assert.Equal(1, picker.Calls);
    }

    [Fact]
    public async Task StartupResolutionDoesNotOpenFolderPickerWhenNoValidRootExists()
    {
        using var temp = new TestDirectory();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        var invalidSavedRoot = System.IO.Path.Combine(temp.Path, "missing");
        await store.SaveDataRootAsync(invalidSavedRoot);
        var picker = new TestDataRootPicker { Result = temp.CreateDataRoot("would-have-been-picked") };

        var error = await Assert.ThrowsAsync<DataRootResolutionException>(() =>
            new DataRootResolver(store, picker).ResolveAsync([], allowFolderPicker: false));

        Assert.Equal("data-root-required", error.Code);
        Assert.Equal(0, picker.Calls);
        Assert.Equal(invalidSavedRoot, await store.LoadDataRootAsync());
    }

    [Fact]
    public async Task ExplicitInvalidRootDoesNotSilentlyFallBack()
    {
        using var temp = new TestDirectory();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        var picker = new TestDataRootPicker { Result = temp.CreateDataRoot() };

        var error = await Assert.ThrowsAsync<DataRootResolutionException>(() =>
            new DataRootResolver(store, picker).ResolveAsync(["--data-root=relative"]));

        Assert.Equal("invalid-explicit-data-root", error.Code);
        Assert.Equal(0, picker.Calls);
    }

    [Fact]
    public async Task MissingManagerAndCancelledPickerAreObservable()
    {
        using var temp = new TestDirectory();
        var store = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "host-settings.json"));
        var empty = System.IO.Path.Combine(temp.Path, "empty");
        Directory.CreateDirectory(empty);

        var invalid = await Assert.ThrowsAsync<DataRootResolutionException>(() =>
            new DataRootResolver(store, new TestDataRootPicker { Result = empty }).ResolveAsync([]));
        var cancelled = await Assert.ThrowsAsync<DataRootResolutionException>(() =>
            new DataRootResolver(store, new TestDataRootPicker()).ResolveAsync([]));

        Assert.Equal("invalid-selected-data-root", invalid.Code);
        Assert.Equal("data-root-selection-cancelled", cancelled.Code);
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
