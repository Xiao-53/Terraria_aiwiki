using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SQLite;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;
using Xunit.Abstractions;

namespace Terraria_Wiki.Core.Tests;

public sealed class RealWikiSyncAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Explicit_copy_checks_three_real_media_samples_conditionally()
    {
        var root = Environment.GetEnvironmentVariable("TERRARIA_SYNC_MEDIA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var manager = new ManagerDbService(Path.Combine(root, "Manager.db"));
        await manager.Init();
        var book = (await manager.GetItemAsync<WikiBook>(1))!;
        var paths = WikiDataPaths.Create(root, book);
        var db = new ContentDbService(paths.ContentDatabasePath);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var http = new MeasuringTransport(new HttpClientWikiHttpTransport(client));
        var backup = paths.ResourceListPath + ".sample-backup";
        try
        {
            await db.Init(seedBook: book);
            var urls = (await db.ReadSyncResourceUrlsAsync(null, default)).Values.SelectMany(value => value).Distinct(StringComparer.Ordinal).Take(3).ToArray();
            Assert.Equal(3, urls.Length);
            File.Copy(paths.ResourceListPath, backup);
            await File.WriteAllLinesAsync(paths.ResourceListPath, urls);
            var timer = Stopwatch.StartNew();
            var summary = await new WikiDataLifecycle(manager, db, http, new WikiDataOperationGate()).DownloadResourcesAsync(
                book, paths, new WikiOperationOptions(ResourceConcurrency: 1, MaxRetryAttempts: 3));
            Assert.Equal(0, summary.Failed);
            book.IsPageDownloaded = await db.GetCountAsync<WikiPage>() > 1;
            await manager.SaveItemAsync(book);
            await File.WriteAllTextAsync(Path.Combine(root, "media-samples.json"), JsonSerializer.Serialize(new
            {
                Stage = "real-conditional-media-samples", Samples = urls.Length, http.Requests, http.Bytes,
                Milliseconds = timer.Elapsed.TotalMilliseconds, Summary = summary
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (File.Exists(backup)) File.Move(backup, paths.ResourceListPath, overwrite: true);
            await db.CloseConnection(); await manager.CloseConnection();
        }
    }

    [Fact]
    public async Task Explicit_source_is_snapshotted_before_real_update()
    {
        var sourcePath = Environment.GetEnvironmentVariable("TERRARIA_SYNC_SOURCE_DB");
        var evidenceRoot = Environment.GetEnvironmentVariable("TERRARIA_SYNC_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(evidenceRoot)) return;
        Assert.True(Path.IsPathFullyQualified(sourcePath));
        Assert.True(Path.IsPathFullyQualified(evidenceRoot));
        var root = Environment.GetEnvironmentVariable("TERRARIA_SYNC_RESUME_ROOT") ??
            Path.Combine(evidenceRoot, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(root);
        var manager = new ManagerDbService(Path.Combine(root, "Manager.db"));
        await manager.Init();
        var book = (await manager.GetItemAsync<WikiBook>(1))!;
        var paths = WikiDataPaths.Create(root, book);
        paths.EnsureDataDirectory();
        Assert.NotEqual(Path.GetFullPath(sourcePath), paths.ContentDatabasePath);
        if (!File.Exists(paths.ContentDatabasePath))
            using (var source = new SQLiteConnection(sourcePath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex))
                source.Backup(paths.ContentDatabasePath);
        var db = new ContentDbService(paths.ContentDatabasePath);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var http = new MeasuringTransport(new HttpClientWikiHttpTransport(client)) { TracePath = Path.Combine(root, "http.log") };
        var lifecycle = new WikiDataLifecycle(manager, db, http, new WikiDataOperationGate());
        var evidence = new List<object>();
        try
        {
            await db.Init(seedBook: book);
            var initialPages = await db.GetCountAsync<WikiPage>();
            book.IsPageDownloaded = initialPages > 1;
            book.IsResourceDownloaded = await db.GetCountAsync<WikiAsset>() > 0;
            await manager.SaveItemAsync(book);
            var initialFavorites = (await db.GetItemsAsync<WikiFavorite>()).Select(f => f.WikiTitle).Order().ToArray();
            // Baseline uses only remote directory metadata and the legacy per-page HTML reads.
            var timer = Stopwatch.StartNew();
            if (Environment.GetEnvironmentVariable("TERRARIA_SYNC_RESUME_ROOT") is null)
            {
                var reader = new WikiSyncReader(book, http, 1, default);
                var listing = await reader.DirectoryAsync();
                foreach (var page in listing.Where(p => !p.Redirect)) await db.GetItemAsync<WikiPage>(page.Title);
                evidence.Add(new { Stage = "legacy-metadata-and-html-check-lower-bound", Milliseconds = timer.Elapsed.TotalMilliseconds,
                    http.Requests, http.Bytes, Pages = initialPages, Note = "Excludes legacy HTML Special:ListRedirects requests; conservative lower bound." });
            }
            http.Reset();
            var includeResources = Environment.GetEnvironmentVariable("TERRARIA_SYNC_INCLUDE_RESOURCES") == "1";
            for (var run = 0; run < 2; run++)
            {
                timer.Restart();
                var progress = new ImmediateProgress(value =>
                {
                    File.AppendAllText(Path.Combine(root, "progress.log"), $"{DateTimeOffset.UtcNow:O} {value.Code} {value.Current}/{value.Total} {value.Detail}\n");
                });
                var summary = await lifecycle.UpdateAsync(book, paths, includeResources,
                    new WikiOperationOptions(PageConcurrency: 1, MaxRetryAttempts: 3), progress);
                evidence.Add(new { Stage = run == 0 ? "baseline-sync" : "incremental-sync", Milliseconds = timer.Elapsed.TotalMilliseconds,
                    http.Requests, http.Bytes, http.ParseRequests, IncludeResources = includeResources, Summary = summary });
                await File.WriteAllTextAsync(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                Assert.Equal(0, summary.Failed);
                http.Reset();
            }
            Assert.Equal(initialFavorites, (await db.GetItemsAsync<WikiFavorite>()).Select(f => f.WikiTitle).Order().ToArray());
            var wikiReader = new WikiReader(db);
            var search = await wikiReader.SearchAsync("雪人", new WikiSearchOptions(10));
            Assert.NotEmpty(search);
            Assert.Equal(search.Count, search.Select(result => result.CanonicalTitle).Distinct().Count());
            Assert.NotNull(await db.GetItemAsync<WikiPage>(search[0].CanonicalTitle));
            Assert.True((await wikiReader.ReadAsync(search[0].CanonicalTitle)).IsFound);
            output.WriteLine(root);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "failure.txt"), exception.ToString());
            await File.WriteAllTextAsync(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally { await db.CloseConnection(); await manager.CloseConnection(); }
    }

    private sealed class ImmediateProgress(Action<WikiProgress> action) : IProgress<WikiProgress>
    {
        public void Report(WikiProgress value) => action(value);
    }

    private sealed class MeasuringTransport(IWikiHttpTransport inner) : IWikiHttpTransport
    {
        public long Requests, Bytes, ParseRequests;
        public string? TracePath;
        private readonly object _traceGate = new();
        public void Reset() { Requests = 0; Bytes = 0; ParseRequests = 0; }
        public async Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Requests);
            if (TracePath is not null)
                lock (_traceGate) File.AppendAllText(TracePath, $"{DateTimeOffset.UtcNow:O} {uri.Query}\n");
            if (uri.Query.Contains("action=parse", StringComparison.Ordinal)) Interlocked.Increment(ref ParseRequests);
            var result = await inner.GetStringAsync(book, uri, cancellationToken);
            Interlocked.Add(ref Bytes, Encoding.UTF8.GetByteCount(result));
            return result;
        }
        public async Task<WikiHttpContentResponse> GetContentAsync(WikiBook book, Uri uri, DateTimeOffset? ifModifiedSince = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Requests);
            var result = await inner.GetContentAsync(book, uri, ifModifiedSince, cancellationToken);
            Interlocked.Add(ref Bytes, result.Data.Length);
            return result;
        }
    }
}
