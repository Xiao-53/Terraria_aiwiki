using System.Net;
using Phase00.Probes;
using SQLite;

namespace Phase00.Probes.Tests;

public sealed class ViewerServerTests
{
    [Fact]
    public async Task ServesHealthStaticAssetAndStrictMethodsAndHosts()
    {
        using var temp = new TestDirectory();
        var db = System.IO.Path.Combine(temp.Path, "fixture.db");
        await SqliteProbe.CreateFixtureAsync(db);
        AddAudioAsset(db);
        var staticRoot = CreateStaticRoot(temp.Path);
        await using var server = new ViewerServer(new ViewerServerOptions { StaticRoot = staticRoot, DataSource = new SqliteViewerDataSource(db) });

        var first = await server.StartAsync();
        var second = await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(first.Origin) };

        Assert.Equal(first.Port, second.Port);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("wiki/Terraria_Wiki_zh/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("_common/iframe-bridge.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("src/fixture.png")).StatusCode);
        Assert.Equal(1, server.GetRequestCount("/src/fixture.png"));
        using (var audio = await client.GetAsync("src/fixture.ogg"))
        {
            Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
            Assert.Equal("audio/ogg", audio.Content.Headers.ContentType?.MediaType);
        }
        Assert.Equal(1, server.GetRequestCount("/src/fixture.ogg"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("not-found")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "health"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("health", null)).StatusCode);
        using (var badHost = new HttpRequestMessage(HttpMethod.Get, "health"))
        {
            badHost.Headers.Host = "evil.example";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badHost)).StatusCode);
        }
        Assert.DoesNotContain("Access-Control-Allow-Origin", (await client.GetAsync("health")).Headers.SelectMany(x => x.Value));

        await server.StopAsync();
        Assert.False(server.IsRunning);
        Assert.Equal(0, server.ActiveClientCount);
        await server.StopAsync();
        var restarted = await server.StartAsync();
        Assert.True(restarted.Running);
        await server.StopAsync();
    }

    [Fact]
    public async Task ExistingPortConflictIsObservable()
    {
        using var temp = new TestDirectory();
        var root = CreateStaticRoot(temp.Path);
        await using var first = new ViewerServer(new ViewerServerOptions { StaticRoot = root });
        var status = await first.StartAsync();
        await using var second = new ViewerServer(new ViewerServerOptions { StaticRoot = root, Port = status.Port });
        await Assert.ThrowsAsync<ViewerServerStartException>(() => second.StartAsync());
    }

    [Fact]
    public async Task TraversalAndMissingAssetsAreNeverServed()
    {
        using var temp = new TestDirectory();
        var root = CreateStaticRoot(temp.Path);
        await using var server = new ViewerServer(new ViewerServerOptions { StaticRoot = root });
        var status = await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(status.Origin) };
        var traversal = await client.GetAsync("src/%2e%2e/%2e%2e/secret");
        Assert.True(traversal.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
        var backslashTraversal = await client.GetAsync("%5c..%5csecret");
        Assert.Equal(HttpStatusCode.BadRequest, backslashTraversal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("src/missing.png")).StatusCode);
    }

    [Fact]
    public async Task BridgeResolvesRedirectAnchorAndKeepsHistorySnapshot()
    {
        using var temp = new TestDirectory();
        var db = System.IO.Path.Combine(temp.Path, "fixture.db");
        await SqliteProbe.CreateFixtureAsync(db);
        var bridge = new SqliteWikiViewerBridge(db);

        var redirected = await bridge.PageRedirectAsync("Alias");
        var anchor = await bridge.GetRedirectedTitleAndAnchorAsync("Alias");
        var explicitAnchor = await bridge.GetRedirectedTitleAndAnchorAsync("Redirected#Section");
        await bridge.SaveToTabHistoryAsync("{\"title\":\"Terraria Wiki\",\"position\":42}");
        bridge.ChangeTheme("True");

        Assert.Equal("Redirected", redirected?.Title);
        Assert.Equal("Redirected", anchor.Title);
        Assert.Equal("Section", anchor.Anchor);
        Assert.Equal("Redirected", explicitAnchor.Title);
        Assert.Equal("Section", explicitAnchor.Anchor);
        Assert.Single(bridge.History);
        Assert.Equal(42, bridge.History[0].Position);
        Assert.True(bridge.IsDarkTheme);
    }

    [Fact]
    public async Task DataSourceFailureReturnsServerErrorAndDoesNotBecomeSuccess()
    {
        using var temp = new TestDirectory();
        var errors = new List<Exception>();
        await using var server = new ViewerServer(new ViewerServerOptions
        {
            StaticRoot = CreateStaticRoot(temp.Path),
            DataSource = new ThrowingDataSource(),
            ErrorSink = errors.Add
        });
        var status = await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(status.Origin) };

        using var response = await client.GetAsync("src/failure.png");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void OuterViewerPageForwardsOnlySameOriginFrameMessages()
    {
        var indexPath = GetViewerIndexPath();
        Assert.True(File.Exists(indexPath), $"Viewer host asset was not found: {indexPath}");
        var index = File.ReadAllText(indexPath);

        Assert.Contains("event.origin !== origin", index, StringComparison.Ordinal);
        Assert.Contains("event.source !== frame.contentWindow", index, StringComparison.Ordinal);
        Assert.Contains("event.data.type !== 'req' && event.data.type !== 'res'", index, StringComparison.Ordinal);
        Assert.Contains("source: 'phase00-viewer'", index, StringComparison.Ordinal);
    }

    private static string GetViewerIndexPath(
        [System.Runtime.CompilerServices.CallerFilePath] string sourcePath = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(sourcePath)!,
            "..",
            "..",
            "assets",
            "index.html"));

    private static string CreateStaticRoot(string temp)
    {
        var root = System.IO.Path.Combine(temp, "static");
        Directory.CreateDirectory(System.IO.Path.Combine(root, "Web", "Terraria_Wiki_zh"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "_common"));
        File.WriteAllText(System.IO.Path.Combine(root, "index.html"), "<iframe id='wiki-frame'></iframe>");
        File.WriteAllText(System.IO.Path.Combine(root, "Web", "Terraria_Wiki_zh", "index.html"), "<!doctype html><html></html>");
        File.WriteAllText(System.IO.Path.Combine(root, "_common", "iframe-bridge.js"), "window.iframeBridge={};");
        return root;
    }

    private static void AddAudioAsset(string databasePath)
    {
        using var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
        connection.Execute("INSERT OR REPLACE INTO WikiAsset (FileName, Data, MimeType, LastModified) VALUES (?, ?, ?, ?)",
            "fixture.ogg", "OggS\0phase00"u8.ToArray(), "audio/ogg", "2026-08-20T00:00:00Z");
    }

    private sealed class ThrowingDataSource : IViewerDataSource
    {
        public Task<ViewerAsset?> GetAssetAsync(string fileName, CancellationToken cancellationToken = default) =>
            Task.FromException<ViewerAsset?>(new IOException("synthetic asset source failure"));
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phase00-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
