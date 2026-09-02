using System.Text.Json;
using Terraria_Wiki.Models;
using Xunit;

namespace Terraria_Wiki.Core.Tests;

public sealed class TransportModelTests
{
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
}
