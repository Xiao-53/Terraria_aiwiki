using System.Net;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Tests;

public sealed class WikiDataLifecycleTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "terraria-core-lifecycle-" + Guid.NewGuid().ToString("N"));
    private readonly List<DatabaseService> _databases = [];

    [Fact]
    public async Task Download_populates_pages_redirects_resources_and_manager_state()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transport = new FixtureTransport();
        var (lifecycle, manager, content) = await CreateLifecycleAsync(book, paths, transport);

        var summary = await lifecycle.DownloadAsync(
            book,
            paths,
            includeResources: true,
            new WikiOperationOptions(PageConcurrency: 1, ResourceConcurrency: 1, MaxRetryAttempts: 0));

        Assert.Equal(1, summary.Pages);
        Assert.Equal(1, summary.Redirects);
        Assert.Equal(1, summary.Resources);
        Assert.Equal(0, summary.Failed);
        Assert.True(book.IsPageDownloaded);
        Assert.True(book.IsResourceDownloaded);

        var page = await content.GetItemAsync<WikiPage>("Sword");
        Assert.NotNull(page);
        Assert.Contains("/src/Sword.png", page.Content);
        Assert.Equal("Sword", (await content.GetItemAsync<WikiRedirect>("Blade"))?.ToTarget);
        Assert.Equal([1, 2, 3], (await content.GetItemAsync<WikiAsset>("Sword.png"))?.Data);

        var persistedBook = await manager.GetItemAsync<WikiBook>(book.Id);
        Assert.True(persistedBook?.IsPageDownloaded);
        Assert.Equal(2, persistedBook?.PageCount); // placeholder + downloaded page
        Assert.Contains("https://wiki.test/images/Sword.png", await File.ReadAllTextAsync(paths.ResourceListPath));
    }

    [Fact]
    public async Task A_second_mutating_operation_is_rejected_while_the_first_is_running()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transport = new BlockingTransport();
        var (lifecycle, _, _) = await CreateLifecycleAsync(book, paths, transport);
        using var cancellation = new CancellationTokenSource();

        var first = lifecycle.DownloadAsync(book, paths, false, cancellationToken: cancellation.Token);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<WikiOperationException>(
            () => lifecycle.DownloadAsync(book, paths, false));
        Assert.Equal("operation-in-progress", error.Code);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task Package_and_lifecycle_mutations_share_the_same_operation_gate()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transport = new BlockingTransport();
        var gate = new WikiDataOperationGate();
        var (lifecycle, manager, content) = await CreateLifecycleAsync(book, paths, transport, gate);
        var packages = new WikiPackageTransferService(manager, content, gate);
        using var cancellation = new CancellationTokenSource();

        var first = lifecycle.DownloadAsync(book, paths, false, cancellationToken: cancellation.Token);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<WikiPackageException>(() => packages.ExportAsync(
            book,
            paths,
            Path.Combine(_root, "blocked.pkg"),
            "1.0.0",
            content));
        Assert.Equal("package-operation-in-progress", error.Code);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task Clearing_failure_files_uses_the_shared_operation_gate()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transport = new BlockingTransport();
        var gate = new WikiDataOperationGate();
        var (lifecycle, _, _) = await CreateLifecycleAsync(book, paths, transport, gate);
        using var cancellation = new CancellationTokenSource();

        var first = lifecycle.DownloadAsync(book, paths, false, cancellationToken: cancellation.Token);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<WikiOperationException>(
            () => lifecycle.ClearFailuresAsync(paths));
        Assert.Equal("operation-in-progress", error.Code);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    [Fact]
    public async Task Update_downloads_changed_page_and_preserves_asset_on_304()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transport = new FixtureTransport();
        var (lifecycle, _, content) = await CreateLifecycleAsync(book, paths, transport);
        var options = new WikiOperationOptions(PageConcurrency: 1, ResourceConcurrency: 1, MaxRetryAttempts: 0);
        await lifecycle.DownloadAsync(book, paths, includeResources: true, options);

        transport.PageTouched = "2026-02-01T00:00:00Z";
        transport.ResourceStatus = HttpStatusCode.NotModified;
        var summary = await lifecycle.UpdateAsync(book, paths, includeResources: true, options);

        Assert.True(summary.Changed);
        Assert.Equal(1, summary.Pages);
        Assert.Equal(0, summary.Resources);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            (await content.GetItemAsync<WikiPage>("Sword"))!.LastModified.ToUniversalTime());
        Assert.Equal([1, 2, 3], (await content.GetItemAsync<WikiAsset>("Sword.png"))!.Data);
        Assert.NotNull(transport.LastIfModifiedSince);
    }

    [Fact]
    public async Task Failed_page_is_persisted_then_retry_clears_it_without_creating_resource_failure()
    {
        var book = CreateBook();
        var paths = WikiDataPaths.Create(_root, book);
        var transport = new FixtureTransport { ParseFailuresRemaining = 1 };
        var (lifecycle, _, content) = await CreateLifecycleAsync(book, paths, transport);
        var options = new WikiOperationOptions(PageConcurrency: 1, ResourceConcurrency: 1, MaxRetryAttempts: 0);

        var failed = await lifecycle.DownloadAsync(book, paths, includeResources: false, options);
        Assert.Equal(1, failed.Failed);
        Assert.True(lifecycle.HasFailures(paths));

        var retried = await lifecycle.RetryFailuresAsync(book, paths, options);

        Assert.Equal(1, retried.Pages);
        Assert.Equal(0, retried.Failed);
        Assert.False(lifecycle.HasFailures(paths));
        Assert.NotNull(await content.GetItemAsync<WikiPage>("Sword"));
        Assert.Contains("https://wiki.test/images/Sword.png", await File.ReadAllTextAsync(paths.ResourceListPath));
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var database in _databases.AsEnumerable().Reverse())
        {
            try { await database.CloseConnection(); } catch { }
        }

        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private async Task<(WikiDataLifecycle Lifecycle, ManagerDbService Manager, ContentDbService Content)> CreateLifecycleAsync(
        WikiBook book,
        WikiDataPaths paths,
        IWikiHttpTransport transport,
        WikiDataOperationGate? operationGate = null)
    {
        var manager = new ManagerDbService(paths.ManagerDatabasePath);
        var content = new ContentDbService(paths.ContentDatabasePath);
        _databases.Add(manager);
        _databases.Add(content);
        await manager.Init();
        await manager.SaveItemAsync(book);
        await content.Init(seedBook: book);
        return (new WikiDataLifecycle(
            manager,
            content,
            transport,
            operationGate ?? new WikiDataOperationGate()), manager, content);
    }

    private static WikiBook CreateBook() => new()
    {
        Id = 99,
        Title = "Fixture Wiki",
        ApiBaseUrl = "https://wiki.test/api.php",
        BaseUrl = "https://wiki.test",
        RedirectListUrl = "/redirects",
        MainNamespace = 0,
        DataFolder = "Fixture_Wiki",
        DefaultPageTitle = "Home",
        DefaultPageContent = "placeholder"
    };

    private sealed class FixtureTransport : IWikiHttpTransport
    {
        public string PageTouched { get; set; } = "2026-01-01T00:00:00Z";
        public HttpStatusCode ResourceStatus { get; set; } = HttpStatusCode.OK;
        public int ParseFailuresRemaining { get; set; }
        public DateTimeOffset? LastIfModifiedSince { get; private set; }

        public Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = uri.AbsoluteUri;
            if (value.EndsWith("/redirects", StringComparison.Ordinal))
            {
                return Task.FromResult("<div class='mw-spcontent'><ol><li><a>Blade</a><a>Sword</a></li></ol></div>");
            }

            if (value.Contains("action=query", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    "{\"query\":{\"pages\":{\"1\":{\"title\":\"Sword\",\"touched\":\""
                    + PageTouched
                    + "\",\"fullurl\":\"https://wiki.test/Sword\"}}}}");
            }

            if (value.Contains("action=parse", StringComparison.Ordinal))
            {
                if (ParseFailuresRemaining-- > 0)
                    throw new HttpRequestException("fixture page failure");
                return Task.FromResult("""
                    <api><parse><text><![CDATA[<p>A useful sword.</p><img src="/images/Sword.png" />]]></text></parse></api>
                    """);
            }

            throw new InvalidOperationException($"Unexpected fixture URL: {uri}");
        }

        public Task<WikiHttpContentResponse> GetContentAsync(
            WikiBook book,
            Uri uri,
            DateTimeOffset? ifModifiedSince = null,
            CancellationToken cancellationToken = default)
        {
            LastIfModifiedSince = ifModifiedSince;
            return Task.FromResult(new WikiHttpContentResponse(
                ResourceStatus,
                ResourceStatus == HttpStatusCode.NotModified ? [] : [1, 2, 3],
                "image/png",
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        }
    }

    private sealed class BlockingTransport : IWikiHttpTransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }

        public Task<WikiHttpContentResponse> GetContentAsync(
            WikiBook book,
            Uri uri,
            DateTimeOffset? ifModifiedSince = null,
            CancellationToken cancellationToken = default) =>
            throw new UnreachableException();
    }
}
