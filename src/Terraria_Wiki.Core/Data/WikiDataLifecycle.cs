using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using HtmlAgilityPack;
using Terraria_Wiki.Core.IO;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Data;

public sealed partial class WikiDataLifecycle
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ManagerDbService _managerDatabase;
    private readonly ContentDbService _contentDatabase;
    private readonly IWikiHttpTransport _httpTransport;
    private readonly WikiDataOperationGate _operationGate;
    private readonly SemaphoreSlim _failureFileGate = new(1, 1);

    public WikiDataLifecycle(
        ManagerDbService managerDatabase,
        ContentDbService contentDatabase,
        IWikiHttpTransport httpTransport,
        WikiDataOperationGate operationGate)
    {
        _managerDatabase = managerDatabase ?? throw new ArgumentNullException(nameof(managerDatabase));
        _contentDatabase = contentDatabase ?? throw new ArgumentNullException(nameof(contentDatabase));
        _httpTransport = httpTransport ?? throw new ArgumentNullException(nameof(httpTransport));
        _operationGate = operationGate ?? throw new ArgumentNullException(nameof(operationGate));
    }

    public Task<WikiOperationSummary> DownloadAsync(
        WikiBook book,
        WikiDataPaths paths,
        bool includeResources,
        WikiOperationOptions? options = null,
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(async () =>
        {
            options = (options ?? new WikiOperationOptions()).Validate();
            Prepare(paths);
            progress?.Report(new WikiProgress("download-start"));
            await _contentDatabase.WriteSyncMetadataAsync("sync_state", "null", cancellationToken);

            var redirects = await FetchRedirectsAsync(book, options, progress, cancellationToken);
            var pagesListed = await FetchPageListAsync(book, paths.PageListPath, options, progress, cancellationToken);
            var pages = await DownloadPagesAsync(book, paths.PageListPath, paths.ResourceListPath, paths.FailedPageListPath, options, progress, cancellationToken);
            await _contentDatabase.OptimizeSearchIndexAsync(reclaimSpace: true, cancellationToken);
            var resources = includeResources
                ? await DownloadResourcesAsyncCore(book, paths.ResourceListPath, paths.TemporaryResourceListPath, paths.FailedResourceListPath, options, progress, cancellationToken)
                : 0;

            book.UpdateTime = DateTime.Now;
            book.IsPageDownloaded = true;
            if (includeResources) book.IsResourceDownloaded = true;
            await _managerDatabase.SaveItemAsync(book, cancellationToken);
            await RefreshBookStatisticsCoreAsync(book, cancellationToken);
            CleanTransientFiles(paths);

            return new WikiOperationSummary(
                Pages: pages,
                Redirects: redirects,
                Resources: resources,
                Failed: CountLines(paths.FailedPageListPath) + CountLines(paths.FailedResourceListPath),
                Changed: pagesListed > 0 || redirects > 0 || pages > 0 || resources > 0);
        });

    public Task<WikiOperationSummary> DownloadResourcesAsync(
        WikiBook book,
        WikiDataPaths paths,
        WikiOperationOptions? options = null,
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(async () =>
        {
            options = (options ?? new WikiOperationOptions()).Validate();
            Prepare(paths, cleanTransient: false);
            if (!WikiFileOperations.HasMeaningfulContent(paths.ResourceListPath))
                throw new WikiOperationException("resource-list-invalid", "The resource list is missing or empty.");

            var resources = await DownloadResourcesAsyncCore(book, paths.ResourceListPath, paths.TemporaryResourceListPath, paths.FailedResourceListPath, options, progress, cancellationToken);
            book.IsResourceDownloaded = true;
            await _managerDatabase.SaveItemAsync(book, cancellationToken);
            await RefreshBookStatisticsCoreAsync(book, cancellationToken);
            CleanTransientFiles(paths);
            return new WikiOperationSummary(Resources: resources, Failed: CountLines(paths.FailedResourceListPath), Changed: resources > 0);
        });

    public Task<WikiOperationSummary> UpdateAsync(
        WikiBook book,
        WikiDataPaths paths,
        bool includeResources,
        WikiOperationOptions? options = null,
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => SynchronizeAsync(book, paths, includeResources,
            (options ?? new WikiOperationOptions()).Validate(), progress, cancellationToken));

    public Task<WikiOperationSummary> RetryFailuresAsync(
        WikiBook book,
        WikiDataPaths paths,
        WikiOperationOptions? options = null,
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(async () =>
        {
            options = (options ?? new WikiOperationOptions()).Validate();
            if ((await ReadSyncStateAsync(cancellationToken))?.Incomplete == true)
                return await SynchronizeAsync(book, paths, book.IsResourceDownloaded, options, progress, cancellationToken);
            Prepare(paths, cleanTransient: false);
            var pages = 0;
            var resources = 0;

            if (WikiFileOperations.HasMeaningfulContent(paths.FailedPageListPath))
            {
                DeleteIfExists(paths.TemporaryFailedPageListPath);
                DeleteIfExists(paths.UpdateResourceListPath);
                pages = await DownloadPagesAsync(book, paths.FailedPageListPath, paths.UpdateResourceListPath, paths.TemporaryFailedPageListPath, options with { PageConcurrency = 1 }, progress, cancellationToken);
                if (pages > 0)
                    await _contentDatabase.OptimizeSearchIndexAsync(cancellationToken: cancellationToken);
                await WikiFileOperations.AppendFileAsync(paths.UpdateResourceListPath, paths.ResourceListPath, cancellationToken);
                DeduplicateInPlace(paths.ResourceListPath);
                DeleteIfExists(paths.UpdateResourceListPath);
                ReplaceFailureList(paths.TemporaryFailedPageListPath, paths.FailedPageListPath);
            }

            if (book.IsResourceDownloaded && WikiFileOperations.HasMeaningfulContent(paths.FailedResourceListPath))
            {
                DeleteIfExists(paths.TemporaryFailedResourceListPath);
                resources = await DownloadResourcesAsyncCore(
                    book,
                    paths.FailedResourceListPath,
                    paths.TemporaryResourceListPath,
                    paths.TemporaryFailedResourceListPath,
                    options with { ResourceConcurrency = 1 },
                    progress,
                    cancellationToken,
                    consumeInput: true);
                ReplaceFailureList(paths.TemporaryFailedResourceListPath, paths.FailedResourceListPath);
            }

            await RefreshBookStatisticsCoreAsync(book, cancellationToken);
            CleanTransientFiles(paths);
            return new WikiOperationSummary(
                Pages: pages,
                Resources: resources,
                Failed: CountLines(paths.FailedPageListPath) + CountLines(paths.FailedResourceListPath),
                Changed: pages > 0 || resources > 0);
        });

    public bool HasFailures(WikiDataPaths paths) =>
        WikiFileOperations.HasMeaningfulContent(paths.FailedPageListPath)
        || WikiFileOperations.HasMeaningfulContent(paths.FailedResourceListPath);

    public Task ClearFailuresAsync(WikiDataPaths paths) =>
        RunExclusiveAsync(() =>
        {
            DeleteIfExists(paths.FailedPageListPath);
            DeleteIfExists(paths.FailedResourceListPath);
            return Task.FromResult(true);
        });

    public Task VacuumAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _contentDatabase.VacuumDatabaseAsync(cancellationToken);
            return true;
        });

    public Task<WikiOperationSummary> DeleteResourcesAsync(
        WikiBook book,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _contentDatabase.DeleteItemsAsync<WikiAsset>(cancellationToken);
            book.IsResourceDownloaded = false;
            await _managerDatabase.SaveItemAsync(book, cancellationToken);
            await RefreshBookStatisticsCoreAsync(book, cancellationToken);
            return new WikiOperationSummary(Changed: true);
        });

    public Task<WikiOperationSummary> DeleteWikiDataAsync(
        WikiBook book,
        WikiDataPaths paths,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backup = paths.DataDirectory + $".delete-backup-{Guid.NewGuid():N}";
            var originalExisted = Directory.Exists(paths.DataDirectory);
            var moved = false;
            var replacementCreated = false;
            try
            {
                await _contentDatabase.CloseConnection(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (originalExisted)
                {
                    Directory.Move(paths.DataDirectory, backup);
                    moved = true;
                }

                // Renaming the live directory is the commit boundary. From this
                // point deletion either completes or rolls back; caller
                // cancellation must not strand a half-installed database.
                var commitToken = CancellationToken.None;
                await _managerDatabase.DeleteItemAsync<WikiBook>(book.Id, commitToken);
                await _managerDatabase.Init(force: true, cancellationToken: commitToken);
                var reseeded = await _managerDatabase.GetItemAsync<WikiBook>(book.Id, commitToken)
                    ?? throw new WikiOperationException("wikibook-reseed-failed", $"WikiBook {book.Id} was not reseeded.");
                Directory.CreateDirectory(paths.DataDirectory);
                replacementCreated = true;
                await _contentDatabase.SwitchDatabaseAsync(paths.ContentDatabasePath, commitToken);
                await _contentDatabase.Init(seedBook: reseeded, cancellationToken: commitToken);

                if (moved && Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
                return new WikiOperationSummary(Changed: true);
            }
            catch
            {
                try
                {
                    await _contentDatabase.CloseConnection();
                    if ((moved || replacementCreated) && Directory.Exists(paths.DataDirectory))
                        Directory.Delete(paths.DataDirectory, recursive: true);
                    if (moved && Directory.Exists(backup)) Directory.Move(backup, paths.DataDirectory);
                    await _managerDatabase.SaveItemAsync(book);
                    await _contentDatabase.SwitchDatabaseAsync(paths.ContentDatabasePath);
                    await _contentDatabase.Init(seedBook: book);
                }
                catch (Exception rollbackException)
                {
                    throw new WikiOperationException("delete-rollback-failed", "Wiki data deletion failed and rollback did not complete.", rollbackException);
                }
                throw;
            }
        });

    public async Task<WikiBook> RefreshBookStatisticsAsync(
        WikiBook book,
        CancellationToken cancellationToken = default) =>
        await RefreshBookStatisticsCoreAsync(book, cancellationToken);

    private async Task<int> FetchPageListAsync(
        WikiBook book,
        string outputPath,
        WikiOperationOptions options,
        IProgress<WikiProgress>? progress,
        CancellationToken cancellationToken)
    {
        DeleteIfExists(outputPath);
        using var writer = new BatchLineWriter(outputPath);
        var pageCount = 0;
        var namespaces = new Queue<int>();
        namespaces.Enqueue(book.MainNamespace);
        if (!string.IsNullOrEmpty(book.AdditionalNamespaces))
            foreach (var value in book.AdditionalNamespaces.Split(',', StringSplitOptions.RemoveEmptyEntries))
                namespaces.Enqueue(int.Parse(value));

        while (namespaces.TryDequeue(out var wikiNamespace))
        {
            string? continuation = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var baseUrl = $"{book.ApiBaseUrl}?action=query&format=json&prop=info&inprop=url&generator=allpages&gapnamespace={wikiNamespace}&gapfilterredir=nonredirects&gaplimit=max";
                var url = baseUrl + (string.IsNullOrEmpty(continuation) ? string.Empty : $"&gapcontinue={Uri.EscapeDataString(continuation)}");
                var json = await RetryAsync(() => _httpTransport.GetStringAsync(book, new Uri(url), cancellationToken), options.MaxRetryAttempts, cancellationToken);
                var data = JsonSerializer.Deserialize<RawResponse>(json, JsonOptions);
                if (data?.Query?.Pages is not null)
                {
                    foreach (var page in data.Query.Pages.Values)
                    {
                        writer.Add($"{page.Title}|{page.Touched}");
                        pageCount++;
                    }
                }
                continuation = data?.Continue?.GapContinue;
                progress?.Report(new WikiProgress("page-list", pageCount, Detail: continuation));
            } while (!string.IsNullOrEmpty(continuation));
        }
        return pageCount;
    }

    private async Task<int> FetchRedirectsAsync(
        WikiBook book,
        WikiOperationOptions options,
        IProgress<WikiProgress>? progress,
        CancellationToken cancellationToken)
    {
        var nextUrl = book.RedirectListUrl;
        var total = 0;
        var page = 0;
        while (!string.IsNullOrEmpty(nextUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = await RetryAsync(
                () => _httpTransport.GetStringAsync(book, new Uri(book.BaseUrl + nextUrl), cancellationToken),
                options.MaxRetryAttempts,
                cancellationToken);
            var document = new HtmlDocument();
            document.LoadHtml(html);
            var items = document.DocumentNode.SelectNodes("//div[@class='mw-spcontent']//ol/li");
            if (items is null) break;

            var redirects = new List<WikiRedirect>();
            foreach (var item in items)
            {
                var links = item.SelectNodes(".//a");
                if (links is null || links.Count < 2) continue;
                redirects.Add(new WikiRedirect
                {
                    FromName = HtmlEntity.DeEntitize(links[0].InnerText),
                    ToTarget = HtmlEntity.DeEntitize(links.Last().InnerText)
                });
                total++;
            }
            await _contentDatabase.SaveItemsAsync(redirects, cancellationToken);
            page++;
            progress?.Report(new WikiProgress("redirect-list", total, Detail: page.ToString()));
            nextUrl = document.DocumentNode.SelectSingleNode("//a[@class='mw-nextlink']") is { } next
                ? HtmlEntity.DeEntitize(next.GetAttributeValue("href", string.Empty))
                : string.Empty;
            if (!string.IsNullOrEmpty(nextUrl)) await Task.Delay(500, cancellationToken);
        }
        return total;
    }

    private async Task<int> DownloadPagesAsync(
        WikiBook book,
        string pageListPath,
        string resourceListPath,
        string failurePath,
        WikiOperationOptions options,
        IProgress<WikiProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var resourceWriter = new BatchLineWriter(resourceListPath);
        var total = CountLines(pageListPath);
        var current = 0;
        await RunBatchAsync(pageListPath, failurePath, options.PageConcurrency, options.MaxRetryAttempts, async (_, line) =>
        {
            var page = ParsePageInfo(line);
            if (page is null) return;
            await DownloadPageAsync(book, page, resourceWriter, cancellationToken);
            var done = Interlocked.Increment(ref current);
            progress?.Report(new WikiProgress("page-download", done, total, page.Title));
        }, cancellationToken);

        if (File.Exists(resourceListPath)) DeduplicateInPlace(resourceListPath);
        return current;
    }

    private async Task DownloadPageAsync(WikiBook book, PageInfo pageInfo, BatchLineWriter resourceWriter, CancellationToken cancellationToken)
    {
        var existing = await _contentDatabase.GetItemAsync<WikiPage>(pageInfo.Title, cancellationToken);
        if (existing?.LastModified == pageInfo.LastModified) return;

        var url = new Uri($"{book.ApiBaseUrl}?action=parse&page={Uri.EscapeDataString(pageInfo.Title)}&prop=text&format=xml");
        var xml = await _httpTransport.GetStringAsync(book, url, cancellationToken);
        var html = XDocument.Parse(xml).Descendants("text").FirstOrDefault()?.Value;
        if (html is null)
            throw new InvalidDataException($"MediaWiki parse response did not contain page HTML for '{pageInfo.Title}'.");

        var processed = WikiContentProcessor.Process(html, book, pageInfo.Title);
        foreach (var resource in processed.ResourceUrls) resourceWriter.Add(resource);
        await _contentDatabase.WriteSyncMetadataAsync("sync_resources:" + pageInfo.Title,
            JsonSerializer.Serialize(processed.ResourceUrls), cancellationToken);
        await _contentDatabase.SavePageWithSearchAsync(new WikiPage
        {
            Title = pageInfo.Title,
            Content = processed.Html,
            LastModified = pageInfo.LastModified
        }, processed, cancellationToken);
    }

    private async Task<int> DownloadResourcesAsyncCore(
        WikiBook book,
        string resourceListPath,
        string temporaryListPath,
        string failurePath,
        WikiOperationOptions options,
        IProgress<WikiProgress>? progress,
        CancellationToken cancellationToken,
        bool consumeInput = false)
    {
        var input = resourceListPath;
        if (!consumeInput)
        {
            File.Copy(resourceListPath, temporaryListPath, overwrite: true);
            input = temporaryListPath;
        }

        var total = CountLines(input);
        var current = 0;
        var changed = 0;
        await RunBatchAsync(input, failurePath, options.ResourceConcurrency, options.MaxRetryAttempts, async (_, line) =>
        {
            var wasChanged = await DownloadResourceAsync(book, line, cancellationToken);
            if (wasChanged) Interlocked.Increment(ref changed);
            var done = Interlocked.Increment(ref current);
            progress?.Report(new WikiProgress(wasChanged ? "resource-downloaded" : "resource-skipped", done, total, WikiContentProcessor.GetFileNameFromUrl(line)));
        }, cancellationToken);
        return changed;
    }

    private async Task<bool> DownloadResourceAsync(WikiBook book, string url, CancellationToken cancellationToken)
    {
        var fileName = WikiContentProcessor.GetFileNameFromUrl(url);
        var existing = await _contentDatabase.GetItemAsync<WikiAsset>(fileName, cancellationToken);
        DateTimeOffset? modified = existing?.LastModified is { } value && value != DateTime.MinValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : null;
        var response = await _httpTransport.GetContentAsync(book, new Uri(url), modified, cancellationToken);
        if (response.IsNotModified)
        {
            if (existing is null) throw new InvalidDataException("Resource returned 304 without a local copy.");
            return false;
        }

        await _contentDatabase.SaveItemAsync(new WikiAsset
        {
            FileName = fileName,
            Data = response.Data,
            MimeType = response.ContentType,
            LastModified = response.LastModified?.UtcDateTime ?? DateTime.MinValue
        }, cancellationToken);
        return true;
    }

    private async Task RunBatchAsync(
        string inputPath,
        string failurePath,
        int concurrency,
        int retryAttempts,
        Func<int, string, Task> processor,
        CancellationToken cancellationToken)
    {
        using var provider = new BatchLineProvider(inputPath);
        var workers = Enumerable.Range(0, concurrency).Select(workerId => Task.Run(async () =>
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = provider.GetNextLine();
                if (string.IsNullOrWhiteSpace(line)) return;
                try
                {
                    await RetryAsync(() => processor(workerId, line), retryAttempts, cancellationToken);
                }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
                {
                    // Preserve legacy behavior: a missing remote resource is skipped,
                    // not persisted into the retry list.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    await AppendFailureAsync(failurePath, line, cancellationToken);
                }
            }
        }, cancellationToken));
        await Task.WhenAll(workers);
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int retryAttempts, CancellationToken cancellationToken)
    {
        var retry = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (++retry <= retryAttempts)
            {
                await WikiHttpRetry.DelayAsync(exception, retry, cancellationToken);
            }
        }
    }

    private static async Task RetryAsync(Func<Task> action, int retryAttempts, CancellationToken cancellationToken) =>
        await RetryAsync(async () =>
        {
            await action();
            return true;
        }, retryAttempts, cancellationToken);

    private async Task AppendFailureAsync(string path, string line, CancellationToken cancellationToken)
    {
        await _failureFileGate.WaitAsync(cancellationToken);
        try { await File.AppendAllLinesAsync(path, [line], cancellationToken); }
        finally { _failureFileGate.Release(); }
    }

    private async Task<WikiBook> RefreshBookStatisticsCoreAsync(
        WikiBook book,
        CancellationToken cancellationToken)
    {
        book.PageCount = await _contentDatabase.GetCountAsync<WikiPage>(cancellationToken);
        book.RedirectCount = await _contentDatabase.GetCountAsync<WikiRedirect>(cancellationToken);
        book.ResourceCount = await _contentDatabase.GetCountAsync<WikiAsset>(cancellationToken);
        book.DataSize = File.Exists(_contentDatabase.DatabasePath) ? new FileInfo(_contentDatabase.DatabasePath).Length : 0;
        await _managerDatabase.SaveItemAsync(book, cancellationToken);
        return book;
    }

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action)
    {
        using var lease = await _operationGate.TryAcquireAsync(CancellationToken.None);
        if (lease is null)
            throw new WikiOperationException("operation-in-progress", "Another Wiki data operation is already running.");
        return await action();
    }

    private async Task RunExclusiveAsync(Func<Task<bool>> action) => await RunExclusiveAsync<bool>(action);

    private static PageInfo? ParsePageInfo(string line)
    {
        var separator = line.IndexOf('|');
        if (separator <= 0 || separator == line.Length - 1) return null;
        return DateTime.TryParse(line[(separator + 1)..], out var modified)
            ? new PageInfo { Title = line[..separator], LastModified = modified }
            : null;
    }

    private static void Prepare(WikiDataPaths paths, bool cleanTransient = true)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureDataDirectory();
        if (cleanTransient) CleanTransientFiles(paths);
    }

    private static void CleanTransientFiles(WikiDataPaths paths)
    {
        foreach (var path in new[]
                 {
                     paths.PageListPath,
                     paths.TemporaryResourceListPath,
                     paths.TemporaryFailedPageListPath,
                     paths.TemporaryFailedResourceListPath,
                     paths.UpdatePageListPath,
                     paths.UpdateResourceListPath
                 })
            DeleteIfExists(path);
    }

    private static void DeduplicateInPlace(string path)
    {
        if (!File.Exists(path)) return;
        var temporary = path + $".dedupe-{Guid.NewGuid():N}";
        WikiFileOperations.WriteDistinctLines(path, temporary);
        File.Move(temporary, path, overwrite: true);
    }

    private static void ReplaceFailureList(string temporaryPath, string targetPath)
    {
        if (WikiFileOperations.HasMeaningfulContent(temporaryPath)) File.Move(temporaryPath, targetPath, overwrite: true);
        else DeleteIfExists(targetPath);
    }

    private static int CountLines(string path) => File.Exists(path) ? File.ReadLines(path).Count() : 0;

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
