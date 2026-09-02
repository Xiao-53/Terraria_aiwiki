using System.Diagnostics;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Xunit;
using Xunit.Abstractions;

namespace Terraria_Wiki.Core.Tests;

public sealed class SearchQueryTests
{
    private readonly ITestOutputHelper _output;

    public SearchQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly DateTime FixtureTime =
        new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SearchUsesSevenStableTiersAndDeduplicatesAcrossSources()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("Crystal", "crystal body text"),
            ("crystal blade", "crystal blade body text"),
            ("ancient crystal", "ancient crystal body text"),
            ("Exact Redirect Target", "redirect destination"),
            ("Prefix Redirect Target", "redirect destination"),
            ("Contains Redirect Target", "redirect destination"),
            ("Body Entry", "the crystal appears only in this body"));
        await fixture.Database.SaveItemsAsync(
        [
            new WikiRedirect { FromName = "crystal", ToTarget = "Exact Redirect Target#Section" },
            new WikiRedirect { FromName = "crystal bridge", ToTarget = "Prefix Redirect Target" },
            new WikiRedirect { FromName = "ancient crystal gate", ToTarget = "Contains Redirect Target" },
        ]);

        var results = await SearchAsync(fixture, "crystal", 100);

        Assert.Collection(
            results,
            item => AssertSearchItem(item, "Crystal", WikiSearchMatchKind.ExactTitle),
            item => AssertSearchItem(item, "crystal", WikiSearchMatchKind.ExactRedirect, "Exact Redirect Target"),
            item => AssertSearchItem(item, "crystal blade", WikiSearchMatchKind.TitlePrefix),
            item => AssertSearchItem(item, "crystal bridge", WikiSearchMatchKind.RedirectPrefix, "Prefix Redirect Target"),
            item => AssertSearchItem(item, "ancient crystal", WikiSearchMatchKind.TitleContains),
            item => AssertSearchItem(item, "ancient crystal gate", WikiSearchMatchKind.RedirectContains, "Contains Redirect Target"),
            item => AssertSearchItem(item, "Body Entry", WikiSearchMatchKind.BodyFts));
        Assert.Equal(results.Count, results.Select(item => item.Title).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task SameTierOrderingIsFullyDeterministic()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("ore zeta", "description"),
            ("ore Beta", "description"),
            ("ore alpha", "description"),
            ("ore aa", "description"));

        var first = await SearchAsync(fixture, "ore", 100);
        var second = await SearchAsync(fixture, "ore", 100);

        Assert.Equal(first.Select(ResultIdentity), second.Select(ResultIdentity));
        Assert.Equal(
            ["ore aa", "ore Beta", "ore zeta", "ore alpha"],
            first.Select(item => item.Title));
        Assert.All(first, item => Assert.Equal(WikiSearchMatchKind.TitlePrefix, item.MatchKind));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public async Task MaxResultsIsAppliedAfterRankingAndBodyIsNotCappedAtFifty(int maxResults)
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await InsertBulkCorpusAsync(fixture, 120, "body-limit-token");

        var results = await SearchAsync(fixture, "body-limit-token", maxResults);

        Assert.Equal(maxResults, results.Count);
        Assert.All(results, item => Assert.Equal(WikiSearchMatchKind.BodyFts, item.MatchKind));
    }

    [Fact]
    public async Task ExactMatchesSurviveLargeFuzzyPools()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await InsertPagesAsync(
            fixture,
            Enumerable.Range(0, 150).Select(index => $"fuzzy needle {index:D3}"));
        await SavePagesAndIndexesAsync(
            fixture,
            ("Needle", "exact page"),
            ("Needle Redirect Target", "redirect page"));
        await fixture.Database.SaveItemAsync(
            new WikiRedirect { FromName = "needle", ToTarget = "Needle Redirect Target#Anchor" });

        var results = await SearchAsync(fixture, "needle", 25);

        Assert.Equal("Needle", results[0].Title);
        Assert.Equal(WikiSearchMatchKind.ExactTitle, results[0].MatchKind);
        Assert.Equal("needle", results[1].Title);
        Assert.Equal(WikiSearchMatchKind.ExactRedirect, results[1].MatchKind);
        Assert.Equal(25, results.Count);
    }

    [Fact]
    public async Task MultiWordQueriesUseAndAndCollapseUnicodeWhitespace()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("Alpha Beta", "both alpha and beta"),
            ("Body Both", "alpha middle beta"),
            ("Body Alpha", "alpha only"),
            ("Body Beta", "beta only"));

        var results = await SearchAsync(fixture, " \talpha\u2003beta\r\n", 100);

        Assert.Equal("Alpha Beta", results[0].Title);
        Assert.Equal(WikiSearchMatchKind.ExactTitle, results[0].MatchKind);
        Assert.Contains(results, item => item.Title == "Body Both" && item.MatchKind == WikiSearchMatchKind.BodyFts);
        Assert.DoesNotContain(results, item => item.Title is "Body Alpha" or "Body Beta");
    }

    [Fact]
    public async Task SpecialCharactersAreSafeAndLikeWildcardsRemainLiteral()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("100%_safe", "literal percent underscore"),
            ("100xxsafe", "wildcard decoy"),
            ("\"quoted\"", "quoted title"),
            ("it's", "apostrophe title"),
            ("path\\name", "backslash title"),
            ("***", "symbol title"),
            ("Quote Body", "a pick\"axe is mentioned here"));

        Assert.Equal(["100%_safe"], (await SearchAsync(fixture, "%_", 100)).Select(item => item.Title));
        Assert.Equal("\"quoted\"", (await SearchAsync(fixture, "\"quoted\"", 100))[0].Title);
        Assert.Equal("it's", (await SearchAsync(fixture, "it's", 100))[0].Title);
        Assert.Equal("path\\name", (await SearchAsync(fixture, "path\\name", 100))[0].Title);
        Assert.Equal("***", (await SearchAsync(fixture, "***", 100))[0].Title);
        Assert.Contains(
            await SearchAsync(fixture, "pick\"axe", 100),
            item => item.Title == "Quote Body" && item.MatchKind == WikiSearchMatchKind.BodyFts);
        Assert.Empty(await SearchAsync(fixture, " \u2003\t\r\n", 100));
    }

    [Fact]
    public async Task ShortTermsOnlySearchTitlesAndRedirects()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("铜镐", "short title"),
            ("Short Body", "正文中只有镐"),
            ("Emoji Body", "正文中只有🙂"),
            ("Redirect Target", "target"));
        await fixture.Database.SaveItemAsync(
            new WikiRedirect { FromName = "镐别名", ToTarget = "Redirect Target" });

        var oneRune = await SearchAsync(fixture, "镐", 100);
        var emoji = await SearchAsync(fixture, "🙂", 100);

        Assert.Contains(oneRune, item => item.Title == "铜镐" && item.MatchKind == WikiSearchMatchKind.TitleContains);
        Assert.Contains(oneRune, item => item.Title == "镐别名" && item.MatchKind == WikiSearchMatchKind.RedirectPrefix);
        Assert.DoesNotContain(oneRune, item => item.Title == "Short Body");
        Assert.DoesNotContain(emoji, item => item.Title == "Emoji Body");
    }

    [Fact]
    public async Task MixedLongAndShortTermsFilterOnlyLongTermFtsCandidates()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("Mixed Match", "use the pickaxe to mine 铜 ore"),
            ("Long Only", "use the pickaxe to mine ore"),
            ("Short Only", "这里只有铜矿"));

        var results = await SearchAsync(fixture, "pickaxe 铜", 100);

        var result = Assert.Single(results);
        Assert.Equal("Mixed Match", result.Title);
        Assert.Equal(WikiSearchMatchKind.BodyFts, result.MatchKind);
    }

    [Fact]
    public async Task BrokenRedirectsAndStaleFtsRowsAreExcluded()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await fixture.Database.SaveItemAsync(
            new WikiRedirect { FromName = "ghost alias", ToTarget = "Missing#Anchor" });
        await fixture.Database.SaveSearchIndexAsync("Stale Fts", "ghost token body");

        Assert.Empty(await SearchAsync(fixture, "ghost", 100));
    }

    [Fact]
    public async Task SnippetsStayPlainTextAndExposeValidUtf16Highlights()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await SavePagesAndIndexesAsync(
            fixture,
            ("Unsafe Body", "<img src=x onerror=alert(1)> pickaxe & <script>bad()</script>"));

        var result = Assert.Single(await SearchAsync(fixture, "pickaxe", 100));

        Assert.Equal(WikiSearchMatchKind.BodyFts, result.MatchKind);
        Assert.DoesNotContain("<mark", result.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("search-highlight", result.Snippet, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Highlights);
        var previousEnd = 0;
        foreach (var highlight in result.Highlights)
        {
            Assert.InRange(highlight.Start, previousEnd, result.Snippet.Length);
            Assert.InRange(highlight.Length, 1, result.Snippet.Length - highlight.Start);
            Assert.Equal(
                "pickaxe",
                result.Snippet.Substring(highlight.Start, highlight.Length),
                ignoreCase: true);
            previousEnd = highlight.Start + highlight.Length;
        }
    }

    [Fact]
    public async Task OptionsCancellationAndUnavailableDataSourceAreExplicit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WikiSearchOptions(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WikiSearchOptions(101));

        await using var fixture = await TestDatabase.CreateContentAsync();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WikiReader(fixture.Database).SearchAsync(
                "anything",
                new WikiSearchOptions(10),
                canceled.Token));

        var root = Path.Combine(Path.GetTempPath(), "terraria-wiki-search-unavailable-" + Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "data.db");
        Directory.CreateDirectory(databasePath);
        var unavailable = new ContentDbService(databasePath);
        try
        {
            var error = await Assert.ThrowsAsync<WikiQueryException>(() =>
                new WikiReader(unavailable).SearchAsync("anything", new WikiSearchOptions(10)));
            Assert.Equal(WikiQueryErrorCode.DataSourceUnavailable, error.Code);
        }
        finally
        {
            try
            {
                await unavailable.CloseConnection();
            }
            catch
            {
            }

            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task TenThousandRowSearchesMeetWindowsReleaseBudget()
    {
        await using var fixture = await TestDatabase.CreateContentAsync();
        await InsertBulkCorpusAsync(fixture, 10_000, "performance-body-token");
        await SavePagesAndIndexesAsync(fixture, ("Unique Exact Title", "exact title description"));
        await fixture.Database.SaveItemAsync(
            new WikiRedirect { FromName = "Unique Exact Alias", ToTarget = "Bulk Page 00000#Anchor" });
        var reader = new WikiReader(fixture.Database);
        var options = new WikiSearchOptions(100);

        await reader.SearchAsync("Unique Exact Title", options);
        await reader.SearchAsync("Unique Exact Alias", options);
        await reader.SearchAsync("performance-body-token", options);

        var (titleResults, titleElapsed) = await MeasureAsync(() =>
            reader.SearchAsync("Unique Exact Title", options));
        var (redirectResults, redirectElapsed) = await MeasureAsync(() =>
            reader.SearchAsync("Unique Exact Alias", options));
        var (bodyResults, bodyElapsed) = await MeasureAsync(() =>
            reader.SearchAsync("performance-body-token", options));

        _output.WriteLine("Exact title: {0:N1} ms", titleElapsed.TotalMilliseconds);
        _output.WriteLine("Exact redirect: {0:N1} ms", redirectElapsed.TotalMilliseconds);
        _output.WriteLine("Body (100 results): {0:N1} ms", bodyElapsed.TotalMilliseconds);

        Assert.Equal(WikiSearchMatchKind.ExactTitle, titleResults[0].MatchKind);
        Assert.Equal(WikiSearchMatchKind.ExactRedirect, redirectResults[0].MatchKind);
        Assert.Equal(100, bodyResults.Count);
        Assert.All(bodyResults, item => Assert.Equal(WikiSearchMatchKind.BodyFts, item.MatchKind));
        Assert.True(titleElapsed < TimeSpan.FromSeconds(2), $"Exact title search took {titleElapsed.TotalMilliseconds:N0} ms.");
        Assert.True(redirectElapsed < TimeSpan.FromSeconds(2), $"Exact redirect search took {redirectElapsed.TotalMilliseconds:N0} ms.");
        Assert.True(bodyElapsed < TimeSpan.FromSeconds(2), $"Body search took {bodyElapsed.TotalMilliseconds:N0} ms.");
    }

    private static async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        TestDatabase fixture,
        string query,
        int maxResults) =>
        await new WikiReader(fixture.Database).SearchAsync(query, new WikiSearchOptions(maxResults));

    private static async Task SavePagesAndIndexesAsync(
        TestDatabase fixture,
        params (string Title, string PlainContent)[] pages)
    {
        await fixture.Database.SaveItemsAsync(
            pages.Select(page => new WikiPage
            {
                Title = page.Title,
                Content = $"<p>{page.Title}</p>",
                LastModified = FixtureTime,
            }));
        foreach (var page in pages)
            await fixture.Database.SaveSearchIndexAsync(page.Title, page.PlainContent);
    }

    private static async Task InsertPagesAsync(TestDatabase fixture, IEnumerable<string> titles)
    {
        var connection = fixture.Database.GetConnection();
        await connection.RunInTransactionAsync(transaction =>
        {
            foreach (var title in titles)
            {
                transaction.Insert(new WikiPage
                {
                    Title = title,
                    Content = $"<p>{title}</p>",
                    LastModified = FixtureTime,
                });
            }
        });
    }

    private static async Task InsertBulkCorpusAsync(TestDatabase fixture, int count, string bodyToken)
    {
        var connection = fixture.Database.GetConnection();
        await connection.RunInTransactionAsync(transaction =>
        {
            for (var index = 0; index < count; index++)
            {
                var title = $"Bulk Page {index:D5}";
                transaction.Insert(new WikiPage
                {
                    Title = title,
                    Content = $"<p>{title}</p>",
                    LastModified = FixtureTime,
                });
                transaction.Execute(
                    "INSERT INTO WikiSearchIndex (Title, PlainContent) VALUES (?, ?)",
                    title,
                    $"{bodyToken} fixture row {index:D5}");
            }
        });
    }

    private static async Task<(IReadOnlyList<SearchResultItem> Results, TimeSpan Elapsed)> MeasureAsync(
        Func<Task<IReadOnlyList<SearchResultItem>>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = await operation();
        stopwatch.Stop();
        return (results, stopwatch.Elapsed);
    }

    private static void AssertSearchItem(
        SearchResultItem item,
        string title,
        WikiSearchMatchKind matchKind,
        string redirectTo = "")
    {
        Assert.Equal(title, item.Title);
        Assert.Equal(matchKind, item.MatchKind);
        Assert.Equal(redirectTo, item.RedirectTo);
    }

    private static string ResultIdentity(SearchResultItem item) =>
        $"{(int)item.MatchKind}|{item.Title}|{item.RedirectTo}";
}
