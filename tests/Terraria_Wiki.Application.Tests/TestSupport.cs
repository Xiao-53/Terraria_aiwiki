using System.Net;
using Terraria_Wiki.Application.Internal;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application.Tests;

internal sealed class ApplicationFixture : IAsyncDisposable
{
    private ApplicationFixture(
        string root,
        WikiApplication application,
        TestSettingsStore settings,
        TestConsumerCoordinator consumers,
        ManualTimeProvider time)
    {
        Root = root;
        Application = application;
        Settings = settings;
        Consumers = consumers;
        Time = time;
    }

    public string Root { get; }
    public WikiApplication Application { get; }
    public TestSettingsStore Settings { get; }
    public TestConsumerCoordinator Consumers { get; }
    public ManualTimeProvider Time { get; }
    public ApplicationState State => Application.State;

    public static async Task<ApplicationFixture> CreateAsync(
        ApplicationPreferences? preferences = null,
        int? activeWikiId = null,
        IWikiHttpTransport? transport = null,
        bool initialize = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "terraria-application-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new TestSettingsStore
        {
            Preferences = preferences,
            ActiveWikiId = activeWikiId,
        };
        var consumers = new TestConsumerCoordinator();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero));
        var app = new WikiApplication(
            root,
            settings,
            consumers,
            transport ?? new RejectingTransport(),
            time);
        var fixture = new ApplicationFixture(root, app, settings, consumers, time);
        if (initialize)
            await app.ActiveWiki.InitializeAsync();
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Application.DisposeAsync();
        }
        catch
        {
        }

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

internal sealed class TestSettingsStore : IApplicationSettingsStore
{
    public ApplicationPreferences? Preferences { get; set; }
    public int? ActiveWikiId { get; set; }
    public int PreferencesSaveCount { get; private set; }
    public int ActiveWikiSaveCount { get; private set; }
    public Func<ApplicationPreferences, Exception?>? PreferencesSaveFailure { get; set; }
    public Func<int, Exception?>? ActiveWikiSaveFailure { get; set; }

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
        if (PreferencesSaveFailure?.Invoke(preferences) is { } failure)
            throw failure;
        Preferences = preferences;
        PreferencesSaveCount++;
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
        if (ActiveWikiSaveFailure?.Invoke(wikiId) is { } failure)
            throw failure;
        ActiveWikiId = wikiId;
        ActiveWikiSaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class TestConsumerCoordinator : IWikiDataConsumerCoordinator
{
    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public Exception? PauseFailure { get; set; }

    public ValueTask<IAsyncDisposable> PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PauseFailure is not null)
            throw PauseFailure;
        PauseCount++;
        return ValueTask.FromResult<IAsyncDisposable>(new Lease(this));
    }

    private sealed class Lease : IAsyncDisposable
    {
        private TestConsumerCoordinator? _owner;

        public Lease(TestConsumerCoordinator owner) => _owner = owner;

        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
                owner.ResumeCount++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value.ToUniversalTime();
}

internal sealed class RejectingTransport : IWikiHttpTransport
{
    public Task<string> GetStringAsync(
        WikiBook book,
        Uri uri,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"Unexpected HTTP request: {uri}");

    public Task<WikiHttpContentResponse> GetContentAsync(
        WikiBook book,
        Uri uri,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException($"Unexpected HTTP request: {uri}");
}

internal sealed class LifecycleFixtureTransport : IWikiHttpTransport
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
            return Task.FromResult("<div class='mw-spcontent'><ol><li><a>Blade</a><a>Sword</a></li></ol></div>");
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
        cancellationToken.ThrowIfCancellationRequested();
        LastIfModifiedSince = ifModifiedSince;
        return Task.FromResult(new WikiHttpContentResponse(
            ResourceStatus,
            ResourceStatus == HttpStatusCode.NotModified ? [] : [1, 2, 3],
            "image/png",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}

internal sealed class BlockingTransport : IWikiHttpTransport
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<string> GetStringAsync(
        WikiBook book,
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The blocking transport should only finish by cancellation.");
    }

    public Task<WikiHttpContentResponse> GetContentAsync(
        WikiBook book,
        Uri uri,
        DateTimeOffset? ifModifiedSince = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The blocking transport does not serve content.");
}
