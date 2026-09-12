using System.Net;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ViewerServerTests
{
    [Fact]
    public async Task ServesOnlyRequiredRoutesMethodsAndExactHost()
    {
        using var temp = new TestDirectory();
        var root = CreateStaticRoot(temp.Path);
        var source = new TestViewerDataSource();
        source.Assets["image.png"] = new ViewerAsset("image.png", [1, 2, 3], "image/png");
        source.Assets["sound.ogg"] = new ViewerAsset("sound.ogg", "OggS"u8.ToArray(), "audio/ogg");
        await using var server = new ViewerServer(new ViewerServerOptions
        {
            StaticRoot = root,
            WikiDataFolder = "Terraria_Wiki_zh",
            DataSource = source,
        });

        var first = await server.StartAsync();
        var second = await server.StartAsync();
        using var client = new HttpClient { BaseAddress = first.BaseUri };

        Assert.Equal(first.Port, second.Port);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("health")).StatusCode);
        var outer = await client.GetStringAsync("");
        Assert.Contains("/wiki/Terraria_Wiki_zh/index.html?theme=light&amp;isMobile=False&amp;host=Avalonia", outer, StringComparison.Ordinal);
        Assert.DoesNotContain("{{WIKI_URI}}", outer, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("wiki/Terraria_Wiki_zh/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("wiki/Terraria_Wiki_zh/site.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("_common/iframe-bridge.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("_common/wiki-app-common.js")).StatusCode);
        Assert.Equal("image/png", (await client.GetAsync("src/image.png")).Content.Headers.ContentType?.MediaType);
        Assert.Equal("audio/ogg", (await client.GetAsync("src/sound.ogg")).Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("wiki/Calamity_Wiki_zh/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("src/missing.png")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync("health", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "health"))).StatusCode);
        using (var badHost = new HttpRequestMessage(HttpMethod.Get, "health"))
        {
            badHost.Headers.Host = "127.0.0.1";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(badHost)).StatusCode);
        }
        using var health = await client.GetAsync("health");
        Assert.False(health.Headers.Contains("Access-Control-Allow-Origin"));

        await server.StopAsync();
        Assert.False(server.IsRunning);
        Assert.Equal(0, server.ActiveClientCount);
        await server.StopAsync();
        Assert.True((await server.StartAsync()).Port > 0);
    }

    [Fact]
    public async Task PortConflictTraversalAndDataFailureAreObservable()
    {
        using var temp = new TestDirectory();
        var root = CreateStaticRoot(temp.Path);
        await using var first = CreateServer(root, new TestViewerDataSource());
        var status = await first.StartAsync();
        await using var conflict = new ViewerServer(new ViewerServerOptions
        {
            StaticRoot = root,
            WikiDataFolder = "Terraria_Wiki_zh",
            DataSource = new TestViewerDataSource(),
            Port = status.Port,
        });
        await Assert.ThrowsAsync<ViewerServerStartException>(() => conflict.StartAsync());

        using (var client = new HttpClient { BaseAddress = status.BaseUri })
        {
            var traversal = await client.GetAsync("src/%2e%2e/secret");
            Assert.True(traversal.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("%5c..%5csecret")).StatusCode);
        }

        await first.StopAsync();
        var errors = new List<Exception>();
        await using var failing = new ViewerServer(new ViewerServerOptions
        {
            StaticRoot = root,
            WikiDataFolder = "Terraria_Wiki_zh",
            DataSource = new ThrowingViewerDataSource(),
            ErrorSink = errors.Add,
        });
        var failingStatus = await failing.StartAsync();
        using var failingClient = new HttpClient { BaseAddress = failingStatus.BaseUri };
        Assert.Equal(HttpStatusCode.InternalServerError, (await failingClient.GetAsync("src/fail.png")).StatusCode);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void OuterHostUsesExactOriginFrameSourceTypeAndRequestIdChecks()
    {
        var sourcePath = FindSourceRoot();
        var html = File.ReadAllText(System.IO.Path.Combine(sourcePath, "Assets", "viewer-host.html"));

        Assert.Contains("event.origin !== origin", html, StringComparison.Ordinal);
        Assert.Contains("event.source !== frame.contentWindow", html, StringComparison.Ordinal);
        Assert.Contains("value.type !== 'req' && value.type !== 'res'", html, StringComparison.Ordinal);
        Assert.Contains("typeof value.id === 'string'", html, StringComparison.Ordinal);
        Assert.Contains("source = 'terraria-wiki-viewer'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("postMessage(envelope, '*')", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WikiAssetsReportViewerReadyOnlyAfterHandlersAndInitialRedirect()
    {
        var repository = System.IO.Path.GetFullPath(System.IO.Path.Combine(FindSourceRoot(), "..", ".."));
        var script = File.ReadAllText(System.IO.Path.Combine(
            repository,
            "assets",
            "windows",
            "Viewer",
            "Web",
            "_common",
            "wiki-app-common.js"));
        var start = script.IndexOf("function start(config)", StringComparison.Ordinal);
        var handlers = script.IndexOf("registerHandlers(config);", start, StringComparison.Ordinal);
        var ready = script.IndexOf("callCSharpAsync(\"ViewerReady\"", handlers, StringComparison.Ordinal);
        var redirect = script.IndexOf("redirect(config.homePage, config, undefined, notifyViewerReady)", ready, StringComparison.Ordinal);
        var contentWritten = script.IndexOf("document.getElementById(\"mw-content-text\").innerHTML", redirect, StringComparison.Ordinal);
        var committed = script.IndexOf("callCSharpAsync(\"ViewerPageCommitted\"", contentWritten, StringComparison.Ordinal);
        var rendered = script.IndexOf("onRendered?.();", committed, StringComparison.Ordinal);
        var refresh = script.IndexOf("config.refresh();", rendered, StringComparison.Ordinal);

        Assert.True(start >= 0 && start < handlers);
        Assert.True(handlers < ready && ready < redirect);
        Assert.True(redirect < contentWritten && contentWritten < committed && committed < rendered && rendered < refresh);
        Assert.Contains("navigationVersion: navigationId", script, StringComparison.Ordinal);
        Assert.Contains("urlParams.get('host') === 'Avalonia'", script, StringComparison.Ordinal);
        Assert.Contains("window.parent.terrariaWikiHost", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopClosesAClientWithAnIncompleteRequestWithinItsShutdownBudget()
    {
        using var temp = new TestDirectory();
        await using var server = new ViewerServer(new ViewerServerOptions
        {
            StaticRoot = CreateStaticRoot(temp.Path),
            WikiDataFolder = "Terraria_Wiki_zh",
            DataSource = new TestViewerDataSource(),
            ShutdownTimeout = TimeSpan.FromMilliseconds(200),
        });
        var status = await server.StartAsync();
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, status.Port);
        await client.GetStream().WriteAsync("GET /health HTTP/1.1\r\n"u8.ToArray());
        await WaitUntilAsync(() => server.ActiveClientCount == 1);

        var timer = System.Diagnostics.Stopwatch.StartNew();
        await server.StopAsync();
        timer.Stop();

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), timer.Elapsed.ToString());
        Assert.Equal(0, server.ActiveClientCount);
    }

    internal static string CreateStaticRoot(string parent)
    {
        var root = System.IO.Path.Combine(parent, "viewer");
        Directory.CreateDirectory(System.IO.Path.Combine(root, "Web", "Terraria_Wiki_zh"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "Web", "_common"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "_common"));
        File.WriteAllText(System.IO.Path.Combine(root, "index.html"), File.ReadAllText(System.IO.Path.Combine(FindSourceRoot(), "Assets", "viewer-host.html")));
        File.WriteAllText(System.IO.Path.Combine(root, "Web", "Terraria_Wiki_zh", "index.html"), "<!doctype html><link rel='stylesheet' href='site.css'>");
        File.WriteAllText(System.IO.Path.Combine(root, "Web", "Terraria_Wiki_zh", "site.css"), "body{}");
        File.WriteAllText(System.IO.Path.Combine(root, "Web", "_common", "wiki-app-common.js"), "window.wikiApp={};");
        File.WriteAllText(System.IO.Path.Combine(root, "_common", "iframe-bridge.js"), "window.iframeBridge={};");
        return root;
    }

    private static ViewerServer CreateServer(string root, IViewerDataSource source) => new(new ViewerServerOptions
    {
        StaticRoot = root,
        WikiDataFolder = "Terraria_Wiki_zh",
        DataSource = source,
    });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private static string FindSourceRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source)!, "..", "..", "src", "Terraria_Wiki.Windows"));

    internal sealed class TestViewerDataSource : IViewerDataSource
    {
        public Dictionary<string, ViewerAsset> Assets { get; } = new(StringComparer.Ordinal);
        public Task<ViewerAsset?> ReadAssetAsync(string fileName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Assets.GetValueOrDefault(fileName));
        }
    }

    private sealed class ThrowingViewerDataSource : IViewerDataSource
    {
        public Task<ViewerAsset?> ReadAssetAsync(string fileName, CancellationToken cancellationToken = default) =>
            Task.FromException<ViewerAsset?>(new IOException("synthetic Viewer data failure"));
    }
}
