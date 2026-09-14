using System.Diagnostics;
using System.Text.Json;
using HtmlAgilityPack;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;
using Xunit;
using Xunit.Abstractions;

namespace Terraria_Wiki.Core.Tests;

public sealed class RealWikiSearchAcceptanceTests(ITestOutputHelper output)
{
    private static readonly string[] Queries =
    [
        "光女 宝藏袋",
        "猪鲨 武器",
        "克眼 掉落",
        "剑",
        "弓",
        "武器",
        "药水",
        "饰品",
    ];

    [Fact]
    public async Task ExplicitRealDatabaseProducesSearchAcceptanceEvidence()
    {
        var databasePath = Environment.GetEnvironmentVariable("TERRARIA_WIKI_ACCEPTANCE_DB");
        if (string.IsNullOrWhiteSpace(databasePath))
            return;

        var aliasPath = Environment.GetEnvironmentVariable("TERRARIA_WIKI_ACCEPTANCE_ALIASES");
        var outputPath = Environment.GetEnvironmentVariable("TERRARIA_WIKI_ACCEPTANCE_OUTPUT");
        Assert.True(Path.IsPathFullyQualified(databasePath));
        Assert.True(File.Exists(databasePath));
        Assert.False(string.IsNullOrWhiteSpace(aliasPath));
        Assert.True(File.Exists(aliasPath));
        Assert.False(string.IsNullOrWhiteSpace(outputPath));
        Assert.True(Path.IsPathFullyQualified(outputPath));

        var database = new ContentDbService(Path.GetFullPath(databasePath));
        try
        {
            await database.Init(seedBook: new WikiBook { Id = 1 });
            database.ConfigureSearchAliases(WikiSearchAliasCatalog.Load(Path.GetFullPath(aliasPath)));
            var reader = new WikiReader(database);
            var firstRun = new List<QueryRun>();
            var warmRuns = new Dictionary<string, List<double>>(StringComparer.Ordinal);

            foreach (var query in Queries)
            {
                var (elapsed, results) = await TimedSearchAsync(reader, query);
                firstRun.Add(new QueryRun(query, elapsed, await DescribeAsync(database, results)));
            }

            const int warmIterations = 25;
            foreach (var query in Queries)
            {
                var timings = new List<double>(warmIterations);
                for (var iteration = 0; iteration < warmIterations; iteration++)
                {
                    var (elapsed, _) = await TimedSearchAsync(reader, query);
                    timings.Add(elapsed);
                }
                warmRuns.Add(query, timings);
            }

            var allWarm = warmRuns.Values.SelectMany(value => value).ToArray();
            var evidence = new AcceptanceEvidence(
                DateTimeOffset.UtcNow,
                Path.GetFullPath(databasePath),
                new FileInfo(databasePath).Length,
                firstRun,
                warmRuns.Select(pair => new TimingSummary(
                    pair.Key,
                    pair.Value.Count,
                    Percentile(pair.Value, 0.50),
                    Percentile(pair.Value, 0.95),
                    pair.Value.Min(),
                    pair.Value.Max())).ToArray(),
                new TimingSummary(
                    "all",
                    allWarm.Length,
                    Percentile(allWarm, 0.50),
                    Percentile(allWarm, 0.95),
                    allWarm.Min(),
                    allWarm.Max()));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(
                Path.GetFullPath(outputPath),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                }));
            output.WriteLine($"Acceptance evidence: {Path.GetFullPath(outputPath)}");

