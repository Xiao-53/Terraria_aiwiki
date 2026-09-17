using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.WikiTools.Tests;

public sealed class WikiSearchNavigationTests
{
    [Fact]
    public void NavigationDeduplicatesPreservesRepresentativeAndMarksOmissions()
    {
        var item = new SearchResultItem
        {
            Title = "别名", CanonicalTitle = "页", SectionPath = "主节", Anchor = "real-id",
            Scope = WikiSearchScope.Partial,
            EvidenceRegions = Enumerable.Range(0, 7).Select(i => new WikiSearchEvidenceBlock(
                i, i, i == 0 ? "主节" : $"节{i}", i == 0 ? "real-id" : $"id-{i}",
                WikiSearchEvidenceKind.Body, "should not be copied")).ToArray(),
        };
        var mapped = WikiMcpTools.MapSearchResult(item);
        Assert.Equal("页", mapped.CanonicalTitle);
        Assert.Equal("partial", mapped.MatchScope);
        Assert.Equal(5, mapped.MatchedSections.Count);
        Assert.Equal(new WikiSearchToolSection("主节", "real-id"), mapped.MatchedSections[0]);
        Assert.Equal(new WikiSearchToolSection("节4", "id-4"), mapped.MatchedSections[4]);
        Assert.True(mapped.MatchedSectionsTruncated);
    }

    [Fact]
    public void NavigationCountsUnicodeScalarsAndNeverCutsAnchors()
    {
        var exact = string.Concat(Enumerable.Repeat("😀", 2047));
        var item = new SearchResultItem { SectionPath = exact, Anchor = "x" };
        var mapped = WikiMcpTools.MapSearchResult(item);
        Assert.Equal(exact, Assert.Single(mapped.MatchedSections).SectionPath);
        Assert.False(mapped.MatchedSectionsTruncated);
        item.Anchor = "xy";
        item.EvidenceRegions = [new WikiSearchEvidenceBlock(1, 1, "导语", "", WikiSearchEvidenceKind.Body, "")];
        mapped = WikiMcpTools.MapSearchResult(item);
        Assert.Equal(new WikiSearchToolSection("导语", ""), Assert.Single(mapped.MatchedSections));
        Assert.True(mapped.MatchedSectionsTruncated);
    }

    [Theory]
    [InlineData("测试武器 掉落")]
    [InlineData("武器别名 掉落")]
    [InlineData("猪鲨 武器")]
    public async Task RealProcessedSearchAnchorsDriveSectionRead(string query)
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        var book = new WikiBook();
        await fixture.WithContentDatabaseAsync(async database =>
        {
            foreach (var title in new[] { "测试武器", "猪龙鱼公爵" })
            {
                var processed = WikiContentProcessor.Process(
                    "<p>仅专家模式。</p><h2 id='condition'>条件</h2><p>击败守卫后。</p>"
                    + "<h3 id='drops-real'>掉落武器</h3><p>掉落测试武器，数量2。</p>"
                    + "<h2 id='unrelated'>无关</h2><p>不应混入的正文。</p>", book, title);
                await database.SavePageWithSearchAsync(new WikiPage
                { Title = title, Content = processed.Html, LastModified = new DateTime(2026, 9, 16) }, processed);
            }
            await database.SaveItemAsync(new WikiRedirect { FromName = "武器别名", ToTarget = "测试武器#drops-real" });
        });
        await using var client = await fixture.CreateClientAsync();
        var search = await client.CallToolAsync("search_wiki", new Dictionary<string, object?> { ["query"] = query });
        Assert.False(search.IsError);
        var candidate = search.StructuredContent!.Value.GetProperty("results").EnumerateArray().First();
        var target = candidate.GetProperty("matched_sections").EnumerateArray()
            .First(section => section.GetProperty("anchor").GetString() == "drops-real");
        var read = await client.CallToolAsync("read_wiki", new Dictionary<string, object?>
        {
            ["title"] = candidate.GetProperty("canonical_title").GetString() + "#" + target.GetProperty("anchor").GetString(),
            ["mode"] = "section",
        });
        Assert.False(read.IsError);
        var body = read.StructuredContent!.Value;
        Assert.True(body.GetProperty("scope").GetProperty("anchor_found").GetBoolean());
        var text = string.Join("\n", body.GetProperty("blocks").EnumerateArray().Select(block => block.GetProperty("text").GetString()));
        Assert.Contains("仅专家模式", text);
        Assert.Contains("击败守卫后", text);
        Assert.Contains("数量2", text);
        Assert.DoesNotContain("不应混入", text);
    }
}
