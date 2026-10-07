using System.Text.Json;
using System.Collections.Concurrent;
using System.Net;
using HtmlAgilityPack;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Data;

public sealed partial class WikiDataLifecycle
{
    private static async Task<WikiSyncState?> CaptureDownloadBaselineAsync(WikiSyncReader reader, bool includeResources)
    {
        try
        {
            var started = await reader.ServerTimeAsync();
            var anchor = (await reader.EventsAsync(null, started)).LastOrDefault();
            if (anchor is null || anchor.Time > started) return null;
            // Keep the START cursor so updates replay changes made during the download.
            return new WikiSyncState
            {
                Identity = reader.Identity, CoveredThrough = started, LastFullScan = started,
                AnchorId = anchor.Id, AnchorTime = anchor.Time, ResourcesCovered = includeResources
            };
        }
        catch (SyncCoverageException) { return null; }
    }

    private async Task<WikiSyncState?> ReadSyncStateAsync(CancellationToken token)
    {
        var json = await _contentDatabase.ReadSyncMetadataAsync("sync_state", token);
        return WikiSyncState.Parse(json);
    }

    private Task SaveSyncStateAsync(WikiSyncState state, CancellationToken token) =>
        _contentDatabase.WriteSyncMetadataAsync("sync_state", JsonSerializer.Serialize(state), token);

