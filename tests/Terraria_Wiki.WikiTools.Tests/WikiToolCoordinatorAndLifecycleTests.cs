using System.Net;
using System.Net.Sockets;

namespace Terraria_Wiki.WikiTools.Tests;

public sealed class WikiToolCoordinatorAndLifecycleTests
{
    [Fact]
    public async Task PauseBeforeServerStartIsANoOpAndDoesNotPauseFutureServer()
    {
        var coordinator = new WikiToolConsumerCoordinator();

        await using var preStartPause = await coordinator.PauseAsync();
        var epoch = coordinator.AttachServer();
        using var request = coordinator.BeginRequest(CancellationToken.None);

        Assert.False(request.CancellationToken.IsCancellationRequested);
        request.Dispose();
        await coordinator.DetachServerAsync(epoch);
    }

    [Fact]
    public async Task PauseRejectsNewCallsCancelsAndWaitsForInflightThenReleaseResumes()
    {
        var coordinator = new WikiToolConsumerCoordinator();
        var epoch = coordinator.AttachServer();
        var request = coordinator.BeginRequest(CancellationToken.None);

        var pauseTask = coordinator.PauseAsync().AsTask();
        await WaitUntilAsync(() => request.CancellationToken.IsCancellationRequested);
        Assert.False(pauseTask.IsCompleted);
        request.Dispose();
        await using var pause = await pauseTask;

        var paused = Assert.Throws<WikiToolAccessException>(() => coordinator.BeginRequest(CancellationToken.None));
        Assert.Equal("wiki-tool-paused", paused.Code);
        await pause.DisposeAsync();
        var resumed = coordinator.BeginRequest(CancellationToken.None);
        Assert.False(resumed.CancellationToken.IsCancellationRequested);
        resumed.Dispose();
        await coordinator.DetachServerAsync(epoch);
    }

    [Fact]
    public async Task NestedPauseLeasesResumeOnlyAfterLastIdempotentRelease()
    {
        var coordinator = new WikiToolConsumerCoordinator();
        var epoch = coordinator.AttachServer();
        var first = await coordinator.PauseAsync();
        var second = await coordinator.PauseAsync();

        await first.DisposeAsync();
        await first.DisposeAsync();
        Assert.Throws<WikiToolAccessException>(() => coordinator.BeginRequest(CancellationToken.None));
        await second.DisposeAsync();
        var request = coordinator.BeginRequest(CancellationToken.None);

        request.Dispose();
        await coordinator.DetachServerAsync(epoch);
    }

    [Fact]
    public async Task McpCallsAreRejectedDuringPauseAndRecoverAfterRelease()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        await using var client = await fixture.CreateClientAsync();
        var pause = await fixture.Coordinator.PauseAsync();

        var paused = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "ore" });
        await pause.DisposeAsync();
        var resumed = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "ore" });

        Assert.True(paused.IsError);
        Assert.Equal("wiki-tool-paused", Assert.Single(paused.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()).Text);
        Assert.False(resumed.IsError);
    }

    [Fact]
    public async Task StopCancelsAndWaitsForInflightThenIsIdempotentAndReleasesPort()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        var port = fixture.Server.Connection.Endpoint.Port;
        var request = fixture.Coordinator.BeginRequest(CancellationToken.None);

        var stop = fixture.Server.StopAsync();
        await WaitUntilAsync(() => request.CancellationToken.IsCancellationRequested);
        Assert.False(stop.IsCompleted);
        request.Dispose();
        await stop;
        await fixture.Server.StopAsync();
        await fixture.Server.DisposeAsync();

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Assert.Equal(port, ((IPEndPoint)listener.LocalEndpoint).Port);
    }

    [Fact]
    public async Task EveryStartUsesLoopbackDynamicEndpointAndRotatesSecretWithoutToStringLeak()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        var firstEndpoint = fixture.Server.Connection.Endpoint;
        var firstToken = fixture.Server.Connection.BearerToken;
        Assert.Equal("127.0.0.1", firstEndpoint.Host);
        Assert.Equal("/mcp", firstEndpoint.AbsolutePath);
        Assert.InRange(firstEndpoint.Port, 1, ushort.MaxValue);
        Assert.DoesNotContain(firstToken, fixture.Server.Connection.ToString(), StringComparison.Ordinal);

        await fixture.Server.StopAsync();
        await using var second = await WikiToolServer.StartAsync(fixture.Query, fixture.Coordinator, CancellationToken.None);

        Assert.NotEqual(firstToken, second.Connection.BearerToken);
        Assert.Equal("127.0.0.1", second.Connection.Endpoint.Host);
    }

    [Fact]
    public async Task SameCoordinatorRejectsASecondLiveServerWithoutAffectingTheFirst()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => WikiToolServer.StartAsync(fixture.Query, fixture.Coordinator, CancellationToken.None));
        await using var client = await fixture.CreateClientAsync();
        var result = await client.CallToolAsync(
            "search_wiki",
            new Dictionary<string, object?> { ["query"] = "ore" });

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task CallerCancellationRemainsCancellationInCoordinatorLease()
    {
        var coordinator = new WikiToolConsumerCoordinator();
        var epoch = coordinator.AttachServer();
        using var cancellation = new CancellationTokenSource();
        using var request = coordinator.BeginRequest(cancellation.Token);

        cancellation.Cancel();

        Assert.True(request.CancellationToken.IsCancellationRequested);
        Assert.True(request.IsCallerCancellationRequested);
        request.Dispose();
        await coordinator.DetachServerAsync(epoch);
    }

    [Fact]
    public async Task ToolHandlerPreservesCallerCancellation()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        var tools = new WikiMcpTools(fixture.Query, fixture.Coordinator);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tools.SearchWikiAsync("ore", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ConcurrentOfficialClientCallsRemainIndependentAndOrdered()
    {
        await using var fixture = await WikiToolFixture.CreateAsync();
        var modified = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
        await fixture.SavePagesAndIndexesAsync(
            Enumerable.Range(0, 20)
                .Select(index => ($"concurrent {index:D2}", "body", $"<p>{index}</p>", modified))
                .ToArray());
        await using var client = await fixture.CreateClientAsync();

        var calls = Enumerable.Range(1, 20)
            .Select(limit => client.CallToolAsync(
                "search_wiki",
                new Dictionary<string, object?> { ["query"] = "concurrent", ["max_results"] = limit }).AsTask())
            .ToArray();
        var results = await Task.WhenAll(calls);

        for (var index = 0; index < results.Length; index++)
        {
            Assert.False(results[index].IsError);
            Assert.Equal(index + 1, results[index].StructuredContent!.Value.GetProperty("results").GetArrayLength());
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}
