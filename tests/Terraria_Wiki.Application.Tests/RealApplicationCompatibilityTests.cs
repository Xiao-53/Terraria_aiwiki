using System.Security.Cryptography;
using Terraria_Wiki.Core.Packages;

namespace Terraria_Wiki.Application.Tests;

public sealed class RealApplicationCompatibilityTests
{
    [Fact]
    public async Task SuppliedGoldenIsInspectedAndItsVerifiedSnapshotSupportsApplicationSearchAndRead()
    {
        var packagePath = Environment.GetEnvironmentVariable("PHASE03_GOLDEN_PACKAGE");
        if (string.IsNullOrWhiteSpace(packagePath))
            return;

        packagePath = Path.GetFullPath(packagePath);
        var before = await ComputeSha256Async(packagePath);
        WikiPackageInspection inspection;
        await using (var inspectFixture = await ApplicationFixture.CreateAsync())
            inspection = await inspectFixture.Application.Data.InspectPackageAsync(packagePath);

        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            "phase03-application-real-" + Guid.NewGuid().ToString("N"));
        WikiApplication? application = null;
        try
        {
            Directory.CreateDirectory(dataRoot);
            var legacyDirectory = Path.Combine(dataRoot, "Terraria_Wiki");
            await WikiPackageReader.ExtractAsync(
                packagePath,
                legacyDirectory,
                inspection.InspectionToken);

            application = new WikiApplication(
                dataRoot,
                new TestSettingsStore { ActiveWikiId = 1 },
                new TestConsumerCoordinator(),
                new RejectingTransport());
            await application.ActiveWiki.InitializeAsync();
            var search = await application.Query.SearchAsync("镐", 10);
            var read = await application.Query.ReadAsync("1.0");

            Assert.Equal(3_584_607_183L, inspection.PackageBytes);
            Assert.Equal(427, inspection.MetadataBytes);
            Assert.Equal(3, inspection.Metadata.Files!.Count);
            Assert.NotEmpty(search);
            Assert.True(read.IsFound);
            Assert.Equal("1.0", read.Title);
            Assert.Equal(
                "d2162d81a9fe2e13aea33228d31135268197f3cd1f54caae41e38df306ea56fa",
                before);
            Assert.Equal(before, await ComputeSha256Async(packagePath));
        }
        finally
        {
            if (application is not null)
            {
                try
                {
                    await application.DisposeAsync();
                }
                catch
                {
                }
            }
            try
            {
                if (Directory.Exists(dataRoot))
                    Directory.Delete(dataRoot, recursive: true);
            }
            catch
            {
            }
        }
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
