using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
    [Description("Searches the active local Terraria Wiki using its existing deterministic title, redirect, and body search.")]
    public Task<CallToolResult> SearchWikiAsync(
        [Required]
        [Description("The local Wiki query. Empty or whitespace-only queries return no results.")]
        string? query = null,
        [Range(1, 100)]
        [Description("Optional maximum result count from 1 through 100. Omit it to use the Application setting.")]
        int? max_results = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            async token =>
            {
                if (query is null)
                    throw new ArgumentNullException(nameof(query));
                var results = await _query.SearchAsync(query, max_results, token).ConfigureAwait(false);
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
    [Description("Reads one page from the active local Terraria Wiki, resolving redirects and anchors without recording history.")]
    public Task<CallToolResult> ReadWikiAsync(
        [Required]
        [Description("The exact local Wiki page title, optionally followed by a #fragment anchor.")]
        string? title = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync(
            async token =>
            {
                if (title is null)
                    throw new ArgumentNullException(nameof(title));
                var result = await _query.ReadAsync(title, token).ConfigureAwait(false);
                return new WikiReadToolResponse(
                    result.RequestedTitle,
                    result.RequestedPageTitle,
                    result.IsFound ? "found" : "not_found",
                    result.IsFound,
                    result.Title,
                    result.Content,
                    result.LastModified,
                    result.IsRedirect,
                    result.RedirectFromTitle,
                    result.RedirectTargetTitle,
                    result.RedirectTargetAnchor,
                    result.Anchor);
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

    private static WikiSearchToolResult MapSearchResult(SearchResultItem item) => new(
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
        item.Highlights.Select(range => new WikiSearchToolHighlight(range.Start, range.Length)).ToArray());

    private static string MapApplicationError(string code) => code switch
    {
        "not-initialized" => "wiki-not-initialized",
        "operation-in-progress" => "wiki-operation-in-progress",
        "unknown-wiki" => "wiki-unknown-source",
        _ => "wiki-application-failed",
    };
}
