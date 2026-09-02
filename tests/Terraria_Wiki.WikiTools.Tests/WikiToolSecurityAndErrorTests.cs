using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Terraria_Wiki.Application;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.WikiTools.Tests;

public sealed class WikiToolSecurityAndErrorTests
{
    [Fact]
    public async Task MissingAndWrongBearerTokensReturn401WithoutCorsOrDetails()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        using var client = new HttpClient();

        var missing = await SendProbeAsync(client, fixture.Server.Connection.Endpoint, bearerToken: null);
        var wrong = await SendProbeAsync(client, fixture.Server.Connection.Endpoint, bearerToken: "wrong-token");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("Bearer", missing.Headers.WwwAuthenticate.Single().Scheme);
        Assert.False(missing.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.DoesNotContain(fixture.Server.Connection.BearerToken, await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Server.Connection.BearerToken, await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongHostIsRejectedBeforeMcpAndCorsRemainsDisabled()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        using var client = new HttpClient();
        using var request = CreateProbe(fixture.Server.Connection.Endpoint, fixture.Server.Connection.BearerToken);
        request.Headers.Host = "localhost:" + fixture.Server.Connection.Endpoint.Port;

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task LegacySseRouteIsNotExposed()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        using var client = new HttpClient();
        var legacyEndpoint = new Uri(fixture.Server.Connection.Endpoint.AbsoluteUri.TrimEnd('/') + "/sse");
        using var request = CreateProbe(legacyEndpoint, fixture.Server.Connection.BearerToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task InvalidArgumentsBecomeStableToolErrorsWithoutImplementationDetails()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        await using var client = await fixture.CreateClientAsync();

        var invalidLimit = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "ore", ["max_results"] = 101 });
        var invalidTitle = await client.CallToolAsync(
            "read_wiki",
            new Dictionary<string, object?> { ["title"] = "  " });
        var missingQuery = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?>());
        var missingTitle = await client.CallToolAsync(
            "read_wiki",
            new Dictionary<string, object?>());

        AssertToolError(invalidLimit, "invalid-arguments");
        AssertToolError(invalidTitle, "invalid-arguments");
        AssertToolError(missingQuery, "invalid-arguments");
        AssertToolError(missingTitle, "invalid-arguments");
    }

    [Fact]
    public async Task UninitializedApplicationAndUnavailableDataUseStableSafeCodes()
    {
        await using (var uninitialized = await WikiToolFixture.CreateAsync(initialize: false))
        await using (var client = await uninitialized.CreateClientAsync())
        {
            var result = await client.CallToolAsync(
                "search_wiki",
                new Dictionary<string, object?> { ["query"] = "ore" });
            AssertToolError(result, "wiki-not-initialized");
        }

        await using var unavailable = await WikiToolFixture.CreateAsync();
        var state = StateField().GetValue(unavailable.Query)!;
        var database = Assert.IsType<ContentDbService>(state.GetType().GetProperty("ContentDatabase")!.GetValue(state));
        var unavailablePath = Path.Combine(unavailable.Root, "unavailable");
        Directory.CreateDirectory(unavailablePath);
        await database.SwitchDatabaseAsync(unavailablePath);
        await using var unavailableClient = await unavailable.CreateClientAsync();

        var unavailableResult = await unavailableClient.CallToolAsync(
            "read_wiki",
            new Dictionary<string, object?> { ["title"] = "Crystal" });

        AssertToolError(unavailableResult, "wiki-data-unavailable");
        var text = ErrorText(unavailableResult);
        Assert.DoesNotContain(unavailable.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLite", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownInternalExceptionIsReducedToWikiToolFailed()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        StateField().SetValue(fixture.Query, null);
        await using var client = await fixture.CreateClientAsync();

        var result = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "ore" });

        AssertToolError(result, "wiki-tool-failed");
        Assert.DoesNotContain("NullReference", ErrorText(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownToolIsRejectedAsProtocolError()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        await using var client = await fixture.CreateClientAsync();

        var error = await Assert.ThrowsAnyAsync<McpException>(
            () => client.CallToolAsync("unknown_wiki_tool", new Dictionary<string, object?>()).AsTask());

        Assert.DoesNotContain(fixture.Root, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static FieldInfo StateField() => typeof(WikiQueryUseCases).GetField(
        "_state",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(WikiQueryUseCases).FullName, "_state");

    private static HttpRequestMessage CreateProbe(Uri endpoint, string? bearerToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (bearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return request;
    }

    private static async Task<HttpResponseMessage> SendProbeAsync(HttpClient client, Uri endpoint, string? bearerToken)
    {
        using var request = CreateProbe(endpoint, bearerToken);
        return await client.SendAsync(request);
    }

    private static void AssertToolError(CallToolResult result, string code)
    {
        Assert.True(result.IsError);
        Assert.Equal(code, ErrorText(result));
        Assert.Null(result.StructuredContent);
    }

    private static string ErrorText(CallToolResult result) =>
        Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
}
