using System.Net;
using System.Text.Json;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Models;
using Xunit;

namespace Terraria_Wiki.Core.Tests;

public sealed class TransportModelTests
{
    [Fact]
    public async Task HttpTransportSendsDescriptiveUserAgentForWikiRequests()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var transport = new HttpClientWikiHttpTransport(client);
        var book = new WikiBook();

        await transport.GetStringAsync(book, new Uri("https://wiki.test/pages"));
        await transport.GetContentAsync(book, new Uri("https://wiki.test/resource.png"));

        Assert.Equal(2, handler.UserAgents.Count);
        Assert.All(handler.UserAgents, value => Assert.Equal("TerrariaAIWiki/1.0", value));
    }

    [Fact]
    public void MediaWikiPageListResponseDeserializesLegacyDtoShape()
    {
        const string json = """
            {
              "continue": { "gapcontinue": "Copper Pickaxe" },
              "query": {
                "pages": {
                  "42": {
                    "title": "Copper Ore",
                    "touched": "2026-08-24T01:02:03Z",
                    "fullurl": "https://terraria.wiki.gg/wiki/Copper_Ore"
                  }
                }
              }
            }
            """;

        var response = JsonSerializer.Deserialize<RawResponse>(json);

        Assert.NotNull(response);
        Assert.Equal("Copper Pickaxe", response.Continue!.GapContinue);
        var page = Assert.Single(response.Query!.Pages).Value;
        Assert.Equal("Copper Ore", page.Title);
        Assert.Equal("2026-08-24T01:02:03Z", page.Touched);
        Assert.Equal("https://terraria.wiki.gg/wiki/Copper_Ore", page.FullUrl);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            });
        }
    }
}
