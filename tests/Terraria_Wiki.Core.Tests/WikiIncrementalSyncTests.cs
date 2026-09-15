using System.Net;
using System.Text.Json;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Tests;

public sealed class WikiIncrementalSyncTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wiki-sync-" + Guid.NewGuid().ToString("N"));
    private readonly WikiBook _book = new() { Id = 99, ApiBaseUrl = "https://wiki.test/api.php", BaseUrl = "https://wiki.test", DataFolder = "sync", DefaultPageTitle = "Home", DefaultPageContent = "home" };
    private readonly SyncTransport _http = new();
    private ContentDbService _db = null!;
    private ManagerDbService _manager = null!;
    private WikiDataLifecycle _lifecycle = null!;
    private WikiDataPaths _paths = null!;
    private readonly WikiOperationOptions _options = new(MaxRetryAttempts: 0);

    public async Task InitializeAsync()
    {
        _paths = WikiDataPaths.Create(_root, _book);
        _manager = new ManagerDbService(_paths.ManagerDatabasePath);
        _db = new ContentDbService(_paths.ContentDatabasePath);
        await _manager.Init();
        await _manager.SaveItemAsync(_book);
        await _db.Init(seedBook: _book);
        _lifecycle = new WikiDataLifecycle(_manager, _db, _http, new WikiDataOperationGate());
        _http.Pages["Home"] = new RemotePage("Home");
    }

    private Task<WikiOperationSummary> Update(bool resources = false, CancellationToken token = default) =>
        _lifecycle.UpdateAsync(_book, _paths, resources, _options, cancellationToken: token);

    private async Task Baseline()
    {
        Assert.Equal(0, (await Update()).Failed);
        _http.Requests.Clear();
        _http.Now = _http.Now.AddHours(1);
    }

    [Fact]
    public async Task Ten_thousand_unchanged_pages_use_constant_requests_without_HTML_or_index_maintenance()
    {
        for (var i = 0; i < 10000; i++) _http.Pages["Page " + i] = new RemotePage("Page " + i);
        await _db.GetConnection().RunInTransactionAsync(connection =>
        {
            foreach (var page in _http.Pages.Values)
                connection.InsertOrReplace(new WikiPage { Title = page.Title, LastModified = page.Modified.ToLocalTime(), Content = "Do not read this HTML" });
        });
        await Baseline();
        // Dropping FTS proves that a no-change operation does not even try optimize.
        await _db.GetConnection().ExecuteAsync("DROP TABLE WikiSearchRegionFts");
        for (var i = 0; i < 3; i++)
        {
            var summary = await Update();
            Assert.False(summary.UsedFullScan);
            Assert.False(summary.Changed);
            Assert.Equal(0, summary.Pages);
            Assert.DoesNotContain(_http.Requests, q => q.ContainsKey("generator") || q.GetValueOrDefault("action") == "parse");
            Assert.Equal(3, _http.Requests.Count); // server time, old anchor, new window
            _http.Requests.Clear();
            _http.Now = _http.Now.AddHours(1);
        }
    }

    [Fact]
    public async Task Repeated_events_download_only_three_final_pages_and_remove_old_search_terms()
    {
        foreach (var title in new[] { "A", "B", "C", "D" }) _http.Pages[title] = new RemotePage(title);
        await Baseline();
        foreach (var title in new[] { "A", "B", "C" })
        {
            _http.Pages[title] = new RemotePage(title, Modified: _http.Now.UtcDateTime, Html: "<p>newneedle</p>");
            _http.Change(title); _http.Change(title);
        }
        var summary = await Update();
        Assert.Equal(3, summary.Pages);
        Assert.False(summary.UsedFullScan);
        Assert.Equal(3, _http.Requests.Count(q => q.GetValueOrDefault("action") == "parse"));
        Assert.DoesNotContain(_http.Requests, q => q.ContainsKey("generator"));
        Assert.Contains("newneedle", (await _db.GetItemAsync<WikiPage>("A"))!.Content);
        Assert.DoesNotContain(await _db.GetItemsAsync<WikiSearchRegion>(), region => region.PageTitle == "A" && region.BodyText.Contains("oldneedle"));
        Assert.Equal(3, (await _db.GetItemsAsync<WikiSearchRegion>()).Count(region => region.BodyText.Contains("newneedle")));
    }

    [Fact]
    public async Task Move_and_redirect_conversion_preserve_favorites_and_remove_obsolete_regions()
    {
        _http.Pages["A"] = new RemotePage("A");
        await Baseline();
        await _db.SaveItemAsync(new WikiFavorite { WikiTitle = "A" });
        _http.Pages["B"] = new RemotePage("B");
        _http.Pages["A"] = new RemotePage("A", Target: "B");
        _http.Change("A", "move", "B");
        var summary = await Update();
        Assert.Equal(1, summary.Pages);
        Assert.Equal(1, summary.Deleted);
        Assert.Null(await _db.GetItemAsync<WikiPage>("A"));
        Assert.Equal("B", (await _db.GetItemAsync<WikiRedirect>("A"))!.ToTarget);
        Assert.NotNull(await _db.GetItemAsync<WikiFavorite>("A"));
        Assert.DoesNotContain(await _db.GetItemsAsync<WikiSearchRegion>(), region => region.PageTitle == "A");
        _http.Pages["A"] = new RemotePage("A", Modified: _http.Now.UtcDateTime);
        _http.Change("A");
        await Update();
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("A"));
        Assert.Null(await _db.GetItemAsync<WikiRedirect>("A"));
    }

    [Fact]
    public async Task Missing_page_is_deleted_but_failed_current_query_never_deletes()
    {
        _http.Pages["A"] = new RemotePage("A");
        await Baseline();
        _http.Pages.Remove("A"); _http.Change("A", "delete");
        _http.FailCurrent = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Update());
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("A"));
        Assert.True((await State()).Incomplete);
        _http.FailCurrent = false;
        var summary = await Update();
        Assert.True(summary.UsedFullScan);
        Assert.Equal(1, summary.Deleted);
        Assert.Null(await _db.GetItemAsync<WikiPage>("A"));
        Assert.False((await State()).Incomplete);
    }

    [Fact]
    public async Task Full_scan_preserves_existing_redirects_outside_download_namespaces()
    {
        _http.Pages["Talk:Home"] = new RemotePage("Talk:Home", Ns: 1, Target: "Home");
        await _db.SaveItemAsync(new WikiRedirect { FromName = "Talk:Home", ToTarget = "Home" });
        var summary = await Update();
        Assert.True(summary.UsedFullScan);
        Assert.Equal(0, summary.Deleted);
        Assert.Equal("Home", (await _db.GetItemAsync<WikiRedirect>("Talk:Home"))!.ToTarget);
    }

    [Fact]
    public async Task Consecutive_moves_coalesce_to_the_current_destination()
    {
        _http.Pages["A"] = new RemotePage("A");
        await Baseline();
        _http.Pages.Remove("A");
        _http.Pages["C"] = new RemotePage("C");
        _http.Change("A", "move", "B"); _http.Change("B", "move", "C");
        var summary = await Update();
        Assert.Equal(1, summary.Pages);
        Assert.Equal(1, summary.Deleted);
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("C"));
        Assert.Null(await _db.GetItemAsync<WikiPage>("A"));
        Assert.Null(await _db.GetItemAsync<WikiRedirect>("B"));
    }

    [Fact]
    public async Task Remote_file_deletion_removes_only_the_exact_media_key_and_forgets_its_url()
    {
        _http.Pages["Home"] = new RemotePage("Home", Html: "<p>text</p><img src='/images/A.png'>");
        await Update(true);
        _http.Now = _http.Now.AddHours(1);
        _http.Change("File:A.png", "delete", ns: 6);
        _http.MissingFile = true;
        var summary = await Update(true);
        Assert.Equal(1, summary.Resources);
        Assert.Null(await _db.GetItemAsync<WikiAsset>("A.png"));
        Assert.Empty((await _db.ReadSyncResourceUrlsAsync(null, default))["Home"]);
    }

    [Fact]
    public async Task Raw_continuation_fields_are_forwarded_and_event_ids_are_deduplicated()
    {
        _http.Override = q => q.GetValueOrDefault("list") == "recentchanges"
            ? q.ContainsKey("rccontinue")
                ? "{\"query\":{\"recentchanges\":[{\"rcid\":7,\"timestamp\":\"2026-09-15T00:00:00Z\",\"title\":\"Home\",\"ns\":0,\"type\":\"edit\"}]}}"
                : "{\"continue\":{\"rccontinue\":\"token|7\",\"continue\":\"-||\"},\"query\":{\"recentchanges\":[{\"rcid\":7,\"timestamp\":\"2026-09-15T00:00:00Z\",\"title\":\"Home\",\"ns\":0,\"type\":\"edit\"}]}}"
            : null;
        var reader = new WikiSyncReader(_book, _http, 0, default);
        Assert.Single(await reader.EventsAsync(_http.Now.AddMinutes(-5), _http.Now));
        var next = _http.Requests.Last();
        Assert.Equal("token|7", next["rccontinue"]);
        Assert.Equal("-||", next["continue"]);
    }

    [Theory]
    [InlineData("<html>gateway failure</html>")]
    [InlineData("{\"warnings\":{\"query\":{}},\"query\":{\"pages\":[]}}")]
    [InlineData("{\"query\":{\"pages\":[]}}")]
    [InlineData("{\"error\":{\"code\":\"permissiondenied\"}}")]
    public async Task Malformed_or_empty_current_response_is_never_missing_evidence(string response)
    {
        _http.Override = _ => response;
        await Assert.ThrowsAsync<SyncCoverageException>(() => new WikiSyncReader(_book, _http, 0, default).CurrentAsync(["Home"]));
    }

    [Fact]
    public async Task Failed_move_destination_keeps_source_and_next_update_recovers()
    {
        _http.Pages["A"] = new RemotePage("A");
        await Baseline();
        _http.Pages.Remove("A"); _http.Pages["B"] = new RemotePage("B");
        _http.Change("A", "move", "B"); _http.FailParse = "B";
        Assert.Equal(1, (await Update()).Failed);
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("A"));
        Assert.True((await State()).Incomplete);
        _http.FailParse = null;
        var summary = await _lifecycle.RetryFailuresAsync(_book, _paths, _options);
        Assert.True(summary.UsedFullScan);
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("B"));
        Assert.Null(await _db.GetItemAsync<WikiPage>("A"));
    }

    [Fact]
    public async Task Nested_dependencies_force_refresh_with_identical_touched_and_cycles_terminate()
    {
        _http.Pages["A"] = new RemotePage("A");
        await Baseline();
        _http.Dependents["Template:T"] = [("Template:N", 10)];
        _http.Dependents["Template:N"] = [("Template:T", 10), ("A", 0)];
        _http.Pages["A"] = _http.Pages["A"] with { Html = "<p>template updated</p>" };
        _http.Change("Template:T", ns: 10);
        Assert.Equal(1, (await Update()).Pages);
        Assert.Contains("template updated", (await _db.GetItemAsync<WikiPage>("A"))!.Content);
    }

    [Fact]
    public async Task Module_event_forces_full_refresh_even_when_touched_is_unchanged()
    {
        await Baseline();
        _http.Change("Module:T", ns: 828);
        var summary = await Update();
        Assert.True(summary.UsedFullScan);
        Assert.Equal(1, summary.Pages);
    }

    [Theory]
    [InlineData("expired-anchor")]
    [InlineData("periodic")]
    [InlineData("identity")]
    [InlineData("malformed")]
    public async Task Invalid_baseline_uses_full_scan(string reason)
    {
        await Baseline();
        if (reason == "expired-anchor") _http.Events.Clear();
        if (reason == "periodic") _http.Now = _http.Now.AddDays(15);
        if (reason == "identity") _book.AdditionalNamespaces = "100";
        if (reason == "malformed") await _db.WriteSyncMetadataAsync("sync_state", "not json", default);
        Assert.True((await Update()).UsedFullScan);
    }

    [Fact]
    public async Task Truncated_directory_and_repeated_continuation_cannot_delete_or_advance_state()
    {
        _http.Pages["A"] = new RemotePage("A");
        await Baseline();
        _http.Now = _http.Now.AddDays(15);
        _http.Pages.Remove("A"); _http.RepeatContinuation = true;
        await Assert.ThrowsAsync<SyncCoverageException>(() => Update());
        Assert.True((await State()).Incomplete);
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("A"));
    }

    [Fact]
    public async Task Page_modified_during_parse_is_not_committed_with_a_false_version()
    {
        await Baseline();
        _http.Change("Home");
        _http.Pages["Home"] = _http.Pages["Home"] with { Modified = _http.Now.UtcDateTime };
        _http.ChangeDuringParse = true;
        Assert.Equal(1, (await Update()).Failed);
        Assert.True((await State()).Incomplete);
        Assert.NotEqual(_http.Pages["Home"].Modified, (await _db.GetItemAsync<WikiPage>("Home"))!.LastModified);
    }

    [Fact]
    public async Task Media_failure_after_page_commit_is_recovered_from_database_without_auxiliary_files()
    {
        _http.Pages["Home"] = new RemotePage("Home", Html: "<p>oldneedle</p><img src='/images/A.png'>");
        _http.FailMedia = true;
        Assert.Equal(1, (await Update(true)).Failed);
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("Home"));
        File.Delete(_paths.ResourceListPath);
        _http.FailMedia = false;
        var summary = await Update(true);
        Assert.True(summary.UsedFullScan);
        Assert.Equal(0, summary.Pages);
        Assert.Equal(1, summary.Resources);
        Assert.NotNull(await _db.GetItemAsync<WikiAsset>("A.png"));
    }

    [Fact]
    public async Task Independent_upload_forces_unconditional_media_request_and_light_mode_adds_no_media()
    {
        _http.Pages["Home"] = new RemotePage("Home", Html: "<p>oldneedle</p><img src='/images/A.png'>");
        await Update(true);
        _http.Now = _http.Now.AddHours(1);
        _http.Change("File:A.png", "upload", ns: 6);
        _http.MediaConditions.Clear();
        var summary = await Update(true);
        Assert.Equal(0, summary.Pages);
        Assert.Equal(1, summary.Resources);
        Assert.Null(Assert.Single(_http.MediaConditions));
        _http.MediaConditions.Clear();
        await Update(false);
        Assert.Empty(_http.MediaConditions);
    }

    [Fact]
    public async Task Cancellation_preserves_incomplete_marker_and_retry_does_full_scan()
    {
        await Baseline();
        using var cancel = new CancellationTokenSource();
        _http.OnRequest = q => { if (q.GetValueOrDefault("list") == "recentchanges") cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Update(token: cancel.Token));
        Assert.True((await State()).Incomplete);
        _http.OnRequest = null;
        Assert.True((await Update()).UsedFullScan);
    }

    private async Task<WikiSyncState> State() => JsonSerializer.Deserialize<WikiSyncState>((await _db.ReadSyncMetadataAsync("sync_state", default))!)!;

    [Theory]
    [InlineData(14, 13, false)]
    [InlineData(14, 14, true)]
    [InlineData(1, 1, true)]
    [InlineData(90, 30, false)]
    [InlineData(null, 90, false)]
    public async Task Periodic_scan_uses_configured_days_or_can_be_disabled(int? days, int elapsedDays, bool full)
    {
        await Baseline();
        _http.Now = _http.Now.AddDays(elapsedDays);
        var result = await _lifecycle.UpdateAsync(_book, _paths, false, _options with { FullScanIntervalDays = days });
        Assert.Equal(full, result.UsedFullScan);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("incomplete")]
    [InlineData("anchor")]
    [InlineData("identity")]
    public async Task Disabling_periodic_scan_never_disables_required_recovery(string reason)
    {
        if (reason != "first") await Baseline();
        if (reason == "incomplete")
            await _db.WriteSyncMetadataAsync("sync_state", JsonSerializer.Serialize((await State()) with { Incomplete = true }), default);
        if (reason == "anchor") _http.Events.Clear();
        if (reason == "identity") _book.AdditionalNamespaces = "100";
        var result = await _lifecycle.UpdateAsync(_book, _paths, false, _options with { FullScanIntervalDays = null });
        Assert.True(result.UsedFullScan);
    }

    [Fact]
    public async Task Compatible_import_preserves_cursor_offline_data_and_next_update_is_incremental()
    {
        await Baseline();
        await _db.SaveItemAsync(new WikiFavorite { WikiTitle = "Home" });
        await _db.SaveItemAsync(new WikiAsset { FileName = "A.png", Data = [1, 2], MimeType = "image/png" });
        var state = await _db.ReadSyncMetadataAsync("sync_state", default);
        var transfer = new WikiDatabaseTransferService(_manager, _db, new WikiDataOperationGate());
        var source = Path.Combine(_root, "snapshot.db");
        await transfer.ExportDatabaseAsync(_paths, source, _db);
        var inspection = await transfer.InspectDatabaseAsync(source);
        await _db.WriteSyncMetadataAsync("sync_state", "null", default);
        _http.Requests.Clear();
        await transfer.ImportDatabaseAsync(new WikiDatabaseImportRequest(source, _root, _book.Id, inspection.InspectionToken));
        Assert.Empty(_http.Requests);
        Assert.Equal(state, await _db.ReadSyncMetadataAsync("sync_state", default));
        Assert.NotNull(await _db.GetItemAsync<WikiPage>("Home"));
        Assert.NotNull(await _db.GetItemAsync<WikiFavorite>("Home"));
        Assert.Equal(new byte[] { 1, 2 }, (await _db.GetItemAsync<WikiAsset>("A.png"))!.Data);
        Assert.NotEmpty(await _db.SearchAsync("oldneedle", 10));
        Assert.False((await Update()).UsedFullScan);
        Assert.DoesNotContain(_http.Requests, query => query.ContainsKey("generator"));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("namespaces")]
    [InlineData("format")]
    [InlineData("extractor")]
    [InlineData("state-version")]
    [InlineData("missing-anchor")]
    [InlineData("damaged")]
    public async Task Incompatible_import_keeps_offline_content_but_invalidates_only_the_cursor(string reason)
    {
        await Baseline();
        var state = await State();
        state = reason switch
        {
            "api" => state with { Identity = "https://other.test/api.php|0" },
            "namespaces" => state with { Identity = "https://wiki.test/api.php|0,100" },
            "format" => state with { FormatVersion = 3 },
            "extractor" => state with { ExtractorVersion = 3 },
            "state-version" => state with { Version = 2 },
            "missing-anchor" => state with { AnchorId = 0 },
            _ => state
        };
        await _db.WriteSyncMetadataAsync("sync_state", reason == "damaged" ? "broken" : JsonSerializer.Serialize(state), default);
        var transfer = new WikiDatabaseTransferService(_manager, _db, new WikiDataOperationGate());
        var source = Path.Combine(_root, "snapshot.db");
        await transfer.ExportDatabaseAsync(_paths, source, _db);
        var inspection = await transfer.InspectDatabaseAsync(source);
        await transfer.ImportDatabaseAsync(new WikiDatabaseImportRequest(source, _root, _book.Id, inspection.InspectionToken));
        Assert.Null(await _db.ReadSyncMetadataAsync("sync_state", default));
        Assert.NotEmpty(await _db.SearchAsync("oldneedle", 10));
        Assert.True((await Update()).UsedFullScan);
    }

    [Fact]
    public async Task Committed_pages_survive_maintenance_failure_and_maintenance_runs_only_once_without_vacuum()
    {
        await Baseline();
        _http.Pages["Home"] = _http.Pages["Home"] with { Modified = _http.Now.UtcDateTime, Html = "<p>newneedle</p>" };
        _http.Change("Home");
        var optimize = 0;
        var vacuum = 0;
        var connection = _db.GetConnection().GetConnection();
        connection.Trace = true;
        connection.Tracer = sql =>
        {
            if (sql.Contains("VACUUM", StringComparison.OrdinalIgnoreCase)) vacuum++;
            if (sql.Contains("VALUES('optimize')", StringComparison.Ordinal))
            {
                optimize++;
                throw new InvalidOperationException("fixture maintenance failure");
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Update());
        connection.Trace = false;
        Assert.Equal(1, optimize);
        Assert.Equal(0, vacuum);
        Assert.True((await State()).Incomplete);
        Assert.Contains("newneedle", (await _db.GetItemAsync<WikiPage>("Home"))!.Content);
        connection.Tracer = sql => { if (sql.Contains("VALUES('optimize')", StringComparison.Ordinal)) optimize++; };
        connection.Trace = true;
        var recovered = await Update();
        connection.Trace = false;
        Assert.Equal(0, recovered.Pages);
        Assert.Equal(2, optimize);
        Assert.Equal("0", await _db.ReadSyncMetadataAsync("sync_index_pending", default));
    }

    [Fact]
    public async Task Cancellation_waits_for_underlying_page_transaction_before_releasing_operation_gate()
    {
        await Baseline();
        _http.Pages["Home"] = _http.Pages["Home"] with { Modified = _http.Now.UtcDateTime };
        _http.Change("Home");
        using var cancel = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = _db.GetConnection().GetConnection();
        connection.Trace = true;
        connection.Tracer = sql =>
        {
            if (sql.Contains("DELETE FROM WikiSearchRegion WHERE PageTitle", StringComparison.Ordinal))
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("fixture transaction release");
            }
        };
        var update = Update(token: cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            var error = await Assert.ThrowsAsync<WikiOperationException>(() => Update());
            Assert.Equal("operation-in-progress", error.Code);
            Assert.False(update.IsCompleted);
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        connection.Trace = false;
        Assert.True((await State()).Incomplete);
    }

    public async Task DisposeAsync()
    {
        await _db.CloseConnection(); await _manager.CloseConnection();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed record RemotePage(string Title, int Ns = 0, DateTime Modified = default, string? Target = null, string Html = "<p>oldneedle</p>")
    {
        public DateTime Modified { get; init; } = Modified == default ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : Modified;
    }

    private sealed class SyncTransport : IWikiHttpTransport
    {
        public DateTimeOffset Now = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        public Dictionary<string, RemotePage> Pages = new(StringComparer.Ordinal);
        public Dictionary<string, (string Title, int Ns)[]> Dependents = new();
        public List<Dictionary<string, string>> Requests = [];
        public List<SyncEvent> Events = [new(1, new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero), "Home", 0, "edit", null, null)];
        public List<DateTimeOffset?> MediaConditions = [];
        public string? FailParse;
        public bool FailCurrent, RepeatContinuation, ChangeDuringParse, FailMedia, MissingFile;
        public Action<Dictionary<string, string>>? OnRequest;
        public Func<Dictionary<string, string>, string?>? Override;

        public void Change(string title, string? log = null, string? target = null, int ns = 0) =>
            Events.Add(new SyncEvent(Events.Count + 1, Now, title, ns, log is null ? "edit" : "log", log, target));

        public Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default)
        {
            var q = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : "");
            lock (Requests) Requests.Add(q);
            OnRequest?.Invoke(q); cancellationToken.ThrowIfCancellationRequested();
            if (Override?.Invoke(q) is { } response) return Task.FromResult(response);
            var root = new Dictionary<string, object>();
            var query = new Dictionary<string, object>(); root["query"] = query;
            if (q.ContainsKey("curtimestamp")) root["curtimestamp"] = Now.ToString("O");
            else if (q.GetValueOrDefault("action") == "parse")
            {
                var title = q["page"];
                if (title == FailParse) throw new HttpRequestException("fixture parse failure");
                if (ChangeDuringParse) Pages[title] = Pages[title] with { Modified = Pages[title].Modified.AddSeconds(1) };
                return Task.FromResult("<api><parse><text><![CDATA[" + Pages[title].Html + "]]></text></parse></api>");
            }
            else if (q.GetValueOrDefault("list") == "recentchanges")
            {
                var start = DateTimeOffset.Parse(q["rcstart"]);
                var selected = q["rcdir"] == "older" ? Events.Where(e => e.Time <= start).OrderByDescending(e => e.Time).Take(1) :
                    Events.Where(e => e.Time >= start && e.Time <= DateTimeOffset.Parse(q["rcend"]));
                query["recentchanges"] = selected.Select(e => new { rcid = e.Id, timestamp = e.Time.ToString("O"), title = e.Title, ns = e.Namespace, type = e.Type,
                    logtype = e.LogType, logparams = new { target_title = e.Target } }).ToArray();
            }
            else if (q.GetValueOrDefault("list") == "allredirects")
                query["allredirects"] = Pages.Values.Where(p => p.Target is not null &&
                    (Pages.GetValueOrDefault(p.Target)?.Ns ?? 0) == int.Parse(q["arnamespace"]))
                    .Select(p => new { fromid = Math.Abs((long)StringComparer.Ordinal.GetHashCode(p.Title)) + 1, title = p.Target }).ToArray();
            else if (q.GetValueOrDefault("list") is "embeddedin" or "imageusage")
            {
                var list = q["list"]; var title = q[list == "embeddedin" ? "eititle" : "iutitle"];
                query[list] = Dependents.GetValueOrDefault(title, []).Select(d => new { title = d.Title, ns = d.Ns }).ToArray();
            }
            else if (q.ContainsKey("generator"))
            {
                query["pages"] = Pages.Values.Where(p => p.Ns == int.Parse(q["gapnamespace"])).Select(p => Page(p.Title)).ToArray();
                if (RepeatContinuation) root["continue"] = new { gapcontinue = "same", @continue = "-||" };
            }
            else if (q.GetValueOrDefault("prop") == "imageinfo")
                query["pages"] = MissingFile
                    ? (object)new[] { new { title = q["titles"], ns = 6, missing = true, imagerepository = "" } }
                    : new[] { new { title = q["titles"], pageid = 100, ns = 6, imageinfo = new[] { new { url = "https://wiki.test/images/A.png" } } } };
            else if (q.ContainsKey("titles"))
            {
                if (FailCurrent) throw new HttpRequestException("fixture unavailable");
                var titles = q["titles"].Split('|');
                query["pages"] = titles.Select(Page).ToArray();
                if (q.ContainsKey("redirects"))
                    query["redirects"] = titles.Where(t => Pages.GetValueOrDefault(t)?.Target is not null).Select(t => new { from = t, to = Pages[t].Target }).ToArray();
            }
            else throw new InvalidOperationException(uri.ToString());
            return Task.FromResult(JsonSerializer.Serialize(root));
        }

        private Dictionary<string, object> Page(string title)
        {
            var value = new Dictionary<string, object> { ["title"] = title, ["ns"] = Pages.GetValueOrDefault(title)?.Ns ?? (title.StartsWith("File:") ? 6 : 0) };
            if (!Pages.TryGetValue(title, out var page)) value["missing"] = true;
            else
            {
                value["pageid"] = Math.Abs((long)StringComparer.Ordinal.GetHashCode(title)) + 1; value["touched"] = page.Modified.ToString("O");
                if (page.Target is not null) value["redirect"] = true;
            }
            return value;
        }

        public Task<WikiHttpContentResponse> GetContentAsync(WikiBook book, Uri uri, DateTimeOffset? ifModifiedSince = null, CancellationToken cancellationToken = default)
        {
            lock (MediaConditions) MediaConditions.Add(ifModifiedSince);
            if (FailMedia) throw new HttpRequestException("fixture media failure", null, HttpStatusCode.NotFound);
            return Task.FromResult(new WikiHttpContentResponse(HttpStatusCode.OK, [1, 2], "image/png", Now));
        }
    }
}
