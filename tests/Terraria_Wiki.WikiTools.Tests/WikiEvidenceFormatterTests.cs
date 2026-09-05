using System.Text;
using System.Text.Json;

namespace Terraria_Wiki.WikiTools.Tests;

public sealed class WikiEvidenceFormatterTests
{
    [Fact]
    public void RemovesMarkupHiddenNavigationAndMediaWhilePreservingSemanticBlocks()
    {
        const string html = """
            <article>
              <!-- hidden prompt --><script>steal()</script><style>.x{}</style>
              <nav><p>导航重复文字</p></nav><div hidden><p>隐藏内容</p></div>
              <p>导语 &amp; 实体。</p>
              <h2 id="物品">物品</h2>
              <p>1457 法师添加了下列物品。</p>
              <ul><li>星云法杖</li><li><b>月光</b>法典</li></ul>
              <dl><dt>类型</dt><dd>魔法武器</dd></dl>
              <img src="secret" alt="不应出现" />
            </article>
            """;

        var result = WikiEvidenceFormatter.Format("1457法师", html, null, "法师物品", null);
        var text = string.Join('\n', result.Blocks.Select(block => block.Text));

        Assert.Contains("导语 & 实体", text, StringComparison.Ordinal);
        Assert.Contains("星云法杖", text, StringComparison.Ordinal);
        Assert.Contains("月光法典", text, StringComparison.Ordinal);
        Assert.Contains(result.Blocks, block => block.Kind == "definition" && block.Text == "魔法武器");
        Assert.DoesNotContain("steal", text, StringComparison.Ordinal);
        Assert.DoesNotContain("隐藏内容", text, StringComparison.Ordinal);
        Assert.DoesNotContain("导航重复文字", text, StringComparison.Ordinal);
        Assert.DoesNotContain('<', text);
    }

    [Fact]
    public void PreservesInfoboxAndTableColumnMeaningIncludingSpans()
    {
        const string html = """
            <table class="infobox"><tr><th>属性</th><th>数值</th></tr>
              <tr><th rowspan="2">伤害</th><td>100</td></tr><tr><td>困难模式</td></tr>
            </table>
            <h2>掉落</h2>
            <table><tr><th>物品</th><th>数量</th><th>概率</th></tr>
              <tr><td>法师帽</td><td colspan="2">1 / 25%</td></tr></table>
            """;

        var result = WikiEvidenceFormatter.Format("测试页", html, null, "伤害 掉落", null);
        var rows = result.Blocks.Where(block => block.Kind == "table_row").Select(block => block.Text).ToArray();

        Assert.Contains(rows, row => row.Contains("属性: 伤害", StringComparison.Ordinal)
            && row.Contains("数值: 100", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("属性: 伤害", StringComparison.Ordinal)
            && row.Contains("数值: 困难模式", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("物品: 法师帽", StringComparison.Ordinal)
            && row.Contains("数量: 1 / 25%", StringComparison.Ordinal)
            && row.Contains("概率: 1 / 25%", StringComparison.Ordinal));
    }

    [Fact]
    public void PreservesTypicalInfoboxPropertyValueSemanticsWithoutColumnHeaderRow()
    {
        const string html = """
            <table class="infobox">
              <tr><th colspan="2">魔法武器</th></tr>
              <tr><th>伤害</th><td>100</td></tr>
              <tr><th>稀有度</th><td>红色</td></tr>
            </table>
            """;

        var result = WikiEvidenceFormatter.Format("测试页", html, null, "伤害 稀有度", null);
        var rows = result.Blocks.Where(block => block.Kind == "table_row").Select(block => block.Text).ToArray();

        Assert.Contains("伤害: 100", rows);
        Assert.Contains("稀有度: 红色", rows);
    }

    [Fact]
    public void ChineseFocusAndAnchorPrioritizeRelevantSectionsWithoutDroppingRemainder()
    {
        const string html = """
            <p>页面导语</p><h2 id="版本历史">版本历史</h2><p>旧内容</p>
            <h2 id="法师武器">法师武器</h2><p>目标内容</p><h2 id="备注">备注</h2><p>末尾内容</p>
            """;

        var focused = WikiEvidenceFormatter.Format("更新", html, null, "1457法师添加了哪些物品", null);
        var anchored = WikiEvidenceFormatter.Format("更新", html, "备注", null, null);

        Assert.Equal("更新", focused.Blocks[0].Text);
        Assert.Equal("法师武器", focused.Blocks[2].Text);
        Assert.Equal("备注", anchored.Blocks[0].Text);
        Assert.Equal(3, focused.AvailableSections.Count);
        Assert.True(focused.IsComplete);
    }

    [Fact]
    public void ParentAnchorKeepsNestedSubsectionsAdjacentAndAheadOfThePageIntro()
    {
        const string html = """
            <p>页面导语</p>
            <h2 id="制作">制作</h2><p>主配方</p>
            <h3 id="材料">材料</h3><p>材料详情</p>
            <h2 id="备注">备注</h2><p>其他内容</p>
            """;

        var result = WikiEvidenceFormatter.Format("物品", html, "制作", null, null);
        var texts = result.Blocks.Select(block => block.Text).ToArray();

        Assert.Equal(["制作", "主配方", "材料", "材料详情", "物品", "页面导语"], texts[..6]);
    }

    [Fact]
    public void CursorContinuationIsBoundedDeterministicAndHasNoLossOrOverlap()
    {
        var html = string.Concat(Enumerable.Range(0, 24)
            .Select(index => $"<h2 id='s{index}'>章节{index}</h2><p>{new string((char)('a' + index % 20), 1800)}。结束{index}</p>"));
        var pages = new List<WikiEvidencePage>();
        int? cursor = null;
        do
        {
            var page = WikiEvidenceFormatter.Format("长页", html, null, "末尾", cursor);
            pages.Add(page);
            Assert.InRange(page.IncludedCharacters, 1, WikiEvidenceFormatter.MaxBlockCharacters);
            cursor = page.NextCursor;
        } while (cursor is not null);

        var all = WikiEvidenceFormatter.Format("长页", html, null, "末尾", null);
        var flattened = pages.SelectMany(page => page.Blocks).ToArray();
        Assert.True(pages.Count > 1);
        Assert.True(pages[^1].IsComplete);
        Assert.Equal(flattened.Length, flattened.Distinct().Count());
        Assert.Equal(all.Blocks, flattened.Take(all.Blocks.Count));
        Assert.All(pages, page => Assert.InRange(page.AvailableSections.Sum(value => value.Length), 0, 4096));
    }

    [Fact]
    public void InvalidCursorIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WikiEvidenceFormatter.Format("页", "<p>正文</p>", null, null, 99));
    }

