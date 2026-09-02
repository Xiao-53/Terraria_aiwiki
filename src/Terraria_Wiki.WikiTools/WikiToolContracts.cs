using System.Text.Json.Serialization;

namespace Terraria_Wiki.WikiTools;

internal sealed record WikiSearchToolResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<WikiSearchToolResult> Results);

internal sealed record WikiSearchToolResult(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("redirect_target")] string RedirectTarget,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("match_type")] string MatchType,
    [property: JsonPropertyName("highlights")] IReadOnlyList<WikiSearchToolHighlight> Highlights);

internal readonly record struct WikiSearchToolHighlight(
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("length")] int Length);

internal sealed record WikiReadToolResponse(
    [property: JsonPropertyName("requested_title")] string RequestedTitle,
    [property: JsonPropertyName("requested_page_title")] string RequestedPageTitle,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_found")] bool IsFound,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("last_modified")] DateTime? LastModified,
    [property: JsonPropertyName("is_redirect")] bool IsRedirect,
    [property: JsonPropertyName("redirect_from_title")] string? RedirectFromTitle,
    [property: JsonPropertyName("redirect_target_title")] string? RedirectTargetTitle,
    [property: JsonPropertyName("redirect_target_anchor")] string? RedirectTargetAnchor,
    [property: JsonPropertyName("anchor")] string? Anchor);