    private async Task<WikiOperationSummary> SynchronizeAsync(WikiBook book, WikiDataPaths paths,
        bool includeResources, WikiOperationOptions options, IProgress<WikiProgress>? progress, CancellationToken token)
    {
        Prepare(paths);
        progress?.Report(new WikiProgress("update-start"));
        var reader = new WikiSyncReader(book, _httpTransport, options.MaxRetryAttempts, token, progress);
        var previous = await ReadSyncStateAsync(token);
        var mediaQueue = await _contentDatabase.ReadSyncMediaQueueAsync(reader.Identity, token);
        var upper = await reader.ServerTimeAsync();
        var quick = options.UpdateMode == WikiUpdateMode.Quick;
        string? fallback = options.UpdateMode == WikiUpdateMode.Full ? "explicit-full-update" : previous is null ? "baseline-missing" : !previous.Matches(book) ? "identity-changed" :
            previous.Incomplete && !(quick && previous.DependenciesSkipped && !previous.ForcePageRefresh) ? "page-recovery" : !mediaQueue.Valid ? "media-queue-invalid" :
            includeResources && !previous.ResourcesCovered ? "media-baseline-missing" :
            previous.CoveredThrough == default || previous.CoveredThrough > upper ||
            previous.LastFullScan == default || previous.LastFullScan > previous.CoveredThrough ? "invalid-cursor" :
            !quick && previous.DependenciesSkipped ? "quick-update-reconciliation" :
            !quick && options.FullScanIntervalDays is { } intervalDays && (upper - previous.LastFullScan).TotalDays >= intervalDays ? "periodic" : null;
        if (quick && fallback is not null)
            throw new WikiOperationException("quick-update-requires-full", "Quick update requires a trustworthy baseline.");
        var full = fallback is not null;
        var forceAll = options.UpdateMode == WikiUpdateMode.Full || fallback == "quick-update-reconciliation" || (!quick && previous?.ForcePageRefresh == true);
        var forced = new HashSet<string>(StringComparer.Ordinal);
        var fileEvents = new HashSet<string>(StringComparer.Ordinal);
        var events = new List<SyncEvent>();
        var candidates = new List<SyncPage>();

        if (!full)
        {
            try
            {
                progress?.Report(new WikiProgress("sync-discovery"));
                if (!await reader.VerifyAnchorAsync(previous!)) throw new SyncCoverageException("anchor-unavailable");
                events = await reader.EventsAsync(previous!.CoveredThrough.AddMinutes(-5), upper);
                var titles = new HashSet<string>(StringComparer.Ordinal);
                var dependencies = new List<(string Title, bool File)>();
                var coveredEvents = (previous.CoveredEventIds ?? []).ToHashSet();
                foreach (var entry in events.Where(entry => !coveredEvents.Contains(entry.Id)))
                {
                    if (entry.Type == "categorize") continue;
                    if (entry.Type is not ("edit" or "new" or "log") ||
                        (entry.Type == "log" && entry.LogType is not ("move" or "delete" or "upload" or "protect" or "patrol")))
                        throw new SyncCoverageException("unhandled-content-event");
                    if (entry.LogType is "protect" or "patrol") continue;
                    if (!quick && entry.Namespace == 828) throw new SyncCoverageException("module-dependency-not-provable");
                    titles.Add(entry.Title);
                    dependencies.Add((entry.Title, entry.Namespace == 6));
                    if (entry.Namespace == 6) fileEvents.Add(entry.Title);
                    if (entry.Target is { } target)
                    {
                        titles.Add(target);
                        dependencies.Add((target, entry.Namespace == 6));
                        if (entry.Namespace == 6) fileEvents.Add(target);
                    }
                }
                forced = quick ? titles.ToHashSet(StringComparer.Ordinal) : await reader.DependenciesAsync(dependencies);
                titles.UnionWith(forced);
                candidates = await reader.CurrentAsync(titles);
            }
            catch (SyncCoverageException exception)
            {
                if (quick) throw new WikiOperationException("quick-update-requires-full", "Quick update cannot establish log coverage.", exception);
                full = true;
                forceAll = exception.Message != "anchor-unavailable"; // Unknown dependencies cannot trust touched alone.
                fallback = exception.Message;
            }
        }

        IReadOnlyList<WikiPageMetadata> local;
        Dictionary<string, WikiRedirect> redirects;
        if (full)
        {
            // Ask before requesting the directory or marking any page incomplete.
            if (options.ConfirmFullScanAsync is { } confirm)
            {
                progress?.Report(new WikiProgress("sync-full-scan-approval", Detail: fallback));
                var approved = await confirm(fallback!, token).WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (!approved) throw new WikiOperationException("full-scan-declined", "Full scan was declined.");
            }
            progress?.Report(new WikiProgress("sync-full-scan", Detail: fallback));
            // Obtain the complete listing before even calculating a deletion difference.
            candidates = await reader.DirectoryAsync();
            local = await _contentDatabase.GetPageMetadataAsync(cancellationToken: token);
            redirects = (await _contentDatabase.GetItemsAsync<WikiRedirect>(token)).ToDictionary(r => r.FromName, StringComparer.Ordinal);
            var present = candidates.Select(p => p.Title).ToHashSet(StringComparer.Ordinal);
            var absent = local.Select(p => p.Title).Concat(redirects.Keys).Distinct(StringComparer.Ordinal).Where(t => !present.Contains(t));
            candidates.AddRange(await reader.CurrentAsync(absent));
            try
            {
                // This anchor is at/before the START of the scan. Never advance to its end.
                events = await reader.EventsAsync(null, upper);
                await reader.DependenciesAsync([("Template:__TerrariaAIWiki_sync_probe__", false), ("File:__TerrariaAIWiki_sync_probe__.png", true)]);
            }
            catch (SyncCoverageException exception)
            {
                progress?.Report(new WikiProgress("sync-capability-unavailable", Detail: exception.Message));
                events.Clear(); // This source remains in full-scan mode until capabilities can be established.
            }
        }
        else
        {
            local = await _contentDatabase.GetPageMetadataAsync(candidates.Select(p => p.Title), token);
            redirects = new Dictionary<string, WikiRedirect>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                var redirect = await _contentDatabase.GetItemAsync<WikiRedirect>(candidate.Title, token);
                if (redirect is not null) redirects[redirect.FromName] = redirect;
            }
        }

