using Terraria_Wiki.Core.Compatibility;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Application.Tests;

public sealed class ActiveWikiTests
{
    [Fact]
    public async Task InitializationRestoresValidSelectionAndIsIdempotent()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 2, initialize: false);

        var first = await fixture.Application.ActiveWiki.InitializeAsync();
        var second = await fixture.Application.ActiveWiki.InitializeAsync();

        Assert.Equal(2, first.Id);
        Assert.Equal(first, second);
        Assert.Equal(2, fixture.Settings.ActiveWikiId);
        Assert.Equal(0, fixture.Settings.ActiveWikiSaveCount);
        Assert.Equal(1, fixture.Consumers.PauseCount);
        Assert.Equal(1, fixture.Consumers.ResumeCount);
    }

    [Fact]
    public async Task MissingSelectionFallsBackToOneAndWritesOnlyActiveId()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(
            preferences: new ApplicationPreferences { SearchLimit = 75 },
            activeWikiId: 999,
            initialize: false);

        var active = await fixture.Application.ActiveWiki.InitializeAsync();

        Assert.Equal(1, active.Id);
        Assert.Equal(1, fixture.Settings.ActiveWikiId);
        Assert.Equal(1, fixture.Settings.ActiveWikiSaveCount);
        Assert.Equal(75, fixture.Settings.Preferences!.SearchLimit);
        Assert.Equal(0, fixture.Settings.PreferencesSaveCount);
    }

    [Fact]
    public void SelectionFallsBackToSmallestRegisteredIdWhenOneIsAbsent()
    {
        var selected = ActiveWikiUseCases.SelectBook(
        [
            new WikiBook { Id = 9 },
            new WikiBook { Id = 4 },
        ], storedId: 99);

        Assert.Equal(4, selected.Id);
    }

    [Fact]
    public async Task ListingIsOrderedAndActiveSnapshotIsAvailableWithoutPersistenceTypes()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 3);

        var books = await fixture.Application.ActiveWiki.ListAsync();
        var active = await fixture.Application.ActiveWiki.GetActiveAsync();

        Assert.Equal([1, 2, 3, 4], books.Select(book => book.Id));
        Assert.Equal(3, active.Id);
        Assert.Equal("Terraria Wiki", active.Title);
    }

    [Fact]
    public async Task SameIdIsIdempotentAndDoesNotPauseConsumersOrRewriteSettings()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var pauses = fixture.Consumers.PauseCount;
        var saves = fixture.Settings.ActiveWikiSaveCount;

        var result = await fixture.Application.ActiveWiki.SwitchAsync(1);

        Assert.Equal(1, result.Id);
        Assert.Equal(pauses, fixture.Consumers.PauseCount);
        Assert.Equal(saves, fixture.Settings.ActiveWikiSaveCount);
    }

    [Fact]
    public async Task UnknownIdDoesNotChangeStateOrPauseConsumers()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var pauses = fixture.Consumers.PauseCount;

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.SwitchAsync(999));

        Assert.Equal("unknown-wiki", error.Code);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        Assert.Equal(1, fixture.Settings.ActiveWikiId);
        Assert.Equal(pauses, fixture.Consumers.PauseCount);
    }

    [Fact]
    public async Task SuccessfulSwitchPausesAndResumesConsumersAndPersistsId()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;

        var switched = await fixture.Application.ActiveWiki.SwitchAsync(2);

        Assert.Equal(2, switched.Id);
        Assert.Equal(2, fixture.Settings.ActiveWikiId);
        Assert.Equal(pauses + 1, fixture.Consumers.PauseCount);
        Assert.Equal(resumes + 1, fixture.Consumers.ResumeCount);
        Assert.EndsWith(Path.Combine("Calamity_Wiki_zh", "data.db"), fixture.State.ContentDatabase.DatabasePath);
    }

    [Fact]
    public async Task OccupiedDataGateRejectsSwitchAndAlwaysResumesConsumers()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;
        using var occupied = await fixture.State.OperationGate.TryAcquireAsync();

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.SwitchAsync(2));

        Assert.Equal("operation-in-progress", error.Code);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        Assert.Equal(pauses + 1, fixture.Consumers.PauseCount);
        Assert.Equal(resumes + 1, fixture.Consumers.ResumeCount);
    }

    [Fact]
    public async Task RunningLifecycleOperationRejectsSwitchWithoutChangingSource()
    {
        var transport = new BlockingTransport();
        await using var fixture = await ApplicationFixture.CreateAsync(
            new ApplicationPreferences { RetryAttempts = 0 },
            activeWikiId: 1,
            transport: transport);
        var active = fixture.State.ActiveBook!;
        active.ApiBaseUrl = "https://wiki.test/api.php";
        active.BaseUrl = "https://wiki.test";
        active.RedirectListUrl = "/redirects";
        await fixture.State.ManagerDatabase.SaveItemAsync(active);
        using var cancellation = new CancellationTokenSource();
        var download = fixture.Application.Data.DownloadAsync(false, cancellationToken: cancellation.Token);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.SwitchAsync(2));

        Assert.Equal("operation-in-progress", error.Code);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
    }

    [Fact]
    public async Task PreCanceledSwitchPropagatesCancellationWithoutChangingState()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var pauses = fixture.Consumers.PauseCount;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.ActiveWiki.SwitchAsync(2, cancellation.Token));

        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        Assert.Equal(pauses, fixture.Consumers.PauseCount);
    }

    [Fact]
    public async Task TargetPreparationFailureRestoresOldDatabaseAndSetting()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var target = await fixture.State.ManagerDatabase.GetItemAsync<WikiBook>(2);
        target!.DataFolder = "../invalid";
        await fixture.State.ManagerDatabase.SaveItemAsync(target);
        var oldPath = fixture.State.ContentDatabase.DatabasePath;

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.SwitchAsync(2));

        Assert.Equal("active-wiki-switch-failed", error.Code);
        Assert.Equal(oldPath, fixture.State.ContentDatabase.DatabasePath);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        Assert.Equal(1, fixture.Settings.ActiveWikiId);
        Assert.Equal(fixture.Consumers.PauseCount, fixture.Consumers.ResumeCount);
    }

    [Fact]
    public async Task SettingsSaveFailureRollsBackTargetAndOldSetting()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var oldPath = fixture.State.ContentDatabase.DatabasePath;
        fixture.Settings.ActiveWikiSaveFailure = id =>
            id == 2 ? new IOException("settings unavailable") : null;

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.SwitchAsync(2));

        Assert.Equal("active-wiki-switch-failed", error.Code);
        Assert.Equal(oldPath, fixture.State.ContentDatabase.DatabasePath);
        Assert.Equal(1, fixture.Settings.ActiveWikiId);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        Assert.Equal(fixture.Consumers.PauseCount, fixture.Consumers.ResumeCount);
    }

    [Fact]
    public async Task RollbackFailureHasDedicatedStableCode()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        var target = await fixture.State.ManagerDatabase.GetItemAsync<WikiBook>(2);
        target!.DataFolder = "../invalid";
        await fixture.State.ManagerDatabase.SaveItemAsync(target);
        fixture.Settings.ActiveWikiSaveFailure = _ => new IOException("all settings writes fail");

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.SwitchAsync(2));

        Assert.Equal("active-wiki-rollback-failed", error.Code);
        Assert.Equal(fixture.Consumers.PauseCount, fixture.Consumers.ResumeCount);
    }

    [Fact]
    public async Task InitializationMigratesLegacyDirectoryAndDatabaseFamilyBeforeOpeningContent()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1, initialize: false);
        var legacyDirectory = Path.Combine(fixture.Root, LegacyWikiCompatibility.LegacyDataFolder);
        Directory.CreateDirectory(legacyDirectory);
        var legacyPath = Path.Combine(legacyDirectory, LegacyWikiCompatibility.LegacyDatabaseName);
        var legacy = new ContentDbService(legacyPath);
        await legacy.Init(seedBook: CreateBookOne());
        await legacy.CloseConnection();

        var active = await fixture.Application.ActiveWiki.InitializeAsync();

        Assert.Equal(1, active.Id);
        Assert.False(Directory.Exists(legacyDirectory));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "Terraria_Wiki_zh", "data.db")));
        Assert.EndsWith(Path.Combine("Terraria_Wiki_zh", "data.db"), fixture.State.ContentDatabase.DatabasePath);
    }

    [Fact]
    public async Task InitializationCompletesAnchorMigrationBeforeRead()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1, initialize: false);
        var directory = Path.Combine(fixture.Root, "Terraria_Wiki_zh");
        var path = Path.Combine(directory, "data.db");
        var content = new ContentDbService(path);
        var book = CreateBookOne();
        await content.Init(seedBook: book);
        await content.SaveItemAsync(new WikiPage
        {
            Title = book.DefaultPageTitle,
            Content = "<p><a title='Sword'>Sword</a></p>",
            LastModified = DateTime.UnixEpoch,
        });
        await content.CloseConnection();

        await fixture.Application.ActiveWiki.InitializeAsync();
        var read = await fixture.Application.Query.ReadAsync(book.DefaultPageTitle);

        Assert.Contains("data-wiki=\"Sword\"", read.Content);
    }

    private static WikiBook CreateBookOne() => new()
    {
        Id = 1,
        Title = "泰拉瑞亚中文百科",
        DataFolder = "Terraria_Wiki_zh",
        DefaultPageTitle = "Terraria Wiki",
        DefaultPageContent = "请先下载数据",
    };
}
