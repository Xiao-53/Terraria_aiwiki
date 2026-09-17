using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Terraria_Wiki.Application;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.WikiTools;

[McpServerToolType]
internal sealed class WikiMcpTools
{
    private readonly WikiQueryUseCases _query;
    private readonly WikiToolConsumerCoordinator _coordinator;

    public WikiMcpTools(WikiQueryUseCases query, WikiToolConsumerCoordinator coordinator)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    [McpServerTool(
        Name = "search_wiki",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(WikiSearchToolResponse))]
    [Description("Search the active local Wiki using entity and topic keywords. Results include bounded matched_sections navigation; use canonical_title#anchor with read_wiki mode=section when relevant. Summaries and navigation are not factual evidence or complete coverage.")]
    public Task<CallToolResult> SearchWikiAsync(
        [Required]
        [Description("The local Wiki query. Empty or whitespace-only queries return no results.")]
        string? query = null,
        [Range(1, 100)]
        [Description("Optional maximum page count from 1 through 100. Normally omit to use the independent AI search setting (default 5); explicitly increase only when more candidates are needed.")]
        int? max_results = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            async token =>
            {
                if (query is null)
                    throw new ArgumentNullException(nameof(query));
                var results = await _query.SearchForAiAsync(query, max_results, token).ConfigureAwait(false);
                return new WikiSearchToolResponse(results.Select(MapSearchResult).ToArray());
            },
            cancellationToken);

    [McpServerTool(
        Name = "read_wiki",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(WikiReadToolResponse))]
    [Description("Read local Wiki evidence after Search. Omit mode for legacy page ordering; mode=page returns a section catalog, mode=section reads title#anchor with parent context. is_complete covers the selected range; scope.page_complete covers the page. Continue using next_cursor and scope.continuation_token with identical inputs. A missing anchor returns scope.anchor_found=false and a catalog; choose a listed anchor or mode=page.")]
    public Task<CallToolResult> ReadWikiAsync(
        [Required]
        [Description("The exact local Wiki page title, optionally followed by a #fragment anchor.")]
        string? title = null,
        [MaxLength(32768)]
        [Description("Optional question or topic used to prioritize matching sections.")]
        string? focus = null,
        [Range(0, int.MaxValue)]
        [Description("Optional continuation cursor returned by an earlier read with the same title and focus.")]
        int? cursor = null,
        [Description("Optional page or section. Section requires title#anchor; focus only ranks, never filters.")]
        string? mode = null,
        [Description("For mode=page/section continuation, pass scope.continuation_token with next_cursor as cursor.")]
        string? continuation_token = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            async token =>
            {
                if (title is null)
                    throw new ArgumentNullException(nameof(title));
                if (mode is not (null or "page" or "section") || cursor < 0
                    || (mode is null && continuation_token is not null))
                    throw new ArgumentException("Invalid evidence arguments.");
                var result = await _query.ReadAsync(title, token).ConfigureAwait(false);
                var evidence = result.IsFound && result.Content is not null && result.Title is not null
                    ? WikiEvidenceFormatter.Format(result.Title, result.Content, result.Anchor, focus, cursor,
                        mode, continuation_token, result.RequestedTitle)
                    : WikiEvidencePage.Empty;
                return new WikiReadToolResponse(
                    result.RequestedTitle,
                    result.RequestedPageTitle,
                    result.IsFound ? "found" : "not_found",
                    result.IsFound,
                    result.Title,
                    result.IsFound ? mode is null ? "wiki_blocks_v1" : "wiki_blocks_v2" : null,
                    evidence.Blocks,
                    evidence.IncludedCharacters,
                    result.Content?.EnumerateRunes().Count() ?? 0,
                    evidence.IsComplete,
                    evidence.NextCursor,
                    evidence.AvailableSections,
                    result.LastModified,
                    result.IsRedirect,
                    result.RedirectFromTitle,
                    result.RedirectTargetTitle,
                    result.RedirectTargetAnchor,
                    result.Anchor,
                    evidence.Scope);
            },
            cancellationToken);

    private async Task<CallToolResult> InvokeAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        WikiToolConsumerCoordinator.WikiToolRequestLease request;
        try
        {
            request = _coordinator.BeginRequest(cancellationToken);
        }
        catch (WikiToolAccessException exception)
        {
            return Error(exception.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Error("wiki-tool-failed");
        }

        using (request)
        {
            try
            {
                return Success(await operation(request.CancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (request.IsCallerCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return Error(request.CancellationCode);
            }
            catch (ArgumentException)
            {
                return Error("invalid-arguments");
            }
            catch (ApplicationUseCaseException exception)
            {
                return Error(MapApplicationError(exception.Code));
            }
            catch (WikiQueryException exception) when (exception.Code == WikiQueryErrorCode.DataSourceUnavailable)
            {
                return Error("wiki-data-unavailable");
            }
            catch
            {
                return Error("wiki-tool-failed");
            }
        }
    }

    private static CallToolResult Success<T>(T value)
    {
        var structured = JsonSerializer.SerializeToElement(value);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = structured.GetRawText() }],
            StructuredContent = structured,
            IsError = false,
        };
    }

    private static CallToolResult Error(string code) => new()
    {
        Content = [new TextContentBlock { Text = code }],
        IsError = true,
    };

    internal static WikiSearchToolResult MapSearchResult(SearchResultItem item)
    {
        var sections = new List<WikiSearchToolSection>();
        var seen = new HashSet<WikiSearchToolSection>();
        var characters = 0;
        var truncated = false;
        var candidates = new[] { new WikiSearchToolSection(item.SectionPath, item.Anchor) }
            .Concat(item.EvidenceRegions.Select(region => new WikiSearchToolSection(region.SectionPath, region.Anchor)));
        foreach (var section in candidates)
        {
            if (!seen.Add(section))
                continue;
            var size = section.SectionPath.EnumerateRunes().Count() + section.Anchor.EnumerateRunes().Count();
            if (sections.Count >= 5 || characters + size > 2048)
            {
                truncated = true;
                continue;
            }
            sections.Add(section);
            characters += size;
        }
        return new(
            item.Title,
            item.RedirectTo,
            item.Snippet,
            item.MatchKind switch
            {
                WikiSearchMatchKind.ExactTitle => "exact_title",
                WikiSearchMatchKind.ExactRedirect => "exact_redirect",
                WikiSearchMatchKind.TitlePrefix => "title_prefix",
                WikiSearchMatchKind.RedirectPrefix => "redirect_prefix",
                WikiSearchMatchKind.TitleContains => "title_contains",
                WikiSearchMatchKind.RedirectContains => "redirect_contains",
                WikiSearchMatchKind.BodyFts => "body_fts",
                _ => throw new InvalidOperationException("Unknown Wiki search match kind."),
            },
            item.Highlights.Select(range => new WikiSearchToolHighlight(range.Start, range.Length)).ToArray(),
            item.CanonicalTitle,
            item.Scope switch
            {
                WikiSearchScope.SameRegion => "same_region",
                WikiSearchScope.SameSection => "same_section",
                WikiSearchScope.SamePage => "same_page",
                WikiSearchScope.Partial => "partial",
                _ => throw new InvalidOperationException("Unknown Wiki search scope."),
            },
            sections,
            truncated);
    }

    private static string MapApplicationError(string code) => code switch
    {
        "not-initialized" => "wiki-not-initialized",
        "operation-in-progress" => "wiki-operation-in-progress",
        "unknown-wiki" => "wiki-unknown-source",
        _ => "wiki-application-failed",
    };
}
