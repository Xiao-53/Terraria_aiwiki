using System.Security.Cryptography;
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

    [Fact]
    public async Task InitializationPreservesCompatibilityCodeAndDoesNotModifyConflictingDataFamilies()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1, initialize: false);
        await fixture.State.ManagerDatabase.Init();
        var legacyDirectory = Path.Combine(fixture.Root, LegacyWikiCompatibility.LegacyDataFolder);
        var currentDirectory = Path.Combine(fixture.Root, "Terraria_Wiki_zh");
        await CreateContentDatabaseAsync(
            Path.Combine(legacyDirectory, LegacyWikiCompatibility.LegacyDatabaseName),
            "legacy-content");
        await CreateContentDatabaseAsync(
            Path.Combine(currentDirectory, LegacyWikiCompatibility.CurrentDatabaseName),
            "current-content");
        File.WriteAllBytes(Path.Combine(legacyDirectory, LegacyWikiCompatibility.LegacyDatabaseName) + "-wal", [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(currentDirectory, LegacyWikiCompatibility.CurrentDatabaseName) + "-shm", [4, 5, 6]);
        await fixture.State.ManagerDatabase.CloseConnection();
        var before = SnapshotProtectedFiles(fixture.Root);

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.InitializeAsync());

        Assert.Equal("data-directory-conflict", error.Code);
        Assert.DoesNotContain(fixture.Root, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, SnapshotProtectedFiles(fixture.Root));
    }

    [Fact]
    public async Task InitializationRetiresLegacyDirectoryWhenCurrentDatabaseCoversIt()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1, initialize: false);
        await fixture.State.ManagerDatabase.Init();
        var legacyDirectory = Path.Combine(fixture.Root, LegacyWikiCompatibility.LegacyDataFolder);
        var currentDirectory = Path.Combine(fixture.Root, "Terraria_Wiki_zh");
        var legacyPath = Path.Combine(legacyDirectory, LegacyWikiCompatibility.LegacyDatabaseName);
        var currentPath = Path.Combine(currentDirectory, LegacyWikiCompatibility.CurrentDatabaseName);
        await CreateContentDatabaseAsync(legacyPath, "legacy-content");
        await CreateContentDatabaseAsync(currentPath, "current-content");
        var current = new ContentDbService(currentPath);
        try
        {
            await current.Init(seedBook: CreateBookOne());
            await current.SaveItemAsync(new WikiPage
            {
                Title = "New Page",
                Content = "new-content",
                LastModified = DateTime.UnixEpoch.AddDays(1),
            });
        }
        finally
        {
            await current.CloseConnection();
        }
        await fixture.State.ManagerDatabase.CloseConnection();

        var active = await fixture.Application.ActiveWiki.InitializeAsync();

        Assert.Equal(1, active.Id);
        Assert.False(Directory.Exists(legacyDirectory));
        Assert.Empty(Directory.EnumerateDirectories(
            fixture.Root,
            LegacyWikiCompatibility.LegacyDataFolder + ".superseded-*"));
        Assert.Equal("new-content", (await fixture.Application.Query.ReadAsync("New Page")).Content);
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
            Path.Combine(fixture.Root, book.DataFolder, LegacyWikiCompatibility.CurrentDatabaseName),
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
    public async Task PendingImportForAnyRegisteredWikiRunsBeforeConflictFastFailure()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1, initialize: false);
        await fixture.State.ManagerDatabase.Init();
        await fixture.State.ManagerDatabase.CloseConnection();
        var legacyDirectory = Path.Combine(fixture.Root, LegacyWikiCompatibility.LegacyDataFolder);
        var currentDirectory = Path.Combine(fixture.Root, "Terraria_Wiki_zh");
        Directory.CreateDirectory(legacyDirectory);
        Directory.CreateDirectory(currentDirectory);
        File.WriteAllBytes(Path.Combine(legacyDirectory, LegacyWikiCompatibility.LegacyDatabaseName), [1]);
        File.WriteAllBytes(Path.Combine(currentDirectory, LegacyWikiCompatibility.CurrentDatabaseName), [2]);
        var stagingRoot = Path.Combine(fixture.Root, "Temp");
        Directory.CreateDirectory(stagingRoot);
        await File.WriteAllTextAsync(Path.Combine(stagingRoot, "import-2.journal.json"), "{}");
        var timings = new List<ApplicationStartupTiming>();

        var error = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.ActiveWiki.InitializeAsync(timings.Add));

        Assert.Equal("initialization-failed", error.Code);
        Assert.Contains(
            timings,
            timing => timing.Stage == ApplicationStartupStage.ManagerInitializationAndRecovery);
        Assert.DoesNotContain(
            timings,
            timing => timing.Stage == ApplicationStartupStage.CompatibilityPreparation);
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

    private static string[] SnapshotProtectedFiles(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path =>
        {
            var name = Path.GetFileName(path);
            return string.Equals(name, "Manager.db", StringComparison.Ordinal)
                || name.StartsWith(LegacyWikiCompatibility.LegacyDatabaseName, StringComparison.Ordinal)
                || name.StartsWith(LegacyWikiCompatibility.CurrentDatabaseName, StringComparison.Ordinal);
        })
        .OrderBy(path => path, StringComparer.Ordinal)
        .Select(path =>
        {
            var relative = Path.GetRelativePath(root, path);
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return $"{relative}|{stream.Length}|{Convert.ToHexString(SHA256.HashData(stream))}";
        })
        .ToArray();

    private static WikiBook CreateBookOne() => new()
    {
        Id = 1,
        Title = "泰拉瑞亚中文百科",
        DataFolder = "Terraria_Wiki_zh",
        DefaultPageTitle = "Terraria Wiki",
        DefaultPageContent = "请先下载数据",
    };
}
