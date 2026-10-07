using System.Globalization;
using System.Text.Json;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Data;

/// <summary>Strict, bounded MediaWiki reads for the existing lifecycle updater.</summary>
internal sealed class WikiSyncReader(WikiBook book, IWikiHttpTransport transport, int retries, CancellationToken token,
    IProgress<Terraria_Wiki.Core.Operations.WikiProgress>? progress = null)
{
    public HashSet<int> Namespaces { get; } = new(
        book.AdditionalNamespaces.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse).Append(book.MainNamespace));

    public string Identity => WikiSyncState.IdentityFor(book);

    public async Task<DateTimeOffset> ServerTimeAsync()
    {
        var root = await QueryAsync("meta=siteinfo&siprop=general&curtimestamp=1");
        return Time(root, "curtimestamp");
    }

    public async Task<List<SyncEvent>> EventsAsync(DateTimeOffset? start, DateTimeOffset end, int maxRequests = 100)
    {
        var result = new Dictionary<long, SyncEvent>();
        var args = "list=recentchanges&rcprop=title%7Cids%7Ctimestamp%7Cloginfo" +
            (start is { } since
                ? "&rclimit=max&rcdir=newer&rcstart=" + Stamp(since) + "&rcend=" + Stamp(end)
                : "&rcdir=older&rcstart=" + Stamp(end) + "&rclimit=1");
        await foreach (var root in PagesAsync(args, maxRequests))
        {
            foreach (var item in Array(root, "recentchanges"))
            {
                if (item.TryGetProperty("suppressed", out _) || item.TryGetProperty("titlehidden", out _))
                    throw new SyncCoverageException("hidden-event");
                var type = Text(item, "type");
                var log = OptionalText(item, "logtype");
                string? target = null;
                if (log == "move")
                {
                    if (!item.TryGetProperty("logparams", out var parameters))
                        throw new SyncCoverageException("move-without-target");
                    target = Text(parameters, "target_title");
                }
                var entry = new SyncEvent(Number(item, "rcid"), Time(item, "timestamp"), Text(item, "title"),
                    (int)Number(item, "ns"), type, log, target);
                if (entry.Id <= 0) throw new SyncCoverageException("invalid-event-id");
                result[entry.Id] = entry;
            }
            if (start is null) break;
        }
        return result.Values.OrderBy(e => e.Time).ThenBy(e => e.Id).ToList();
    }

    public async Task<bool> VerifyAnchorAsync(WikiSyncState state)
    {
        if (state.AnchorId <= 0 || state.AnchorTime > state.CoveredThrough) return false;
        var events = await EventsAsync(state.AnchorTime, state.AnchorTime, maxRequests: 10);
        return events.Any(e => e.Id == state.AnchorId && e.Time == state.AnchorTime);
    }

    public async Task<List<SyncPage>> DirectoryAsync()
    {
        var result = new Dictionary<string, SyncPage>(StringComparer.Ordinal);
        foreach (var ns in Namespaces.Order())
        {
            await foreach (var root in PagesAsync($"generator=allpages&gapnamespace={ns}&gaplimit=max&prop=info", 10000))
                if (root.TryGetProperty("query", out _))
                    foreach (var page in ParsePages(root)) result[page.Title] = page;
        }
        return result.Values.ToList();
    }

    public async Task<List<SyncPage>> CurrentAsync(IEnumerable<string> titles)
    {
        var result = new List<SyncPage>();
        foreach (var batch in titles.Distinct(StringComparer.Ordinal).Chunk(50))
        {
            var root = await QueryAsync("prop=info&titles=" + Uri.EscapeDataString(string.Join('|', batch)));
            var pages = ParsePages(root);
            var returned = pages.Select(p => p.Title).ToHashSet(StringComparer.Ordinal);
            var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.GetProperty("query").TryGetProperty("normalized", out var mappings))
                foreach (var mapping in mappings.EnumerateArray()) normalized[Text(mapping, "from")] = Text(mapping, "to");
            foreach (var title in batch)
                if (!returned.Contains(normalized.GetValueOrDefault(title, title)))
                    throw new SyncCoverageException("incomplete-current-page-response");
            result.AddRange(pages);
        }
        return result;
    }

    public async Task<Dictionary<string, string>> RedirectsAsync(IEnumerable<string> titles)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var batch in titles.Distinct(StringComparer.Ordinal).Chunk(50))
        {
            var root = await QueryAsync("prop=info&redirects=1&titles=" + Uri.EscapeDataString(string.Join('|', batch)));
            if (!root.GetProperty("query").TryGetProperty("redirects", out var mappings))
                throw new SyncCoverageException("redirect-resolution-missing");
            foreach (var mapping in mappings.EnumerateArray())
            {
                var target = Text(mapping, "to");
                if (OptionalText(mapping, "tofragment") is { Length: > 0 } anchor) target += "#" + anchor;
                result[Text(mapping, "from")] = target;
            }
            if (batch.Any(title => !result.ContainsKey(title)))
                throw new SyncCoverageException("incomplete-redirect-resolution");
        }
        return result;
    }

    public async Task<Dictionary<string, string>> FullRedirectsAsync(IEnumerable<SyncPage> pages)
    {
        var sources = pages.Where(page => page.Redirect).ToDictionary(page => page.PageId);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sources.Count == 0) return result;
        try
        {
            // allredirects filters TARGET namespaces. Residual cross-namespace and
            // interwiki mappings are resolved by title below, never silently dropped.
            foreach (var ns in Namespaces)
                await foreach (var root in PagesAsync($"list=allredirects&arprop=ids%7Ctitle%7Cfragment%7Cinterwiki&arlimit=max&arnamespace={ns}", 10000))
                    foreach (var entry in Array(root, "allredirects"))
                    {
                        if (!sources.TryGetValue(Number(entry, "fromid"), out var source)) continue;
                        if (OptionalText(entry, "interwiki") is { Length: > 0 }) continue;
                        var target = Text(entry, "title");
                        if (OptionalText(entry, "fragment") is { Length: > 0 } fragment) target += "#" + fragment;
                        result[source.Title] = target;
                    }
        }
        catch (SyncCoverageException) { result.Clear(); }
        foreach (var mapping in await RedirectsAsync(sources.Values.Select(page => page.Title).Where(title => !result.ContainsKey(title))))
            result[mapping.Key] = mapping.Value;
        return result;
    }

    public async Task<HashSet<string>> DependenciesAsync(IEnumerable<(string Title, bool File)> sources)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var scheduled = new HashSet<(string Title, bool File)>();
        var pending = new Queue<(string Title, bool File)>();
        void Schedule((string Title, bool File) source)
        {
            if (!scheduled.Add(source)) return;
            if (scheduled.Count > 10000) throw new SyncCoverageException("dependency-candidate-limit");
            pending.Enqueue(source);
        }
        foreach (var source in sources) Schedule(source);
        var requests = 0;
        void CountRequest()
        {
            if (++requests > 1000) throw new SyncCoverageException("dependency-request-limit");
        }
        while (pending.TryDequeue(out var source))
        {
            var args = source.File
                ? "list=imageusage&iulimit=max&iutitle="
                : "list=embeddedin&eilimit=max&eititle=";
            await foreach (var root in PagesAsync(args + Uri.EscapeDataString(source.Title), 100, CountRequest))
            {
                foreach (var entry in Array(root, source.File ? "imageusage" : "embeddedin"))
                {
                    var title = Text(entry, "title");
                    var ns = (int)Number(entry, "ns");
                    if (ns == 828) throw new SyncCoverageException("module-dependency-not-provable");
                    if (Namespaces.Contains(ns)) result.Add(title);
                    Schedule((title, false));
                }
            }
        }
        return result;
    }

    public async Task<bool> FileMissingAsync(string title)
    {
        var root = await QueryAsync("prop=imageinfo&iiprop=url&titles=" + Uri.EscapeDataString(title));
        var pages = root.GetProperty("query").GetProperty("pages");
        var entries = pages.ValueKind == JsonValueKind.Array ? pages.EnumerateArray().ToArray() : pages.EnumerateObject().Select(p => p.Value).ToArray();
        if (entries.Length != 1 || Text(entries[0], "title") != title)
            throw new SyncCoverageException("file-status-not-provable");
        return entries[0].TryGetProperty("missing", out _) && !entries[0].TryGetProperty("imageinfo", out _) &&
            OptionalText(entries[0], "imagerepository") == string.Empty;
    }

    public async Task<string?> AssetSourceAsync(string key)
    {
        var root = await QueryAsync("prop=imageinfo&iiprop=url&redirects=1&titles=" + Uri.EscapeDataString("File:" + key));
        var pages = root.GetProperty("query").GetProperty("pages");
        var entries = pages.ValueKind == JsonValueKind.Array ? pages.EnumerateArray().ToArray() : pages.EnumerateObject().Select(p => p.Value).ToArray();
        if (entries.Length != 1 || !entries[0].TryGetProperty("imageinfo", out var info)) return null;
        var urls = info.EnumerateArray().Select(item => OptionalText(item, "url"))
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .Distinct(StringComparer.Ordinal).ToArray();
        return urls.Length == 1 ? urls[0] : null;
    }

    public async Task<Dictionary<string, string>> AssetSourcesAsync(IEnumerable<string> keys)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var batch in keys.Distinct(StringComparer.Ordinal).Chunk(50))
        {
            var root = await QueryAsync("prop=imageinfo&iiprop=url&titles=" + Uri.EscapeDataString(string.Join('|', batch.Select(key => "File:" + key))));
            var pages = root.GetProperty("query").GetProperty("pages");
            var entries = pages.ValueKind == JsonValueKind.Array ? pages.EnumerateArray().ToArray() : pages.EnumerateObject().Select(p => p.Value).ToArray();
            foreach (var page in entries)
            {
                if (!page.TryGetProperty("imageinfo", out var info)) continue;
                foreach (var item in info.EnumerateArray())
                {
                    var url = OptionalText(item, "url");
                    if (url is null) continue;
                    var key = WikiContentProcessor.GetFileNameFromUrl(url);
                    if (batch.Contains(key, StringComparer.Ordinal)) result[key] = url;
                }
            }
        }
        return result;
    }

    private async IAsyncEnumerable<JsonElement> PagesAsync(string args, int maxRequests, Action? beforeRequest = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var continuation = string.Empty;
        for (var count = 0; count < maxRequests; count++)
        {
            beforeRequest?.Invoke();
            var root = await QueryAsync(args + continuation);
            yield return root;
            if (!root.TryGetProperty("continue", out var next)) yield break;
            if (next.ValueKind != JsonValueKind.Object || !next.EnumerateObject().Any())
                throw new SyncCoverageException("invalid-continuation");
            continuation = string.Concat(next.EnumerateObject().OrderBy(p => p.Name).Select(p =>
                "&" + Uri.EscapeDataString(p.Name) + "=" + Uri.EscapeDataString(p.Value.ToString())));
            if (!seen.Add(continuation)) throw new SyncCoverageException("repeated-continuation");
        }
        throw new SyncCoverageException("request-limit");
    }

    private async Task<JsonElement> QueryAsync(string args)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var json = await transport.GetStringAsync(book,
                    new Uri(book.ApiBaseUrl + "?action=query&format=json&formatversion=2&" + args), token);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    var code = OptionalText(error, "code");
                    if (code is "maxlag" or "ratelimited")
                        throw new WikiHttpThrottleException(System.Net.HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(30));
                    throw new SyncCoverageException("api-error:" + code);
                }
                if (root.TryGetProperty("warnings", out _)) throw new SyncCoverageException("api-warning");
                if (!root.TryGetProperty("query", out _) &&
                    !(args.Contains("generator=allpages", StringComparison.Ordinal) && root.TryGetProperty("batchcomplete", out _)))
                    throw new SyncCoverageException("query-missing");
                return root.Clone();
            }
            catch (HttpRequestException exception) when (attempt < retries && !WikiHttpRetry.IsAccessDenied(exception))
            {
                progress?.Report(new Terraria_Wiki.Core.Operations.WikiProgress("http-retry", attempt + 1, retries, Error: exception));
                await WikiHttpRetry.DelayAsync(exception, attempt + 1, token);
            }
            catch (OperationCanceledException exception) when (!token.IsCancellationRequested && attempt < retries)
            {
                progress?.Report(new Terraria_Wiki.Core.Operations.WikiProgress("http-retry", attempt + 1, retries, Error: exception));
                await WikiHttpRetry.DelayAsync(exception, attempt + 1, token);
            }
            catch (JsonException exception) { throw new SyncCoverageException("invalid-json:" + exception.GetType().Name); }
        }
    }

    private static List<SyncPage> ParsePages(JsonElement root)
    {
        if (!root.GetProperty("query").TryGetProperty("pages", out var pages))
            throw new SyncCoverageException("pages-missing");
        var entries = pages.ValueKind == JsonValueKind.Array ? pages.EnumerateArray().ToArray() :
            pages.ValueKind == JsonValueKind.Object ? pages.EnumerateObject().Select(p => p.Value).ToArray() :
            throw new SyncCoverageException("invalid-pages");
        return entries.Select(page =>
        {
            if (page.TryGetProperty("invalid", out _)) throw new SyncCoverageException("invalid-title");
            var missing = page.TryGetProperty("missing", out _);
            return new SyncPage(Text(page, "title"), (int)Number(page, "ns"), missing ? 0 : Number(page, "pageid"),
                // Existing v2 page timestamps use DateTime.TryParse (local wall time) and
                // sqlite-net stores ticks without Kind. Preserve that representation.
                missing ? default : Time(page, "touched").LocalDateTime, page.TryGetProperty("redirect", out _), missing);
        }).ToList();
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement root, string name)
    {
        if (!root.GetProperty("query").TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new SyncCoverageException(name + "-missing");
        return array.EnumerateArray();
    }
    private static string Text(JsonElement item, string name) => OptionalText(item, name)
        ?? throw new SyncCoverageException(name + "-missing");
    private static string? OptionalText(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long Number(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : throw new SyncCoverageException(name + "-missing");
    private static DateTimeOffset Time(JsonElement item, string name) =>
        DateTimeOffset.TryParse(Text(item, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time.ToUniversalTime() : throw new SyncCoverageException(name + "-invalid");
    private static string Stamp(DateTimeOffset time) => Uri.EscapeDataString(time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
}
