using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application.Tests;

public sealed class WikiLibraryUseCaseTests
{
    [Fact]
    public async Task PagesAreFilteredCountedAndPagedInAscendingTitleOrder()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.State.ContentDatabase.SaveItemsAsync(
        [
            Page("Zebra"),
            Page("Copper Sword"),
            Page("Copper Pickaxe"),
            Page("Amber"),
        ]);

        var first = await fixture.Application.Library.GetPagesAsync(new PageRequest(0, 2));
        var filtered = await fixture.Application.Library.GetPagesAsync(new PageRequest(0, 20, " Copper "));

        Assert.Equal(5, first.TotalCount); // Includes the seeded default page.
        Assert.Equal(["Amber", "Copper Pickaxe"], first.Items.Select(item => item.Title));
        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(["Copper Pickaxe", "Copper Sword"], filtered.Items.Select(item => item.Title));
        Assert.Equal(0, filtered.Offset);
        Assert.Equal(20, filtered.Limit);
    }

    [Fact]
    public void PageRequestRejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(-1, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PageRequest(0, 201));
    }

    [Fact]
    public void PersistentWikiHistoryIsNotExposedByTheApplicationBoundary()
    {
        Assert.Null(typeof(WikiLibraryUseCases).GetMethod("GetHistoryAsync"));
        Assert.Null(typeof(WikiLibraryUseCases).GetMethod("RecordVisitAsync"));
        Assert.Null(typeof(WikiLibraryUseCases).GetMethod("ClearHistoryAsync"));
    }

    [Fact]
    public async Task FavoritesAreIdempotentFilteredSortedAndRemovable()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        Assert.True(await fixture.Application.Library.AddFavoriteAsync("Copper Sword"));
        Assert.False(await fixture.Application.Library.AddFavoriteAsync("Copper Sword"));
        fixture.Time.SetUtcNow(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        Assert.True(await fixture.Application.Library.AddFavoriteAsync("Wooden Sword"));
        fixture.Time.SetUtcNow(new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero));
        Assert.True(await fixture.Application.Library.AddFavoriteAsync("Pickaxe"));

        var all = await fixture.Application.Library.GetFavoritesAsync(new PageRequest(0, 2));
        var filtered = await fixture.Application.Library.GetFavoritesAsync(new PageRequest(0, 20, "Sword"));

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["Pickaxe", "Wooden Sword"], all.Items.Select(item => item.WikiTitle));
        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(["Wooden Sword", "Copper Sword"], filtered.Items.Select(item => item.WikiTitle));
        Assert.True(await fixture.Application.Library.IsFavoriteAsync("Copper Sword"));
        Assert.True(await fixture.Application.Library.RemoveFavoriteAsync("Copper Sword"));
        Assert.False(await fixture.Application.Library.RemoveFavoriteAsync("Copper Sword"));
        Assert.False(await fixture.Application.Library.IsFavoriteAsync("Copper Sword"));
    }

    [Fact]
    public async Task LibraryWritesRejectAnOccupiedSharedDataGate()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        using var occupied = await fixture.State.OperationGate.TryAcquireAsync();

        var favoriteError = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.Library.AddFavoriteAsync("Sword"));

        Assert.Equal("operation-in-progress", favoriteError.Code);
    }

    [Fact]
    public async Task LibraryCancellationPropagates()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.Library.GetPagesAsync(new PageRequest(0, 20), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.Library.GetFavoritesAsync(new PageRequest(0, 20), cancellation.Token));
    }

    [Fact]
    public async Task GlobalFavoritesIncludeEveryRegisteredWikiAndAreNotLimitedToTwoHundred()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        var books = await fixture.State.ManagerDatabase.GetItemsAsync<WikiBook>();
        var active = fixture.State.RequireActiveBook();
        var second = books.First(book => book.Id != active.Id);
        await fixture.State.ContentDatabase.SaveItemsAsync(
            Enumerable.Range(0, 205).Select(index => new WikiFavorite
            {
                WikiTitle = $"Active {index:000}",
                FavoritedAt = new DateTime(2026, 8, 1).AddMinutes(index),
            }));
        var secondPaths = Terraria_Wiki.Core.Data.WikiDataPaths.Create(fixture.Root, second);
        secondPaths.EnsureDataDirectory();
        var secondDatabase = new ContentDbService(secondPaths.ContentDatabasePath);
        await secondDatabase.Init(seedBook: second);
        await secondDatabase.SaveItemAsync(new WikiFavorite
        {
            WikiTitle = "Active 000",
            FavoritedAt = new DateTime(2026, 9, 1),
        });
        await secondDatabase.CloseConnection();

        var result = await fixture.Application.Library.GetAllFavoritesAsync();

        Assert.Equal(206, result.Items.Count);
        Assert.Empty(result.Issues);
        Assert.Equal(second.Id, result.Items[0].WikiId);
        Assert.Equal(second.Title, result.Items[0].WikiDisplayTitle);
        Assert.Equal(2, result.Items.Count(item => item.PageTitle == "Active 000"));
    }

    [Fact]
    public async Task ExplicitWikiFavoriteOperationsDoNotSwitchTheActiveWikiOrPauseConsumers()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        var active = fixture.State.RequireActiveBook();
        var second = (await fixture.State.ManagerDatabase.GetItemsAsync<WikiBook>())
            .First(book => book.Id != active.Id);
        var secondPaths = Terraria_Wiki.Core.Data.WikiDataPaths.Create(fixture.Root, second);
        secondPaths.EnsureDataDirectory();
        var secondDatabase = new ContentDbService(secondPaths.ContentDatabasePath);
        await secondDatabase.Init(seedBook: second);
        await secondDatabase.CloseConnection();
        var pauseCount = fixture.Consumers.PauseCount;

        Assert.True(await fixture.Application.Library.SetFavoriteAsync(second.Id, "Shared", true));
        Assert.True(await fixture.Application.Library.IsFavoriteAsync(second.Id, "Shared"));
        Assert.False(await fixture.Application.Library.SetFavoriteAsync(second.Id, "Shared", false));
        Assert.Equal(active.Id, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        Assert.Equal(pauseCount, fixture.Consumers.PauseCount);
    }

    [Fact]
    public async Task UnreadableWikiReportsAnIssueWithoutDiscardingOtherFavorites()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.Application.Library.AddFavoriteAsync("Healthy");
        var active = fixture.State.RequireActiveBook();
        var broken = (await fixture.State.ManagerDatabase.GetItemsAsync<WikiBook>())
            .First(book => book.Id != active.Id);
        var brokenPaths = Terraria_Wiki.Core.Data.WikiDataPaths.Create(fixture.Root, broken);
        brokenPaths.EnsureDataDirectory();
        await File.WriteAllTextAsync(brokenPaths.ContentDatabasePath, "not sqlite");

        var result = await fixture.Application.Library.GetAllFavoritesAsync();

        Assert.Equal("Healthy", Assert.Single(result.Items).PageTitle);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(broken.Id, issue.WikiId);
        Assert.Equal("favorite-database-unreadable", issue.Code);
    }

    private static WikiPage Page(string title) => new()
    {
        Title = title,
        Content = "content",
        LastModified = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
    };
}
