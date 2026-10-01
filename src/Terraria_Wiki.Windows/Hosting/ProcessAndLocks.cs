using System.Security.Cryptography;
using System.Text;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed class NamedMutexLease : IDisposable
{
    private Mutex? _mutex;

    private NamedMutexLease(Mutex mutex, string name)
    {
        _mutex = mutex;
        Name = name;
    }

    public string Name { get; }

    public static NamedMutexLease? TryAcquire(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A mutex name is required.", nameof(name));

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }
            return new NamedMutexLease(mutex, name);
        }
        catch
        {
            mutex?.Dispose();
            throw;
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _mutex, null)?.Dispose();
}

internal static class WindowsMutexNames
{
    public const string SingleInstance = @"Local\TerrariaAIWiki.Windows.Singleton";

    public static string ForDataRoot(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("The data root must be absolute.", nameof(dataRoot));
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot)).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return @"Local\TerrariaAIWiki.DataRoot." + hash;
    }
}

internal sealed class WindowsMountGuard : IDisposable
{
    private NamedMutexLease? _singleInstance;
    private NamedMutexLease? _dataRoot;

    public void AcquireSingleInstance()
    {
        if (_singleInstance is not null)
            return;
        _singleInstance = NamedMutexLease.TryAcquire(WindowsMutexNames.SingleInstance)
            ?? throw new HostMountException(
                "client-already-running",
                "Another Terraria AI Wiki window is already running. Close it and retry.");
    }

    public void AcquireDataRoot(string dataRoot)
    {
        if (_dataRoot is not null)
            return;
        _dataRoot = NamedMutexLease.TryAcquire(WindowsMutexNames.ForDataRoot(dataRoot))
            ?? throw new HostMountException(
                "data-root-in-use",
                "This Wiki data folder is already in use. Close the other client and retry.");
    }

    public void ReleaseDataRoot() => Interlocked.Exchange(ref _dataRoot, null)?.Dispose();

    public void Dispose()
    {
        ReleaseDataRoot();
        Interlocked.Exchange(ref _singleInstance, null)?.Dispose();
    }
}

internal sealed class HostMountException : IOException
{
    public HostMountException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
