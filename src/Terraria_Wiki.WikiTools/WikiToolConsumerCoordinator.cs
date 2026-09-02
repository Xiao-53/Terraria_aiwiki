using Terraria_Wiki.Application;

namespace Terraria_Wiki.WikiTools;

public sealed class WikiToolConsumerCoordinator : IWikiDataConsumerCoordinator
{
    private readonly object _sync = new();
    private CancellationTokenSource? _requestGeneration;
    private TaskCompletionSource _drained = CompletedDrain();
    private long _epoch;
    private int _activeRequests;
    private int _pauseCount;
    private bool _attached;
    private bool _detaching;

    public async ValueTask<IAsyncDisposable> PauseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CancellationTokenSource? generationToCancel = null;
        Task drainTask;
        long epoch;
        lock (_sync)
        {
            if (!_attached)
                return NoOpLease.Instance;

            epoch = _epoch;
            _pauseCount++;
            if (_pauseCount == 1)
                generationToCancel = _requestGeneration;
            drainTask = _drained.Task;
        }

        CancelGeneration(generationToCancel);
        await drainTask.ConfigureAwait(false);
        return new PauseLease(this, epoch);
    }

    internal long AttachServer()
    {
        lock (_sync)
        {
            if (_attached || _detaching)
                throw new InvalidOperationException("A Wiki Tool server is already using this coordinator.");

            _epoch++;
            _attached = true;
            _pauseCount = 0;
            _requestGeneration = new CancellationTokenSource();
            return _epoch;
        }
    }

    internal async Task DetachServerAsync(long epoch)
    {
        CancellationTokenSource? generationToCancel;
        Task drainTask;
        lock (_sync)
        {
            if (!_attached || epoch != _epoch)
                return;

            _attached = false;
            _detaching = true;
            _pauseCount = 0;
            generationToCancel = _requestGeneration;
            drainTask = _drained.Task;
        }

        CancelGeneration(generationToCancel);
        await drainTask.ConfigureAwait(false);

        lock (_sync)
        {
            if (epoch == _epoch)
            {
                _requestGeneration?.Dispose();
                _requestGeneration = null;
                _detaching = false;
            }
        }
    }

    internal WikiToolRequestLease BeginRequest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_attached)
                throw new WikiToolAccessException("wiki-tool-stopped");
            if (_pauseCount != 0)
                throw new WikiToolAccessException("wiki-tool-paused");

            var generation = _requestGeneration
                ?? throw new InvalidOperationException("The Wiki Tool request generation is unavailable.");
            if (++_activeRequests == 1)
                _drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                generation.Token);
            return new WikiToolRequestLease(this, _epoch, cancellationToken, linked);
        }
    }

    private string GetCancellationCode(long epoch)
    {
        lock (_sync)
        {
            return _attached && epoch == _epoch && _pauseCount != 0
                ? "wiki-tool-paused"
                : "wiki-tool-stopped";
        }
    }

    private void CompleteRequest()
    {
        TaskCompletionSource? drained = null;
        lock (_sync)
        {
            if (_activeRequests <= 0)
                return;
            if (--_activeRequests == 0)
                drained = _drained;
        }
        drained?.TrySetResult();
    }

    private ValueTask ReleasePauseAsync(long epoch)
    {
        CancellationTokenSource? oldGeneration = null;
        lock (_sync)
        {
            if (!_attached || epoch != _epoch || _pauseCount == 0)
                return ValueTask.CompletedTask;

            _pauseCount--;
            if (_pauseCount == 0)
            {
                oldGeneration = _requestGeneration;
                _requestGeneration = new CancellationTokenSource();
            }
        }
        oldGeneration?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static TaskCompletionSource CompletedDrain()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private static void CancelGeneration(CancellationTokenSource? generation)
    {
        try
        {
            generation?.Cancel();
        }
        catch (AggregateException)
        {
            // A consumer cancellation callback must not prevent request draining.
        }
    }

    private sealed class PauseLease : IAsyncDisposable
    {
        private WikiToolConsumerCoordinator? _owner;
        private readonly long _epoch;

        public PauseLease(WikiToolConsumerCoordinator owner, long epoch)
        {
            _owner = owner;
            _epoch = epoch;
        }

        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            return owner is null
                ? ValueTask.CompletedTask
                : owner.ReleasePauseAsync(_epoch);
        }
    }

    private sealed class NoOpLease : IAsyncDisposable
    {
        public static NoOpLease Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class WikiToolRequestLease : IDisposable
    {
        private WikiToolConsumerCoordinator? _owner;
        private readonly long _epoch;
        private readonly CancellationToken _callerCancellation;
        private readonly CancellationTokenSource _linkedCancellation;

        public WikiToolRequestLease(
            WikiToolConsumerCoordinator owner,
            long epoch,
            CancellationToken callerCancellation,
            CancellationTokenSource linkedCancellation)
        {
            _owner = owner;
            _epoch = epoch;
            _callerCancellation = callerCancellation;
            _linkedCancellation = linkedCancellation;
        }

        public CancellationToken CancellationToken => _linkedCancellation.Token;
        public bool IsCallerCancellationRequested => _callerCancellation.IsCancellationRequested;
        public string CancellationCode => _owner?.GetCancellationCode(_epoch) ?? "wiki-tool-stopped";

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            _linkedCancellation.Dispose();
            owner?.CompleteRequest();
        }
    }
}

internal sealed class WikiToolAccessException : Exception
{
    public WikiToolAccessException(string code) : base(code) => Code = code;
    public string Code { get; }
}
