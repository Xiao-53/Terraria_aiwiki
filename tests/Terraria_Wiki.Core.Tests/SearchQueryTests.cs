using System.Text.Json;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Xunit;

namespace Terraria_Wiki.Core.Tests;

public sealed class SearchQueryTests
{
    private static readonly DateTime FixtureTime =
        new(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ChineseSingleAndDoubleCharacterTermsSearchRegionBodies()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "武器指南", "<h2 id='types'>类型</h2><p>剑和弓都是常见武器。</p>");

        foreach (var query in new[] { "剑", "弓", "武器" })
        {
            var result = (await SearchAsync(fixture, query)).First(item =>
                item.EvidenceText.Contains(query, StringComparison.Ordinal)
                && item.EvidenceText.Contains("常见", StringComparison.Ordinal));
            Assert.Equal("武器指南", result.CanonicalTitle);
            Assert.Contains(query, result.EvidenceText);
            Assert.NotEmpty(result.Highlights);
        }
    }

    [Fact]
    public async Task EightAcceptanceQueriesReachTheirSyntheticContentEvidence()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "光之女皇", "<div class='infobox'><div class='title'>光之女皇</div><div class='section drops'><div class='title'>掉落</div><ul><li>宝藏袋 100%</li></ul></div></div>");
        await SaveAsync(fixture, "猪龙鱼公爵", "<h2 id='loot'>掉落</h2><p>猪龙鱼公爵会掉落多种武器。</p>");
        await SaveAsync(fixture, "克苏鲁之眼", "<h2 id='loot'>掉落</h2><p>克苏鲁之眼掉落恶魔矿。</p>");
        await SaveAsync(fixture, "武器指南", "<p>剑和弓都是武器。</p>");
        await SaveAsync(fixture, "药剂指南", "<p>药水可以提供增益。</p>");
        await SaveAsync(fixture, "装备指南", "<p>饰品可以装备在配饰栏。</p>");
        var aliasPath = Path.Combine(fixture.RootPath, "aliases.json");
        await File.WriteAllTextAsync(aliasPath, JsonSerializer.Serialize(new
        {
            version = 1,
            wikis = new[]
            {
                new
                {
                    wiki_id = 1,
                    aliases = new[]
                    {
                        new { alias = "光女", target_title = "光之女皇" },
                        new { alias = "猪鲨", target_title = "猪龙鱼公爵" },
                        new { alias = "克眼", target_title = "克苏鲁之眼" },
                    }
                }
            }
        }));
        fixture.Database.ConfigureSearchAliases(WikiSearchAliasCatalog.Load(aliasPath));

        var checks = new[]
        {
            (Query: "光女 宝藏袋", CanonicalTitle: "光之女皇", EvidenceTerm: "宝藏袋"),
            (Query: "猪鲨 武器", CanonicalTitle: "猪龙鱼公爵", EvidenceTerm: "武器"),
            (Query: "克眼 掉落", CanonicalTitle: "克苏鲁之眼", EvidenceTerm: "掉落"),
            (Query: "剑", CanonicalTitle: "武器指南", EvidenceTerm: "剑"),
            (Query: "弓", CanonicalTitle: "武器指南", EvidenceTerm: "弓"),
            (Query: "武器", CanonicalTitle: "武器指南", EvidenceTerm: "武器"),
            (Query: "药水", CanonicalTitle: "药剂指南", EvidenceTerm: "药水"),
            (Query: "饰品", CanonicalTitle: "装备指南", EvidenceTerm: "饰品"),
        };

        foreach (var check in checks)
        {
            var results = await SearchAsync(fixture, check.Query);
            Assert.Contains(results, result => result.CanonicalTitle == check.CanonicalTitle
                && result.EvidenceText.Contains(check.EvidenceTerm, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TableAndInfoboxRowsRetainHeadersGroupsAndMergedContext()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        const string html = """
            <div class="infobox npc"><div class="title">光之女皇</div>
              <div class="section drops"><div class="title">掉落</div>
                <ul><li class="groupstart caption">专家模式</li><li>宝藏袋 100%</li></ul>
              </div>
            </div>
            <h2 id="loot">宝藏袋内容</h2>
            <table><caption>Boss 掉落</caption>
              <tr><th>Boss</th><th>物品</th><th>概率</th></tr>
              <tr><th colspan="3">专家模式</th></tr>
              <tr><th rowspan="2">光之女皇</th><td>星光</td><td>25%</td></tr>
              <tr><td>彩虹猫之刃</td><td>25%</td></tr>
            </table>
            """;
        await SaveAsync(fixture, "光之女皇", html);

        var bag = (await SearchAsync(fixture, "专家模式 宝藏袋 100%"))[0];
        Assert.Equal(WikiSearchScope.SameRegion, bag.Scope);
        Assert.Equal(WikiSearchEvidenceKind.Infobox, bag.EvidenceKind);
        Assert.Contains("掉落", bag.EvidenceText);
        Assert.Contains("光之女皇", bag.EvidenceText);

        var merged = (await SearchAsync(fixture, "光之女皇 彩虹猫之刃"))[0];
        Assert.Equal(WikiSearchEvidenceKind.TableRow, merged.EvidenceKind);
        Assert.Contains("Boss 掉落", merged.EvidenceText);
        Assert.Contains("专家模式", merged.EvidenceText);
        Assert.Contains("物品", merged.EvidenceText);
        Assert.Contains("光之女皇", merged.EvidenceText);
        Assert.Contains("彩虹猫之刃", merged.EvidenceText);
        Assert.Equal("loot", merged.Anchor);
        var sections = await fixture.Database.GetItemsAsync<WikiSearchSection>();
        Assert.Contains(sections, section => section.PageTitle == "光之女皇"
            && section.Title == "宝藏袋内容" && section.Anchor == "loot");
    }

    [Fact]
    public async Task SameRegionRanksBeforeSamePageAndPartialMatches()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "直接相关", "<h2>掉落</h2><p>克苏鲁之眼会掉落恶魔矿。</p>");
        await SaveAsync(fixture, "同页分散", "<h2>Boss</h2><p>克苏鲁之眼。</p><h2>战利品</h2><p>掉落物列表。</p>");
        await SaveAsync(fixture, "部分页面", "<p>这里只讨论掉落。</p>");

        var results = await SearchAsync(fixture, "克苏鲁之眼 掉落");

        Assert.Equal("直接相关", results[0].CanonicalTitle);
        Assert.Equal(WikiSearchScope.SameRegion, results[0].Scope);
        Assert.Contains(results, item => item.CanonicalTitle == "同页分散" && item.Scope == WikiSearchScope.SamePage);
        var pageRelaxation = results.First(item => item.CanonicalTitle == "同页分散" && item.Scope == WikiSearchScope.SamePage);
        Assert.True(pageRelaxation.EvidenceRegions.Count >= 2);
        Assert.Contains(pageRelaxation.EvidenceRegions, region => region.EvidenceText.Contains("克苏鲁之眼"));
        Assert.Contains(pageRelaxation.EvidenceRegions, region => region.EvidenceText.Contains("掉落物列表"));
        var firstPartial = results.ToList().FindIndex(item => item.Scope == WikiSearchScope.Partial);
        var lastFull = results.ToList().FindLastIndex(item => item.Scope != WikiSearchScope.Partial);
        Assert.True(firstPartial < 0 || firstPartial > lastFull);
    }

    [Fact]
    public async Task CommunityAliasesResolveDeterministicallyWithoutChangingRedirects()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "猪龙鱼公爵", "<h2 id='drops'>掉落</h2><p>会掉落多种强力武器。</p>");
        var aliasPath = Path.Combine(fixture.RootPath, "aliases.json");
        await File.WriteAllTextAsync(aliasPath, JsonSerializer.Serialize(new
        {
            version = 1,
            wikis = new[] { new { wiki_id = 1, aliases = new[] { new { alias = "猪鲨", target_title = "猪龙鱼公爵" } } } }
        }));
        fixture.Database.ConfigureSearchAliases(WikiSearchAliasCatalog.Load(aliasPath));

        var result = (await SearchAsync(fixture, "猪鲨 武器"))[0];

        Assert.Equal("猪龙鱼公爵", result.Title);
        Assert.Equal("猪龙鱼公爵", result.CanonicalTitle);
        Assert.Equal(WikiSearchEvidenceKind.ExactCommunityAlias, result.EvidenceKind);
        Assert.Equal(WikiSearchScope.SameRegion, result.Scope);
        Assert.Null(await fixture.Database.GetItemAsync<WikiRedirect>("猪鲨"));

        var canonicalFirst = await SearchAsync(fixture, "猪龙鱼公爵 猪鲨 武器");
        var aliasFirst = await SearchAsync(fixture, "武器 猪鲨 猪龙鱼公爵");
        Assert.Equal(
            canonicalFirst.Select(item => (item.CanonicalTitle, item.RegionOrdinal, item.Scope)),
            aliasFirst.Select(item => (item.CanonicalTitle, item.RegionOrdinal, item.Scope)));
        Assert.Equal(WikiSearchScope.SameRegion, canonicalFirst[0].Scope);
    }

    [Fact]
    public async Task OfficialRedirectResolvesToCanonicalPageAndKeepsNavigationIdentity()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "克苏鲁之眼", "<h2 id='loot'>掉落</h2><p>掉落矿石。</p>");
        await fixture.Database.SaveItemAsync(new WikiRedirect
        {
            FromName = "克眼", ToTarget = "克苏鲁之眼#loot"
        });

        var result = (await SearchAsync(fixture, "克眼 掉落"))[0];

        Assert.Equal("克眼", result.Title);
        Assert.Equal("克苏鲁之眼", result.RedirectTo);
        Assert.Equal("克苏鲁之眼", result.CanonicalTitle);
        Assert.Equal(WikiSearchEvidenceKind.ExactRedirect, result.EvidenceKind);
    }

    [Fact]
    public async Task SamePageMayReturnSeveralDistinctRegions()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "药水", "<h2 id='a'>制作</h2><p>药水需要水瓶。</p><h2 id='b'>使用</h2><p>药水提供增益。</p>");

        var results = await SearchAsync(fixture, "药水");

        Assert.True(results.Count >= 2);
        Assert.Equal(results.Count, results.Select(item => item.EvidenceText).Distinct().Count());
        Assert.Contains(results, item => item.Anchor == "a");
        Assert.Contains(results, item => item.Anchor == "b");
    }

    [Fact]
    public async Task UpdatingPageAtomicallyReplacesOldRegionsIncludingEmptyContent()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "更新页", "<p>旧词药水</p>");
        Assert.NotEmpty(await SearchAsync(fixture, "药水"));

        await SaveAsync(fixture, "更新页", "<p>新词饰品</p>");
        Assert.DoesNotContain(await SearchAsync(fixture, "药水"), item => item.CanonicalTitle == "更新页");
        Assert.Contains(await SearchAsync(fixture, "饰品"), item => item.CanonicalTitle == "更新页");

        await SaveAsync(fixture, "更新页", "<div></div>");
        Assert.DoesNotContain(await SearchAsync(fixture, "饰品"), item => item.CanonicalTitle == "更新页");
    }

    [Fact]
    public async Task Failed_page_transaction_rolls_back_html_regions_and_fts()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "事务页", "<p>保留旧证据</p>");
        await fixture.Database.GetConnection().ExecuteAsync(
            "CREATE TRIGGER fail_region_insert BEFORE INSERT ON WikiSearchRegion WHEN NEW.BodyText LIKE '%触发失败%' BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");

        await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(fixture, "事务页", "<p>触发失败的新证据</p>"));

        Assert.Contains(await SearchAsync(fixture, "旧证据"), item => item.CanonicalTitle == "事务页");
        Assert.DoesNotContain(await SearchAsync(fixture, "新证据"), item => item.CanonicalTitle == "事务页");
        Assert.Contains("保留旧证据", (await fixture.Database.GetItemAsync<WikiPage>("事务页"))!.Content);
    }

    [Fact]
    public async Task Page_delete_removes_region_rows_and_contentless_fts_entries_together()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "删除页", "<p>只在这里出现的羽落药水</p>");

        await fixture.Database.DeleteItemAsync<WikiPage>("删除页");

        Assert.Null(await fixture.Database.GetItemAsync<WikiPage>("删除页"));
        Assert.DoesNotContain(await SearchAsync(fixture, "羽落药水"), item => item.CanonicalTitle == "删除页");
        Assert.Equal(0, await fixture.Database.GetCountAsync<WikiSearchRegion>());
    }

    [Fact]
    public async Task Nested_lists_collapsed_modes_malformed_html_and_unwrapped_text_are_preserved()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        const string html = "<h2 id='craft'>制作</h2><ul><li>父项说明<ul><li>子项材料 +4%</li></ul></li></ul>"
            + "<dl><dt>射速</dt><dd>每秒两发</dd></dl>"
            + "<div class='mw-collapsible' style='display:none'><p>大师模式限定掉落</p></div>"
            + "<section>没有段落包裹的通用文字<span>以及尾部文字</span><div><b>异常标签";
        await SaveAsync(fixture, "结构页", html);

        var nested = Assert.Single(await SearchAsync(fixture, "父项说明 子项材料"),
            item => item.CanonicalTitle == "结构页" && item.Scope == WikiSearchScope.SameRegion);
        Assert.Equal(WikiSearchEvidenceKind.ListItem, nested.EvidenceKind);
        Assert.Contains("+4%", nested.EvidenceText);
        Assert.Contains(await SearchAsync(fixture, "大师模式"), item => item.CanonicalTitle == "结构页");
        Assert.Contains(await SearchAsync(fixture, "通用文字"), item => item.CanonicalTitle == "结构页");
        Assert.Contains(await SearchAsync(fixture, "+4%"), item => item.CanonicalTitle == "结构页");
        Assert.Contains(await SearchAsync(fixture, "异常标签"), item => item.CanonicalTitle == "结构页");
        var definition = Assert.Single(await SearchAsync(fixture, "射速 每秒"),
            item => item.CanonicalTitle == "结构页" && item.Scope == WikiSearchScope.SameRegion);
        Assert.Equal(WikiSearchEvidenceKind.Definition, definition.EvidenceKind);
    }

    [Fact]
    public async Task PreviewIsBoundedButFullEvidenceAndUtf16HighlightsRemainAvailable()
    {
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "长页面", $"<p>{new string('前', 220)}药水{new string('后', 220)}</p>");

        var result = Assert.Single(await SearchAsync(fixture, "药水"));

        Assert.True(result.Snippet.Length <= 182);
        Assert.True(result.EvidenceText.Length > result.Snippet.Length);
        Assert.All(result.Highlights, range =>
            Assert.Equal("药水", result.Snippet.Substring(range.Start, range.Length)));
    }

    [Fact]
    public async Task OptionsCancellationAndDeterministicOrderingRemainExplicit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WikiSearchOptions(0));
        await using var fixture = await TestDatabase.CreateContentAsync(CreateBook());
        await SaveAsync(fixture, "乙", "<p>武器</p>");
        await SaveAsync(fixture, "甲", "<p>武器</p>");
        var first = await SearchAsync(fixture, "武器");
        var second = await SearchAsync(fixture, "武器");
        Assert.Equal(first.Select(item => (item.CanonicalTitle, item.RegionOrdinal)),
            second.Select(item => (item.CanonicalTitle, item.RegionOrdinal)));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WikiReader(fixture.Database).SearchAsync("武器", new WikiSearchOptions(10), canceled.Token));
    }

    private static async Task SaveAsync(TestDatabase fixture, string title, string html)
    {
        var book = CreateBook();
        var processed = WikiContentProcessor.Process(html, book, title);
        await fixture.Database.SavePageWithSearchAsync(new WikiPage
        {
            Title = title,
            Content = processed.Html,
            LastModified = FixtureTime,
        }, processed);
    }

    private static async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        TestDatabase fixture,
        string query,
        int max = 100) =>
        await new WikiReader(fixture.Database).SearchAsync(query, new WikiSearchOptions(max));

    private static WikiBook CreateBook() => new()
    {
        Id = 1,
        Title = "泰拉瑞亚中文百科",
        BaseUrl = "https://terraria.wiki.gg",
        ApiBaseUrl = "https://terraria.wiki.gg/api.php",
        RedirectListUrl = "/wiki/Special:ListRedirects",
        DataFolder = "Terraria_Wiki_zh",
        DefaultPageTitle = "首页",
        DefaultPageContent = "请先下载数据",
    };
}
