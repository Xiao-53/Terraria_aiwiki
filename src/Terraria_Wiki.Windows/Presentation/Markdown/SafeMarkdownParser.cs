using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Terraria_Wiki.Windows.AI;

namespace Terraria_Wiki.Windows.Presentation.Markdown;

internal sealed class SafeMarkdownParser
{
    public const int MaxMarkdownLength = 32 * 1024;

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseCjkFriendlyEmphasis()
        .UsePipeTables()
        .Build();

    public SafeMarkdownDocument Parse(string? markdown, int maximumCharacters = MaxMarkdownLength)
    {
        markdown ??= string.Empty;
        if (AiTextLimits.Count(markdown) > maximumCharacters)
            throw new ArgumentOutOfRangeException(nameof(markdown), "Markdown exceeds the 32 KiB answer boundary.");
        if (markdown.Length == 0)
            return SafeMarkdownDocument.Empty;

        var syntax = Markdig.Markdown.Parse(markdown, _pipeline);
        var blocks = new List<SafeMarkdownBlock>();
        foreach (var block in syntax)
        {
            var converted = ConvertBlock(block);
            if (converted is not null)
                blocks.Add(converted);
        }
        return new SafeMarkdownDocument(markdown, blocks);
    }

    private static SafeMarkdownBlock? ConvertBlock(Block block) => block switch
    {
        HeadingBlock heading => SafeMarkdownBlock.Text(
            SafeMarkdownBlockKind.Heading,
            ConvertInlines(heading.Inline),
            Math.Clamp(heading.Level, 1, 6)),
        ParagraphBlock paragraph => SafeMarkdownBlock.Text(
            SafeMarkdownBlockKind.Paragraph,
            ConvertInlines(paragraph.Inline)),
        QuoteBlock quote => new SafeMarkdownBlock(
            SafeMarkdownBlockKind.Quote,
            [],
            ConvertChildren(quote)),
        ListBlock list => new SafeMarkdownBlock(
            SafeMarkdownBlockKind.List,
            [],
            ConvertChildren(list),
            IsOrdered: list.IsOrdered),
        ListItemBlock item => new SafeMarkdownBlock(
            SafeMarkdownBlockKind.ListItem,
            [],
            ConvertChildren(item)),
        FencedCodeBlock code => new SafeMarkdownBlock(
            SafeMarkdownBlockKind.Code,
            [new SafeMarkdownInline(code.Lines.ToString(), IsCode: true)],
            [],
            CodeLanguage: code.Info?.ToString()),
        CodeBlock code => new SafeMarkdownBlock(
            SafeMarkdownBlockKind.Code,
            [new SafeMarkdownInline(code.Lines.ToString(), IsCode: true)],
            []),
        Table table => ConvertTable(table),
        ThematicBreakBlock => new SafeMarkdownBlock(SafeMarkdownBlockKind.ThematicBreak, [], []),
        LeafBlock leaf => SafeMarkdownBlock.Text(
            SafeMarkdownBlockKind.Paragraph,
            [new SafeMarkdownInline(leaf.Lines.ToString())]),
        ContainerBlock container => new SafeMarkdownBlock(
            SafeMarkdownBlockKind.Quote,
            [],
            ConvertChildren(container)),
        _ => null,
    };

    private static IReadOnlyList<SafeMarkdownBlock> ConvertChildren(ContainerBlock container)
    {
        var result = new List<SafeMarkdownBlock>();
        foreach (var child in container)
        {
            var converted = ConvertBlock(child);
            if (converted is not null)
                result.Add(converted);
        }
        return result;
    }

    private static SafeMarkdownBlock ConvertTable(Table table)
    {
        var rows = new List<IReadOnlyList<SafeMarkdownTableCell>>();
        foreach (var rowObject in table)
        {
            if (rowObject is not TableRow row)
                continue;
            var cells = new List<SafeMarkdownTableCell>();
            foreach (var cellObject in row)
            {
                if (cellObject is not TableCell cell)
                    continue;
                var inlines = new List<SafeMarkdownInline>();
                foreach (var child in cell)
                {
                    if (child is ParagraphBlock paragraph)
                        inlines.AddRange(ConvertInlines(paragraph.Inline));
                    else if (child is LeafBlock leaf)
                        inlines.Add(new SafeMarkdownInline(leaf.Lines.ToString()));
                }
                cells.Add(new SafeMarkdownTableCell(inlines, row.IsHeader));
            }
            rows.Add(cells);
        }
        return new SafeMarkdownBlock(SafeMarkdownBlockKind.Table, [], [], Rows: rows);
    }

    private static IReadOnlyList<SafeMarkdownInline> ConvertInlines(ContainerInline? container)
    {
        var result = new List<SafeMarkdownInline>();
        if (container is not null)
            AppendInlines(container.FirstChild, result, false, false);
        return result;
    }

    private static void AppendInlines(
        Inline? current,
        List<SafeMarkdownInline> output,
        bool isBold,
        bool isItalic)
    {
        while (current is not null)
        {
            switch (current)
            {
                case LiteralInline literal:
                    Add(output, literal.Content.ToString(), isBold, isItalic);
                    break;
                case CodeInline code:
                    Add(output, code.Content, isBold, isItalic, isCode: true);
                    break;
                case LineBreakInline:
                    Add(output, "\n", isBold, isItalic);
                    break;
                case EmphasisInline emphasis:
                    AppendInlines(
                        emphasis.FirstChild,
                        output,
                        isBold || emphasis.DelimiterCount >= 2,
                        isItalic || emphasis.DelimiterCount == 1);
                    break;
                case LinkInline link:
                {
                    var before = output.Count;
                    AppendInlines(link.FirstChild, output, isBold, isItalic);
                    if (output.Count == before)
                    {
                        var text = link.Url ?? string.Empty;
                        if (text.Length > 0)
                            Add(output, text, isBold, isItalic);
                    }
                    break;
                }
                case HtmlInline html:
                    Add(output, html.Tag, isBold, isItalic);
                    break;
                case ContainerInline nested:
                    AppendInlines(nested.FirstChild, output, isBold, isItalic);
                    break;
            }
            current = current.NextSibling;
        }
    }

    private static void Add(
        List<SafeMarkdownInline> output,
        string? text,
        bool bold,
        bool italic,
        bool isCode = false)
    {
        if (string.IsNullOrEmpty(text))
            return;
        output.Add(new SafeMarkdownInline(text, bold, italic, isCode));
    }
}
