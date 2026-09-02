using Terraria_Wiki.Windows.Hosting;

namespace Terraria_Wiki.Windows.Tests;

internal sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "terraria-windows-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateDataRoot(string name = "data")
    {
        var root = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(root);
        var database = new byte[512];
        "SQLite format 3\0"u8.CopyTo(database);
        File.WriteAllBytes(System.IO.Path.Combine(root, "Manager.db"), database);
        return root;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
        }
    }
}

internal sealed class TestDataRootPicker : IDataRootPicker
{
    public string? Result { get; set; }
    public int Calls { get; private set; }

    public Task<string?> PickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(Result);
    }
}

internal sealed class TestLegacyProcessProbe : ILegacyProcessProbe
{
    public bool Running { get; set; }
    public bool IsRunning() => Running;
}
