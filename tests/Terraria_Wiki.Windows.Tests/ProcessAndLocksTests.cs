using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.Tests;

public sealed class ProcessAndLocksTests
{
    [Fact]
    public void NamedMutexConflictAndReleaseAreObservable()
    {
        var name = @"Local\TerrariaAIWiki.Tests." + Guid.NewGuid().ToString("N");
        using var first = NamedMutexLease.TryAcquire(name);

        Assert.NotNull(first);
        Assert.Null(NamedMutexLease.TryAcquire(name));

        first.Dispose();
        using var reacquired = NamedMutexLease.TryAcquire(name);
        Assert.NotNull(reacquired);
    }

    [Fact]
    public void DataRootMutexIsStableAndCaseInsensitive()
    {
        var upper = WindowsMutexNames.ForDataRoot(@"C:\WikiData\");
        var lower = WindowsMutexNames.ForDataRoot(@"c:\wikidata");

        Assert.Equal(upper, lower);
        Assert.StartsWith(@"Local\TerrariaAIWiki.DataRoot.", upper, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyProcessBlocksDataRootBeforeMutexIsTaken()
    {
        using var temp = new TestDirectory();
        var probe = new TestLegacyProcessProbe { Running = true };
        using var guard = new WindowsMountGuard(probe);

        var error = Assert.Throws<HostMountException>(() => guard.AcquireDataRoot(temp.Path));

        Assert.Equal("legacy-client-running", error.Code);
        probe.Running = false;
        guard.AcquireDataRoot(temp.Path);
        guard.ReleaseDataRoot();
        guard.AcquireDataRoot(temp.Path);
    }
}