            Assert.All(firstRun, run => Assert.NotEmpty(run.Results));
            Assert.Equal("光之女皇", firstRun.Single(run => run.Query == "光女 宝藏袋").Results[0].CanonicalTitle);
            Assert.Equal("猪龙鱼公爵", firstRun.Single(run => run.Query == "猪鲨 武器").Results[0].CanonicalTitle);
            Assert.Equal("克苏鲁之眼", firstRun.Single(run => run.Query == "克眼 掉落").Results[0].CanonicalTitle);
            Assert.All(firstRun, run => Assert.All(run.Results, result =>
            {
                Assert.False(string.IsNullOrWhiteSpace(result.EvidenceText));
                Assert.True(result.EvidenceText.Length >= result.Preview.Length);
                Assert.True(result.EvidenceRegionCount > 0);
                Assert.True(result.AnchorExistsInViewerHtml);
            }));
            Assert.True(evidence.WarmOverall.P95Milliseconds <= 200,
                $"Warm query P95 was {evidence.WarmOverall.P95Milliseconds:F2} ms.");
        }
        finally
        {
            await database.CloseConnection();
        }
    }

    [Fact]
    public async Task ExplicitMutableCopyRebuildsAllSearchRegionsAndMeasuresPageUpdates()
    {
        var databasePath = Environment.GetEnvironmentVariable("TERRARIA_WIKI_REBUILD_DB");
        if (string.IsNullOrWhiteSpace(databasePath))
            return;

        var outputPath = Environment.GetEnvironmentVariable("TERRARIA_WIKI_REBUILD_OUTPUT");
        Assert.True(Path.IsPathFullyQualified(databasePath));
        Assert.True(File.Exists(databasePath));
        Assert.False(string.IsNullOrWhiteSpace(outputPath));
        Assert.True(Path.IsPathFullyQualified(outputPath));

        var book = new WikiBook
        {
            Id = 1,
            BaseUrl = "https://terraria.wiki.gg",
            ApiBaseUrl = "https://terraria.wiki.gg/zh/api.php",
            DataFolder = "Terraria_Wiki_zh",
            DefaultPageTitle = "Terraria Wiki",
            JunkXPath = "//div[@id='marker-for-new-portlet-link']|//span[@class='mw-editsection']|//comment()",
        };
        var database = new ContentDbService(Path.GetFullPath(databasePath));
        try
        {
            await database.Init(seedBook: book);
            var pageCount = await database.GetCountAsync<WikiPage>();
            const int batchSize = 20;
            var processedCount = 0;
            var timer = Stopwatch.StartNew();
            for (var offset = 0; offset < pageCount; offset += batchSize)
            {
                var pages = await database.GetConnection().QueryAsync<WikiPage>(
                    "SELECT * FROM WikiPage ORDER BY Title LIMIT ? OFFSET ?", batchSize, offset);
                foreach (var page in pages)
                {
                    var processed = WikiContentProcessor.Process(page.Content, book, page.Title);
                    await database.SavePageWithSearchAsync(page, processed);
                    processedCount++;
                }
                output.WriteLine($"Rebuilt {processedCount}/{pageCount} pages.");
            }
            timer.Stop();

            var representative = await database.GetItemAsync<WikiPage>("光之女皇")
                ?? throw new InvalidOperationException("The representative real page is missing.");
            var updateTimings = new List<double>();
            for (var iteration = 0; iteration < 20; iteration++)
            {
                var update = Stopwatch.StartNew();
                var processed = WikiContentProcessor.Process(representative.Content, book, representative.Title);
                await database.SavePageWithSearchAsync(representative, processed);
                update.Stop();
                updateTimings.Add(update.Elapsed.TotalMilliseconds);
            }

            var result = new RebuildEvidence(
                DateTimeOffset.UtcNow,
                Path.GetFullPath(databasePath),
                processedCount,
                await database.GetCountAsync<WikiSearchSection>(),
                await database.GetCountAsync<WikiSearchRegion>(),
                timer.Elapsed.TotalSeconds,
                timer.Elapsed.TotalMilliseconds / processedCount,
                new TimingSummary(
                    representative.Title,
                    updateTimings.Count,
                    Percentile(updateTimings, 0.50),
                    Percentile(updateTimings, 0.95),
                    updateTimings.Min(),
                    updateTimings.Max()));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            await File.WriteAllTextAsync(
                Path.GetFullPath(outputPath),
                JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true,
                }));
            output.WriteLine($"Rebuild evidence: {Path.GetFullPath(outputPath)}");
            Assert.Equal(pageCount, processedCount);
        }
        finally
        {
            await database.CloseConnection();
        }
    }

    [Fact]
    public async Task ExplicitAcceptedDatabaseUsesProtectedImportExchange()
    {
        var dataRoot = Environment.GetEnvironmentVariable("TERRARIA_WIKI_ACCEPTANCE_IMPORT_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
            return;
        var sourcePath = Environment.GetEnvironmentVariable("TERRARIA_WIKI_ACCEPTANCE_IMPORT_DB");
        Assert.True(Path.IsPathFullyQualified(dataRoot));
        Assert.True(Directory.Exists(dataRoot));
        Assert.False(string.IsNullOrWhiteSpace(sourcePath));
        Assert.True(Path.IsPathFullyQualified(sourcePath));
        Assert.True(File.Exists(sourcePath));

        var manager = new ManagerDbService(Path.Combine(dataRoot, "Manager.db"));
        ContentDbService? content = null;
        try
        {
            await manager.Init();
            var book = await manager.GetItemAsync<WikiBook>(1)
                ?? throw new InvalidOperationException("WikiBook 1 is not registered.");
            var paths = WikiDataPaths.Create(dataRoot, book);
            content = new ContentDbService(paths.ContentDatabasePath);
            var transfer = new WikiDatabaseTransferService(manager, content, new WikiDataOperationGate());
            var inspection = await transfer.InspectDatabaseAsync(sourcePath);
            var imported = await transfer.ImportDatabaseAsync(new WikiDatabaseImportRequest(
                sourcePath,
                dataRoot,
                book.Id,
                inspection.InspectionToken));

            Assert.True(imported.ReplacedExistingData);
            Assert.Equal(4359, imported.WikiBook.PageCount);
            Assert.Equal(28700, imported.WikiBook.RedirectCount);
            Assert.Equal(25730, imported.WikiBook.ResourceCount);
            Assert.False(WikiDatabaseTransferService.HasPendingImportJournal(dataRoot, book.Id));
            output.WriteLine($"Imported {inspection.DatabaseBytes} bytes with token {inspection.InspectionToken}.");
        }
        finally
        {
            if (content is not null)
                await content.CloseConnection();
            await manager.CloseConnection();
        }
    }

    private static async Task<(double ElapsedMilliseconds, IReadOnlyList<SearchResultItem> Results)> TimedSearchAsync(
        WikiReader reader,
        string query)
    {
        var timer = Stopwatch.StartNew();
        var results = await reader.SearchAsync(query, new WikiSearchOptions(10));
        timer.Stop();
        return (timer.Elapsed.TotalMilliseconds, results);
    }

    private static async Task<IReadOnlyList<ResultEvidence>> DescribeAsync(
        ContentDbService database,
        IReadOnlyList<SearchResultItem> results)
    {
        var described = new List<ResultEvidence>(results.Count);
        foreach (var result in results)
        {
            var anchorExists = true;
            if (!string.IsNullOrWhiteSpace(result.Anchor))
            {
                var page = await database.GetItemAsync<WikiPage>(result.CanonicalTitle);
                var document = new HtmlDocument();
                document.LoadHtml(page?.Content ?? string.Empty);
                var expectedAnchor = result.Anchor.TrimStart('#');
                anchorExists = document.DocumentNode.DescendantsAndSelf().Any(node =>
                    string.Equals(
                        HtmlEntity.DeEntitize(node.GetAttributeValue("id", string.Empty)),
                        expectedAnchor,
                        StringComparison.Ordinal));
            }
            described.Add(new ResultEvidence(
                result.Title,
                result.CanonicalTitle,
                result.RegionId,
                result.RegionOrdinal,
                result.SectionPath,
                result.Anchor,
                result.Scope.ToString(),
                result.EvidenceKind.ToString(),
                result.EvidenceText,
                result.Snippet,
                result.EvidenceRegions.Count,
                anchorExists));
        }
        return described;
    }

    private static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        var index = Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private sealed record AcceptanceEvidence(
        DateTimeOffset CapturedAt,
        string DatabasePath,
        long DatabaseBytes,
        IReadOnlyList<QueryRun> FirstRun,
        IReadOnlyList<TimingSummary> WarmByQuery,
        TimingSummary WarmOverall);

    private sealed record QueryRun(
        string Query,
        double ElapsedMilliseconds,
        IReadOnlyList<ResultEvidence> Results);

    private sealed record TimingSummary(
        string Query,
        int Samples,
        double P50Milliseconds,
        double P95Milliseconds,
        double MinimumMilliseconds,
        double MaximumMilliseconds);

    private sealed record RebuildEvidence(
        DateTimeOffset CapturedAt,
        string DatabasePath,
        int PagesProcessed,
        int SectionCount,
        int RegionCount,
        double TotalSeconds,
        double AverageMillisecondsPerPage,
        TimingSummary RepresentativePageUpdates);

    private sealed record ResultEvidence(
        string Title,
        string CanonicalTitle,
        long RegionId,
        int RegionOrdinal,
        string SectionPath,
        string Anchor,
        string Scope,
        string EvidenceKind,
        string EvidenceText,
        string Preview,
        int EvidenceRegionCount,
        bool AnchorExistsInViewerHtml);
}
