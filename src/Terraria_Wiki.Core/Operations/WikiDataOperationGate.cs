namespace Terraria_Wiki.Core.Operations;

/// <summary>
/// Coordinates mutations that can touch the same Wiki data root. Hosts must
/// share one instance between lifecycle and package services.
/// </summary>
public sealed class WikiDataOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<WikiDataOperationLease?> TryAcquireAsync(
        CancellationToken cancellationToken = default) =>
        await _gate.WaitAsync(0, cancellationToken)
            ? new WikiDataOperationLease(this)
            : null;

    private void Release() => _gate.Release();

    public sealed class WikiDataOperationLease : IDisposable
    {
        private WikiDataOperationGate? _owner;

        internal WikiDataOperationLease(WikiDataOperationGate owner) => _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release();
            GC.SuppressFinalize(this);
        }
    }
}
