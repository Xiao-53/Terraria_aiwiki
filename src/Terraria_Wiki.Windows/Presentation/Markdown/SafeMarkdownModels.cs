namespace Terraria_Wiki.Windows.Presentation.Markdown;

internal enum SafeMarkdownBlockKind
{
    Paragraph,
    Heading,
    Quote,
    List,
    ListItem,
    Code,
    Table,
    ThematicBreak,
}

internal sealed record SafeMarkdownInline(
    string Text,
    bool IsBold = false,
    bool IsItalic = false,
    bool IsCode = false);

internal sealed record SafeMarkdownTableCell(
    IReadOnlyList<SafeMarkdownInline> Inlines,
    bool IsHeader);

internal sealed record SafeMarkdownBlock(
    SafeMarkdownBlockKind Kind,
    IReadOnlyList<SafeMarkdownInline> Inlines,
    IReadOnlyList<SafeMarkdownBlock> Children,
    int Level = 0,
    bool IsOrdered = false,
    IReadOnlyList<IReadOnlyList<SafeMarkdownTableCell>>? Rows = null,
    string? CodeLanguage = null)
{
    public static SafeMarkdownBlock Text(
        SafeMarkdownBlockKind kind,
        IReadOnlyList<SafeMarkdownInline> inlines,
        int level = 0) => new(kind, inlines, [], level);
}

internal sealed record SafeMarkdownDocument(
    string Source,
    IReadOnlyList<SafeMarkdownBlock> Blocks)
{
    public static readonly SafeMarkdownDocument Empty = new(string.Empty, []);
}
