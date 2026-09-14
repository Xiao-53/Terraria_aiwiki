using System.Text.Json.Serialization;

namespace Terraria_Wiki.Models;

public class PageInfo
{
    public string Title { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class RawResponse
{
    [JsonPropertyName("continue")]
    public RawContinue? Continue { get; set; }

    [JsonPropertyName("query")]
    public RawQuery? Query { get; set; }
}

public class RawContinue
{
    [JsonPropertyName("gapcontinue")]
    public string? GapContinue { get; set; }
}

public class RawQuery
{
    [JsonPropertyName("pages")]
    public Dictionary<string, RawPage> Pages { get; set; } = [];
}

public class RawPage
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("touched")]
    public string Touched { get; set; } = string.Empty;

    [JsonPropertyName("fullurl")]
    public string FullUrl { get; set; } = string.Empty;
}

public class WikiPageSummary
{
    public string Title { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}

public class SearchResultItem
{
    public string Title { get; set; } = string.Empty;
    public string RedirectTo { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public WikiSearchMatchKind MatchKind { get; set; }
    public IReadOnlyList<WikiSearchHighlight> Highlights { get; set; } = [];
    public long RegionId { get; set; }
    public int RegionOrdinal { get; set; }
    public string CanonicalTitle { get; set; } = string.Empty;
    public string SectionPath { get; set; } = string.Empty;
    public string Anchor { get; set; } = string.Empty;
    public string EvidenceText { get; set; } = string.Empty;
    public IReadOnlyList<WikiSearchEvidenceBlock> EvidenceRegions { get; set; } = [];
    public WikiSearchEvidenceKind EvidenceKind { get; set; }
    public WikiSearchScope Scope { get; set; }
}

/// <summary>
/// Stable Search match tiers in ascending result order.
/// </summary>
public enum WikiSearchMatchKind
{
    ExactTitle = 0,
    ExactRedirect = 1,
    TitlePrefix = 2,
    RedirectPrefix = 3,
    TitleContains = 4,
    RedirectContains = 5,
    BodyFts = 6,
}

public enum WikiSearchEvidenceKind
{
    Body,
    Heading,
    TableRow,
    ListItem,
    Definition,
    Infobox,
    Generic,
    ExactTitle,
    ExactRedirect,
    ExactCommunityAlias,
}

public enum WikiSearchScope
{
    SameRegion,
    SameSection,
    SamePage,
    Partial,
}

public sealed record WikiSearchEvidenceBlock(
    long RegionId,
    int RegionOrdinal,
    string SectionPath,
    string Anchor,
    WikiSearchEvidenceKind Kind,
    string EvidenceText);

/// <summary>
/// A highlighted range in <see cref="SearchResultItem.Snippet"/>. Start and
/// Length use zero-based .NET UTF-16 string indexes.
/// </summary>
public readonly record struct WikiSearchHighlight(int Start, int Length);

public class RawSearchResult
{
    public string Title { get; set; } = string.Empty;
    public string PlainContent { get; set; } = string.Empty;
}

public class AssetMetadata
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime? LastModified { get; set; }
}
