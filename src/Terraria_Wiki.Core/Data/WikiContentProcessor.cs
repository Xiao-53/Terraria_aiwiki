using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Data;

public sealed record WikiSearchRegionDraft(
    int SectionOrdinal,
    int RegionOrdinal,
    string Kind,
    string HeadingPath,
    string Anchor,
    string ContextText,
    string BodyText);

public sealed record WikiSearchSectionDraft(
    int SectionOrdinal,
    string Title,
    string HeadingPath,
    string Anchor);

public sealed record ProcessedWikiContent(
    string Html,
    string SearchText,
    IReadOnlyList<WikiSearchSectionDraft> SearchSections,
    IReadOnlyList<WikiSearchRegionDraft> SearchRegions,
    IReadOnlyList<string> ResourceUrls);

public static partial class WikiContentProcessor
{
    private static readonly HashSet<string> IgnoredTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "canvas", "iframe", "object",
        "embed", "video", "audio", "picture", "img", "source", "form", "button", "nav",
    };

    public static ProcessedWikiContent Process(string html, WikiBook book, string? pageTitle = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        var contentNode = document.DocumentNode;

        CleanJunkElements(contentNode, book.JunkXPath);
        ProcessAnchorLinks(contentNode);
        ProcessAudioTags(contentNode);
        var resources = ProcessImages(contentNode, book);

        var storedHtml = contentNode.OuterHtml;
        var (sections, regions) = ExtractSearchRegions(contentNode, NormalizeText(pageTitle));
        var searchText = NormalizeText(string.Join(' ', regions.Select(region =>
            JoinDistinct(region.ContextText, region.BodyText))));
        return new ProcessedWikiContent(storedHtml, searchText, sections, regions, resources);
    }

    public static string GetFileNameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var cleanUrl = CleanUrl(url);
        var slash = cleanUrl.LastIndexOf('/');
        return WebUtility.UrlDecode(slash >= 0 ? cleanUrl[(slash + 1)..] : cleanUrl);
    }

    private static (IReadOnlyList<WikiSearchSectionDraft> Sections, IReadOnlyList<WikiSearchRegionDraft> Regions)
        ExtractSearchRegions(HtmlNode root, string pageTitle)
    {
        var sections = new List<WikiSearchSectionDraft>
        {
            new(0, pageTitle, pageTitle, string.Empty),
        };
        var regions = new List<WikiSearchRegionDraft>();
        var headings = new List<(int Level, string Text)>();
        var sectionOrdinal = 0;
        var regionOrdinal = 0;
        var sectionAnchor = string.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in root.Descendants())
        {
            if (node.NodeType != HtmlNodeType.Element || IsIgnored(node)) continue;

            if (TryHeadingLevel(node.Name, out var level))
            {
                var heading = ExtractText(node);
                if (heading.Length == 0) continue;
                while (headings.Count > 0 && headings[^1].Level >= level) headings.RemoveAt(headings.Count - 1);
                headings.Add((level, heading));
                sectionOrdinal++;
                var headingPath = BuildHeadingPath(pageTitle, headings);
                var anchor = FindAnchor(node);
                sectionAnchor = anchor;
                sections.Add(new WikiSearchSectionDraft(sectionOrdinal, heading, headingPath, anchor));
                AddRegion(
                    regions, seen, sectionOrdinal, ref regionOrdinal, "heading",
                    headingPath, anchor, pageTitle, heading);
                continue;
            }

            if (node.Name.Equals("table", StringComparison.OrdinalIgnoreCase)
                && !node.Ancestors("table").Any())
            {
                var headingPath = BuildHeadingPath(pageTitle, headings);
                var tableKind = IsInsideInfobox(node) ? "infobox" : "table_row";
                var tableAnchor = FindNearestAnchor(node);
                if (tableAnchor.Length == 0) tableAnchor = sectionAnchor;
                foreach (var row in ExtractTableRows(node, pageTitle, headingPath))
                    AddRegion(regions, seen, sectionOrdinal, ref regionOrdinal, tableKind,
                        headingPath, tableAnchor, row.Context, row.Body);
                continue;
            }

            if (node.Ancestors("table").Any()) continue;
            var isDedicatedBlock = node.Name is "p" or "li" or "dt" or "dd" or "pre";
            if (!isDedicatedBlock
                && (node.Ancestors().Any(ancestor => ancestor.Name is "p" or "li" or "dt" or "dd" or "pre")
                    || TryHeadingLevel(node.Name, out _)
                    || HasAnyClass(node, "title", "caption", "header")))
                continue;

            var body = isDedicatedBlock ? ExtractOwnBlockText(node) : ExtractDirectText(node);
            if (body.Length == 0) continue;
            var headingPathForBlock = BuildHeadingPath(pageTitle, headings);
            var structural = BuildStructuralContext(node);
            var context = JoinDistinct(pageTitle, headingPathForBlock, structural);
            var blockAnchor = FindNearestAnchor(node);
            if (blockAnchor.Length == 0) blockAnchor = sectionAnchor;
            var kind = node.Name switch
            {
                "li" => IsInsideInfobox(node) ? "infobox" : "list_item",
                "dt" or "dd" => IsInsideInfobox(node) ? "infobox" : "definition",
                "pre" => "preformatted",
                _ when !isDedicatedBlock => "generic",
                _ => IsInsideInfobox(node) ? "infobox" : "paragraph",
            };
            AddRegion(regions, seen, sectionOrdinal, ref regionOrdinal, kind,
                headingPathForBlock, blockAnchor, context, body);
        }

        if (regions.Count == 0)
        {
            var fallback = ExtractText(root);
            if (fallback.Length > 0)
                AddRegion(regions, seen, 0, ref regionOrdinal, "content", pageTitle, string.Empty, pageTitle, fallback);
        }
        return (sections, regions);
    }

    private static IEnumerable<(string Context, string Body)> ExtractTableRows(
        HtmlNode table,
        string pageTitle,
        string headingPath)
    {
        var caption = ExtractText(table.SelectSingleNode("./caption"));
        var structural = BuildStructuralContext(table);
        var active = new Dictionary<int, SpanningCell>();
        var columnHeaders = new Dictionary<int, string>();
        var groupHeader = string.Empty;

        var rows = table.SelectNodes(".//tr")?
            .Where(row => ReferenceEquals(row.Ancestors("table").FirstOrDefault(), table))
            .ToArray() ?? [];
        foreach (var row in rows)
        {
            var inherited = active.ToDictionary(pair => pair.Key, pair => pair.Value);
            var logical = inherited.ToDictionary(
                pair => pair.Key,
                pair => new LogicalCell(pair.Value.Text, pair.Value.IsHeader));
            var newSpans = new HashSet<int>();
            var column = 0;
            var physical = row.SelectNodes("./th|./td")?.ToArray() ?? [];
            foreach (var cell in physical)
            {
                while (logical.ContainsKey(column)) column++;
                var text = ExtractText(cell);
                var colspan = PositiveSpan(cell.GetAttributeValue("colspan", "1"));
                var rowspan = PositiveSpan(cell.GetAttributeValue("rowspan", "1"));
                var isHeader = cell.Name.Equals("th", StringComparison.OrdinalIgnoreCase);
                for (var offset = 0; offset < colspan; offset++)
                {
                    var target = column + offset;
                    logical[target] = new LogicalCell(text, isHeader);
                    if (rowspan > 1)
                    {
                        active[target] = new SpanningCell(text, isHeader, rowspan - 1);
                        newSpans.Add(target);
                    }
                }
                column += colspan;
            }

            foreach (var pair in inherited)
            {
                if (newSpans.Contains(pair.Key)) continue;
                if (pair.Value.RemainingRows <= 1) active.Remove(pair.Key);
                else active[pair.Key] = pair.Value with { RemainingRows = pair.Value.RemainingRows - 1 };
            }

            var ordered = logical.OrderBy(pair => pair.Key).ToArray();
            var nonEmpty = ordered.Where(pair => pair.Value.Text.Length > 0).ToArray();
            if (nonEmpty.Length == 0) continue;
            if (physical.Length > 0 && physical.All(cell => cell.Name.Equals("th", StringComparison.OrdinalIgnoreCase)))
            {
                var headerText = NormalizeText(string.Join(" | ", nonEmpty.Select(pair => pair.Value.Text).Distinct()));
                var isGroupRow = columnHeaders.Count > 0
                    && (physical.Length == 1 || physical.Any(cell => PositiveSpan(cell.GetAttributeValue("colspan", "1")) > 1));
                if (isGroupRow)
                {
                    groupHeader = headerText;
                }
                else
                {
                    foreach (var pair in nonEmpty) columnHeaders[pair.Key] = pair.Value.Text;
                }
                continue;
            }

            var localHeaders = new List<string>();
            var values = new List<string>();
            foreach (var pair in nonEmpty)
            {
                if (pair.Value.IsHeader)
                {
                    localHeaders.Add(pair.Value.Text);
                    continue;
                }
                if (columnHeaders.TryGetValue(pair.Key, out var header)) localHeaders.Add(header);
                values.Add(pair.Value.Text);
            }
            if (values.Count == 0) values.AddRange(nonEmpty.Select(pair => pair.Value.Text));
            var context = JoinDistinct(pageTitle, headingPath, structural, caption, groupHeader,
                NormalizeText(string.Join(" | ", localHeaders.Distinct())));
            var body = NormalizeText(string.Join(" | ", values.Distinct()));
            if (body.Length > 0) yield return (context, body);
        }
    }

    private static void AddRegion(
        ICollection<WikiSearchRegionDraft> regions,
        ISet<string> seen,
        int sectionOrdinal,
        ref int regionOrdinal,
        string kind,
        string headingPath,
        string anchor,
        string context,
        string body)
    {
        context = NormalizeText(context);
        body = NormalizeText(body);
        if (body.Length == 0) return;
        var key = $"{sectionOrdinal}\u001f{kind}\u001f{context}\u001f{body}";
        if (!seen.Add(key)) return;
        regions.Add(new WikiSearchRegionDraft(
            sectionOrdinal,
            regionOrdinal++,
            kind,
            NormalizeText(headingPath),
            NormalizeText(anchor),
            context,
            body));
    }

    private static string BuildHeadingPath(string pageTitle, IEnumerable<(int Level, string Text)> headings) =>
        JoinDistinct(pageTitle, string.Join(" > ", headings.Select(item => item.Text)));

    private static string BuildStructuralContext(HtmlNode node)
    {
        var values = new List<string>();
        if (node.Name.Equals("dd", StringComparison.OrdinalIgnoreCase))
        {
            var term = node.PreviousSibling;
            while (term is not null && term.NodeType == HtmlNodeType.Text && string.IsNullOrWhiteSpace(term.InnerText))
                term = term.PreviousSibling;
            if (term is not null && term.Name.Equals("dt", StringComparison.OrdinalIgnoreCase))
            {
                var termText = ExtractText(term);
                if (termText.Length > 0) values.Add(termText);
            }
        }
        foreach (var ancestor in node.Ancestors().Reverse())
        {
            if (ancestor.Name.Equals("li", StringComparison.OrdinalIgnoreCase))
            {
                var parentItem = ExtractOwnBlockText(ancestor);
                if (parentItem.Length > 0) values.Add(parentItem);
            }
            var classes = ancestor.GetAttributeValue("class", string.Empty);
            if (!classes.Contains("infobox", StringComparison.OrdinalIgnoreCase)
                && !classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("section", StringComparer.OrdinalIgnoreCase))
                continue;
            var label = ancestor.ChildNodes.FirstOrDefault(child =>
                child.NodeType == HtmlNodeType.Element
                && HasAnyClass(child, "title", "caption", "header"));
            var text = ExtractText(label);
            if (text.Length > 0) values.Add(text);
        }

        var previousCaption = node.PreviousSibling;
        while (previousCaption is not null && previousCaption.NodeType == HtmlNodeType.Text
               && string.IsNullOrWhiteSpace(previousCaption.InnerText))
            previousCaption = previousCaption.PreviousSibling;
        if (previousCaption is not null && HasAnyClass(previousCaption, "caption", "groupstart", "header"))
        {
            var text = ExtractText(previousCaption);
            if (text.Length > 0) values.Add(text);
        }
        return NormalizeText(string.Join(" > ", values.Distinct()));
    }

    private static string ExtractOwnBlockText(HtmlNode node)
    {
        var text = new StringBuilder();
        AppendText(node, text, node, skipNestedBlocks: true);
        return NormalizeText(text.ToString());
    }

    private static string ExtractDirectText(HtmlNode node) => NormalizeText(string.Join(
        ' ',
        node.ChildNodes
            .Where(child => child.NodeType == HtmlNodeType.Text)
            .Select(child => child.InnerText)));

    private static string ExtractText(HtmlNode? node)
    {
        if (node is null) return string.Empty;
        var text = new StringBuilder();
        AppendText(node, text, node, skipNestedBlocks: false);
        return NormalizeText(text.ToString());
    }

    private static void AppendText(HtmlNode node, StringBuilder text, HtmlNode root, bool skipNestedBlocks)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            text.Append(' ').Append(node.InnerText);
            return;
        }
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Element && (IsIgnored(child)
                || (skipNestedBlocks && !ReferenceEquals(child, root)
                    && child.Name is "ul" or "ol" or "table" or "li" or "p" or "dt" or "dd")))
                continue;
            AppendText(child, text, root, skipNestedBlocks);
        }
    }

    private static bool IsIgnored(HtmlNode node)
    {
        if (IgnoredTags.Contains(node.Name)) return true;
        foreach (var current in node.AncestorsAndSelf())
        {
            if (IgnoredTags.Contains(current.Name)) return true;
            if (HasAnyClass(current, "navbox", "toc", "mw-editsection", "searchaux")) return true;
            if (string.Equals(current.GetAttributeValue("role", string.Empty), "navigation", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsInsideInfobox(HtmlNode node) => node.AncestorsAndSelf().Any(ancestor =>
        ancestor.GetAttributeValue("class", string.Empty).Contains("infobox", StringComparison.OrdinalIgnoreCase));

    private static bool HasAnyClass(HtmlNode node, params string[] expected)
    {
        var classes = node.GetAttributeValue("class", string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return expected.Any(value => classes.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    private static string FindNearestAnchor(HtmlNode node) =>
        node.AncestorsAndSelf()
            .Select(item => NormalizeText(item.GetAttributeValue("id", string.Empty)))
            .FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static string FindAnchor(HtmlNode node) =>
        NormalizeText(node.GetAttributeValue("id", string.Empty).Length > 0
            ? node.GetAttributeValue("id", string.Empty)
            : node.ChildNodes.Select(item => item.GetAttributeValue("id", string.Empty))
                .FirstOrDefault(value => value.Length > 0));

    private static bool TryHeadingLevel(string name, out int level)
    {
        level = name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]) ? name[1] - '0' : 0;
        return level is >= 1 and <= 6;
    }

    private static int PositiveSpan(string value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? Math.Min(parsed, 100) : 1;

    private static string JoinDistinct(params string?[] values) => NormalizeText(string.Join(" > ", values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(NormalizeText)
        .Distinct(StringComparer.Ordinal)));

    private static string NormalizeText(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : WhitespaceRegex().Replace(WebUtility.HtmlDecode(value), " ").Trim();

    private static void CleanJunkElements(HtmlNode node, string? junkXPath)
    {
        if (!string.IsNullOrEmpty(junkXPath))
            node.SelectNodes(junkXPath)?.ToList().ForEach(item => item.Remove());
    }

    private static void ProcessAnchorLinks(HtmlNode node)
    {
        node.SelectNodes("//a[@href and @title]")?.ToList().ForEach(anchor =>
        {
            var href = anchor.Attributes["href"].Value;
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("//", StringComparison.Ordinal))
                return;
            var hashIndex = href.IndexOf('#');
            if (hashIndex >= 0)
                anchor.SetAttributeValue("title", anchor.GetAttributeValue("title", string.Empty) + href[hashIndex..]);
            anchor.SetAttributeValue("data-wiki", anchor.GetAttributeValue("title", string.Empty));
            anchor.Attributes.Remove("href");
        });
    }

    private static void ProcessAudioTags(HtmlNode node)
    {
        node.SelectNodes("//audio")?.ToList().ForEach(audio =>
        {
            var sources = audio.SelectNodes("./source");
            if (sources is null || sources.Count <= 1) return;
            var keep = sources.FirstOrDefault(source => !source.GetAttributeValue("src", string.Empty).Contains("/transcoded/", StringComparison.Ordinal))
                       ?? sources.Last();
            foreach (var source in sources.ToArray())
                if (source != keep) source.Remove();
        });
    }

    private static IReadOnlyList<string> ProcessImages(HtmlNode node, WikiBook book)
    {
        var resources = new List<string>();
        node.SelectNodes("//a[@class='image' and @href]")?.ToList().ForEach(anchor => anchor.Attributes.Remove("href"));
        node.SelectNodes("//*[@src]")?.ToList().ForEach(element =>
        {
            foreach (var attribute in new[] { "loading", "data-file-width", "data-file-height", "srcset" })
                element.Attributes.Remove(attribute);

            var source = element.Attributes["src"].Value;
            if (!source.Contains("https://", StringComparison.Ordinal)) source = book.BaseUrl + source;
            source = ThumbnailUrlRegex().Replace(source, "/$1");
            source = CleanUrl(source);
            if (book.Id == 2) source = source.Replace("huiji-thumb", "huiji-public", StringComparison.Ordinal);

            resources.Add(source);
            var localName = Uri.EscapeDataString(GetFileNameFromUrl(source));
            element.SetAttributeValue("src", "/src/" + localName);
        });
        return resources;
    }

    private static string CleanUrl(string url)
    {
        var queryIndex = url.IndexOf('?');
        return queryIndex > 0 ? url[..queryIndex] : url;
    }

    private sealed record SpanningCell(string Text, bool IsHeader, int RemainingRows);
    private sealed record LogicalCell(string Text, bool IsHeader);

    [GeneratedRegex(@"/thumb/(.+)/[^/]+$")]
    private static partial Regex ThumbnailUrlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
