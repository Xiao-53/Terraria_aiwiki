using System.Text.Json;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Chat;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Favorites;
using Terraria_Wiki.Windows.Presentation.Shell;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ChatConversationStoreTests
{
    [Fact]
    public async Task SavesFiltersOrdersAndReloadsCompleteConversations()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        var older = Conversation("旧问题", 1);
        var newer = Conversation("新问题", 2, turns: 2);

        await store.SaveAsync(older);
        await store.SaveAsync(newer);

        var reloaded = new ChatConversationStore(store.DirectoryPath);
        Assert.Equal(["新问题", "旧问题"], (await reloaded.GetAllAsync()).Select(item => item.Title));
        Assert.Equal("新问题", Assert.Single(await reloaded.GetAllAsync(" 新 ")).Title);
        Assert.Equal(2, (await reloaded.GetAsync(newer.Id))!.Turns.Count);
        Assert.True(File.Exists(store.IndexPath));
        Assert.True(File.Exists(store.IndexBackupPath));
    }

    [Fact]
    public async Task PendingTurnBecomesInterruptedAndIsNeverModelHistory()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        var pending = Conversation("中断问题", 1, status: ChatConversationTurnStatus.Pending);
        await store.SaveAsync(pending);

        var restored = Assert.Single(await store.GetAllAsync());

        Assert.Equal(ChatConversationTurnStatus.Interrupted, restored.Turns.Single().Status);
        Assert.Equal("上次回答中断。", restored.Turns.Single().Markdown);
        Assert.Empty(ChatViewModel.BuildHistory(restored.Turns));
        Assert.Equal(restored.UpdatedAt, (await store.GetAsync(restored.Id))!.UpdatedAt);
    }

    [Fact]
    public async Task CorruptPrimaryIndexUsesBackupAndCorruptConversationIsSkipped()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        var first = Conversation("第一段", 1);
        var second = Conversation("第二段", 2);
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await File.WriteAllTextAsync(store.IndexPath, "{ broken");

        Assert.Equal("第一段", Assert.Single(await store.GetAllAsync()).Title);

        var conversationPath = Path.Combine(store.DirectoryPath, first.Id + ".json");
        await File.WriteAllTextAsync(conversationPath, "{ broken");
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task StructurallyInvalidIndexFailsWithSafeStoreErrorInsteadOfNullReference()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        Directory.CreateDirectory(store.DirectoryPath);
        await File.WriteAllTextAsync(store.IndexPath, "{\"version\":1,\"items\":null}");

        var error = await Assert.ThrowsAsync<ChatConversationStoreException>(() => store.GetAllAsync());

        Assert.Equal("conversation-index-invalid", error.Code);
    }

    [Fact]
    public async Task MalformedNullableConversationFieldsAreSkippedWithoutCrashingHistory()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        var conversation = Conversation("损坏字段", 1);
        await store.SaveAsync(conversation);
        var path = Path.Combine(store.DirectoryPath, conversation.Id + ".json");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("\"sources\": []", "\"sources\": null"));

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task KeepsNewestHundredUnfavoritedAndNeverPrunesFavorites()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        var favorite = Conversation("收藏", 0, favorite: true);
        await store.SaveAsync(favorite);
        for (var index = 0; index < 105; index++)
            await store.SaveAsync(Conversation($"普通 {index:000}", index + 1));

        var all = await store.GetAllAsync();

        Assert.Equal(101, all.Count);
        Assert.Contains(all, item => item.Id == favorite.Id && item.IsFavorite);
        Assert.DoesNotContain(all, item => item.Title == "普通 000");
        Assert.Contains(all, item => item.Title == "普通 104");
    }

    [Fact]
    public async Task RejectsMoreThanTwoHundredFavoritesWithoutDeletingExistingOnes()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        for (var index = 0; index < 200; index++)
            await store.SaveAsync(Conversation($"收藏 {index:000}", index, favorite: true));

        var error = await Assert.ThrowsAsync<ChatConversationStoreException>(
            () => store.SaveAsync(Conversation("超额收藏", 300, favorite: true)));

        Assert.Equal("conversation-favorite-limit", error.Code);
        Assert.Equal(200, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task DeleteUnfavoritedPreservesFavoritesAndDeleteRemovesEitherKind()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        var favorite = Conversation("收藏", 1, favorite: true);
        var ordinary = Conversation("普通", 2);
        await store.SaveAsync(favorite);
        await store.SaveAsync(ordinary);

        await store.DeleteUnfavoritedAsync();
        Assert.Equal(favorite.Id, Assert.Single(await store.GetAllAsync()).Id);

        await store.DeleteAsync(favorite.Id);
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task MigratesLegacyFavoritesOnceAndRetainsOriginalFile()
    {
        using var temp = new TestDirectory();
        var legacyPath = Path.Combine(temp.Path, "chat-favorites.json");
        var oldStore = new ChatFavoriteStore(legacyPath);
        await oldStore.SaveAsync(new ChatFavoriteRecord(
            Guid.NewGuid(),
            "旧问题",
            "# 旧回答",
            [new ChatFavoriteSource("请求页", "真实页", "章节", null)],
            new DateTimeOffset(2026, 9, 5, 1, 0, 0, TimeSpan.Zero)));
        var store = new ChatConversationStore(Path.Combine(temp.Path, "conversations"), legacyPath);

        var migrated = Assert.Single(await store.GetAllAsync());

        Assert.True(migrated.IsFavorite);
        Assert.Equal("旧问题", migrated.Title);
        Assert.Equal("# 旧回答", migrated.Turns.Single().Markdown);
        Assert.True(File.Exists(legacyPath));
        await oldStore.SaveAsync(new ChatFavoriteRecord(
            Guid.NewGuid(), "后加旧项", "旧回答", [], DateTimeOffset.Now));
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task InvalidLegacyFileLeavesMigrationRetryable()
    {
        using var temp = new TestDirectory();
        var legacyPath = Path.Combine(temp.Path, "chat-favorites.json");
        await File.WriteAllTextAsync(legacyPath, "{ broken");
        var store = new ChatConversationStore(Path.Combine(temp.Path, "conversations"), legacyPath);

        var error = await Assert.ThrowsAsync<ChatConversationStoreException>(() => store.GetAllAsync());
        Assert.Equal("conversation-legacy-invalid", error.Code);
        Assert.False(File.Exists(store.IndexPath));

        var document = new ChatFavoriteDocument(1,
        [
            new ChatFavoriteRecord(
                Guid.NewGuid(), "可重试", "回答", [],
                new DateTimeOffset(2026, 9, 5, 1, 0, 0, TimeSpan.Zero)),
        ]);
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(document));
        Assert.Equal("可重试", Assert.Single(await store.GetAllAsync()).Title);
    }

    [Fact]
    public async Task ConcurrentSavesRemainReadableAndTurnLimitIsRejected()
    {
        using var temp = new TestDirectory();
        var store = Store(temp);
        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(index => store.SaveAsync(Conversation($"并发 {index}", index))));
        Assert.Equal(12, (await store.GetAllAsync()).Count);

        var invalid = Conversation("过多轮次", 1) with
        {
            Turns = Enumerable.Range(0, 101).Select(index => Turn(index, 1)).ToArray(),
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(invalid));
    }

    [Fact]
    public async Task ShellAlwaysStartsBlankEvenWhenHistoryExists()
    {
        var store = new MemoryChatConversationStore();
        await store.SaveAsync(Conversation("历史对话", 1));

        using var shell = new ShellViewModel(store);

        Assert.True(shell.Chat.IsEmpty);
        Assert.Null(shell.Chat.CurrentConversationId);
    }

    [Fact]
    public void ModelHistoryUsesOnlyLatestFourEligiblePlainTextTurns()
    {
        var turns = new[]
        {
            Turn(0, 1),
            Turn(1, 2, ChatConversationTurnStatus.Failed),
            Turn(2, 3),
            Turn(3, 4, ChatConversationTurnStatus.Cancelled),
            Turn(4, 5),
            Turn(5, 6, ChatConversationTurnStatus.InsufficientEvidence),
            Turn(6, 7),
        };

        var history = ChatViewModel.BuildHistory(turns);

        Assert.Equal(["问题 2", "问题 4", "问题 5", "问题 6"], history.Select(item => item.User));
        Assert.Equal(["回答 2", "回答 4", "回答 5", "回答 6"], history.Select(item => item.Assistant));
    }

    [Fact]
    public void ModelHistoryTruncatesOnlyTheOldestIncludedAnswerAtParagraphBoundary()
    {
        var newest = Turn(2, 3) with { Markdown = new string('新', 10_000) };
        var oldest = Turn(1, 2) with { Markdown = new string('旧', 10_000) + "\n\n" + new string('尾', 10_000) };

        var history = ChatViewModel.BuildHistory([oldest, newest]);

        Assert.Equal(2, history.Count);
        Assert.Equal(oldest.Question, history[0].User);
        Assert.EndsWith("[较早回答已在本地截断]", history[0].Assistant, StringComparison.Ordinal);
        Assert.Equal(newest.Markdown, history[1].Assistant);
        Assert.InRange(history.Sum(turn => turn.User.Length + turn.Assistant.Length), 1,
            ChatViewModel.HistoryCharacterBudget);
    }

    private static ChatConversationStore Store(TestDirectory temp) =>
        new(Path.Combine(temp.Path, "conversations"));

    private static ChatConversation Conversation(
        string title,
        int hours,
        bool favorite = false,
        int turns = 1,
        ChatConversationTurnStatus status = ChatConversationTurnStatus.Completed)
    {
        var timestamp = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero).AddHours(hours);
        return new ChatConversation
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = timestamp,
            UpdatedAt = timestamp.AddMinutes(turns - 1),
            IsFavorite = favorite,
            Turns = Enumerable.Range(0, turns).Select(index => Turn(index, hours, status)).ToArray(),
        };
    }

    private static ChatConversationTurn Turn(
        int index,
        int hours,
        ChatConversationTurnStatus status = ChatConversationTurnStatus.Completed)
    {
        var timestamp = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
            .AddHours(hours).AddMinutes(index);
        return new ChatConversationTurn
        {
            Id = Guid.NewGuid(),
            Question = $"问题 {index}",
            Markdown = $"回答 {index}",
            Status = status,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
        };
    }
}
