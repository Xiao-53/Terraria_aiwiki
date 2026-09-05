using System.Net;
using System.Text.Json;
using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ViewerBridgeTests
{
    private static readonly Uri Origin = new("http://127.0.0.1:32145/");

    [Fact]
    public async Task ReadRedirectAnchorAndInMemoryBackUseApplicationBoundary()
    {
        await using var fixture = await BridgeApplicationFixture.CreateAsync();
        var bridge = new ViewerBridge(fixture.Application, fixture.ExternalUris);

        var redirect = await SendAsync(bridge, "redirect", "GetRedirectedTitleAndAnchorAsync", "Alias");
        var redirectPayload = JsonDocument.Parse(ResponseData(redirect));
        Assert.Equal("Target", redirectPayload.RootElement.GetProperty("title").GetString());
        Assert.Equal("Section", redirectPayload.RootElement.GetProperty("anchor").GetString());

        var page = await SendAsync(bridge, "page", "PageRedirectAsync", "Alias");
        var pagePayload = JsonDocument.Parse(ResponseData(page));
        Assert.Equal("Target", pagePayload.RootElement.GetProperty("title").GetString());
        Assert.Contains("/src/image.png", pagePayload.RootElement.GetProperty("content").GetString(), StringComparison.Ordinal);
        var legacyHistory = Assert.Single(await fixture.ReadLegacyHistoryAsync());
        Assert.Equal("Legacy page", legacyHistory.WikiTitle);

        await SendAsync(bridge, "history", "SaveToTabHistory", "{\"title\":\"Home\",\"position\":42}");
        var back = await SendAsync(bridge, "back", "WikiBackAsync", "");
        Assert.Equal("BackToPage", back.Command?.Method);
        Assert.Contains("\"position\":42", back.Command?.Data, StringComparison.Ordinal);
        Assert.Empty(bridge.History);
    }

    [Fact]
    public async Task AssetsAreReadOnlyThroughApplicationDataSource()
    {
        await using var fixture = await BridgeApplicationFixture.CreateAsync();
        var source = new ApplicationViewerDataSource(() => fixture.Application);

        var asset = await source.ReadAssetAsync("image.png");
        var missing = await source.ReadAssetAsync("missing.png");

        Assert.Equal("image/png", asset?.MimeType);
        Assert.Equal([1, 2, 3, 4], asset?.Data);
        Assert.Null(missing);
    }

    [Fact]
    public async Task OriginSourceTypeAndRequestIdAreStrictlyRejected()
    {
        await using var fixture = await BridgeApplicationFixture.CreateAsync();
        var bridge = new ViewerBridge(fixture.Application, fixture.ExternalUris);
        var validEnvelope = new { type = "req", id = "one", method = "PageRedirectAsync", data = "Target" };

        Assert.Null(await bridge.HandleOuterMessageAsync(Wrap(validEnvelope, source: "evil"), Origin));
        Assert.Null(await bridge.HandleOuterMessageAsync(Wrap(validEnvelope, origin: "http://127.0.0.1:9999"), Origin));
        Assert.Null(await bridge.HandleOuterMessageAsync(Wrap(new { type = "event", id = "one", method = "PageRedirectAsync", data = "Target" }), Origin));
        Assert.Null(await bridge.HandleOuterMessageAsync(Wrap(new { type = "req", id = "", method = "PageRedirectAsync", data = "Target" }), Origin));
        Assert.Null(await bridge.HandleOuterMessageAsync("not-json", Origin));
    }

    [Fact]
    public async Task ExternalLinksUnknownMethodsUnsupportedFeaturesAndCancellationAreObservable()
    {
        await using var fixture = await BridgeApplicationFixture.CreateAsync();
        var bridge = new ViewerBridge(fixture.Application, fixture.ExternalUris);

        var allowed = await SendAsync(bridge, "external", "OpenExternalWebsite", "https://terraria.wiki.gg/wiki/Sword");
        var denied = await SendAsync(bridge, "denied", "OpenExternalWebsite", "file:///C:/secret.txt");
        var unsupported = await SendAsync(bridge, "tab", "OpenInNewTab", "{}");
        var unknown = await SendAsync(bridge, "unknown", "DoesNotExist", "");
        var ready = await SendAsync(bridge, "ready", "ViewerReady", "Home");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await bridge.HandleOuterMessageAsync(
            Wrap(new { type = "req", id = "cancel", method = "PageRedirectAsync", data = "Target" }),
            Origin,
            cancellation.Token);

        Assert.Null(allowed.Error);
        Assert.Single(fixture.ExternalUris.Opened);
        Assert.Equal("external-uri-not-allowed", denied.Error?.Code);
        Assert.Equal("not-supported", unsupported.Error?.Code);
        Assert.Equal("unknown-viewer-method", unknown.Error?.Code);
        Assert.Null(ready.Error);
        Assert.Equal("cancelled", cancelled?.Error?.Code);
    }

    [Fact]
    public async Task DuplicateActiveRequestIdIsRejected()
    {
        await using var fixture = await BridgeApplicationFixture.CreateAsync();
        var blocking = new BlockingExternalUriLauncher();
        var bridge = new ViewerBridge(fixture.Application, blocking);
        var message = Wrap(new { type = "req", id = "same", method = "OpenExternalWebsite", data = "https://example.com" });

        var first = bridge.HandleOuterMessageAsync(message, Origin);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var duplicate = await bridge.HandleOuterMessageAsync(message, Origin);
        blocking.Release.TrySetResult();
        await first;

        Assert.Equal("duplicate-request-id", duplicate?.Error?.Code);
    }

    [Fact]
    public async Task SideButtonBackSettingCanChangeWithoutRecreatingBridge()
    {
        await using var fixture = await BridgeApplicationFixture.CreateAsync();
        var bridge = new ViewerBridge(fixture.Application, fixture.ExternalUris);
        await SendAsync(bridge, "history-setting", "SaveToTabHistory", "{\"title\":\"Home\",\"position\":0}");

        bridge.SetSideButtonBackEnabled(false);
        var disabled = await SendAsync(bridge, "back-disabled", "WikiBackAsync", "");

        Assert.Equal("side-button-back-disabled", disabled.Error?.Code);
        Assert.Single(bridge.History);

        bridge.SetSideButtonBackEnabled(true);
        var enabled = await SendAsync(bridge, "back-enabled", "WikiBackAsync", "");

        Assert.Null(enabled.Error);
        Assert.Equal("BackToPage", enabled.Command?.Method);
        Assert.Empty(bridge.History);
    }

    private static async Task<ViewerDispatchResult> SendAsync(
        ViewerBridge bridge,
        string id,
        string method,
        string data) =>
        Assert.IsType<ViewerDispatchResult>(await bridge.HandleOuterMessageAsync(
            Wrap(new { type = "req", id, method, data }),
            Origin));

    private static string ResponseData(ViewerDispatchResult result)
    {
        Assert.Null(result.Error);
        using var document = JsonDocument.Parse(result.ResponseJson);
        return document.RootElement.GetProperty("data").GetString()!;
    }

    private static string Wrap(object envelope, string? source = null, string? origin = null) =>
        JsonSerializer.Serialize(new
        {
            source = source ?? ViewerBridge.MessageSource,
            origin = origin ?? Origin.GetLeftPart(UriPartial.Authority),
            envelope,
        });

    private sealed class BlockingExternalUriLauncher : IExternalUriLauncher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}

