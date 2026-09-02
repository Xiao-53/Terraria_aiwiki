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
    public async Task HistoryUsesInjectedTimeUpdatesSameDayAndSkipsDefaultPage()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();

        Assert.False(await fixture.Application.Library.RecordVisitAsync("Terraria Wiki"));
        Assert.False(await fixture.Application.Library.RecordVisitAsync("Terraria Wiki#Guide"));
        Assert.True(await fixture.Application.Library.RecordVisitAsync("Sword"));
        fixture.Time.SetUtcNow(new DateTimeOffset(2026, 8, 24, 12, 30, 0, TimeSpan.Zero));
        Assert.True(await fixture.Application.Library.RecordVisitAsync("Sword"));

        var history = await fixture.Application.Library.GetHistoryAsync(new PageRequest(0, 20));

        var item = Assert.Single(history.Items);
        Assert.Equal("Sword", item.WikiTitle);
        Assert.Equal(new DateTime(2026, 8, 24, 12, 30, 0), item.ReadAt);
        Assert.Equal("2026-08-24", item.DateKey);
    }

    [Fact]
    public async Task HistoryIsFilteredCountedPagedAndSortedDescending()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.Application.Library.RecordVisitAsync("Copper Sword");
        fixture.Time.SetUtcNow(new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero));
        await fixture.Application.Library.RecordVisitAsync("Wooden Sword");
        fixture.Time.SetUtcNow(new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero));
        await fixture.Application.Library.RecordVisitAsync("Pickaxe");

        var all = await fixture.Application.Library.GetHistoryAsync(new PageRequest(0, 2));
        var filtered = await fixture.Application.Library.GetHistoryAsync(new PageRequest(0, 20, "Sword"));

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["Pickaxe", "Wooden Sword"], all.Items.Select(item => item.WikiTitle));
        Assert.Equal(2, filtered.TotalCount);
        Assert.Equal(["Wooden Sword", "Copper Sword"], filtered.Items.Select(item => item.WikiTitle));
    }

    [Fact]
    public async Task HistoryCanBeCleared()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.Application.Library.RecordVisitAsync("Sword");

        await fixture.Application.Library.ClearHistoryAsync();

        Assert.Empty((await fixture.Application.Library.GetHistoryAsync(new PageRequest(0, 20))).Items);
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

        var historyError = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.Library.RecordVisitAsync("Sword"));
        var favoriteError = await Assert.ThrowsAsync<ApplicationUseCaseException>(
            () => fixture.Application.Library.AddFavoriteAsync("Sword"));

        Assert.Equal("operation-in-progress", historyError.Code);
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
            () => fixture.Application.Library.RecordVisitAsync("Sword", cancellation.Token));
    }

    private static WikiPage Page(string title) => new()
    {
        Title = title,
        Content = "content",
        LastModified = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
    };
}
