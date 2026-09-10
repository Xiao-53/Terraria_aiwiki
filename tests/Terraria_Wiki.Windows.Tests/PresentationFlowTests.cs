using Avalonia.Input;
using System.Net.Sockets;
using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Presentation.Chat;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Favorites;
using Terraria_Wiki.Windows.Presentation.Library;
using Terraria_Wiki.Windows.Presentation.Markdown;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Presentation.Shell;
using Terraria_Wiki.Windows.Presentation.Wiki;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Tests;

public sealed class PresentationFlowTests
{
    [Fact]
    public void StartupRecoveryOnlyExposesTheActionForTheCurrentFailure()
    {
        using var shell = new ShellViewModel();
        var selectRequested = false;
        var webViewRequested = false;
        shell.SelectDataRootRequested += (_, _) => selectRequested = true;
        shell.GetWebView2Requested += (_, _) => webViewRequested = true;

        shell.SetStartupStatus(
            "数据目录不可用。",
            isError: true,
            StartupRecoveryKind.SelectDataRoot);
        Assert.False(shell.CanRetryStartup);
        Assert.False(shell.CanGetWebView2);
        Assert.False(shell.GetWebView2Command.CanExecute(null));
        shell.Management.SelectDataRootCommand.Execute(null);
        Assert.True(selectRequested);
        Assert.False(webViewRequested);

        shell.SetStartupStatus(
            "WebView2 不可用。",
            isError: true,
            StartupRecoveryKind.InstallWebView2);
        Assert.True(shell.CanRetryStartup);
        Assert.True(shell.CanGetWebView2);
        shell.GetWebView2Command.Execute(null);
        Assert.True(webViewRequested);

        shell.SetStartupStatus("应用已就绪。");
        Assert.False(shell.HasStartupError);
        Assert.False(shell.CanRetryStartup);
        Assert.False(shell.CanGetWebView2);
    }

    [Fact]
    public void DataRootRecoveryOpensManagementAndSelectionCanBeRetried()
    {
        using var shell = new ShellViewModel();

        shell.ShowDataRootRecovery("请选择有效的数据文件夹。");

        Assert.Equal(ShellSection.Management, shell.CurrentSection);
        Assert.True(shell.IsManagementVisible);
        Assert.Contains("请选择", shell.Management.StatusMessage, StringComparison.Ordinal);
        Assert.True(shell.Management.SelectDataRootCommand.CanExecute(null));

        shell.Management.BeginDataRootSelection();
        Assert.False(shell.Management.SelectDataRootCommand.CanExecute(null));
        shell.Management.ReportDataRootSelection("所选文件夹不可用；可以重新选择。");
        shell.Management.EndDataRootSelection();

        Assert.True(shell.Management.SelectDataRootCommand.CanExecute(null));
        Assert.Contains("重新选择", shell.Management.StatusMessage, StringComparison.Ordinal);
        shell.Management.SetDataRoot(@"C:\WikiData");
        Assert.Equal(@"C:\WikiData", shell.Management.DataRootPath);
        Assert.Equal("更改文件夹", shell.Management.DataRootActionText);
    }