internal sealed class BridgeApplicationFixture : IAsyncDisposable
{
    private BridgeApplicationFixture(
        TestDirectory directory,
        WikiApplication application,
        TestExternalUriLauncher externalUris,
        string contentDatabasePath)
    {
        Directory = directory;
        Application = application;
        ExternalUris = externalUris;
        ContentDatabasePath = contentDatabasePath;
    }

    public TestDirectory Directory { get; }
    public WikiApplication Application { get; }
    public TestExternalUriLauncher ExternalUris { get; }
    public string ContentDatabasePath { get; }

    public async Task<IReadOnlyList<WikiHistory>> ReadLegacyHistoryAsync()
    {
        var content = new ContentDbService(ContentDatabasePath);
        await content.Init();
        try { return await content.GetWikiHistoryPagedAsync(0, 20); }
        finally { await content.CloseConnection(); }
    }

    public static async Task<BridgeApplicationFixture> CreateAsync()
    {
        var temp = new TestDirectory();
        var manager = new ManagerDbService(System.IO.Path.Combine(temp.Path, "Manager.db"));
        await manager.Init();
        var book = (await manager.GetItemsAsync<WikiBook>()).Single(item => item.Id == 1);
        var paths = WikiDataPaths.Create(temp.Path, book);
        paths.EnsureDataDirectory();
        var content = new ContentDbService(paths.ContentDatabasePath);
        await content.Init(seedBook: book);
        await content.SaveItemAsync(new WikiPage
        {
            Title = "Target",
            Content = "<h2 id='Section'>Target</h2><img src='/src/image.png'><audio src='/src/sound.ogg'></audio>",
            LastModified = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc),
        });
        await content.SaveItemAsync(new WikiRedirect { FromName = "Alias", ToTarget = "Target#Section" });
        await content.SaveItemAsync(new WikiAsset { FileName = "image.png", Data = [1, 2, 3, 4], MimeType = "image/png" });
        await content.SaveItemAsync(new WikiAsset { FileName = "sound.ogg", Data = "OggS"u8.ToArray(), MimeType = "audio/ogg" });
        await content.SaveHistoryAsync(new WikiHistory
        {
            WikiTitle = "Legacy page",
            ReadAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            DateKey = "2026-08-01",
        });
        await content.CloseConnection();
        await manager.CloseConnection();

        var settings = new HostSettingsStore(System.IO.Path.Combine(temp.Path, "settings", "host-settings.json"));
        var application = new WikiApplication(temp.Path, settings, new NoopConsumerCoordinator(), new RejectingWikiTransport());
        await application.ActiveWiki.InitializeAsync();
        return new BridgeApplicationFixture(
            temp, application, new TestExternalUriLauncher(), paths.ContentDatabasePath);
    }

    public async ValueTask DisposeAsync()
    {
        try { await Application.DisposeAsync(); } catch { }
        Directory.Dispose();
    }
}

internal sealed class NoopConsumerCoordinator : IWikiDataConsumerCoordinator
{
    public ValueTask<IAsyncDisposable> PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable>(new NoopLease());
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class RejectingWikiTransport : IWikiHttpTransport
{
    public Task<string> GetStringAsync(WikiBook book, Uri uri, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected network request.");

    public Task<WikiHttpContentResponse> GetContentAsync(
        WikiBook book,
        Uri uri,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unexpected network request.");
}

internal sealed class TestExternalUriLauncher : IExternalUriLauncher
{
    public List<Uri> Opened { get; } = [];
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Opened.Add(uri);
        return Task.CompletedTask;
    }
}
