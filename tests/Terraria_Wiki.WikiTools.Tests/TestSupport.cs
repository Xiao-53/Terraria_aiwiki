using System.Net;
using ModelContextProtocol.Client;
using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.WikiTools.Tests;

internal sealed class WikiToolFixture : IAsyncDisposable
{
    private WikiToolFixture(
        string root,
        WikiApplication application,
        WikiToolConsumerCoordinator coordinator,
        WikiToolServer server)
    {
        Root = root;
        Application = application;
        Coordinator = coordinator;
        Server = server;
    }

    public string Root { get; }
    public WikiApplication Application { get; }
    public WikiQueryUseCases Query => Application.Query;
    public WikiToolConsumerCoordinator Coordinator { get; }
    public WikiToolServer Server { get; }

    public static async Task<WikiToolFixture> CreateAsync(
        ApplicationPreferences? preferences = null,
        bool initialize = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "terraria-wiki-tools-tests-" + Guid.NewGuid().ToString("N"));
        var coordinator = new WikiToolConsumerCoordinator();
        var application = new WikiApplication(
            root,
            new TestSettingsStore { Preferences = preferences },
            coordinator,
            new RejectingTransport());
        if (initialize)
            await application.ActiveWiki.InitializeAsync();
        var server = await WikiToolServer.StartAsync(application.Query, coordinator, CancellationToken.None);
        return new WikiToolFixture(root, application, coordinator, server);
    }

    public async Task<McpClient> CreateClientAsync(string? bearerToken = null)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = Server.Connection.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + (bearerToken ?? Server.Connection.BearerToken),
            },
        });
        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ProtocolVersion = "2025-11-25",
                InitializationTimeout = TimeSpan.FromSeconds(15),
            });
    }

    public async Task SaveSearchFixtureAsync()
    {
        var modified = new DateTime(2026, 8, 24, 12, 34, 56, DateTimeKind.Utc);
        await SavePagesAndIndexesAsync(
            ("Crystal", "crystal title content", "<p>Crystal raw HTML</p>", modified),
            ("crystal blade", "description", "<p>prefix</p>", modified),
            ("ancient crystal", "description", "<p>contains</p>", modified),
            ("Exact Redirect Target", "description", "<p>exact redirect</p>", modified),
            ("Prefix Redirect Target", "description", "<p>prefix redirect</p>", modified),
            ("Contains Redirect Target", "description", "<p>contains redirect</p>", modified),
            ("Body Entry", "body has crystal and 😀 marker", "<section><b>raw body</b></section>", modified));

        await WithContentDatabaseAsync(async database =>
        {
            await database.SaveItemsAsync(
            [
                new WikiRedirect { FromName = "crystal", ToTarget = "Exact Redirect Target#Section" },
                new WikiRedirect { FromName = "crystal bridge", ToTarget = "Prefix Redirect Target" },
                new WikiRedirect { FromName = "ancient crystal gate", ToTarget = "Contains Redirect Target" },
                new WikiRedirect { FromName = "Blade", ToTarget = "Crystal#Crafting" },
            ]);
        });
    }

    public async Task SavePagesAndIndexesAsync(
        params (string Title, string PlainContent, string Html, DateTime Modified)[] pages)
    {
        await WithContentDatabaseAsync(async database =>
        {
            await database.SaveItemsAsync(pages.Select(page => new WikiPage
            {
                Title = page.Title,
                Content = page.Html,
                LastModified = page.Modified,
            }));
            foreach (var page in pages)
                await database.SaveSearchIndexAsync(page.Title, page.PlainContent);
        });
    }

    private async Task WithContentDatabaseAsync(Func<ContentDbService, Task> operation)
    {
        var active = await Application.ActiveWiki.GetActiveAsync();
        var database = new ContentDbService(Path.Combine(Root, active.DataFolder, "data.db"));
        try
        {
            await operation(database);
        }
        finally
        {
            await database.CloseConnection();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Server.DisposeAsync();
        }
        finally
        {
            try
            {
                await Application.DisposeAsync();
            }
            finally
            {
                try
                {
                    if (Directory.Exists(Root))
                        Directory.Delete(Root, recursive: true);
                }
                catch
                {
                }
            }
        }
    }
}

internal sealed class TestSettingsStore : IApplicationSettingsStore
{
    public ApplicationPreferences? Preferences { get; set; }
    public int? ActiveWikiId { get; set; }

    public Task<ApplicationPreferences?> LoadPreferencesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Preferences);
    }

    public Task SavePreferencesAsync(
        ApplicationPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Preferences = preferences;
        return Task.CompletedTask;
    }

    public Task<int?> LoadActiveWikiIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ActiveWikiId);
    }

    public Task SaveActiveWikiIdAsync(int wikiId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActiveWikiId = wikiId;
        return Task.CompletedTask;
    }
}

internal sealed class RejectingTransport : IWikiHttpTransport
{
    public Task<string> GetStringAsync(
        WikiBook book,
        Uri uri,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Wiki Tool tests do not perform remote HTTP requests.");

    public Task<WikiHttpContentResponse> GetContentAsync(
        WikiBook book,
        Uri uri,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Wiki Tool tests do not perform remote HTTP requests.");
}
