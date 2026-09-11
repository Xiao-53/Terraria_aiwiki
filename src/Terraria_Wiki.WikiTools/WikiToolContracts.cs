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
    [property: JsonPropertyName("content_format")] string? ContentFormat,
    [property: JsonPropertyName("blocks")] IReadOnlyList<WikiEvidenceBlock> Blocks,
    [property: JsonPropertyName("included_characters")] int IncludedCharacters,
    [property: JsonPropertyName("original_characters")] int OriginalCharacters,
    [property: JsonPropertyName("is_complete")] bool IsComplete,
    [property: JsonPropertyName("next_cursor")] int? NextCursor,
    [property: JsonPropertyName("available_sections")] IReadOnlyList<string> AvailableSections,
    [property: JsonPropertyName("last_modified")] DateTime? LastModified,
    [property: JsonPropertyName("is_redirect")] bool IsRedirect,
    [property: JsonPropertyName("redirect_from_title")] string? RedirectFromTitle,
    [property: JsonPropertyName("redirect_target_title")] string? RedirectTargetTitle,
    [property: JsonPropertyName("redirect_target_anchor")] string? RedirectTargetAnchor,
    [property: JsonPropertyName("anchor")] string? Anchor,
    [property: JsonPropertyName("scope"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WikiEvidenceScope? Scope = null);

internal sealed record WikiEvidenceScope(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("selected_section")] string? SelectedSection,
    [property: JsonPropertyName("anchor_found")] bool AnchorFound,
    [property: JsonPropertyName("page_complete")] bool PageComplete,
    [property: JsonPropertyName("content_version")] string ContentVersion,
    [property: JsonPropertyName("continuation_token")] string? ContinuationToken,
    [property: JsonPropertyName("sections")] IReadOnlyList<WikiEvidenceSection> Sections);

internal sealed record WikiEvidenceSection(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("anchor")] string? Anchor);

internal sealed record WikiEvidenceBlock(
    [property: JsonPropertyName("section_path")] string SectionPath,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("text")] string Text);
