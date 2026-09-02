using Terraria_Wiki.Application;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed class CompositeWikiDataConsumerCoordinator : IWikiDataConsumerCoordinator
{
    private readonly IReadOnlyList<IWikiDataConsumerCoordinator> _consumers;

    public CompositeWikiDataConsumerCoordinator(params IWikiDataConsumerCoordinator[] consumers)
    {
        ArgumentNullException.ThrowIfNull(consumers);
        if (consumers.Length == 0 || consumers.Any(consumer => consumer is null))
            throw new ArgumentException("At least one consumer is required.", nameof(consumers));
        _consumers = consumers;
    }

    public async ValueTask<IAsyncDisposable> PauseAsync(CancellationToken cancellationToken = default)
    {
        var leases = new List<IAsyncDisposable>(_consumers.Count);
        try
        {
            foreach (var consumer in _consumers)
                leases.Add(await consumer.PauseAsync(cancellationToken).ConfigureAwait(false));
            return new CompositePauseLease(leases);
        }
        catch
        {
            await DisposeReverseAsync(leases, suppressErrors: true).ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask DisposeReverseAsync(
        IReadOnlyList<IAsyncDisposable> leases,
        bool suppressErrors)
    {
        Exception? firstError = null;
        for (var index = leases.Count - 1; index >= 0; index--)
        {
            try
            {
                await leases[index].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstError ??= exception;
            }
        }
        if (!suppressErrors && firstError is not null)
            throw firstError;
    }

    private sealed class CompositePauseLease : IAsyncDisposable
    {
        private IReadOnlyList<IAsyncDisposable>? _leases;

        public CompositePauseLease(IReadOnlyList<IAsyncDisposable> leases) => _leases = leases;

        public ValueTask DisposeAsync()
        {
            var leases = Interlocked.Exchange(ref _leases, null);
            return leases is null
                ? ValueTask.CompletedTask
                : DisposeReverseAsync(leases, suppressErrors: false);
        }
    }
}