        candidates = candidates.DistinctBy(p => p.Title).ToList();
        var localByTitle = local.ToDictionary(p => p.Title, StringComparer.Ordinal);
        var targets = candidates.Where(p => !p.Missing && (reader.Namespaces.Contains(p.Namespace) ||
            (p.Redirect && redirects.ContainsKey(p.Title)))).ToList();
        var redirectTargets = full ? await reader.FullRedirectsAsync(targets) :
            await reader.RedirectsAsync(targets.Where(p => p.Redirect).Select(p => p.Title));
        var resourcesByPage = new ConcurrentDictionary<string, string[]>(
            await _contentDatabase.ReadSyncResourceUrlsAsync(full || fileEvents.Count > 0 || mediaQueue.Tasks.Count > 0 ? null : targets.Select(p => p.Title), token), StringComparer.Ordinal);
        var urls = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;
        var deleted = 0;
        var redirectCount = 0;
        var skipped = 0;
        var failed = 0;
        var resourceCount = 0;
        var indexChanged = await _contentDatabase.ReadSyncMetadataAsync("sync_index_pending", token) == "1";
        var resourceMetadataChanged = false;
        var pageProcessed = 0;

        // Discovery alone cannot dirty an otherwise valid cursor. Persist recovery only
        // before the first mutation, including the need to reparse unknown dependencies.
        await SaveSyncStateAsync((previous ?? new WikiSyncState()) with
        { Incomplete = true, ForcePageRefresh = forceAll, DependenciesSkipped = quick || previous?.DependenciesSkipped == true }, token);

