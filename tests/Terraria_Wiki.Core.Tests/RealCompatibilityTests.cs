using System.Security.Cryptography;
using Terraria_Wiki.Core.Compatibility;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Core.Tests;

/// <summary>
/// Reproducible acceptance hooks for explicit, read-only Phase 00 inputs.
/// Normal fixture runs remain self-contained; the Phase 01 acceptance command
/// supplies these environment variables and records the resulting evidence.
/// </summary>
public sealed class RealCompatibilityTests
{
    [Fact]
    public async Task Supplied_golden_package_is_extracted_by_the_production_reader_without_source_changes()
    {
        var packagePath = Environment.GetEnvironmentVariable("PHASE01_GOLDEN_PACKAGE");
        if (string.IsNullOrWhiteSpace(packagePath)) return;

        packagePath = Path.GetFullPath(packagePath);
        var before = await ComputeSha256Async(packagePath);
        var staging = Path.Combine(Path.GetTempPath(), "phase01-core-golden-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inspection = await WikiPackageReader.InspectAsync(packagePath);
            var result = await WikiPackageReader.ExtractAsync(
                packagePath,
                staging,
                inspection.InspectionToken);

            Assert.Equal(3_584_607_183L, inspection.PackageBytes);
            Assert.Equal(427, inspection.MetadataBytes);
            Assert.Equal(3, inspection.Metadata.Files!.Count);
            Assert.Equal(3_584_606_744L, inspection.PayloadBytes);
            Assert.Equal(3_583_139_840L, result.Files.Single(file => file.RelativePath == "Terraria_Wiki.db").Size);
            Assert.All(result.Files, file => Assert.True(File.Exists(file.FullPath)));
            var normalized = LegacyWikiCompatibility.NormalizeDatabaseFamily(staging);
            WikiContentSchemaValidator.Validate(normalized.DatabasePath);
            Assert.Equal(
                "d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa",
                before);
            Assert.Equal(before, await ComputeSha256Async(packagePath));
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Supplied_real_snapshot_is_normalized_initialized_and_queried_by_the_new_core()
    {
        var databasePath = Environment.GetEnvironmentVariable("PHASE01_REAL_DATABASE_COPY");
        if (string.IsNullOrWhiteSpace(databasePath)) return;

        databasePath = Path.GetFullPath(databasePath);
        var normalized = LegacyWikiCompatibility.NormalizeDatabaseFamily(Path.GetDirectoryName(databasePath)!);
        var database = new ContentDbService(normalized.DatabasePath);
        try
        {
            await database.Init();

            Assert.True(await database.GetCountAsync<WikiPage>() > 0);
            Assert.True(await database.GetCountAsync<WikiRedirect>() > 0);
            Assert.True(await database.GetCountAsync<WikiAsset>() > 0);
            Assert.NotNull(await database.GetItemAsync<WikiPage>("1.0"));
            Assert.NotEmpty(await database.SearchAsync("镐", 10));

            var backup = Path.Combine(Path.GetDirectoryName(databasePath)!, "phase01-core-backup.db");
            await database.BackupExistingAsync(backup);
            Assert.True(new FileInfo(backup).Length > 1_000_000_000);
        }
        finally
        {
            await database.CloseConnection();
        }
    }

    [Fact]
    public async Task Supplied_output_path_receives_a_core_writer_package_for_the_independent_oracle()
    {
        var packagePath = Environment.GetEnvironmentVariable("PHASE01_CORE_WRITER_OUTPUT");
        if (string.IsNullOrWhiteSpace(packagePath)) return;

        packagePath = Path.GetFullPath(packagePath);
        var root = Path.GetDirectoryName(packagePath)!;
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "writer-first.txt");
        var second = Path.Combine(root, "writer-second.bin");
        await File.WriteAllTextAsync(first, "Phase 01 production writer fixture\n");
        await File.WriteAllBytesAsync(second, Enumerable.Range(0, 1024).Select(value => (byte)value).ToArray());

        var result = await WikiPackageWriter.WriteAsync(
            packagePath,
            new WikiPackageDescriptor(1, "Phase 01 Writer", true, true, DateTime.UnixEpoch, "1.0.0"),
            [
                new WikiPackageSourceFile("notes/fixture.txt", first),
                new WikiPackageSourceFile("data.bin", second),
            ]);

        Assert.Equal(2, result.Metadata.Files!.Count);
        Assert.True(result.PackageBytes > 1_024);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }
}
