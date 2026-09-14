using SQLite;
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
    public async Task InitializationUsesSavedStatisticsSnapshotUntilExplicitRefresh()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1, initialize: false);
        await fixture.State.ManagerDatabase.Init();
        var book = (await fixture.State.ManagerDatabase.GetItemsAsync<WikiBook>()).Single(item => item.Id == 1);
        book.PageCount = 9001;
        book.RedirectCount = 9002;
        book.ResourceCount = 9003;
        book.DataSize = 9004;
        await fixture.State.ManagerDatabase.SaveItemAsync(book);
        await CreateContentDatabaseAsync(
            Path.Combine(fixture.Root, book.DataFolder, "data.db"),
            "current-content");
        var timings = new List<ApplicationStartupTiming>();

        var initialized = await fixture.Application.ActiveWiki.InitializeAsync(timings.Add);

        Assert.Equal(9001, initialized.PageCount);
        Assert.Equal(9002, initialized.RedirectCount);
        Assert.Equal(9003, initialized.ResourceCount);
        Assert.Equal(9004, initialized.DataSize);
        Assert.Equal(
            [
                ApplicationStartupStage.DataLayoutPreflight,
                ApplicationStartupStage.ManagerInitializationAndRecovery,
                ApplicationStartupStage.Settings,
                ApplicationStartupStage.CompatibilityPreparation,
                ApplicationStartupStage.ContentInitialization,
                ApplicationStartupStage.StatisticsSnapshot,
            ],
            timings.Select(timing => timing.Stage));
        Assert.Equal(0, timings.Single(timing => timing.Stage == ApplicationStartupStage.StatisticsSnapshot).DurationMilliseconds);

        var refreshed = await fixture.Application.Data.GetStatisticsAsync();

        Assert.Equal(2, refreshed.PageCount);
        Assert.Equal(0, refreshed.RedirectCount);
        Assert.Equal(0, refreshed.ResourceCount);
        Assert.NotEqual(9004, refreshed.DataSize);
    }

    [Fact]
    public async Task Initialization_rejects_old_content_format_without_converting_it()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1, initialize: false);
        await fixture.State.ManagerDatabase.Init();
        var book = (await fixture.State.ManagerDatabase.GetItemsAsync<WikiBook>()).Single(item => item.Id == 1);
        var path = Path.Combine(fixture.Root, book.DataFolder, "data.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var old = new SQLiteConnection(path))
            old.Execute("CREATE TABLE WikiPage (Title TEXT PRIMARY KEY, Content TEXT, LastModified TEXT)");

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(() =>
            fixture.Application.ActiveWiki.InitializeAsync());

        Assert.Equal("content-database-format-unsupported", error.Code);
        using var unchanged = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly);
        Assert.Equal(0, unchanged.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE name = 'WikiDatabaseMetadata'"));
    }

    private static async Task CreateContentDatabaseAsync(string path, string contentValue)
    {
        var database = new ContentDbService(path);
        try
        {
            var book = CreateBookOne();
            await database.Init(seedBook: book);
            await database.SaveItemAsync(new WikiPage
            {
                Title = "Distinct Page",
                Content = contentValue,
                LastModified = DateTime.UnixEpoch,
            });
        }
        finally
        {
            await database.CloseConnection();
        }
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
