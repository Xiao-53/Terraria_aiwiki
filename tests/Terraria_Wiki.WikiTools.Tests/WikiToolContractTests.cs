using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Terraria_Wiki.Application;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.WikiTools.Tests;

public sealed class WikiToolContractTests
{
    [Fact]
    public async Task ExplicitSectionModeUsesV2WhileLegacyCallsKeepV1()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        await fixture.SavePagesAndIndexesAsync(("页", "配方", "<h2 id='craft'>制作</h2><p>12锭</p>"
            + "<h2 id='other'>其他</h2><p>无关内容</p>", DateTime.UtcNow));
        await using var client = await fixture.CreateClientAsync();
        var legacy = await client.CallToolAsync("read_wiki", new Dictionary<string, object?> { ["title"] = "页#craft" });
        Assert.Equal("wiki_blocks_v1", legacy.StructuredContent!.Value.GetProperty("content_format").GetString());
        Assert.False(legacy.StructuredContent.Value.TryGetProperty("scope", out _));
        foreach (var anchor in new[] { "craft", "missing" })
        {
            var result = await client.CallToolAsync("read_wiki", new Dictionary<string, object?>
                { ["title"] = "页#" + anchor, ["mode"] = "section" });
            Assert.False(result.IsError);
            var body = result.StructuredContent!.Value;
            Assert.Equal("wiki_blocks_v2", body.GetProperty("content_format").GetString());
            Assert.True(body.GetProperty("is_complete").GetBoolean());
            Assert.False(body.GetProperty("scope").GetProperty("page_complete").GetBoolean());
            Assert.Equal(anchor == "craft", body.GetProperty("scope").GetProperty("anchor_found").GetBoolean());
        }
        var invalid = await client.CallToolAsync("read_wiki", new Dictionary<string, object?>
            { ["title"] = "页#craft", ["mode"] = "section", ["cursor"] = 1 });
        Assert.True(invalid.IsError);
        Assert.Equal("invalid-arguments", Assert.IsType<TextContentBlock>(Assert.Single(invalid.Content)).Text);
    }

    [Fact]
    public async Task OfficialClientInitializesAndDiscoversExactlyTheTwoFixedTools()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        await using var client = await fixture.CreateClientAsync();

        var tools = await client.ListToolsAsync();

        Assert.Null(client.SessionId);
        Assert.NotNull(client.ServerCapabilities.Tools);
        Assert.Null(client.ServerCapabilities.Prompts);
        Assert.Null(client.ServerCapabilities.Resources);
        Assert.Equal(["read_wiki", "search_wiki"], tools.Select(tool => tool.Name).Order().ToArray());
        var search = tools.Single(tool => tool.Name == "search_wiki").ProtocolTool;
        var read = tools.Single(tool => tool.Name == "read_wiki").ProtocolTool;
        AssertToolAnnotations(search);
        AssertToolAnnotations(read);

        Assert.Equal(["max_results", "query"], PropertyNames(search.InputSchema));
        Assert.Equal(["query"], RequiredNames(search.InputSchema));
        var maxResults = search.InputSchema.GetProperty("properties").GetProperty("max_results");
        Assert.Equal(1, maxResults.GetProperty("minimum").GetInt32());
        Assert.Equal(100, maxResults.GetProperty("maximum").GetInt32());

        Assert.Equal(["continuation_token", "cursor", "focus", "mode", "title"], PropertyNames(read.InputSchema));
        Assert.Equal(["title"], RequiredNames(read.InputSchema));
        Assert.Contains("results", search.OutputSchema!.Value.GetRawText(), StringComparison.Ordinal);
        foreach (var property in new[] { "title", "redirect_target", "summary", "match_type", "highlights", "start", "length", "canonical_title", "match_scope", "matched_sections", "matched_sections_truncated" })
            Assert.Contains($"\"{property}\"", search.OutputSchema.Value.GetRawText(), StringComparison.Ordinal);
        foreach (var property in new[]
                 {
                     "requested_title", "requested_page_title", "status", "is_found", "title", "content_format",
                     "blocks", "section_path", "kind", "text", "included_characters", "original_characters",
                     "is_complete", "next_cursor", "available_sections",
                     "last_modified", "is_redirect", "redirect_from_title", "redirect_target_title",
                     "redirect_target_anchor", "anchor",
                 })
            Assert.Contains($"\"{property}\"", read.OutputSchema!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchStructuredOutputExactlyMatchesDirectApplicationOrderingAndFields()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        await fixture.SaveSearchFixtureAsync();
        var direct = await fixture.Query.SearchAsync("crystal", 100);
        await using var client = await fixture.CreateClientAsync();

        var result = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "crystal", ["max_results"] = 100 });

        Assert.False(result.IsError);
        var values = result.StructuredContent!.Value.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(direct.Count, values.Length);
        Assert.NotEmpty(direct);
        Assert.Equal(WikiSearchMatchKind.ExactTitle, direct[0].MatchKind);
        Assert.Equal(direct.OrderBy(item => item.Scope).Select(item => item.Scope),
            direct.Select(item => item.Scope));

        for (var index = 0; index < direct.Count; index++)
        {
            var expected = direct[index];
            var actual = values[index];
            Assert.Equal(expected.Title, actual.GetProperty("title").GetString());
            Assert.Equal(expected.RedirectTo, actual.GetProperty("redirect_target").GetString());
            Assert.Equal(expected.Snippet, actual.GetProperty("summary").GetString());
            Assert.Equal(expected.CanonicalTitle, actual.GetProperty("canonical_title").GetString());
            Assert.Equal(MatchType(expected.MatchKind), actual.GetProperty("match_type").GetString());
            var highlights = actual.GetProperty("highlights").EnumerateArray().ToArray();
            Assert.Equal(expected.Highlights.Count, highlights.Length);
            for (var highlightIndex = 0; highlightIndex < highlights.Length; highlightIndex++)
            {
                Assert.Equal(expected.Highlights[highlightIndex].Start, highlights[highlightIndex].GetProperty("start").GetInt32());
                Assert.Equal(expected.Highlights[highlightIndex].Length, highlights[highlightIndex].GetProperty("length").GetInt32());
            }
        }
    }

    [Fact]
    public async Task SearchUsesApplicationDefaultAllowsExplicitLimitAndPreservesEmptyQuery()
    {
        await using var fixture = await WikiToolFixture.CreateAsync(new ApplicationPreferences { SearchLimit = 3, AiSearchLimit = 2 });
        var modified = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
        await fixture.SavePagesAndIndexesAsync(
            Enumerable.Range(0, 6)
                .Select(index => ($"ore {index:D2}", "ore body", $"<p>ore {index}</p>", modified))
                .ToArray());
        await using var client = await fixture.CreateClientAsync();

        var configured = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "ore" });
        var explicitLimit = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "ore", ["max_results"] = 5 });
        var empty = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "  " });

        Assert.Equal(2, ResultCount(configured));
        Assert.Equal(5, ResultCount(explicitLimit));
        Assert.Equal(0, ResultCount(empty));
    }

    [Fact]
    public async Task ReadPreservesIdentityRedirectAnchorAndNotFoundWithoutExposingHtml()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        await fixture.SaveSearchFixtureAsync();
        await using var client = await fixture.CreateClientAsync();

        foreach (var title in new[] { "Crystal#Stats", "Blade", "Missing" })
        {
            var direct = await fixture.Query.ReadAsync(title);
            var result = await client.CallToolAsync(
                "read_wiki",
                new Dictionary<string, object?> { ["title"] = title });

            Assert.False(result.IsError);
            var actual = result.StructuredContent!.Value;
            Assert.Equal(direct.RequestedTitle, actual.GetProperty("requested_title").GetString());
            Assert.Equal(direct.RequestedPageTitle, actual.GetProperty("requested_page_title").GetString());
            Assert.Equal(direct.IsFound ? "found" : "not_found", actual.GetProperty("status").GetString());
            Assert.Equal(direct.IsFound, actual.GetProperty("is_found").GetBoolean());
            AssertNullableString(direct.Title, actual.GetProperty("title"));
            Assert.False(actual.TryGetProperty("content", out _));
            Assert.Equal(direct.IsFound ? "wiki_blocks_v1" : null, actual.GetProperty("content_format").GetString());
            Assert.Equal(
                direct.Content?.EnumerateRunes().Count() ?? 0,
                actual.GetProperty("original_characters").GetInt32());
            var included = actual.GetProperty("blocks").EnumerateArray()
                .Sum(block => block.GetProperty("text").GetString()!.EnumerateRunes().Count());
            Assert.Equal(included, actual.GetProperty("included_characters").GetInt32());
            Assert.InRange(included, 0, WikiEvidenceFormatter.MaxBlockCharacters);
            AssertNullableString(direct.RedirectFromTitle, actual.GetProperty("redirect_from_title"));
            AssertNullableString(direct.RedirectTargetTitle, actual.GetProperty("redirect_target_title"));
            AssertNullableString(direct.RedirectTargetAnchor, actual.GetProperty("redirect_target_anchor"));
            AssertNullableString(direct.Anchor, actual.GetProperty("anchor"));
            Assert.Equal(direct.IsRedirect, actual.GetProperty("is_redirect").GetBoolean());
            if (direct.LastModified is { } modified)
                Assert.Equal(modified, actual.GetProperty("last_modified").GetDateTime());
            else
                Assert.Equal(JsonValueKind.Null, actual.GetProperty("last_modified").ValueKind);
        }
    }

    private static string[] PropertyNames(JsonElement schema) => schema
        .GetProperty("properties")
        .EnumerateObject()
        .Select(property => property.Name)
        .Order()
        .ToArray();

    private static string[] RequiredNames(JsonElement schema) => schema
        .GetProperty("required")
        .EnumerateArray()
        .Select(value => value.GetString()!)
        .Order()
        .ToArray();

    private static void AssertToolAnnotations(Tool tool)
    {
        Assert.True(tool.Annotations?.ReadOnlyHint);
        Assert.False(tool.Annotations?.DestructiveHint);
        Assert.True(tool.Annotations?.IdempotentHint);
        Assert.False(tool.Annotations?.OpenWorldHint);
        Assert.NotNull(tool.OutputSchema);
    }

    private static int ResultCount(CallToolResult result) =>
        result.StructuredContent!.Value.GetProperty("results").GetArrayLength();

    private static string MatchType(WikiSearchMatchKind kind) => kind switch
    {
        WikiSearchMatchKind.ExactTitle => "exact_title",
        WikiSearchMatchKind.ExactRedirect => "exact_redirect",
        WikiSearchMatchKind.TitlePrefix => "title_prefix",
        WikiSearchMatchKind.RedirectPrefix => "redirect_prefix",
        WikiSearchMatchKind.TitleContains => "title_contains",
        WikiSearchMatchKind.RedirectContains => "redirect_contains",
        WikiSearchMatchKind.BodyFts => "body_fts",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void AssertNullableString(string? expected, JsonElement actual)
    {
        if (expected is null)
            Assert.Equal(JsonValueKind.Null, actual.ValueKind);
        else
            Assert.Equal(expected, actual.GetString());
    }
}
