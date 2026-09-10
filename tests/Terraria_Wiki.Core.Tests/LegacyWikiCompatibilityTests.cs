using System.Text.Json;
using System.Security.Cryptography;
using SQLite;
using Terraria_Wiki.Core.Compatibility;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Tests;

public sealed class LegacyWikiCompatibilityTests
{
    [Fact]
    public async Task CheckpointMergesAClosedLegacyWalBeforeSafeMigration()
    {
        using var temp = new TestDirectory();
        var source = temp.File("source.db");
        var target = temp.File("target.db");
        using (var writer = new SQLiteConnection(
            source,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex))
        {
            Assert.Equal("wal", writer.ExecuteScalar<string>("PRAGMA journal_mode = WAL;"));
            writer.Execute("CREATE TABLE Sample (Value TEXT NOT NULL);");
            writer.Execute("INSERT INTO Sample (Value) VALUES ('preserved');");
            Assert.True(File.Exists(source + "-wal"));

            File.Copy(source, target);
            File.Copy(source + "-wal", target + "-wal");
        }

        await LegacyWikiCompatibility.CheckpointDatabaseForMigrationAsync(target);

        Assert.False(File.Exists(target + "-wal"));
        Assert.False(File.Exists(target + "-shm"));
        using var verify = new SQLiteConnection(target, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        Assert.Equal("preserved", verify.ExecuteScalar<string>("SELECT Value FROM Sample;"));
    }

    [Fact]
    public void NormalizesLegacyDatabaseAndPreservesWalShmFamily()
    {
        using var temp = new TestDirectory();
        var dataDirectory = temp.File("data");
        Directory.CreateDirectory(dataDirectory);
        var legacy = Path.Combine(dataDirectory, LegacyWikiCompatibility.LegacyDatabaseName);
        File.WriteAllBytes(legacy, new byte[] { 1 });
        File.WriteAllBytes(legacy + "-wal", new byte[] { 2 });
        File.WriteAllBytes(legacy + "-shm", new byte[] { 3 });

        var result = LegacyWikiCompatibility.NormalizeDatabaseFamily(dataDirectory);

        Assert.True(result.Changed);
        Assert.Equal(Path.Combine(dataDirectory, "data.db"), result.DatabasePath);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(result.DatabasePath));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(result.DatabasePath + "-wal"));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(result.DatabasePath + "-shm"));
        Assert.False(File.Exists(legacy));
        Assert.False(File.Exists(legacy + "-wal"));
        Assert.False(File.Exists(legacy + "-shm"));
    }

    [Fact]
    public void Resumes_an_interrupted_database_family_normalization()
    {
        using var temp = new TestDirectory();
        var dataDirectory = temp.File("data");
        Directory.CreateDirectory(dataDirectory);
        var legacy = Path.Combine(dataDirectory, LegacyWikiCompatibility.LegacyDatabaseName);
        var current = Path.Combine(dataDirectory, LegacyWikiCompatibility.CurrentDatabaseName);
        File.WriteAllBytes(legacy, [1]);
        File.WriteAllBytes(current + "-wal", [2]);
        File.WriteAllBytes(legacy + "-shm", [3]);
        var journal = Path.Combine(dataDirectory, ".wiki-database-family-normalization.json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            Version = 1,
            NormalizationId = "0123456789abcdef0123456789abcdef",
            JournalPath = journal,
            DataDirectory = dataDirectory,
            CompletedMoves = 1,
            Moves = new object[]
            {
                new
                {
                    Role = "wal",
                    SourcePath = legacy + "-wal",
                    DestinationPath = current + "-wal",
                    Length = 1,
                    Sha256 = Hash([2]),
                },
                new
                {
                    Role = "shm",
                    SourcePath = legacy + "-shm",
                    DestinationPath = current + "-shm",
                    Length = 1,
                    Sha256 = Hash([3]),
                },
                new
                {
                    Role = "database",
                    SourcePath = legacy,
                    DestinationPath = current,
                    Length = 1,
                    Sha256 = (string?)null,
                },
            },
        }));

        var result = LegacyWikiCompatibility.NormalizeDatabaseFamily(dataDirectory);

        Assert.True(result.Changed);
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(current + "-wal"));
        Assert.True(File.Exists(current + "-shm"));
        Assert.False(File.Exists(legacy));
        Assert.False(File.Exists(legacy + "-shm"));
    }

    [Fact]
    public void Recovery_journal_rejects_a_sidecar_that_does_not_match_recorded_identity()
    {
        using var temp = new TestDirectory();
        var dataDirectory = temp.File("data");
        Directory.CreateDirectory(dataDirectory);
        var legacy = Path.Combine(dataDirectory, LegacyWikiCompatibility.LegacyDatabaseName);
        var current = Path.Combine(dataDirectory, LegacyWikiCompatibility.CurrentDatabaseName);
        File.WriteAllBytes(legacy, [1]);
        File.WriteAllBytes(current + "-wal", [2]);
        var journal = Path.Combine(dataDirectory, ".wiki-database-family-normalization.json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            Version = 1,
            NormalizationId = "abcdef0123456789abcdef0123456789",
            JournalPath = journal,
            DataDirectory = dataDirectory,
            CompletedMoves = 1,
            Moves = new object[]
            {
                new
                {
                    Role = "wal",
                    SourcePath = legacy + "-wal",
                    DestinationPath = current + "-wal",
                    Length = 1,
                    Sha256 = Hash([9]),
                },
                new
                {
                    Role = "database",
                    SourcePath = legacy,
                    DestinationPath = current,
                    Length = 1,
                    Sha256 = (string?)null,
                },
            },
        }));

        var exception = Assert.Throws<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.NormalizeDatabaseFamily(dataDirectory));

        Assert.Equal("database-family-journal-invalid", exception.Code);
        Assert.True(File.Exists(legacy));
        Assert.Equal([2], File.ReadAllBytes(current + "-wal"));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public void Rejects_current_sidecar_next_to_legacy_main_without_recovery_journal()
    {
        using var temp = new TestDirectory();
        var dataDirectory = temp.File("data");
        Directory.CreateDirectory(dataDirectory);
        var legacy = Path.Combine(dataDirectory, LegacyWikiCompatibility.LegacyDatabaseName);
        File.WriteAllBytes(legacy, [1]);
        File.WriteAllBytes(Path.Combine(dataDirectory, LegacyWikiCompatibility.CurrentDatabaseName) + "-wal", [2]);

        var exception = Assert.Throws<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.NormalizeDatabaseFamily(dataDirectory));

        Assert.Equal("orphan-database-sidecar", exception.Code);
        Assert.True(File.Exists(legacy));
        Assert.False(File.Exists(Path.Combine(dataDirectory, LegacyWikiCompatibility.CurrentDatabaseName)));
    }

    [Fact]
    public void Resumes_normalization_after_the_legacy_directory_was_already_moved()
    {
        using var temp = new TestDirectory();
        var currentDirectory = temp.File("Terraria_Wiki_zh");
        Directory.CreateDirectory(currentDirectory);
        File.WriteAllBytes(Path.Combine(currentDirectory, LegacyWikiCompatibility.LegacyDatabaseName), [1]);

        var result = LegacyWikiCompatibility.MigrateLegacyDataDirectory(temp.Path, "Terraria_Wiki_zh");

        Assert.False(result.DirectoryChanged);
        Assert.True(result.Database.Changed);
        Assert.True(File.Exists(Path.Combine(currentDirectory, LegacyWikiCompatibility.CurrentDatabaseName)));
    }

    [Fact]
    public void RejectsConflictingAndOrphanDatabaseFamiliesWithoutChangingThem()
    {
        using var temp = new TestDirectory();
        var conflictDirectory = temp.File("conflict");
        Directory.CreateDirectory(conflictDirectory);
        var legacy = Path.Combine(conflictDirectory, "Terraria_Wiki.db");
        var current = Path.Combine(conflictDirectory, "data.db");
        File.WriteAllText(legacy, "legacy");
        File.WriteAllText(current, "current");
        var orphanDirectory = temp.File("orphan");
        Directory.CreateDirectory(orphanDirectory);
        File.WriteAllText(Path.Combine(orphanDirectory, "Terraria_Wiki.db-wal"), "wal");

        var conflict = Assert.Throws<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.NormalizeDatabaseFamily(conflictDirectory));
        var orphan = Assert.Throws<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.NormalizeDatabaseFamily(orphanDirectory));

        Assert.Equal("database-family-conflict", conflict.Code);
        Assert.Equal("orphan-database-sidecar", orphan.Code);
        Assert.Equal("legacy", File.ReadAllText(legacy));
        Assert.Equal("current", File.ReadAllText(current));
    }

    [Fact]
    public void MigratesLegacyDirectoryAndRollsDirectoryBackWhenDatabaseIsMissing()
    {
        using var temp = new TestDirectory();
        var oldDirectory = temp.File("Terraria_Wiki");
        Directory.CreateDirectory(oldDirectory);
        File.WriteAllText(Path.Combine(oldDirectory, "failed_pages.txt"), "keep");

        var exception = Assert.Throws<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.MigrateLegacyDataDirectory(temp.Path, "Terraria_Wiki_zh"));

        Assert.Equal("data-directory-migration-failed", exception.Code);
        Assert.True(Directory.Exists(oldDirectory));
        Assert.False(Directory.Exists(temp.File("Terraria_Wiki_zh")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(oldDirectory, "failed_pages.txt")));
    }

    [Fact]
    public void RefusesToMergeLegacyAndCurrentDataDirectories()
    {
        using var temp = new TestDirectory();
        Directory.CreateDirectory(temp.File("Terraria_Wiki"));
        Directory.CreateDirectory(temp.File("Terraria_Wiki_zh"));

        var exception = Assert.Throws<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.MigrateLegacyDataDirectory(temp.Path, "Terraria_Wiki_zh"));

        Assert.Equal("data-directory-conflict", exception.Code);
        Assert.True(Directory.Exists(temp.File("Terraria_Wiki")));
        Assert.True(Directory.Exists(temp.File("Terraria_Wiki_zh")));
    }

    [Fact]
    public async Task AnchorMigrationRunsTransactionallyAndIsIdempotent()
    {
        using var temp = new TestDirectory();
        var database = temp.File("data.db");
        using (var connection = new SQLiteConnection(database))
        {
            connection.CreateTable<WikiPage>();
            connection.Insert(new WikiPage
            {
                Title = "Home",
                Content = "<p><a title=\"Guide\">Guide</a></p>",
                LastModified = DateTime.UnixEpoch,
            });
            connection.Insert(new WikiPage
            {
                Title = "Guide",
                Content = "<p><a title=\"Target#Section\">Target</a><a title=\"\">Empty</a></p>",
                LastModified = DateTime.UnixEpoch,
            });
        }

        var first = await LegacyWikiCompatibility.MigrateAnchorDataAsync(database, "Home");
        var second = await LegacyWikiCompatibility.MigrateAnchorDataAsync(database, "Home");

        using var verify = new SQLiteConnection(database, SQLiteOpenFlags.ReadOnly);
        var home = verify.Find<WikiPage>("Home");
        var guide = verify.Find<WikiPage>("Guide");
        Assert.True(first.Required);
        Assert.Equal(2, first.PagesProcessed);
        Assert.Equal(2, first.PagesChanged);
        Assert.False(second.Required);
        Assert.Contains("data-wiki=\"Guide\"", home.Content, StringComparison.Ordinal);
        Assert.Contains("data-wiki=\"Target#Section\"", guide.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("data-wiki=\"\"", guide.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Safe_anchor_migration_uses_a_validated_copy_and_cleans_swap_artifacts()
    {
        using var temp = new TestDirectory();
        var database = temp.File("data.db");
        var content = new ContentDbService(database);
        await content.Init(seedBook: new WikiBook
        {
            DefaultPageTitle = "Home",
            DefaultPageContent = "placeholder",
        });
        await content.SaveItemAsync(new WikiPage
        {
            Title = "Home",
            Content = "<a title=\"Guide\">Guide</a>",
        });
        await content.CloseConnection();

        var result = await LegacyWikiCompatibility.MigrateAnchorDataSafelyAsync(database, "Home");

        Assert.True(result.Required);
        using var verify = new SQLiteConnection(database, SQLiteOpenFlags.ReadOnly);
        Assert.Contains("data-wiki=\"Guide\"", verify.Find<WikiPage>("Home").Content, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path, "*.anchor-*"));
    }

    [Fact]
    public async Task Safe_anchor_migration_does_not_copy_when_migration_is_not_required()
    {
        using var temp = new TestDirectory();
        var database = temp.File("data.db");
        var content = new ContentDbService(database);
        await content.Init(seedBook: new WikiBook
        {
            DefaultPageTitle = "Home",
            DefaultPageContent = "placeholder",
        });
        await content.SaveItemAsync(new WikiPage
        {
            Title = "Home",
            Content = "<a title=\"Guide\" data-wiki=\"Guide\">Guide</a>",
        });
        await content.CloseConnection();
        var copyCount = 0;

        var result = await LegacyWikiCompatibility.MigrateAnchorDataSafelyCoreAsync(
            database,
            "Home",
            (source, destination) =>
            {
                copyCount++;
                File.Copy(source, destination, overwrite: false);
            });

        Assert.False(result.Required);
        Assert.Equal(0, copyCount);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path, "*.anchor-*"));
        using var verify = new SQLiteConnection(database, SQLiteOpenFlags.ReadOnly);
        Assert.Contains("data-wiki=\"Guide\"", verify.Find<WikiPage>("Home").Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepared_anchor_migration_leaves_a_healthy_wal_database_uncheckpointed()
    {
        using var temp = new TestDirectory();
        var database = temp.File("data.db");
        var content = new ContentDbService(database);
        await content.Init(seedBook: new WikiBook
        {
            DefaultPageTitle = "Home",
            DefaultPageContent = "placeholder",
        });
        await content.SaveItemAsync(new WikiPage
        {
            Title = "Home",
            Content = "<a title=\"Guide\" data-wiki=\"Guide\">Guide</a>",
        });
        Assert.True(File.Exists(database + "-wal"));
        var checkpointCount = 0;
        var copyCount = 0;

        var result = await LegacyWikiCompatibility.PrepareAndMigrateAnchorDataSafelyCoreAsync(
            database,
            "Home",
            (_, _) =>
            {
                checkpointCount++;
                return Task.CompletedTask;
            },
            (_, _) => copyCount++);

        Assert.False(result.Required);
        Assert.Equal(0, checkpointCount);
        Assert.Equal(0, copyCount);
        Assert.True(File.Exists(database + "-wal"));
        await content.CloseConnection();
    }

    [Fact]
    public async Task Prepared_anchor_migration_reads_latest_wal_data_then_checkpoints_and_migrates()
    {
        using var temp = new TestDirectory();
        var source = temp.File("source.db");
        var target = temp.File("target.db");
        var content = new ContentDbService(source);
        await content.Init(seedBook: new WikiBook
        {
            DefaultPageTitle = "Home",
            DefaultPageContent = "placeholder",
        });
        await content.SaveItemAsync(new WikiPage
        {
            Title = "Home",
            Content = "<a title=\"Guide\">Guide</a>",
            LastModified = DateTime.UnixEpoch,
        });
        Assert.True(File.Exists(source + "-wal"));
        File.Copy(source, target);
        File.Copy(source + "-wal", target + "-wal");
        await content.CloseConnection();

        var result = await LegacyWikiCompatibility.PrepareAndMigrateAnchorDataSafelyAsync(target, "Home");

        Assert.True(result.Required);
        Assert.False(File.Exists(target + "-wal"));
        Assert.False(File.Exists(target + "-shm"));
        using var verify = new SQLiteConnection(target, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
        Assert.Contains("data-wiki=\"Guide\"", verify.Find<WikiPage>("Home").Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepared_anchor_migration_fails_closed_when_wal_checkpoint_is_busy()
    {
        using var temp = new TestDirectory();
        var database = temp.File("data.db");
        using var writer = new SQLiteConnection(
            database,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
        Assert.Equal("wal", writer.ExecuteScalar<string>("PRAGMA journal_mode = WAL;"));
        writer.CreateTable<WikiPage>();
        writer.Insert(new WikiPage
        {
            Title = "Home",
            Content = "<a title=\"Guide\">Guide</a>",
            LastModified = DateTime.UnixEpoch,
        });
        writer.BeginTransaction();
        writer.Insert(new WikiPage
        {
            Title = "Uncommitted",
            Content = "hold checkpoint",
            LastModified = DateTime.UnixEpoch,
        });

        var error = await Assert.ThrowsAsync<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.PrepareAndMigrateAnchorDataSafelyAsync(database, "Home"));

        Assert.Equal("database-checkpoint-failed", error.Code);
        Assert.True(File.Exists(database));
        Assert.True(File.Exists(database + "-wal"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path, "*.anchor-*"));
        writer.Rollback();
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    public async Task Safe_anchor_migration_rejects_any_database_sidecar(string suffix)
    {
        using var temp = new TestDirectory();
        var database = temp.File("data.db");
        File.WriteAllText(database, "original");
        File.WriteAllBytes(database + suffix, []);

        var error = await Assert.ThrowsAsync<WikiCompatibilityException>(() =>
            LegacyWikiCompatibility.MigrateAnchorDataSafelyAsync(database, "Home"));

        Assert.Equal("database-not-checkpointed", error.Code);
        Assert.Equal("original", File.ReadAllText(database));
        Assert.True(File.Exists(database + suffix));
    }

    [Fact]
    public void Anchor_recovery_restores_original_when_target_move_preceded_journal_update()
    {
        using var temp = new TestDirectory();
        var target = temp.File("data.db");
        const string migrationId = "0123456789abcdef0123456789abcdef";
        var staging = target + $".anchor-staging-{migrationId}";
        var backup = target + $".anchor-backup-{migrationId}";
        var journal = target + ".anchor-migration.json";
        File.WriteAllText(target, "original");
        File.Move(target, backup);
        File.WriteAllText(target, "migrated");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            JournalPath = journal,
            TargetPath = target,
            StagingPath = staging,
            BackupPath = backup,
            MigrationId = migrationId,
            Phase = 1,
        }));

        Assert.True(LegacyWikiCompatibility.RecoverPendingAnchorMigration(target));

        Assert.Equal("original", File.ReadAllText(target));
        Assert.False(File.Exists(backup));
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public void Anchor_recovery_rejects_an_unknown_phase_without_touching_files()
    {
        using var temp = new TestDirectory();
        var target = temp.File("data.db");
        const string migrationId = "ffeeddccbbaa99887766554433221100";
        var staging = target + $".anchor-staging-{migrationId}";
        var backup = target + $".anchor-backup-{migrationId}";
        var journal = target + ".anchor-migration.json";
        File.WriteAllText(target, "original");
        File.WriteAllText(staging, "staged");
        File.WriteAllText(backup, "backup");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            JournalPath = journal,
            TargetPath = target,
            StagingPath = staging,
            BackupPath = backup,
            MigrationId = migrationId,
            Phase = 999,
        }));

        var error = Assert.Throws<WikiCompatibilityException>(
            () => LegacyWikiCompatibility.RecoverPendingAnchorMigration(target));

        Assert.Equal("anchor-migration-journal-invalid", error.Code);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Equal("staged", File.ReadAllText(staging));
        Assert.Equal("backup", File.ReadAllText(backup));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public void Anchor_recovery_rejects_a_journal_without_an_explicit_phase()
    {
        using var temp = new TestDirectory();
        var target = temp.File("data.db");
        const string migrationId = "aabbccddeeff00112233445566778899";
        var staging = target + $".anchor-staging-{migrationId}";
        var backup = target + $".anchor-backup-{migrationId}";
        var journal = target + ".anchor-migration.json";
        File.WriteAllText(target, "original");
        File.WriteAllText(staging, "staged");
        File.WriteAllText(backup, "backup");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            JournalPath = journal,
            TargetPath = target,
            StagingPath = staging,
            BackupPath = backup,
            MigrationId = migrationId,
        }));

        var error = Assert.Throws<WikiCompatibilityException>(
            () => LegacyWikiCompatibility.RecoverPendingAnchorMigration(target));

        Assert.Equal("anchor-migration-journal-invalid", error.Code);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.True(File.Exists(staging));
        Assert.True(File.Exists(backup));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public void Committed_anchor_recovery_cleans_orphaned_staging_sidecars_before_removing_journal()
    {
        using var temp = new TestDirectory();
        var target = temp.File("data.db");
        const string migrationId = "99887766554433221100ffeeddccbbaa";
        var staging = target + $".anchor-staging-{migrationId}";
        var backup = target + $".anchor-backup-{migrationId}";
        var journal = target + ".anchor-migration.json";
        File.WriteAllText(target, "migrated");
        File.WriteAllBytes(staging + "-wal", []);
        File.WriteAllText(backup, "original");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            JournalPath = journal,
            TargetPath = target,
            StagingPath = staging,
            BackupPath = backup,
            MigrationId = migrationId,
            Phase = 2,
        }));

        Assert.True(LegacyWikiCompatibility.RecoverPendingAnchorMigration(target));

        Assert.Equal("migrated", File.ReadAllText(target));
        Assert.False(File.Exists(staging + "-wal"));
        Assert.False(File.Exists(backup));
        Assert.False(File.Exists(journal));
    }

    [Theory]
    [InlineData("0.3.0.3", true)]
    [InlineData("0.4", false)]
    [InlineData("1.0", false)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void LegacyPackageConfirmationMatchesExistingVersionRule(string? version, bool expected)
    {
        Assert.Equal(expected, LegacyWikiCompatibility.RequiresLegacyPackageConfirmation(version));
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