    [Fact]
    public void StartupFailureClassificationSeparatesDataCompatibilityFromViewerFailures()
    {
        var conflict = MainWindow.ClassifyStartupFailure(
            new ApplicationUseCaseException("data-directory-conflict", "unsafe path C:\\secret"));
        var assets = MainWindow.ClassifyStartupFailure(
            MainWindow.WrapViewerServerFailure(new FileNotFoundException("C:\\secret\\index.html")));
        var bind = MainWindow.ClassifyStartupFailure(
            MainWindow.WrapViewerServerFailure(new ViewerServerStartException(0, new SocketException())));
        var dom = MainWindow.ClassifyStartupFailure(
            new WindowsClientStartupException("viewer-dom-ready-timeout", "unsafe detail"));

        Assert.Equal(StartupRecoveryKind.SelectDataRoot, conflict.Recovery);
        Assert.Contains("未修改", conflict.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", conflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StartupRecoveryKind.Retry, assets.Recovery);
        Assert.Contains("静态资源", assets.Message, StringComparison.Ordinal);
        Assert.Equal(StartupRecoveryKind.Retry, bind.Recovery);
        Assert.Contains("loopback", bind.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StartupRecoveryKind.Retry, dom.Recovery);
        Assert.Contains("页面", dom.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyWikiStartupClosesReaderAndOpensDownloadOnboarding()
    {
        using var shell = new ShellViewModel();

        shell.ShowEmptyWikiOnboarding();

        Assert.Equal(ShellSection.Management, shell.CurrentSection);
        Assert.True(shell.IsManagementVisible);
        Assert.Equal(ReaderPaneState.Closed, shell.ReaderState);
        Assert.False(shell.IsReaderVisible);
        Assert.False(shell.HasStartupError);
        Assert.Contains("尚无页面内容", shell.StartupStatus, StringComparison.Ordinal);
        Assert.Contains("下载页面与资源", shell.Management.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WikiPreviewOpensReaderWithoutReplacingTheSelectedMainPage()
    {
        using var shell = new ShellViewModel();

        Assert.Equal(ShellSection.Chat, shell.CurrentSection);
        Assert.True(shell.IsChatSelected);
        Assert.False(shell.IsWikiSelected);
        Assert.Equal(ReaderPaneState.Home, shell.ReaderState);
        Assert.True(shell.IsReaderVisible);

        shell.SetViewportWidth(1099.9);
        Assert.False(shell.IsReaderVisible);
        Assert.False(shell.IsReaderOverlayVisible);

        shell.SetViewportWidth(1100);
        Assert.True(shell.IsReaderVisible);

        shell.CloseReader();
        shell.OpenWikiReader();
        Assert.Equal(ShellSection.Chat, shell.CurrentSection);
        Assert.True(shell.IsReaderVisible);

        shell.NavigateFavorites();
        Assert.Equal(ShellSection.Favorites, shell.CurrentSection);
        Assert.True(shell.IsFavoritesSelected);
        Assert.True(shell.IsReaderVisible);

        shell.OpenWikiReader();
        Assert.Equal(ShellSection.Favorites, shell.CurrentSection);
        Assert.True(shell.IsReaderVisible);

        shell.NavigateChat();
        Assert.Equal(ShellSection.Chat, shell.CurrentSection);
        Assert.Equal(ReaderPaneState.Home, shell.ReaderState);
        Assert.True(shell.IsReaderVisible);
    }

    [Fact]
    public void SettingsTemporarilyHidesReaderAndLeavingRestoresItsPreviousState()
    {
        using var shell = new ShellViewModel();
        shell.OpenWikiReader();

        Assert.Equal(ReaderPaneState.Wiki, shell.ReaderState);
        Assert.True(shell.IsReaderVisible);

        shell.NavigateSettings();

        Assert.True(shell.IsSettingsSelected);
        Assert.Equal(ReaderPaneState.Wiki, shell.ReaderState);
        Assert.False(shell.IsReaderVisible);
        Assert.False(shell.IsReaderInlineVisible);
        Assert.False(shell.IsReaderOverlayVisible);

        shell.NavigateFavorites();

        Assert.True(shell.IsFavoritesSelected);
        Assert.Equal(ReaderPaneState.Wiki, shell.ReaderState);
        Assert.True(shell.IsReaderVisible);
    }

    [Fact]
    public void ReaderSplitterHasHardPaneLimitsWithoutAnAutomaticCloseThreshold()
    {
        Assert.True(ShellView.MainPaneMinWidth >= 420);
        Assert.True(ShellView.ReaderPaneMinWidth >= 400);
    }

    [Fact]
    public void ReaderUsesInlineAndOverlayModesAtExactThresholdAndEscapeClosesOverlay()
    {
        using var shell = new ShellViewModel();
        shell.OpenWikiReader();

        shell.SetViewportWidth(1100);
        Assert.True(shell.IsWideLayout);
        Assert.True(shell.IsReaderInlineVisible);

        shell.SetViewportWidth(1099.9);
        Assert.False(shell.IsWideLayout);
        Assert.True(shell.IsReaderOverlayVisible);
        Assert.True(shell.TryCloseOverlay());
        Assert.False(shell.IsReaderVisible);
        Assert.False(shell.TryCloseOverlay());
    }

    [Fact]
    public async Task LibraryPagesAndConversationHistoryFilterOpenAndClearIndependently()
    {
        var openedTitle = string.Empty;
        var openedConversation = string.Empty;
        var pageRequests = new List<PageRequest>();
        var allPages = Enumerable.Range(1, 101)
            .Select(index => new WikiPageListItem(
                $"页面 {index:000}",
                new DateTime(2026, 9, 1).AddMinutes(index)))
            .ToList();
        var store = new MemoryChatConversationStore();
        var today = Conversation("今天一", 3);
        var favorite = Conversation("今天二", 2, favorite: true);
        var yesterday = Conversation("昨天", 1);
        await store.SaveAsync(today);
        await store.SaveAsync(favorite);
        await store.SaveAsync(yesterday);
        var deleted = new List<Guid>();
        using var library = new WikiLibraryViewModel(store, title =>
        {
            openedTitle = title;
            return Task.CompletedTask;
        }, conversation =>
        {
            openedConversation = conversation.Title;
            return Task.CompletedTask;
        }, deleted.Add);
        library.Attach(
            (request, _) =>
            {
                pageRequests.Add(request);
                var filtered = allPages
                    .Where(item => string.IsNullOrWhiteSpace(request.FilterText)
                                   || item.Title.Contains(request.FilterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return Task.FromResult(new PageSlice<WikiPageListItem>(
                    filtered.Skip(request.Offset).Take(request.Limit).ToArray(),
                    filtered.Count,
                    request.Offset,
                    request.Limit));
            });

        await library.RefreshAsync();

        Assert.Equal(WikiLibraryViewModel.PageSize, library.Pages.Count);
        Assert.True(library.CanLoadMore);
        Assert.Equal(101, library.TotalCount);
        await library.LoadMoreAsync();
        Assert.Equal(101, library.Pages.Count);
        Assert.False(library.CanLoadMore);
        await library.Pages[100].OpenAsync();
        Assert.Equal("页面 101", openedTitle);

        library.Query = "101";
        await library.RefreshAsync();
        Assert.Single(library.Pages);
        Assert.Equal("101", pageRequests[^1].FilterText);
        Assert.Equal(0, pageRequests[^1].Offset);

        library.Query = string.Empty;
        await library.SelectSectionAsync(WikiLibrarySection.Conversations);
        Assert.Equal(3, library.History.Count);
        await library.History[0].OpenAsync();
        Assert.Equal("今天一", openedConversation);

        library.Query = "今天一";
        await library.RefreshAsync();
        Assert.Single(library.History);

        library.RequestClearHistory();
        Assert.True(library.IsClearConfirmationVisible);
        library.CancelClearHistory();
        Assert.False(library.IsClearConfirmationVisible);
        library.RequestClearHistory();
        await library.ConfirmClearHistoryAsync();
        Assert.Empty(library.History);
        Assert.Equal(new[] { today.Id, yesterday.Id }.Order(), deleted.Order());
        Assert.Equal("未收藏的对话历史已清空。", library.StatusMessage);
        library.Query = string.Empty;
        await library.RefreshAsync();
        Assert.Single(library.History);
        Assert.Equal("今天二", library.History[0].Title);

        library.History[0].DeleteCommand.Execute(null);
        Assert.True(library.IsDeleteConfirmationVisible);
        Assert.Contains("今天二", library.DeleteConfirmationText, StringComparison.Ordinal);
        await library.ConfirmDeleteAsync();
        Assert.Empty(library.History);
        Assert.Contains(favorite.Id, deleted);
    }

    [Fact]
    public async Task DeletingTheOpenConversationFromHistoryStartsABlankConversation()
    {
        var store = new MemoryChatConversationStore();
        var conversation = Conversation("当前对话", 1);
        await store.SaveAsync(conversation);
        using var shell = new ShellViewModel(store);
        Assert.True(shell.Chat.LoadConversation(conversation));

        await shell.Library.SelectSectionAsync(WikiLibrarySection.Conversations);
        shell.Library.History.Single().DeleteCommand.Execute(null);
        await shell.Library.ConfirmDeleteAsync();

        Assert.True(shell.Chat.IsEmpty);
        Assert.Null(shell.Chat.CurrentConversationId);
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task FavoriteRemovalFailureKeepsTheItemAndReportsASafeStatus()
    {
        var favorite = Conversation("保留收藏", 1, favorite: true);
        var store = new FailingSaveConversationStore(favorite);
        var favorites = new FavoritesViewModel(store, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await favorites.RefreshAsync();

        await favorites.ChatFavorites.Single().RemoveAsync();

        Assert.Single(favorites.ChatFavorites);
        Assert.Equal("无法更新对话收藏；现有收藏未被更改。", favorites.StatusMessage);
    }

    [Fact]
    public async Task LibraryClearFailureKeepsHistoryAndReportsSafeStatus()
    {
        var store = new ThrowingConversationStore(Conversation("保留对话", 1));
        using var library = new WikiLibraryViewModel(
            store, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { });
        library.Attach((request, _) => Task.FromResult(
            new PageSlice<WikiPageListItem>([], 0, request.Offset, request.Limit)));

        await library.SelectSectionAsync(WikiLibrarySection.Conversations);
        library.RequestClearHistory();
        await library.ConfirmClearHistoryAsync();

        Assert.Single(library.History);
        Assert.Contains("现有记录未被更改", library.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryFilterCancelsAnOlderRequestBeforePublishingTheNewResult()
    {
        var slowRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowRequestCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var library = new WikiLibraryViewModel(
            new MemoryChatConversationStore(), _ => Task.CompletedTask, _ => Task.CompletedTask, _ => { });
        library.Attach(
            async (request, token) =>
            {
                if (request.FilterText == "慢请求")
                {
                    slowRequestStarted.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        slowRequestCancelled.TrySetResult();
                        throw;
                    }
                }

                return new PageSlice<WikiPageListItem>(
                    [new WikiPageListItem(request.FilterText ?? "初始", new DateTime(2026, 9, 2))],
                    1,
                    request.Offset,
                    request.Limit);
            });

        library.Query = "慢请求";
        await slowRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        library.Query = "新结果";
        await library.RefreshAsync();
        await slowRequestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Single(library.Pages);
        Assert.Equal("新结果", library.Pages[0].Title);
        Assert.Equal("已显示 1 / 1 个页面", library.StatusMessage);
    }

    [Fact]
    public async Task LibraryNavigationOpensAnItemInTheSingleSharedReader()
    {
        string? openedTitle = null;
        using var shell = new ShellViewModel();
        shell.AttachReader(
            (title, _) =>
            {
                openedTitle = title;
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask);
        shell.Library.Attach(
            (request, _) => Task.FromResult(new PageSlice<WikiPageListItem>(
                [new WikiPageListItem("资料库页面", new DateTime(2026, 9, 2))],
                1,
                request.Offset,
                request.Limit)));

        shell.NavigateLibrary();
        await shell.Library.RefreshAsync();
        await shell.Library.Pages.Single().OpenAsync();

        Assert.Equal(ShellSection.Library, shell.CurrentSection);
        Assert.True(shell.IsLibrarySelected);
        Assert.Equal(ReaderPaneState.Wiki, shell.ReaderState);
        Assert.True(shell.IsReaderVisible);
        Assert.Equal("资料库页面", openedTitle);
    }

    [Fact]
    public void EnterSendsAfterImeCompositionAndShiftEnterKeepsNewline()
    {
        Assert.True(ChatInputBehavior.ShouldSend(Key.Enter, KeyModifiers.None));
        Assert.True(ChatInputBehavior.ShouldSend(Key.Enter, KeyModifiers.Control));
        Assert.False(ChatInputBehavior.ShouldSend(Key.Enter, KeyModifiers.Shift));
        Assert.False(ChatInputBehavior.ShouldSend(Key.Enter, KeyModifiers.None, isComposing: true));
        Assert.False(ChatInputBehavior.ShouldSend(Key.A, KeyModifiers.None));
    }

    [Fact]
    public async Task AnsweringStateClearsComposerAndTransitionsFromThinkingToCompactTrace()
    {
        var store = new MemoryChatConversationStore();
        using var chat = new ChatViewModel(
            new SafeMarkdownParser(),
            _ => Task.CompletedTask,
            () => { },
            store);
        var toolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        chat.AttachRuntime(FakeChatRuntime.Ready(async (_, onEvent, token) =>
        {
            await onEvent(new AiRunEvent(AiRunEventKind.RunStarted));
            await onEvent(new AiRunEvent(AiRunEventKind.ToolStarted, Name: "search_wiki"));
            toolStarted.TrySetResult();
            await complete.Task.WaitAsync(token);
            await onEvent(new AiRunEvent(
                AiRunEventKind.ToolFinished,
                Name: "search_wiki",
                Status: "succeeded"));
            return Answer("完成答案") with
            {
                ToolTrace = [new AiAgentToolCall { Name = "search_wiki", Status = "succeeded" }],
            };
        }));
        chat.Question = "需要分析的问题";

        var run = chat.SendAsync();
        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(string.Empty, chat.Question);
        Assert.Equal("需要分析的问题", chat.SubmittedQuestion);
        Assert.True(chat.IsThinking);
        Assert.False(chat.IsStreamingAnswer);
        Assert.False(chat.IsToolTraceVisible);
        Assert.Equal("AI 正在回答…", chat.ComposerPlaceholder);
        Assert.True(chat.IsQuestionReadOnly);
        Assert.False(chat.CanStartNewConversation);
        var activeId = chat.CurrentConversationId;
        chat.StartNewConversation();
        Assert.Equal(activeId, chat.CurrentConversationId);
        Assert.False(chat.IsEmpty);

        complete.TrySetResult();
        await run;

        Assert.False(chat.IsThinking);
        Assert.True(chat.IsToolTraceVisible);
        Assert.True(chat.IsTerminalStatusVisible);
        Assert.Equal("向 AI 提问…", chat.ComposerPlaceholder);

        chat.Question = "尚未发送的草稿";
        chat.StartNewConversation();
        Assert.True(chat.IsEmpty);
        Assert.Null(chat.CurrentConversationId);
        Assert.Equal("尚未发送的草稿", chat.Question);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task ChatStreamsToolsAndKeepsTranscriptWhileSendingPlainTextHistory()
    {
        var openedWiki = false;
        AiAgentSource? openedSource = null;
        using var chat = new ChatViewModel(
            new SafeMarkdownParser(),
            source => { openedSource = source; return Task.CompletedTask; },
            () => openedWiki = true);
        var runtime = FakeChatRuntime.Ready(async (_, onEvent, _) =>
        {
            await onEvent(new AiRunEvent(AiRunEventKind.RunStarted));
            await onEvent(new AiRunEvent(AiRunEventKind.ToolStarted, Name: "search_wiki"));
            await onEvent(new AiRunEvent(AiRunEventKind.ToolFinished, Name: "search_wiki", Status: "succeeded"));
            await onEvent(new AiRunEvent(AiRunEventKind.AnswerDelta, Delta: "# 答"));
            await onEvent(new AiRunEvent(AiRunEventKind.AnswerDelta, Delta: "案"));
            return Answer("# 答案", new AiAgentSource
            {
                RequestedTitle = "别名",
                ResolvedTitle = "真实页面",
                Anchor = "章节",
                LastModified = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            }) with
            {
                ToolTrace = [new AiAgentToolCall { Name = "search_wiki", Status = "succeeded" }],
            };
        });
        chat.AttachRuntime(runtime);
        chat.Question = " 第一个问题 ";

        await chat.SendAsync();

        Assert.Equal(ChatRunState.Completed, chat.RunState);
        Assert.Equal("第一个问题", chat.SubmittedQuestion);
        Assert.Equal(string.Empty, chat.Question);
        Assert.Equal("# 答案", chat.AnswerDocument.Source);
        Assert.Single(chat.Tools);
        Assert.Equal("完成", chat.Tools[0].Status);
        Assert.Single(chat.Sources);
        Assert.Equal("输入 3（缓存未命中/不可用，未缓存 3） · 输出 2 · 合计 5 Token", chat.UsageText);

        await chat.Sources[0].OpenAsync();
        Assert.Equal("真实页面", openedSource!.ResolvedTitle);
        chat.OpenWikiCommand.Execute(null);
        Assert.True(openedWiki);

        runtime.AnswerHandler = (_, _, _) => Task.FromResult(Answer("第二个答案"));
        chat.Question = "第二个问题";
        await chat.SendAsync();
        Assert.Equal(2, chat.Turns.Count);
        Assert.Equal("第一个问题", chat.Turns[0].Question);
        var prior = Assert.Single(runtime.LastHistory);
        Assert.Equal("第一个问题", prior.User);
        Assert.Equal("# 答案", prior.Assistant);
        Assert.Equal("第二个问题", chat.SubmittedQuestion);
        Assert.Equal("第二个答案", chat.AnswerDocument.Source);
        Assert.Empty(chat.Sources);
        Assert.Empty(chat.Tools);
    }

    [Fact]
    public async Task CompletedChatCanBeFavoritedAndRestoredWithoutNewAgentRun()
    {
        var store = new MemoryChatConversationStore();
        using var shell = new ShellViewModel(store);
        var runtime = FakeChatRuntime.Ready((_, _, _) => Task.FromResult(Answer("# 收藏答案")));
        shell.Chat.AttachRuntime(runtime);
        shell.Chat.Question = "收藏问题";
        await shell.Chat.SendAsync();

        await shell.Chat.ToggleFavoriteAsync();
        Assert.True(shell.Chat.IsFavorite);
        Assert.Single((await store.GetAllAsync()));

        shell.NavigateFavorites();
        await shell.Favorites.RefreshAsync();
        await shell.Favorites.ChatFavorites.Single().OpenAsync();

        Assert.Equal(ShellSection.Chat, shell.CurrentSection);
        Assert.Equal("收藏问题", shell.Chat.SubmittedQuestion);
        Assert.Equal("# 收藏答案", shell.Chat.AnswerDocument.Source);

        await shell.Chat.ToggleFavoriteAsync();
        Assert.False(shell.Chat.IsFavorite);
        Assert.False(Assert.Single(await store.GetAllAsync()).IsFavorite);
    }

    [Fact]
    public async Task ReaderFavoriteUsesCurrentResolvedPageAndClearsOnBack()
    {
        var reader = new ReaderPaneViewModel();
        string? changedTitle = null;
        var changedValue = false;
        reader.Attach((_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        reader.AttachFavorites(
            title => Task.FromResult(title == "收藏页"),
            (title, favorite) =>
            {
                changedTitle = title;
                changedValue = favorite;
                return Task.FromResult(favorite);
            });

        await reader.OpenAsync(new ReaderPageRequest("收藏页", "章节"));
        Assert.True(reader.IsWikiFavorite);
        await reader.ToggleFavoriteAsync();
        Assert.Equal("收藏页", changedTitle);
        Assert.False(changedValue);
        Assert.False(reader.IsWikiFavorite);

        await reader.BackAsync();
        Assert.False(reader.CanFavorite);
    }

    [Fact]
    public async Task ReaderHomePageCanBeFavoritedImmediatelyAfterRuntimeAttach()
    {
        var reader = new ReaderPaneViewModel();
        string? changedTitle = null;
        reader.Attach((_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        reader.AttachFavorites(
            _ => Task.FromResult(false),
            (title, favorite) =>
            {
                changedTitle = title;
                return Task.FromResult(favorite);
            });

        await reader.SetHomePageAsync("Terraria Wiki");

        Assert.True(reader.CanFavorite);
        await reader.ToggleFavoriteAsync();
        Assert.Equal("Terraria Wiki", changedTitle);
        Assert.True(reader.IsWikiFavorite);
        Assert.Contains("已收藏", reader.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableWikiLibraryNeverReportsAChangedFavoriteState()
    {
        var favorites = new FavoritesViewModel(
            new MemoryChatConversationStore(),
            _ => Task.CompletedTask,
            _ => Task.CompletedTask);

        Assert.False(await favorites.SetWikiFavoriteAsync("页面", favorite: true));
        Assert.False(await favorites.SetWikiFavoriteAsync("页面", favorite: false));
    }

    [Fact]
    public async Task NavigationDoesNotCancelActiveAnswerAndChatStateSurvivesRoundTrip()
    {
        using var shell = new ShellViewModel();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = FakeChatRuntime.Ready(async (_, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Answer("unreachable");
        });
        shell.Chat.AttachRuntime(runtime);
        shell.Chat.Question = "运行中的问题";
        var run = shell.Chat.SendAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        shell.OpenWikiReader();
        shell.NavigateChat();

        Assert.True(shell.Chat.IsAnswering);
        Assert.Equal(0, runtime.CancelCalls);
        Assert.Equal("运行中的问题", shell.Chat.SubmittedQuestion);

        await shell.Chat.StopAsync();
        await run;
        Assert.Equal(ChatRunState.Cancelled, shell.Chat.RunState);
        Assert.Equal(1, runtime.CancelCalls);
    }

    [Fact]
    public async Task FirstStreamingChunkIsVisibleBeforeTypedCompletionAndFinalFlushWins()
    {
        using var chat = new ChatViewModel(
            new SafeMarkdownParser(),
            _ => Task.CompletedTask,
            () => { });
        var firstDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = FakeChatRuntime.Ready(async (_, onEvent, token) =>
        {
            await onEvent(new AiRunEvent(AiRunEventKind.AnswerDelta, Delta: "# 部分"));
            firstDelta.TrySetResult();
            await finish.Task.WaitAsync(token);
            return Answer("# 最终答案");
        });
        chat.AttachRuntime(runtime);
        chat.Question = "流式问题";

        var run = chat.SendAsync();
        await firstDelta.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("# 部分", chat.AnswerDocument.Source);

        finish.TrySetResult();
        await run;
        Assert.Equal("# 最终答案", chat.AnswerDocument.Source);
    }

    [Fact]
    public void ComposerAcceptsDraftBeforeRuntimeIsReadyButCannotSendIt()
    {
        using var chat = new ChatViewModel(
            new SafeMarkdownParser(),
            _ => Task.CompletedTask,
            () => { });

        Assert.True(chat.CanEditQuestion);
        Assert.False(chat.IsQuestionReadOnly);

        chat.Question = "Runtime 启动期间的草稿";

        Assert.Equal("Runtime 启动期间的草稿", chat.Question);
        Assert.False(chat.CanSend);
    }

    [Fact]
    public async Task DisabledAiCanRetryWithoutLeavingChatAndWikiActionRemainsAvailable()
    {
        var openedWiki = false;
        using var chat = new ChatViewModel(
            new SafeMarkdownParser(),
            _ => Task.CompletedTask,
            () => openedWiki = true);
        var runtime = new FakeChatRuntime(new ChatAvailability(
            ChatAvailabilityState.Disabled,
            "未配置 AI；离线 Wiki 仍可使用。",
            "model_authentication"));
        chat.AttachRuntime(runtime);

        Assert.True(chat.IsAiUnavailable);
        Assert.True(chat.CanRetry);
        Assert.False(chat.IsQuestionReadOnly);
        chat.Question = "可再次提问";
        Assert.False(chat.CanSend);
        chat.OpenWikiCommand.Execute(null);
        Assert.True(openedWiki);

        await chat.RetryAsync();
        Assert.False(chat.IsAiUnavailable);
        Assert.True(chat.CanSend);
        Assert.False(chat.IsQuestionReadOnly);
        Assert.Equal(1, runtime.RetryCalls);
    }

    [Fact]
    public async Task RuntimeFailureRemainsOnChatAndOffersSafeRetryAndWikiFallback()
    {
        var openedWiki = false;
        using var chat = new ChatViewModel(
            new SafeMarkdownParser(),
            _ => Task.CompletedTask,
            () => openedWiki = true);
        FakeChatRuntime? runtime = null;
        runtime = FakeChatRuntime.Ready((_, _, _) =>
        {
            runtime!.SetStatus(new ChatAvailability(
                ChatAvailabilityState.Failed,
                "AI Runtime 已停止；离线 Wiki 仍可使用。",
                "runtime_crashed"));
            throw new AiRuntimeException("runtime_crashed");
        });
        chat.AttachRuntime(runtime);
        chat.Question = "故障问题";

        await chat.SendAsync();

        Assert.Equal(ChatRunState.Failed, chat.RunState);
        Assert.True(chat.IsAiUnavailable);
        Assert.True(chat.CanRetry);
        Assert.DoesNotContain("path", chat.StatusMessage, StringComparison.OrdinalIgnoreCase);
        chat.OpenWikiCommand.Execute(null);
        Assert.True(openedWiki);
    }

    [Fact]
    public async Task TrustedSourceOpensResolvedTitleAndAnchorInSharedReader()
    {
        using var shell = new ShellViewModel();
        shell.SetViewportWidth(900);
        Assert.False(shell.IsReaderVisible);
        var targets = new List<string>();
        shell.AttachReader(
            (target, _) => { targets.Add(target); return Task.CompletedTask; },
            _ => Task.CompletedTask);
        var runtime = FakeChatRuntime.Ready((_, _, _) => Task.FromResult(Answer(
            "答案",
            new AiAgentSource
            {
                RequestedTitle = "请求页",
                ResolvedTitle = "解析页",
                Anchor = "精确锚点",
            })));
        shell.Chat.AttachRuntime(runtime);
        shell.Chat.Question = "问题";
        await shell.Chat.SendAsync();

        await shell.Chat.Sources.Single().OpenAsync();

        Assert.Equal(ReaderPaneState.ChatSource, shell.ReaderState);
        Assert.True(shell.IsReaderOverlayVisible);
        Assert.Equal("解析页#精确锚点", Assert.Single(targets));

        shell.OpenWikiReader();
        shell.NavigateChat();
        Assert.Equal("答案", shell.Chat.AnswerDocument.Source);
    }

    private static AiAgentAnswer Answer(string markdown, params AiAgentSource[] sources) => new()
    {
        Status = "answered",
        Markdown = markdown,
        Sources = sources,
        Usage = new AiAgentUsage
        {
            Requests = 1,
            ToolCalls = sources.Length,
            InputTokens = 3,
            CachedInputTokens = 0,
            OutputTokens = 2,
            TotalTokens = 5,
            CostUsd = null,
        },
        ToolTrace = [],
    };

    private sealed class FakeChatRuntime(ChatAvailability status) : IChatRuntimeGateway
    {
        public event EventHandler<ChatAvailability>? StatusChanged;
        public ChatAvailability Status { get; private set; } = status;
        public int CancelCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public IReadOnlyList<AiConversationHistoryTurn> LastHistory { get; private set; } = [];
        public Func<string, Func<AiRunEvent, ValueTask>, CancellationToken, Task<AiAgentAnswer>> AnswerHandler { get; set; } =
            (_, _, _) => Task.FromResult(Answer("默认答案"));

        public static FakeChatRuntime Ready(
            Func<string, Func<AiRunEvent, ValueTask>, CancellationToken, Task<AiAgentAnswer>> answer)
        {
            var runtime = new FakeChatRuntime(new ChatAvailability(ChatAvailabilityState.Ready, "AI 已就绪。"));
            runtime.AnswerHandler = answer;
            return runtime;
        }

        public Task<AiAgentAnswer> AnswerAsync(
            string question,
            IReadOnlyList<AiConversationHistoryTurn> history,
            Func<AiRunEvent, ValueTask> onEvent,
            CancellationToken cancellationToken)
        {
            LastHistory = history;
            return AnswerHandler(question, onEvent, cancellationToken);
        }

        public Task CancelAsync()
        {
            CancelCalls++;
            return Task.CompletedTask;
        }

        public Task RetryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RetryCalls++;
            Status = new ChatAvailability(ChatAvailabilityState.Ready, "AI 已就绪。");
            StatusChanged?.Invoke(this, Status);
            return Task.CompletedTask;
        }

        public void SetStatus(ChatAvailability status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    private static ChatConversation Conversation(string title, int hours, bool favorite = false)
    {
        var timestamp = new DateTimeOffset(2026, 9, 5, hours, 0, 0, TimeSpan.Zero);
        return new ChatConversation
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            IsFavorite = favorite,
            Turns =
            [
                new ChatConversationTurn
                {
                    Id = Guid.NewGuid(),
                    Question = title + "的问题",
                    Markdown = title + "的回答",
                    Status = ChatConversationTurnStatus.Completed,
                    CreatedAt = timestamp,
                    UpdatedAt = timestamp,
                },
            ],
        };
    }

    private sealed class ThrowingConversationStore(ChatConversation conversation) : IChatConversationStore
    {
        public Task<IReadOnlyList<ChatConversation>> GetAllAsync(
            string? filter = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatConversation>>([conversation]);

        public Task<ChatConversation?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatConversation?>(conversation.Id == id ? conversation : null);

        public Task SaveAsync(ChatConversation value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteUnfavoritedAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated");
    }

    private sealed class FailingSaveConversationStore(ChatConversation conversation) : IChatConversationStore
    {
        public Task<IReadOnlyList<ChatConversation>> GetAllAsync(
            string? filter = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatConversation>>([conversation]);

        public Task<ChatConversation?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatConversation?>(conversation.Id == id ? conversation : null);

        public Task SaveAsync(ChatConversation value, CancellationToken cancellationToken = default) =>
            throw new IOException("simulated");

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteUnfavoritedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

public sealed class WikiPresentationTests
{
    [Fact]
    public async Task SearchDebouncesAndCancelsPreviousRequest()
    {
        var firstCancelled = false;
        using var wiki = new WikiViewModel(_ => Task.CompletedTask);
        wiki.Attach(async (query, token) =>
        {
            if (query == "first")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { firstCancelled = true; throw; }
            }
            return [new SearchResultItem { Title = query, MatchKind = WikiSearchMatchKind.ExactTitle }];
        });

        wiki.Query = "first";
        await Task.Delay(230);
        wiki.Query = "second";
        await Task.Delay(300);

        Assert.True(firstCancelled);
        Assert.Equal("second", Assert.Single(wiki.Results).Title);
    }

    [Fact]
    public async Task SearchReportsNoResultsAndDataSourceUnavailableSafely()
    {
        using var wiki = new WikiViewModel(_ => Task.CompletedTask);
        wiki.Attach((_, _) => Task.FromResult<IReadOnlyList<SearchResultItem>>([]));
        wiki.Query = "missing";
        await wiki.SearchNowAsync();
        Assert.True(wiki.IsEmpty);
        Assert.Equal("没有找到结果。", wiki.StatusMessage);

        wiki.Attach((_, _) => throw new WikiQueryException(
            WikiQueryErrorCode.DataSourceUnavailable,
            "sensitive path",
            new IOException("db")));
        wiki.Query = "unavailable";
        await wiki.SearchNowAsync();
        Assert.Empty(wiki.Results);
        Assert.DoesNotContain("sensitive", wiki.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不可用", wiki.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultOpensInProvidedSharedReaderAction()
    {
        SearchResultItem? opened = null;
        using var wiki = new WikiViewModel(result => { opened = result; return Task.CompletedTask; });
        wiki.Attach((_, _) => Task.FromResult<IReadOnlyList<SearchResultItem>>(
            [new SearchResultItem { Title = "页面", MatchKind = WikiSearchMatchKind.ExactTitle }]));
        wiki.Query = "页面";
        await wiki.SearchNowAsync();

        await wiki.Results.Single().OpenAsync();

        Assert.Equal("页面", opened!.Title);
    }
}
