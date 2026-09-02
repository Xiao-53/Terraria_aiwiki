using ModelContextProtocol.Client;
using Terraria_Wiki.WikiTools.TestHost;

namespace Terraria_Wiki.WikiTools.Tests;

public sealed class WikiToolTestHostTests
{
    [Fact]
    public async Task TestHostServesTheFixedSyntheticRedirectAndRecipe()
    {
        await using var host = await WikiToolTestHost.StartAsync();
        await using var client = await CreateClientAsync(host.Connection);

        var tools = await client.ListToolsAsync();
        var search = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "月镐" });
        var read = await client.CallToolAsync(
            "read_wiki",
            new Dictionary<string, object?> { ["title"] = "月镐" });

        Assert.Equal(["read_wiki", "search_wiki"], tools.Select(tool => tool.Name).Order().ToArray());
        Assert.False(search.IsError);
        var searchItem = Assert.Single(
            search.StructuredContent!.Value.GetProperty("results").EnumerateArray());
        Assert.Equal("月镐", searchItem.GetProperty("title").GetString());
        Assert.Equal("月铸镐", searchItem.GetProperty("redirect_target").GetString());
        Assert.False(read.IsError);
        Assert.Equal("月镐", read.StructuredContent!.Value.GetProperty("requested_title").GetString());
        Assert.Equal("月铸镐", read.StructuredContent!.Value.GetProperty("title").GetString());
        Assert.Equal("配方", read.StructuredContent!.Value.GetProperty("anchor").GetString());
        Assert.Contains("12夜银锭", read.StructuredContent!.Value.GetProperty("content").GetString());
    }

    [Fact]
    public async Task TestHostUsesAndDeletesOnlyItsUniqueTemporaryDataRoot()
    {
        var host = await WikiToolTestHost.StartAsync();
        var root = host.DataRoot;

        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("terraria-wiki-ai-test-host-", Path.GetFileName(root), StringComparison.Ordinal);
        Assert.True(Directory.Exists(root));

        await host.DisposeAsync();
        await host.DisposeAsync();

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void TestHostProjectCannotEnterAProductionProjectTransitively()
    {
        var repository = FindRepositoryRoot();
        var hostProject = File.ReadAllText(Path.Combine(
            repository,
            "tests",
            "Terraria_Wiki.WikiTools.TestHost",
            "Terraria_Wiki.WikiTools.TestHost.csproj"));
        var productionProjects = Directory
            .EnumerateFiles(Path.Combine(repository, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Contains("<IsPackable>false</IsPackable>", hostProject, StringComparison.Ordinal);
        Assert.All(productionProjects, project =>
            Assert.DoesNotContain("Terraria_Wiki.WikiTools.TestHost", project, StringComparison.Ordinal));
    }

    private static async Task<McpClient> CreateClientAsync(WikiToolConnection connection)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = connection.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + connection.BearerToken,
            },
        });
        return await McpClient.CreateAsync(transport);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Terraria_Wiki.Core.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
