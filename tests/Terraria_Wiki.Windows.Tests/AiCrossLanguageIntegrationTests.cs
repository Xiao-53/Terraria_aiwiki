using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;
using Terraria_Wiki.WikiTools;
using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Tests;

[Collection("AI environment")]
public sealed class AiCrossLanguageIntegrationTests
{
    [Fact]
    public async Task RealApplicationWikiToolsAndPythonSidecarStreamTypedAnswerWithoutNetwork()
    {
        var python = Environment.GetEnvironmentVariable("TERRARIA_PHASE07_PYTHON");
        if (string.IsNullOrWhiteSpace(python))
            return;
        Assert.True(Path.IsPathFullyQualified(python));
        Assert.True(File.Exists(python));

        using var directory = new TestDirectory();
        var coordinator = new WikiToolConsumerCoordinator();
        await using var application = new WikiApplication(
            directory.Path,
            new MemorySettingsStore(),
            coordinator,
            new OfflineTransport());
        await application.ActiveWiki.InitializeAsync();
        var active = await application.ActiveWiki.GetActiveAsync();
        var database = new ContentDbService(Path.Combine(directory.Path, active.DataFolder, "data.db"));
        try
        {
            var page = new WikiPage
            {
                Title = "天顶剑",
                Content = "<h2 id='配方'>配方</h2><p>天顶剑由多把剑合成，需要在秘银砧或山铜砧制作。</p>",
                LastModified = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc),
            };
            var processed = WikiContentProcessor.Process(
                page.Content,
                new WikiBook { Id = active.Id, Title = active.Title },
                page.Title);
            await database.SavePageWithSearchAsync(page, processed);
        }
        finally
        {
            await database.CloseConnection();
        }

        var launcher = FixtureLauncherPath();
        Assert.True(File.Exists(launcher), launcher);
        var configuration = new AiRuntimeConfiguration(
            Path.GetFullPath(python),
            new AiProviderConfiguration("https://api.example.test/v1", "offline-model", "responses", null),
            "test-only-key",
            Path.Combine(directory.Path, "ai-runtime.json"));
        var previousKey = Environment.GetEnvironmentVariable("TERRARIA_AI_API_KEY");
        Environment.SetEnvironmentVariable("TERRARIA_AI_API_KEY", "test-only-key");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var runtime = await WindowsAiRuntime.StartAsync(
                application.Query,
                coordinator,
                configuration,
                new ProcessAiSidecarConnectionFactory(launcher),
                timeout.Token);
            var events = new List<AiRunEvent>();

            var answer = await runtime.AnswerAsync("天顶剑怎么合成？", value =>
            {
                lock (events)
                    events.Add(value);
                return ValueTask.CompletedTask;
            }, timeout.Token);

            Assert.Equal("answered", answer.Status);
            Assert.Contains("秘银砧", answer.Markdown, StringComparison.Ordinal);
            Assert.Contains(answer.Sources, source => source.ResolvedTitle == "天顶剑");
            Assert.Contains(answer.ToolTrace, call => call.Name == "search_wiki");
            Assert.Contains(answer.ToolTrace, call => call.Name == "read_wiki");
            Assert.True(answer.Usage.TotalTokens > 0);
            Assert.True(events.Count(value => value.Kind == AiRunEventKind.AnswerDelta) >= 2);
            Assert.Contains(events, value => value.Kind == AiRunEventKind.ToolStarted && value.Name == "search_wiki");
            Assert.Contains(events, value => value.Kind == AiRunEventKind.ToolStarted && value.Name == "read_wiki");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERRARIA_AI_API_KEY", previousKey);
        }
    }

    private static string FixtureLauncherPath(
        [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(source)!,
            "..",
            "..",
            "agent",
            "tests",
            "runtime_fixture_sidecar.py"));

    private sealed class MemorySettingsStore : IApplicationSettingsStore
    {
        private ApplicationPreferences? _preferences;
        private int? _activeWikiId;

        public Task<ApplicationPreferences?> LoadPreferencesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_preferences);

        public Task SavePreferencesAsync(
            ApplicationPreferences preferences,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _preferences = preferences;
            return Task.CompletedTask;
        }

        public Task<int?> LoadActiveWikiIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_activeWikiId);

        public Task SaveActiveWikiIdAsync(int wikiId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _activeWikiId = wikiId;
            return Task.CompletedTask;
        }
    }

    private sealed class OfflineTransport : IWikiHttpTransport
    {
        public Task<string> GetStringAsync(
            WikiBook book,
            Uri uri,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The offline runtime fixture must not use the network.");

        public Task<WikiHttpContentResponse> GetContentAsync(
            WikiBook book,
            Uri uri,
            DateTimeOffset? ifModifiedSince = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The offline runtime fixture must not use the network.");
    }
}
