using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Core.Data;

public sealed record ProcessedWikiContent(string Html, string SearchText, IReadOnlyList<string> ResourceUrls);

public static partial class WikiContentProcessor
{
    public static ProcessedWikiContent Process(string html, WikiBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);
        var contentNode = document.DocumentNode;

        CleanJunkElements(contentNode, book.JunkXPath);
        ProcessAnchorLinks(contentNode);
        ProcessAudioTags(contentNode);
        var resources = ProcessImages(contentNode, book);

        // Preserve the legacy mutation order: the search-text extraction removes
        // nodes and must happen only after the stored HTML has been captured.
        var storedHtml = contentNode.OuterHtml;
        var searchText = ExtractSearchableText(contentNode);
        return new ProcessedWikiContent(storedHtml, searchText, resources);
    }

    public static string GetFileNameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var cleanUrl = CleanUrl(url);
        var slash = cleanUrl.LastIndexOf('/');
        return WebUtility.UrlDecode(slash >= 0 ? cleanUrl[(slash + 1)..] : cleanUrl);
    }

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
            var hashIndex = href.IndexOf('#');
            if (hashIndex >= 0)
                anchor.SetAttributeValue("title", anchor.GetAttributeValue("title", string.Empty) + href[hashIndex..]);
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

    private static string ExtractSearchableText(HtmlNode contentNode)
    {
        var excluded = contentNode.SelectNodes("//div[contains(concat(' ', @class, ' '), ' message-box ') or contains(concat(' ', @class, ' '), ' infobox ') or contains(concat(' ', @role, ' '), ' navigation ')]");
        if (excluded is not null)
            foreach (var node in excluded) node.Remove();

        var nodes = contentNode.SelectNodes("//p | //h1 | //h2 | //h3 | //h4 | //h5 | //h6 | //li");
        var text = nodes is null ? string.Empty : string.Join(" ", nodes.Select(node => node.InnerText));
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string CleanUrl(string url)
    {
        var queryIndex = url.IndexOf('?');
        return queryIndex > 0 ? url[..queryIndex] : url;
    }

    [GeneratedRegex(@"/thumb/(.+)/[^/]+$")]
    private static partial Regex ThumbnailUrlRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
