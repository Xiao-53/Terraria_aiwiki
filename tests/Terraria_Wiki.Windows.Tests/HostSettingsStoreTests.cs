using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.Tests;

public sealed class HostSettingsStoreTests
{
    [Fact]
    public async Task LegacyPreferencesWithoutAiLimitUseFive()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "host-settings.json");
        await File.WriteAllTextAsync(path, """{"version":1,"preferences":{"searchLimit":23}}""");
        var preferences = await new HostSettingsStore(path).LoadPreferencesAsync();
        Assert.NotNull(preferences);
        Assert.Equal(23, preferences.SearchLimit);
        Assert.Equal(5, preferences.AiSearchLimit);
    }

    [Fact]
    public async Task SavesAllHostValuesWithoutLosingOtherFields()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "settings", "host-settings.json");
        var store = new HostSettingsStore(path);
        var preferences = new ApplicationPreferences { SearchLimit = 17, AiSearchLimit = 8, Language = "en-us" };
        var dataRoot = temp.CreateDataRoot();

        await store.SavePreferencesAsync(preferences);
        await store.SaveActiveWikiIdAsync(4);
        await store.SaveDataRootAsync(dataRoot + System.IO.Path.DirectorySeparatorChar);

        var reloaded = new HostSettingsStore(path);
        Assert.Equal(preferences, await reloaded.LoadPreferencesAsync());
        Assert.Equal(4, await reloaded.LoadActiveWikiIdAsync());
        Assert.Equal(System.IO.Path.TrimEndingDirectorySeparator(dataRoot), await reloaded.LoadDataRootAsync());
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task CorruptPrimaryIsRecoveredFromLastValidBackup()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "host-settings.json");
        var store = new HostSettingsStore(path);
        var first = new ApplicationPreferences { SearchLimit = 23 };

        await store.SavePreferencesAsync(first);
        await store.SaveActiveWikiIdAsync(2);
        Assert.True(File.Exists(store.BackupPath));
        await File.WriteAllTextAsync(path, "{broken");

        var recovered = await store.LoadPreferencesAsync();

        Assert.Equal(first, recovered);
        Assert.Null(await store.LoadActiveWikiIdAsync());
        Assert.Contains("\"searchLimit\": 23", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidPrimaryAndBackupAreReported()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "host-settings.json");
        await File.WriteAllTextAsync(path, "not-json");
        await File.WriteAllTextAsync(path + ".backup", "also-not-json");
        var store = new HostSettingsStore(path);

        var error = await Assert.ThrowsAsync<HostSettingsException>(() => store.LoadDataRootAsync());

        Assert.Equal("host-settings-invalid", error.Code);
    }

    [Fact]
    public async Task CancellationDoesNotLeavePartialSettingsFile()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "host-settings.json");
        var store = new HostSettingsStore(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SavePreferencesAsync(new ApplicationPreferences(), cancellation.Token));

        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }
}
