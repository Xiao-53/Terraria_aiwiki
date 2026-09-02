using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.Tests;

public sealed class CompositeWikiDataConsumerCoordinatorTests
{
    [Fact]
    public async Task PausesInOrderAndReleasesInReverseOrder()
    {
        var events = new List<string>();
        var coordinator = new CompositeWikiDataConsumerCoordinator(
            new RecordingConsumer("viewer", events),
            new RecordingConsumer("wiki-tools", events));

        await using (await coordinator.PauseAsync())
            Assert.Equal(["pause:viewer", "pause:wiki-tools"], events);

        Assert.Equal(
            ["pause:viewer", "pause:wiki-tools", "resume:wiki-tools", "resume:viewer"],
            events);
    }

    [Fact]
    public async Task FailedPauseRollsBackAlreadyAcquiredLeasesInReverseOrder()
    {
        var events = new List<string>();
        var coordinator = new CompositeWikiDataConsumerCoordinator(
            new RecordingConsumer("viewer", events),
            new RecordingConsumer("wiki-tools", events, failPause: true));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await coordinator.PauseAsync());

        Assert.Equal(["pause:viewer", "pause:wiki-tools", "resume:viewer"], events);
    }

    private sealed class RecordingConsumer(
        string name,
        List<string> events,
        bool failPause = false) : IWikiDataConsumerCoordinator
    {
        public ValueTask<IAsyncDisposable> PauseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("pause:" + name);
            if (failPause)
                throw new InvalidOperationException("pause failed");
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(name, events));
        }
    }

    private sealed class Lease(string name, List<string> events) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                events.Add("resume:" + name);
            return ValueTask.CompletedTask;
        }
    }
}
