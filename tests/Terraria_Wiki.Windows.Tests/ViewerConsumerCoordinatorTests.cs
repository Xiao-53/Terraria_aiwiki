using System.Net;
using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Viewer;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ViewerConsumerCoordinatorTests
{
    [Fact]
    public async Task PauseStopsRequestsAndReleaseRestartsCurrentWiki()
    {
        using var temp = new TestDirectory();
        var root = ViewerServerTests.CreateStaticRoot(temp.Path);
        var current = Snapshot("Terraria_Wiki_zh");
        await using var coordinator = new ViewerConsumerCoordinator(
            root,
            new ViewerServerTests.TestViewerDataSource(),
            () => "light");
        coordinator.SetActiveWikiProvider(_ => Task.FromResult(current));

        await coordinator.EnableAsync();
        var first = Assert.IsType<Uri>(coordinator.Status.BaseUri);
        using (var client = new HttpClient { BaseAddress = first })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("health")).StatusCode);

        var pause = await coordinator.PauseAsync();
        Assert.Equal(ViewerConsumerState.Paused, coordinator.Status.State);
        current = Snapshot("Terraria_Wiki_zh");
        await pause.DisposeAsync();

        Assert.Equal(ViewerConsumerState.Ready, coordinator.Status.State);
        Assert.NotNull(coordinator.Status.BaseUri);
    }

    [Fact]
    public async Task ReleaseAfterHostStopNeverRestartsViewer()
    {
        using var temp = new TestDirectory();
        var coordinator = new ViewerConsumerCoordinator(
            ViewerServerTests.CreateStaticRoot(temp.Path),
            new ViewerServerTests.TestViewerDataSource(),
            () => "dark");
        coordinator.SetActiveWikiProvider(_ => Task.FromResult(Snapshot("Terraria_Wiki_zh")));
        await coordinator.EnableAsync();
        var pause = await coordinator.PauseAsync();

        await coordinator.DisposeAsync();
        await pause.DisposeAsync();

        Assert.Equal(ViewerConsumerState.Stopped, coordinator.Status.State);
        Assert.Null(coordinator.Status.BaseUri);
    }

    private static WikiBookSnapshot Snapshot(string folder) => new(
        1,
        "Wiki",
        "Fixture",
        true,
        true,
        1,
        0,
        0,
        0,
        DateTime.UnixEpoch,
        folder,
        "Home");
}
