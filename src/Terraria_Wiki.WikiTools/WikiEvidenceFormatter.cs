using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Terraria_Wiki.WikiTools;

internal sealed record WikiEvidencePage(
    IReadOnlyList<WikiEvidenceBlock> Blocks,
    int IncludedCharacters,
    bool IsComplete,
    int? NextCursor,
    IReadOnlyList<string> AvailableSections,
    WikiEvidenceScope? Scope = null)
{
    public static WikiEvidencePage Empty { get; } = new([], 0, true, null, []);
}

internal static partial class WikiEvidenceFormatter
{
    internal const int MaxBlockCharacters = 16_384;
    internal const int MaxSectionCharacters = 4_096;
    internal const int MaxSections = 64;
    private const int PreferredChunkCharacters = 4_096;

    private static readonly HashSet<string> RemovedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "canvas", "iframe", "object",
        "embed", "video", "audio", "picture", "img", "source", "form", "button", "nav",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "li", "dt", "dd", "pre", "table", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    public static WikiEvidencePage Format(
        string title,
        string html,
        string? anchor,
        string? focus,
        int? cursor,
        string? mode = null,
        string? continuationToken = null,
        string? requestedTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(html);
        if (cursor < 0)
            throw new ArgumentOutOfRangeException(nameof(cursor));
        if (mode is not (null or "page" or "section")
            || (mode == "section" && string.IsNullOrWhiteSpace(anchor))
            || (mode is null && continuationToken is not null))
            throw new ArgumentException("Invalid evidence range.");

        var contentVersion = mode is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)));
        var binding = mode is null ? null : Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new[] { requestedTitle ?? title, title, contentVersion, anchor, focus, mode })));
        if (mode is not null && ((cursor > 0 && continuationToken is null)
            || (continuationToken is not null && continuationToken != binding)))
            throw new ArgumentException("Invalid evidence continuation.");

        var document = new HtmlDocument();
        document.LoadHtml(html);
        RemoveUnsafeAndNonContentNodes(document.DocumentNode);

        var sections = new List<Section>();
        var root = new Section(title, title, null, true, 0);
        sections.Add(root);
        var sectionStack = new List<Section> { root };
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in document.DocumentNode.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Element || !BlockTags.Contains(node.Name))
                continue;
            if (HasSelectedAncestor(node))
                continue;

            if (TryHeadingLevel(node.Name, out var level))
            {
                var text = ExtractText(node);
                if (text.Length == 0)
                    continue;
                while (sectionStack.Count > 1 && sectionStack[^1].Level >= level)
                    sectionStack.RemoveAt(sectionStack.Count - 1);
                var parent = sectionStack[^1];
                var path = parent.IsRoot ? $"{title} > {text}" : $"{parent.Path} > {text}";
                var section = new Section(text, path, NodeAnchor(node), false, level, parent);
                sections.Add(section);
                sectionStack.Add(section);
                AddBlock(section, "heading", text, seen, deduplicate: false);
                continue;
            }

            var current = sectionStack[^1];
            if (node.Name.Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var row in ExtractTableRows(node))
                    AddBlock(current, "table_row", row, seen, deduplicate: true);
                continue;
            }

            var kind = node.Name.ToLowerInvariant() switch
            {
                "li" => "list_item",
                "dt" or "dd" => "definition",
                "pre" => "code",
                _ => "paragraph",
            };
            AddBlock(current, kind, ExtractText(node), seen, deduplicate: true);
        }

        root.Blocks.Insert(0, new WikiEvidenceBlock(title, "heading", title));
        var availableSections = BuildSectionCatalog(sections.Skip(1));
        var match = sections.Skip(1).FirstOrDefault(section =>
            string.Equals(section.Anchor, anchor, StringComparison.Ordinal)
            || (section.Anchor is null && string.Equals(section.Name, anchor, StringComparison.Ordinal)));
        IEnumerable<Section> chosen = OrderSections(sections, anchor, focus);
        if (mode == "section")
        {
            var ancestors = new HashSet<Section>();
            for (var parent = match?.Parent; parent is not null; parent = parent.Parent)
                ancestors.Add(parent);
            chosen = match is null ? [] : sections.Where(section => ancestors.Contains(section)
                || IsWithin(section, match));
        }
        var ordered = chosen
            .SelectMany(section => section.Blocks)
            .SelectMany(SplitBlock)
            .ToArray();
        var start = cursor ?? 0;
        if (start > ordered.Length || (start > 0 && start == ordered.Length))
            throw new ArgumentOutOfRangeException(nameof(cursor));

        var page = new List<WikiEvidenceBlock>();
        var characters = 0;
        var next = start;
        while (next < ordered.Length)
        {
            var block = ordered[next];
            var blockCharacters = CharacterCount(block.Text);
            if (characters + blockCharacters > MaxBlockCharacters)
                break;
            page.Add(block);
            characters += blockCharacters;
            next++;
        }

        var complete = next >= ordered.Length;
        WikiEvidenceScope? scope = mode is null ? null : new(
            mode,
            mode == "section" ? match?.Path : null,
            string.IsNullOrWhiteSpace(anchor) || match is not null,
            mode == "page" && complete,
            contentVersion!,
            complete ? null : binding,
            BuildAnchorCatalog(sections.Skip(1)));
        return new WikiEvidencePage(page, characters, complete, complete ? null : next, availableSections, scope);
    }

    private static bool IsWithin(Section section, Section target)
    {
        for (Section? current = section; current is not null; current = current.Parent)
            if (ReferenceEquals(current, target))
                return true;
        return false;
    }

    private static IReadOnlyList<WikiEvidenceSection> BuildAnchorCatalog(IEnumerable<Section> sections)
    {
        var result = new List<WikiEvidenceSection>();
        var characters = 0;
        foreach (var section in sections)
        {
            var anchor = section.Anchor ?? section.Name;
            var size = CharacterCount(section.Path) + CharacterCount(anchor);
            if (result.Count >= MaxSections || characters + size > MaxSectionCharacters)
                break;
            result.Add(new(section.Path, anchor));
            characters += size;
        }
        return result;
    }

    private static IEnumerable<Section> OrderSections(
        IReadOnlyList<Section> sections,
        string? anchor,
        string? focus)
    {
        var selected = new HashSet<Section>();
        if (!string.IsNullOrWhiteSpace(anchor))
        {
            var normalizedAnchor = NormalizeKey(anchor);
            foreach (var match in sections.Where(section => section.MatchesAnchor(normalizedAnchor)))
            {
                foreach (var section in sections.Where(section => ReferenceEquals(section, match)
                             || section.Path.StartsWith(match.Path + " > ", StringComparison.Ordinal)))
                {
                    if (selected.Add(section))
                        yield return section;
                }
            }
        }

        if (selected.Add(sections[0]))
            yield return sections[0];

        var focusTerms = BuildTerms(focus);
        if (focusTerms.Count > 0)
        {
            foreach (var section in sections
                         .Select((value, index) => (value, index, score: Score(value, focusTerms)))
                         .Where(item => item.score > 0)
                         .OrderByDescending(item => item.score)
                         .ThenBy(item => item.index)
                         .Select(item => item.value))
            {
                if (selected.Add(section))
                    yield return section;
            }
        }

        foreach (var section in sections)
            if (selected.Add(section))
                yield return section;
    }

    private static int Score(Section section, IReadOnlySet<string> terms)
    {
        var haystack = NormalizeKey(section.Path + " " + string.Join(' ', section.Blocks.Select(block => block.Text)));
        return terms.Sum(term => haystack.Contains(term, StringComparison.Ordinal) ? Math.Max(1, term.Length) : 0);
    }

    private static HashSet<string> BuildTerms(string? focus)
    {
        var normalized = NormalizeKey(focus);
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in WordRegex().Matches(normalized))
        {
            var value = match.Value;
            if (value.Length > 1)
                terms.Add(value);
        }
        var chinese = new string(normalized.Where(IsCjk).ToArray());
        for (var index = 0; index + 1 < chinese.Length; index++)
            terms.Add(chinese.Substring(index, 2));
        return terms;
    }

    private static IReadOnlyList<string> BuildSectionCatalog(IEnumerable<Section> sections)
    {
        var result = new List<string>();
        var characters = 0;
        foreach (var section in sections)
        {
            var sectionCharacters = CharacterCount(section.Path);
            if (result.Count >= MaxSections || characters + sectionCharacters > MaxSectionCharacters)
                break;
            result.Add(section.Path);
            characters += sectionCharacters;
        }
        return result;
    }

    private static IEnumerable<WikiEvidenceBlock> SplitBlock(WikiEvidenceBlock block)
    {
        var remaining = block.Text;
        while (CharacterCount(remaining) > PreferredChunkCharacters)
        {
            var split = FindNaturalSplit(remaining, PreferredChunkCharacters);
            yield return block with { Text = remaining[..split].TrimEnd() };
            remaining = remaining[split..].TrimStart();
        }
        if (remaining.Length > 0)
            yield return block with { Text = remaining };
    }

    private static int FindNaturalSplit(string value, int limit)
    {
        var lower = Math.Max(1, limit / 2);
        var runeCount = 0;
        var utf16Index = 0;
        var naturalSplit = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            runeCount++;
            utf16Index += rune.Utf16SequenceLength;
            if (runeCount >= lower && IsNaturalBoundary(rune))
                naturalSplit = utf16Index;
            if (runeCount >= limit)
                break;
        }
        return naturalSplit > 0 ? naturalSplit : utf16Index;
    }

    private static bool IsNaturalBoundary(Rune rune) => Rune.IsWhiteSpace(rune)
        || rune.Value is '。' or '；' or '，' or '.' or ';' or ',';

    internal static int CharacterCount(string value) => value.EnumerateRunes().Count();

    private static IEnumerable<string> ExtractTableRows(HtmlNode table)
    {
        var rows = table.SelectNodes(".//tr")?.Where(row => ReferenceEquals(row.Ancestors("table").First(), table))
                   ?? Enumerable.Empty<HtmlNode>();
        var grid = new Dictionary<(int Row, int Column), TableCell>();
        var headers = new Dictionary<int, string>();
        var output = new List<string>();
        var rowIndex = 0;
        var previousWasHeader = false;
        foreach (var row in rows)
        {
            var cells = row.ChildNodes.Where(node => node.Name is "th" or "td").ToArray();
            if (cells.Length == 0)
                continue;
            var values = new List<(int Column, string Value, bool Header)>();
            var column = 0;
            foreach (var cell in cells)
            {
                while (grid.TryGetValue((rowIndex, column), out var spanned))
                {
                    values.Add((column, spanned.Value, spanned.IsHeader));
                    column++;
                }
                var value = ExtractText(cell);
                var colspan = PositiveSpan(cell, "colspan");
                var rowspan = PositiveSpan(cell, "rowspan");
                for (var offset = 0; offset < colspan; offset++)
                {
                    values.Add((column + offset, value, cell.Name == "th"));
                    for (var future = 1; future < rowspan; future++)
                        grid[(rowIndex + future, column + offset)] = new TableCell(value, cell.Name == "th");
                }
                column += colspan;
            }
            while (grid.TryGetValue((rowIndex, column), out var trailing))
            {
                values.Add((column, trailing.Value, trailing.IsHeader));
                column++;
            }

            if (values.All(value => value.Header))
            {
                foreach (var value in values.Where(value => value.Value.Length > 0))
                    headers[value.Column] = previousWasHeader && headers.TryGetValue(value.Column, out var parent)
                        && parent != value.Value ? $"{parent} / {value.Value}" : value.Value;
                previousWasHeader = true;
                var labels = values.Select(value => value.Value).Where(value => value.Length > 0).Distinct().ToArray();
                if (labels.Length > 0)
                    output.Add("表头: " + string.Join(" | ", labels));
            }
            else
            {
                previousWasHeader = false;
                var rowHeaders = values.Where(value => value.Header && value.Value.Length > 0).ToArray();
                var data = values.Where(value => !value.Header && value.Value.Length > 0).ToArray();
                var useRowHeader = rowHeaders.Length > 0
                                   && data.Length > 0
                                   && (headers.Count == 0 || headers.Values.Distinct().Count() == 1);
                var formatted = useRowHeader
                    ? data.Select(value => $"{string.Join(" / ", rowHeaders.Select(header => header.Value))}: {value.Value}")
                        .ToArray()
                    : values.Where(value => value.Value.Length > 0)
                        .Select(value => $"{headers.GetValueOrDefault(value.Column, $"列{value.Column + 1}")}: {value.Value}")
                        .ToArray();
                if (formatted.Length > 0)
                    output.Add(string.Join(" | ", formatted));
            }
            rowIndex++;
        }
        return output;
    }

    private static int PositiveSpan(HtmlNode node, string name) =>
        int.TryParse(node.GetAttributeValue(name, "1"), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value is >= 1 and <= 100 ? value : 1;

    private static void AddBlock(
        Section section,
        string kind,
        string text,
        HashSet<string> seen,
        bool deduplicate)
    {
        if (text.Length == 0)
            return;
        var key = $"{section.Path}|{kind}|{text}";
        if (deduplicate && (key.Length == 0 || !seen.Add(key)))
            return;
        section.Blocks.Add(new WikiEvidenceBlock(section.Path, kind, text));
    }

    private static bool HasSelectedAncestor(HtmlNode node)
    {
        for (var parent = node.ParentNode; parent is not null; parent = parent.ParentNode)
        {
            if (!BlockTags.Contains(parent.Name))
                continue;
            if (parent.Name.Equals("table", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("li", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("dt", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("dd", StringComparison.OrdinalIgnoreCase)
                || parent.Name.Equals("pre", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void RemoveUnsafeAndNonContentNodes(HtmlNode root)
    {
        // Keep icon semantics only inside content; hidden ancestors are still removed below.
        foreach (var icon in root.Descendants("img").ToArray())
        {
            var alt = icon.GetAttributeValue("alt", "");
            if (!ShouldRemove(icon, preserveImage: true) && !string.IsNullOrWhiteSpace(alt)
                && icon.Ancestors().Any(node => BlockTags.Contains(node.Name)))
                icon.ParentNode.ReplaceChild(root.OwnerDocument.CreateTextNode(WebUtility.HtmlEncode(alt)), icon);
        }
        foreach (var node in root.DescendantsAndSelf().Where(node => ShouldRemove(node)).ToArray())
            node.Remove();
    }

    private static bool ShouldRemove(HtmlNode node, bool preserveImage = false)
    {
        if (node.NodeType == HtmlNodeType.Comment)
            return true;
        if (node.NodeType != HtmlNodeType.Element)
            return false;
        if ((RemovedTags.Contains(node.Name) && !(preserveImage && node.Name == "img"))
            || node.Attributes.Contains("hidden")
            || node.GetAttributeValue("aria-hidden", "false").Equals("true", StringComparison.OrdinalIgnoreCase)
            || node.GetAttributeValue("role", "").Equals("navigation", StringComparison.OrdinalIgnoreCase))
            return true;
        var style = node.GetAttributeValue("style", "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        if (style.Contains("display:none", StringComparison.Ordinal)
            || style.Contains("visibility:hidden", StringComparison.Ordinal))
            return true;
        var classes = node.GetClasses().Select(value => value.ToLowerInvariant()).ToHashSet();
        return classes.Overlaps(["navbox", "navbar", "toc", "mw-editsection", "noprint", "navigation-not-searchable"]);
    }

    private static string ExtractText(HtmlNode node)
    {
        var builder = new StringBuilder();
        AppendText(node, builder);
        return NormalizeWhitespace(WebUtility.HtmlDecode(builder.ToString()));
    }

    private static void AppendText(HtmlNode node, StringBuilder builder)
    {
        if (node.Name == "table")
        {
            builder.Append(string.Join(" ; ", ExtractTableRows(node)));
            return;
        }
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(node.InnerText);
            return;
        }
        foreach (var child in node.ChildNodes)
        {
            AppendText(child, builder);
            if (child.NodeType == HtmlNodeType.Element && child.Name is "br" or "p" or "div" or "li")
                builder.Append(' ');
        }
    }

    private static string NormalizeWhitespace(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
            builder.Append(char.IsLetterOrDigit(character) || IsCjk(character) ? character : ' ');
        return NormalizeWhitespace(builder.ToString());
    }

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9fff';

    private static string? NodeAnchor(HtmlNode node)
    {
        var id = node.GetAttributeValue("id", string.Empty);
        if (!string.IsNullOrWhiteSpace(id))
            return id;
        var named = node.DescendantsAndSelf()
            .Select(candidate => candidate.GetAttributeValue("id", string.Empty) is { Length: > 0 } candidateId
                ? candidateId
                : candidate.GetAttributeValue("name", string.Empty))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return named;
    }

    private static bool TryHeadingLevel(string name, out int level)
    {
        level = 0;
        return name.Length == 2
               && name[0] is 'h' or 'H'
               && int.TryParse(name[1..], out level)
               && level is >= 1 and <= 6;
    }

    private sealed class Section(
        string name,
        string path,
        string? anchor,
        bool isRoot,
        int level,
        Section? parent = null)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public string? Anchor { get; } = anchor;
        public bool IsRoot { get; } = isRoot;
        public int Level { get; } = level;
        public Section? Parent { get; } = parent;
        public List<WikiEvidenceBlock> Blocks { get; } = [];

        public bool MatchesAnchor(string normalizedAnchor) =>
            NormalizeKey(Anchor) == normalizedAnchor || NormalizeKey(Name) == normalizedAnchor;
    }

    private sealed record TableCell(string Value, bool IsHeader);

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