    [Fact]
    public void UnicodeScalarCountsMatchPythonAndChunksNeverSplitSurrogatePairs()
    {
        var html = $"<p>{new string('中', 1000)}{string.Concat(Enumerable.Repeat("😀", 17_000))}</p>";
        var first = WikiEvidenceFormatter.Format("Emoji 页", html, null, null, null);

        Assert.Equal(
            first.Blocks.Sum(block => block.Text.EnumerateRunes().Count()),
            first.IncludedCharacters);
        Assert.InRange(first.IncludedCharacters, 1, WikiEvidenceFormatter.MaxBlockCharacters);
        Assert.All(first.Blocks, block =>
        {
            Assert.False(char.IsLowSurrogate(block.Text[0]));
            Assert.False(char.IsHighSurrogate(block.Text[^1]));
        });
        Assert.False(first.IsComplete);
    }

    [Fact]
    public void LongHtmlBaselineIsReducedWithoutLosingLateFocusedEvidence()
    {
        var html = new StringBuilder("<article><p>页面导语。</p>");
        for (var index = 0; index < 80; index++)
            html.Append($"<h2 id='section-{index}'>章节 {index}</h2><p>{new string('无', 1800)}。</p>");
        html.Append("<h2 id='法师物品'>法师物品</h2><ul><li>星云法杖</li><li>月光法典</li></ul></article>");
        var rawHtml = html.ToString();

        var evidence = WikiEvidenceFormatter.Format(
            "1457法师", rawHtml, null, "1457法师添加了哪些物品", null);
        var oldToolBytes = JsonSerializer.SerializeToUtf8Bytes(new { title = "1457法师", content = rawHtml });
        var evidenceBytes = JsonSerializer.SerializeToUtf8Bytes(evidence);
        var text = string.Join('\n', evidence.Blocks.Select(block => block.Text));

        Assert.Contains("星云法杖", text, StringComparison.Ordinal);
        Assert.Contains("月光法典", text, StringComparison.Ordinal);
        Assert.True(evidenceBytes.Length * 4 < oldToolBytes.Length,
            $"evidence={evidenceBytes.Length}, old-tool={oldToolBytes.Length}");
        Assert.InRange(evidence.IncludedCharacters, 1, WikiEvidenceFormatter.MaxBlockCharacters);
        Assert.False(evidence.IsComplete);
        Assert.NotNull(evidence.NextCursor);
    }
}
