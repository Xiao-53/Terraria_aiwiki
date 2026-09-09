using Phase00.Probes;
using SQLite;

namespace Phase00.Probes.Tests;

public sealed class SqliteProbeTests
{
    [Fact]
    public async Task FixtureUsesSqliteNetFts5AndLeavesSourceUnchanged()
    {
        using var temp = new TestDirectory();
        var db = System.IO.Path.Combine(temp.Path, "fixture.db");
        await SqliteProbe.CreateFixtureAsync(db);

        var result = await SqliteProbe.ProbeAsync(db, System.IO.Path.Combine(temp.Path, "probe"));

        Assert.True(result.SourceUnchanged);
        Assert.Equal("3.53.3", result.ReadOnly.SqliteVersion);
        Assert.True(result.ReadOnly.Fts5Enabled);
        Assert.True(result.ReadOnly.FtsTrigram);
        Assert.Contains("WikiPage", result.ReadOnly.Tables);
        Assert.Contains(result.ReadOnly.Pages, page => page.Title == "Guide");
        Assert.Contains(result.ReadOnly.Redirects, redirect => redirect.FromName == "Alias" && redirect.ToTarget == "Redirected#Section");
        Assert.Contains(result.ReadOnly.Assets, asset => asset.FileName == "fixture.png" && asset.DataBytes > 0);
        Assert.Contains(result.ReadOnly.SearchResults, hit => hit.Title == "Guide");
        Assert.Single(result.ReadOnly.PaginationSample);
        Assert.True(result.Writable.CrudInsert && result.Writable.CrudUpdate && result.Writable.CrudDelete);
        Assert.True(result.Writable.BackupBytes > 0);
        Assert.True(result.Writable.BackupReadable && result.Writable.BackupPreservesSourceTables);
        Assert.Contains("WikiPage", result.Writable.BackupTables);
        Assert.False(result.Snapshot.IncludesWal);
        Assert.False(result.Snapshot.IncludesShm);
    }

    [Fact]
    public async Task CapturesActiveWalAndShmBeforeCheckpointingOnlyTheSnapshot()
    {
        using var temp = new TestDirectory();
        var db = System.IO.Path.Combine(temp.Path, "wal.db");
        await SqliteProbe.CreateFixtureAsync(db);

        using var writer = new SQLiteConnection(db, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
        Assert.Equal("wal", writer.ExecuteScalar<string>("PRAGMA journal_mode=WAL"));
        writer.Execute(
            "INSERT INTO WikiPage (Title, Content, LastModified) VALUES (?, ?, ?)",
            "WAL page",
            "<p>Written while the source WAL is active.</p>",
            "2026-08-23T00:00:00Z");
        Assert.True(File.Exists(db + "-wal"));
        Assert.True(File.Exists(db + "-shm"));

        var result = await SqliteProbe.ProbeAsync(db, System.IO.Path.Combine(temp.Path, "probe"));

        Assert.True(result.Success);
        Assert.True(result.Snapshot.IncludesWal);
        Assert.True(result.Snapshot.IncludesShm);
        Assert.All(result.Snapshot.SourceFiles, source => Assert.True(source.Unchanged));
        Assert.Contains(result.Snapshot.SourceFiles, source => source.Role == "wal" && source.PresentBefore && source.PresentAfter);
        Assert.Contains(result.Snapshot.SourceFiles, source => source.Role == "shm" && source.PresentBefore && source.PresentAfter);
        Assert.True(result.Snapshot.Checkpoint?.Completed);
        Assert.Contains(result.ReadOnly.Pages, page => page.Title == "WAL page");
    }

    [Fact]
    public async Task ManagerBackupUsesObservedTablesInsteadOfWikiPageAssumption()
    {
        using var temp = new TestDirectory();
        var db = System.IO.Path.Combine(temp.Path, "Manager.db");
        using (var connection = new SQLiteConnection(db, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex))
        {
            connection.Execute("CREATE TABLE WikiBook (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL, DataFolder TEXT NULL)");
            connection.Execute("INSERT INTO WikiBook (Id, Title, DataFolder) VALUES (?, ?, ?)", 1, "Terraria Wiki", "Terraria_Wiki_zh");
        }

        var result = await SqliteProbe.ProbeAsync(db, System.IO.Path.Combine(temp.Path, "probe"));

        Assert.True(result.Success);
        Assert.Contains("WikiBook", result.ReadOnly.Tables);
        Assert.Empty(result.ReadOnly.Pages);
        Assert.Contains("WikiBook", result.Writable.SourceTables);
        Assert.Contains("WikiBook", result.Writable.BackupTables);
        Assert.True(result.Writable.BackupPreservesSourceTables);
    }

    [Fact]
    public async Task SourceHashAndSnapshotEvidenceRemainDistinctAfterSourceChanges()
    {
        using var temp = new TestDirectory();
        var db = System.IO.Path.Combine(temp.Path, "content.db");
        await SqliteProbe.CreateFixtureAsync(db);

        var result = await SqliteProbe.ProbeAsync(db, System.IO.Path.Combine(temp.Path, "probe"));
        var recordedSourceHash = result.SourceSha256After;
        var recordedSnapshotHash = result.Snapshot.SourceFiles.Single(file => file.Role == "database").Sha256After;

        using (var connection = new SQLiteConnection(db, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex))
            connection.Execute("INSERT INTO WikiPage (Title, Content, LastModified) VALUES (?, ?, ?)", "Changed after probe", "x", "2026-08-23T00:00:00Z");

        var changedSourceHash = await SqliteProbe.ComputeSha256Async(db);
        Assert.NotEqual(recordedSourceHash, changedSourceHash);
        Assert.Equal(recordedSourceHash, recordedSnapshotHash);
        Assert.Equal(recordedSnapshotHash,
            await SqliteProbe.ComputeSha256Async(result.Snapshot.Files.Single(file => file.Role == "database").Path));
    }

    [Fact]
    public async Task ReadOnlyConnectionCanQueryPageRedirectAndAssetWithoutMutatingHash()
    {
        using var temp = new TestDirectory();
        var db = System.IO.Path.Combine(temp.Path, "fixture.db");
        await SqliteProbe.CreateFixtureAsync(db);
        var before = await SqliteProbe.ComputeSha256Async(db);
        using (var connection = new SQLiteConnection(db, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex))
        {
            Assert.Equal("Redirected", connection.ExecuteScalar<string>("SELECT Title FROM WikiPage WHERE Title='Redirected'"));
            Assert.Equal("Redirected#Section", connection.ExecuteScalar<string>("SELECT ToTarget FROM WikiRedirect WHERE FromName='Alias'"));
            Assert.True(connection.ExecuteScalar<long>("SELECT length(Data) FROM WikiAsset WHERE FileName='fixture.png'") > 0);
        }
        Assert.Equal(before, await SqliteProbe.ComputeSha256Async(db));
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phase00-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