        if (includeResources && full)
        {
            // Old v2 processors already replaced thumbnail URLs with original file keys.
            // Recover only missing source metadata from stored /src links plus the live
            // file API. This one-time media recovery never rebuilds search or rewrites HTML.
            var missingSources = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var page in targets.Where(page => !page.Redirect && localByTitle.ContainsKey(page.Title) &&
                         !resourcesByPage.ContainsKey(page.Title) && localByTitle[page.Title].LastModified == page.Modified))
            {
                var stored = await _contentDatabase.GetItemAsync<WikiPage>(page.Title, token);
                var document = new HtmlDocument();
                document.LoadHtml(stored!.Content);
                var sources = document.DocumentNode.SelectNodes("//*[@src]")?.Select(node => node.GetAttributeValue("src", string.Empty)).ToArray() ?? [];
                if (sources.All(source => source.StartsWith("/src/", StringComparison.Ordinal)))
                    missingSources[page.Title] = sources.Select(source => Uri.UnescapeDataString(source["/src/".Length..])).Distinct(StringComparer.Ordinal).ToArray();
            }
            var resolved = await reader.AssetSourcesAsync(missingSources.Values.SelectMany(keys => keys));
            foreach (var entry in missingSources)
            {
                if (entry.Value.Any(key => !resolved.ContainsKey(key))) continue;
                var recovered = entry.Value.Select(key => resolved[key]).ToArray();
                resourcesByPage[entry.Key] = recovered;
                await _contentDatabase.WriteSyncMetadataAsync("sync_resources:" + entry.Key, JsonSerializer.Serialize(recovered), token);
                resourceMetadataChanged = true;
            }
        }

        // Save all new/moved destinations before deleting or converting any old title.
        var pageTargets = targets.Where(p => !p.Redirect).ToList();
        progress?.Report(new WikiProgress("sync-pages-start", Total: pageTargets.Count));
        await Parallel.ForEachAsync(pageTargets,
            new ParallelOptions { MaxDegreeOfParallelism = options.PageConcurrency, CancellationToken = token }, async (page, _) =>
        {
            token.ThrowIfCancellationRequested();
            var replace = forceAll || forced.Contains(page.Title) || !localByTitle.TryGetValue(page.Title, out var stored) || stored.LastModified != page.Modified;
            var recoverUrls = includeResources && full && !resourcesByPage.ContainsKey(page.Title);
            if (!replace && !recoverUrls)
            {
                Interlocked.Increment(ref skipped);
                progress?.Report(new WikiProgress("page-skipped", Interlocked.Increment(ref pageProcessed), pageTargets.Count, page.Title));
                return;
            }
            try
            {
                await RetryAsync(async () =>
                {
                    // Explicit full refresh downloads each page once, using the directory
                    // timestamp conservatively. The START cursor replays concurrent edits.
                    var downloadReplacement = options.UpdateMode == WikiUpdateMode.Full;
                    var before = downloadReplacement ? page : (await reader.CurrentAsync([page.Title])).Single();
                    if (before.Missing || before.Redirect || !reader.Namespaces.Contains(before.Namespace))
                        throw new InvalidDataException("Page changed identity during synchronization.");
                    var processed = await FetchProcessedPageAsync(book, page.Title, token);
                    if (!downloadReplacement)
                    {
                        var after = (await reader.CurrentAsync([page.Title])).Single();
                        if (before != after) throw new InvalidDataException("Page changed while parsing; retry required.");
                    }
                    resourcesByPage[page.Title] = processed.ResourceUrls.ToArray();
                    // Persist URL tasks before committing the page; a crash cannot lose pending media.
                    await _contentDatabase.WriteSyncMetadataAsync("sync_resources:" + page.Title, JsonSerializer.Serialize(resourcesByPage[page.Title]), token);
                    resourceMetadataChanged = true;
                    lock (urls) urls.UnionWith(processed.ResourceUrls);
                    if (replace || before.Modified != page.Modified)
                    {
                        await _contentDatabase.WriteSyncMetadataAsync("sync_index_pending", "1", token);
                        await _contentDatabase.SavePageWithSearchAsync(new WikiPage
                        {
                            Title = page.Title, Content = processed.Html, LastModified = before.Modified
                        }, processed, token);
                        Interlocked.Increment(ref pages);
                        indexChanged = true;
                    }
                    else Interlocked.Increment(ref skipped);
                }, options.MaxRetryAttempts, token, progress);
                progress?.Report(new WikiProgress("page-download", Interlocked.Increment(ref pageProcessed), pageTargets.Count, page.Title));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (HttpRequestException exception) when (WikiHttpRetry.IsAccessDenied(exception) ||
                exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                progress?.Report(new WikiProgress("sync-page-failed", Detail: page.Title, Error: exception));
                throw;
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failed);
                progress?.Report(new WikiProgress("sync-page-failed", Detail: page.Title, Error: exception));
                await AppendFailureAsync(paths.FailedPageListPath, $"{page.Title}|{page.Modified:O}", token);
                Interlocked.Increment(ref pageProcessed);
            }
        });

        if (failed == 0)
        {
            var changes = new List<(SyncPage Page, string? Target, bool HadPage, bool HadRedirect)>();
            foreach (var page in candidates.Where(p => p.Missing || p.Redirect))
            {
                token.ThrowIfCancellationRequested();
                var target = !page.Missing ? redirectTargets.GetValueOrDefault(page.Title) : null;
                var hadPage = localByTitle.ContainsKey(page.Title);
                var hadRedirect = redirects.TryGetValue(page.Title, out var oldRedirect);
                if (!hadPage && (target is null ? !hadRedirect : oldRedirect?.ToTarget == target)) { skipped++; continue; }
                changes.Add((page, target, hadPage, hadRedirect));
            }
            foreach (var batch in changes.Chunk(50))
            {
                // Re-confirm at the destructive boundary; network failures are never absence.
                var current = (await reader.CurrentAsync(batch.Select(change => change.Page.Title))).ToDictionary(page => page.Title, StringComparer.Ordinal);
                foreach (var change in batch)
                {
                    if (current.GetValueOrDefault(change.Page.Title) != change.Page)
                        throw new InvalidDataException("Page changed before deletion or redirect replacement.");
                    if (change.HadPage)
                        await _contentDatabase.WriteSyncMetadataAsync("sync_index_pending", "1", token);
                    await _contentDatabase.ApplySyncRedirectAsync(change.Page.Title, change.Target, token);
                    if (change.HadPage) { deleted++; indexChanged = true; }
                    else if (change.Target is null && change.HadRedirect) deleted++;
                    if (change.Target is not null) redirectCount++;
                    progress?.Report(new WikiProgress("sync-delete", deleted, Detail: change.Page.Title));
                }
            }
        }

        if (indexChanged)
        {
            progress?.Report(new WikiProgress("sync-index-maintenance"));
            await _contentDatabase.OptimizeSearchIndexAsync(reclaimSpace: false, cancellationToken: token);
            await _contentDatabase.WriteSyncMetadataAsync("sync_index_pending", "0", token);
        }

        async Task CommitPageCursorAsync()
        {
            var anchor = events.LastOrDefault();
            await SaveSyncStateAsync(new WikiSyncState
            {
                Identity = reader.Identity, CoveredThrough = upper,
                AnchorId = anchor?.Id ?? (full ? 0 : previous!.AnchorId),
                AnchorTime = anchor?.Time ?? (full ? default : previous!.AnchorTime),
                LastFullScan = full ? upper : previous!.LastFullScan,
                Incomplete = false, ResourcesCovered = includeResources || previous?.ResourcesCovered == true,
                DependenciesSkipped = !full && (quick || previous?.DependenciesSkipped == true),
                CoveredEventIds = events.Where(entry => entry.Time >= upper.AddMinutes(-5)).Select(entry => entry.Id).ToArray()
            }, token);
            DeleteIfExists(paths.FailedPageListPath);
            progress?.Report(new WikiProgress("sync-page-cursor-committed"));
        }

        if (!includeResources && failed == 0) await CommitPageCursorAsync();

        if (includeResources && failed == 0)
        {
            foreach (var page in candidates.Where(page => page.Missing || page.Redirect))
                resourcesByPage.TryRemove(page.Title, out _);
            if (full)
            {
                var currentPages = pageTargets.Select(page => page.Title).ToHashSet(StringComparer.Ordinal);
                urls.UnionWith(resourcesByPage.Where(entry => currentPages.Contains(entry.Key)).SelectMany(entry => entry.Value));
                var mapped = urls.Select(WikiContentProcessor.GetFileNameFromUrl).ToHashSet(StringComparer.Ordinal);
                var orphanCount = (await _contentDatabase.GetAllPrimaryKeysAsync<WikiAsset>(token)).Count(asset => !mapped.Contains(asset));
                if (orphanCount > 0)
                    progress?.Report(new WikiProgress("sync-orphan-media-retained", Detail: orphanCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            var forceUrls = new HashSet<string>(StringComparer.Ordinal);
            if (previous?.Incomplete == true || !mediaQueue.Valid) forceUrls.UnionWith(urls);
            foreach (var file in fileEvents)
            {
                var name = file[(file.IndexOf(':') + 1)..].Replace(' ', '_');
                var matches = resourcesByPage.Values.SelectMany(value => value).Distinct(StringComparer.Ordinal)
                    .Where(url => Uri.UnescapeDataString(url).Replace(' ', '_').Contains("/" + name, StringComparison.Ordinal) ||
                        WikiContentProcessor.GetFileNameFromUrl(url).EndsWith(name, StringComparison.Ordinal)).ToArray();
                if (await reader.FileMissingAsync(file))
                {
                    // Only the exact, unique local key is eligible; keep ambiguous thumbnails and orphan assets.
                    var exact = matches.Where(url => WikiContentProcessor.GetFileNameFromUrl(url) == name).ToArray();
                    if (exact.Length == 1)
                    {
                        var exists = await _contentDatabase.ItemExistsAsync<WikiAsset>(name, token);
                        await _contentDatabase.DeleteItemAsync<WikiAsset>(name, CancellationToken.None);
                        token.ThrowIfCancellationRequested();
                        if (exists) resourceCount++;
                        urls.ExceptWith(exact);
                        foreach (var url in exact) mediaQueue.Tasks.Remove(url);
                        foreach (var pageResources in resourcesByPage)
                        {
                            var retained = pageResources.Value.Except(exact, StringComparer.Ordinal).ToArray();
                            if (retained.Length == pageResources.Value.Length) continue;
                            resourcesByPage[pageResources.Key] = retained;
                            await _contentDatabase.WriteSyncMetadataAsync("sync_resources:" + pageResources.Key,
                                JsonSerializer.Serialize(retained), token);
                        }
                    }
                }
                else { urls.UnionWith(matches); forceUrls.UnionWith(matches); }
            }
            var referencedUrls = resourcesByPage.Values.SelectMany(value => value).ToHashSet(StringComparer.Ordinal);
            var pending = new ConcurrentDictionary<string, bool>(mediaQueue.Tasks.Where(task => referencedUrls.Contains(task.Key)), StringComparer.Ordinal);
            var previouslyForcedKeys = mediaQueue.Tasks.Where(task => task.Value).Select(task => WikiContentProcessor.GetFileNameFromUrl(task.Key)).ToHashSet(StringComparer.Ordinal);
            forceUrls.UnionWith(urls.Where(url => previouslyForcedKeys.Contains(WikiContentProcessor.GetFileNameFromUrl(url))));
            foreach (var url in urls) pending.AddOrUpdate(url, forceUrls.Contains(url), (_, force) => force || forceUrls.Contains(url));
            // The queue transaction precedes the cursor. A crash in between still
            // leaves Incomplete=true; a crash afterwards resumes individual media.
            await _contentDatabase.SaveSyncMediaQueueAsync(reader.Identity, pending, token);
            if (!pending.IsEmpty) await File.WriteAllLinesAsync(paths.FailedResourceListPath, pending.Keys, token);
            await CommitPageCursorAsync();
            var totalMedia = pending.Count;
            var mediaProcessed = 0;
            progress?.Report(new WikiProgress("sync-media-start", Total: totalMedia));
            try
            {
                await Parallel.ForEachAsync(pending.OrderBy(task => task.Key, StringComparer.Ordinal).ToArray(),
                    new ParallelOptions { MaxDegreeOfParallelism = options.ResourceConcurrency, CancellationToken = token }, async (url, _) =>
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var changed = await RetryAsync(() => DownloadResourceAsync(book, url.Key, token, url.Value, progress), options.MaxRetryAttempts, token, progress);
                        if (changed) Interlocked.Increment(ref resourceCount);
                        await _contentDatabase.CompleteSyncMediaTaskAsync(url.Key, token);
                        pending.TryRemove(url.Key, out var completedForce);
                        progress?.Report(new WikiProgress(changed ? "resource-downloaded" : "resource-skipped", Interlocked.Increment(ref mediaProcessed), totalMedia,
                            WikiContentProcessor.GetFileNameFromUrl(url.Key)));
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (HttpRequestException exception) when (WikiHttpRetry.IsAccessDenied(exception) ||
                        exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                    {
                        progress?.Report(new WikiProgress("sync-media-failed", Detail: url.Key, Error: exception));
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Interlocked.Increment(ref failed);
                        progress?.Report(new WikiProgress("sync-media-failed", Interlocked.Increment(ref mediaProcessed), totalMedia,
                            url.Key, exception));
                    }
                });
            }
            finally
            {
                // This helper list exposes pending work to the existing retry UI;
                // the database queue is authoritative after process termination.
                if (pending.IsEmpty) DeleteIfExists(paths.FailedResourceListPath);
                else await File.WriteAllLinesAsync(paths.FailedResourceListPath, pending.Keys, CancellationToken.None);
            }
        }

        if (resourceMetadataChanged)
        {
            await File.AppendAllLinesAsync(paths.ResourceListPath, resourcesByPage.Values.SelectMany(value => value).Distinct(StringComparer.Ordinal), token);
            DeduplicateInPlace(paths.ResourceListPath);
        }
        book.UpdateTime = DateTime.Now;
        await RefreshBookStatisticsCoreAsync(book, token);
        if (failed == 0)
        {
            DeleteIfExists(paths.FailedPageListPath);
            if (includeResources) DeleteIfExists(paths.FailedResourceListPath);
            CleanTransientFiles(paths);
        }
        return new WikiOperationSummary(pages, redirectCount, resourceCount, failed,
            pages + redirectCount + deleted + resourceCount > 0, candidates.Count, skipped, deleted, full);
    }

}
