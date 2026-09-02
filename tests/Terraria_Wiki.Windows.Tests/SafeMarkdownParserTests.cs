using Terraria_Wiki.Windows.Presentation.Markdown;

namespace Terraria_Wiki.Windows.Tests;

public sealed class SafeMarkdownParserTests
{
    private readonly SafeMarkdownParser _parser = new();

    [Fact]
    public void ParsesSupportedNativeBlocksAndCjkEmphasis()
    {
        const string markdown = """
            # 标题

            中文**重点**，还有*强调*。

            > 引用

            1. 第一项
            2. 第二项

            ```csharp
            Console.WriteLine("safe");
            ```

            | 名称 | 数量 |
            | --- | ---: |
            | 木材 | 10 |
            """;

        var document = _parser.Parse(markdown);

        Assert.Contains(document.Blocks, block => block.Kind == SafeMarkdownBlockKind.Heading);
        Assert.Contains(AllInlines(document.Blocks), inline => inline.Text == "重点" && inline.IsBold);
        Assert.Contains(AllInlines(document.Blocks), inline => inline.Text == "强调" && inline.IsItalic);
        Assert.Contains(document.Blocks, block => block.Kind == SafeMarkdownBlockKind.Quote);
        Assert.Contains(document.Blocks, block => block.Kind == SafeMarkdownBlockKind.List && block.IsOrdered);
        Assert.Contains(document.Blocks, block => block.Kind == SafeMarkdownBlockKind.Code);
        var table = Assert.Single(document.Blocks, block => block.Kind == SafeMarkdownBlockKind.Table);
        Assert.Equal(2, table.Rows!.Count);
        Assert.All(table.Rows[0], cell => Assert.True(cell.IsHeader));
    }

    [Fact]
    public void HtmlImagesLinksAndMaliciousUrisRemainNonNavigableText()
    {
        const string markdown = """
            <script>window.location='https://evil.example'</script>

            [普通链接](https://example.com) [恶意](javascript:alert(1))

            ![远程图片](https://example.com/pixel.png)
            """;

        var document = _parser.Parse(markdown);
        var visibleText = string.Concat(AllInlines(document.Blocks).Select(inline => inline.Text));

        Assert.Contains("<script>", visibleText, StringComparison.Ordinal);
        Assert.Contains("普通链接", visibleText, StringComparison.Ordinal);
        Assert.Contains("恶意", visibleText, StringComparison.Ordinal);
        Assert.Contains("远程图片", visibleText, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pixel.png", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.All(AllInlines(document.Blocks), inline => Assert.IsType<string>(inline.Text));
    }

    [Fact]
    public void AcceptsExactAnswerBoundaryAndRejectsOneCharacterMore()
    {
        var exact = new string('文', SafeMarkdownParser.MaxMarkdownLength);
        var document = _parser.Parse(exact);

        Assert.Equal(exact, document.Source);
        Assert.Throws<ArgumentOutOfRangeException>(() => _parser.Parse(exact + "界"));
    }

    private static IEnumerable<SafeMarkdownInline> AllInlines(IEnumerable<SafeMarkdownBlock> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var inline in block.Inlines)
                yield return inline;
            if (block.Rows is not null)
            {
                foreach (var inline in block.Rows.SelectMany(row => row).SelectMany(cell => cell.Inlines))
                    yield return inline;
            }
            foreach (var inline in AllInlines(block.Children))
                yield return inline;
        }
    }
}
