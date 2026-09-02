using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Favorites;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ChatFavoriteStoreTests
{
    [Fact]
    public async Task SavesOrdersRemovesAndReloadsVersionedFavorites()
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Path, "favorites", "chat-favorites.json");
        var store = new ChatFavoriteStore(path);
        var older = Favorite("旧问题", new DateTimeOffset(2026, 8, 27, 1, 0, 0, TimeSpan.Zero));
        var newer = Favorite("新问题", new DateTimeOffset(2026, 8, 28, 1, 0, 0, TimeSpan.Zero));

        await store.SaveAsync(older);
        await store.SaveAsync(newer);

        var reloaded = await new ChatFavoriteStore(path).GetAllAsync();
        Assert.Equal(["新问题", "旧问题"], reloaded.Select(item => item.Question));
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(store.BackupPath));

        await store.RemoveAsync(newer.Id);
        Assert.Equal("旧问题", Assert.Single(await store.GetAllAsync()).Question);
    }

    [Fact]
    public async Task CorruptPrimaryUsesLastValidBackupAndInvalidPayloadIsRejected()
    {
        using var temp = new TestDirectory();
        var path = Path.Combine(temp.Path, "chat-favorites.json");
        var store = new ChatFavoriteStore(path);
        var first = Favorite("第一条", DateTimeOffset.UtcNow.AddMinutes(-1));
        var second = Favorite("第二条", DateTimeOffset.UtcNow);
        await store.SaveAsync(first);
        await store.SaveAsync(second);

        await File.WriteAllTextAsync(path, "{ broken");

        var recovered = await store.GetAllAsync();
        Assert.Equal("第一条", Assert.Single(recovered).Question);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
            Favorite(" ", DateTimeOffset.UtcNow)));
    }

    private static ChatFavoriteRecord Favorite(string question, DateTimeOffset time) => new(
        Guid.NewGuid(),
        question,
        "# 回答",
        [new ChatFavoriteSource("请求", "页面", "章节", time)],
        time);
}
