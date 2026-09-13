using System.Net;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application.Tests;

public sealed class WikiDataUseCaseTests
{
    private static readonly ApplicationPreferences OperationPreferences = new()
    {
        PageConcurrency = 1,
        ResourceConcurrency = 1,
        RetryAttempts = 0,
    };

    [Fact]
    public async Task DownloadResourceDownloadAndUpdateUseCoreLifecycleWithConfiguredOptions()
    {
        var transport = new LifecycleFixtureTransport();
        await using var fixture = await ApplicationFixture.CreateAsync(OperationPreferences, transport: transport);
        await ConfigureFixtureBookAsync(fixture);

        var downloaded = await fixture.Application.Data.DownloadAsync(includeResources: false);
        var resources = await fixture.Application.Data.DownloadResourcesAsync();
        transport.PageTouched = "2026-02-01T00:00:00Z";
        transport.ResourceStatus = HttpStatusCode.NotModified;
        var updated = await fixture.Application.Data.UpdateAsync(includeResources: true);

        Assert.Equal(1, downloaded.Pages);
        Assert.Equal(1, downloaded.Redirects);
        Assert.Equal(0, downloaded.Resources);
        Assert.Equal(1, resources.Resources);
        Assert.True(updated.Changed);
        Assert.Equal(1, updated.Pages);
        Assert.Equal(0, updated.Resources);
        Assert.Equal("Sword", (await fixture.State.ContentDatabase.GetItemAsync<WikiRedirect>("Blade"))?.ToTarget);
        Assert.Equal([1, 2, 3], (await fixture.State.ContentDatabase.GetItemAsync<WikiAsset>("Sword.png"))?.Data);
        Assert.NotNull(transport.LastIfModifiedSince);
        var active = await fixture.Application.ActiveWiki.GetActiveAsync();
        Assert.True(active.IsPageDownloaded);
        Assert.True(active.IsResourceDownloaded);
    }

    [Fact]
    public async Task FailedDownloadCanBeRetriedAndFailureFilesCanBeCleared()
    {
        var transport = new LifecycleFixtureTransport { ParseFailuresRemaining = 1 };
        await using var fixture = await ApplicationFixture.CreateAsync(OperationPreferences, transport: transport);
        await ConfigureFixtureBookAsync(fixture);

        var failed = await fixture.Application.Data.DownloadAsync(includeResources: false);
        Assert.Equal(1, failed.Failed);
        Assert.True(await fixture.Application.Data.HasFailuresAsync());

        var retried = await fixture.Application.Data.RetryFailuresAsync();
        Assert.Equal(1, retried.Pages);
        Assert.False(await fixture.Application.Data.HasFailuresAsync());

        var paths = WikiDataPaths.Create(fixture.Root, fixture.State.ActiveBook!);
        await File.WriteAllTextAsync(paths.FailedPageListPath, "Broken|2026-01-01T00:00:00Z");
        Assert.True(await fixture.Application.Data.HasFailuresAsync());
        await fixture.Application.Data.ClearFailuresAsync();
        Assert.False(await fixture.Application.Data.HasFailuresAsync());
    }

    [Fact]
    public async Task StatisticsVacuumAndResourceDeletionStayOnActiveWiki()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.State.ContentDatabase.SaveItemAsync(new WikiPage
        {
            Title = "Sword",
            Content = "content",
            LastModified = DateTime.UtcNow,
        });
        await fixture.State.ContentDatabase.SaveItemAsync(new WikiAsset
        {
            FileName = "Sword.png",
            Data = [1, 2, 3],
            MimeType = "image/png",
        });

        var statistics = await fixture.Application.Data.GetStatisticsAsync();
        await fixture.Application.Data.VacuumAsync();
        var deleted = await fixture.Application.Data.DeleteResourcesAsync();

        Assert.Equal(2, statistics.PageCount);
        Assert.Equal(1, statistics.ResourceCount);
        Assert.True(statistics.DataSize > 0);
        Assert.True(deleted.Changed);
        Assert.Null(await fixture.Application.Query.ReadAssetAsync("Sword.png"));
        Assert.Equal(0, (await fixture.Application.ActiveWiki.GetActiveAsync()).ResourceCount);
    }

    [Fact]
    public async Task DestructiveDatabaseDeletePausesConsumersAndRecreatesActiveDefault()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.State.ContentDatabase.SaveItemAsync(new WikiPage
        {
            Title = "Temporary Page",
            Content = "temporary",
            LastModified = DateTime.UtcNow,
        });
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;

        var result = await fixture.Application.Data.DeleteWikiDataAsync();

        Assert.True(result.Changed);
        Assert.Equal(pauses + 1, fixture.Consumers.PauseCount);
        Assert.Equal(resumes + 1, fixture.Consumers.ResumeCount);
        Assert.False((await fixture.Application.Query.ReadAsync("Temporary Page")).IsFound);
        Assert.True((await fixture.Application.Query.ReadAsync("Terraria Wiki")).IsFound);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
    }

    [Fact]
    public async Task ActivePackageExportInspectAndImportUseTokenAndConsumerLease()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.State.ContentDatabase.SaveItemAsync(Page("Packaged Page"));
        var packagePath = PackagePath(fixture, "active.pkg");

        var written = await fixture.Application.Data.ExportPackageAsync(1, packagePath, "1.0.0");
        var inspection = await fixture.Application.Data.InspectPackageAsync(packagePath);
        await fixture.State.ContentDatabase.SaveItemAsync(Page("After Export"));
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;

        var imported = await fixture.Application.Data.ImportPackageAsync(
            packagePath,
            inspection.InspectionToken);

        Assert.Equal(packagePath, written.PackagePath);
        Assert.Equal(1, imported.WikiBook.Id);
        Assert.Equal(inspection.InspectionToken, imported.Inspection.InspectionToken);
        Assert.Equal(pauses + 1, fixture.Consumers.PauseCount);
        Assert.Equal(resumes + 1, fixture.Consumers.ResumeCount);
        Assert.True((await fixture.Application.Query.ReadAsync("Packaged Page")).IsFound);
        Assert.False((await fixture.Application.Query.ReadAsync("After Export")).IsFound);
    }

    [Fact]
    public async Task ActiveDatabaseExportInspectAndImportUseTokenAndConsumerLease()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.State.ContentDatabase.SaveItemAsync(Page("Database Snapshot Page"));
        var databasePath = PackagePath(fixture, "active.db");

        var written = await fixture.Application.Data.ExportDatabaseAsync(databasePath);
        var inspection = await fixture.Application.Data.InspectDatabaseAsync(databasePath);
        await fixture.State.ContentDatabase.SaveItemAsync(Page("After Database Export"));
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;

        var imported = await fixture.Application.Data.ImportDatabaseAsync(
            databasePath,
            inspection.InspectionToken);

        Assert.Equal(databasePath, written.DatabasePath);
        Assert.Equal(1, imported.WikiBook.Id);
        Assert.Equal(inspection.InspectionToken, imported.Inspection.InspectionToken);
        Assert.Equal(pauses + 1, fixture.Consumers.PauseCount);
        Assert.Equal(resumes + 1, fixture.Consumers.ResumeCount);
        Assert.True((await fixture.Application.Query.ReadAsync("Database Snapshot Page")).IsFound);
        Assert.False((await fixture.Application.Query.ReadAsync("After Database Export")).IsFound);
        Assert.True(imported.WikiBook.PageCount > 1);
    }

    [Fact]
    public async Task TokenMismatchAndLegacyConfirmationAreExplicitAndAlwaysResumeActiveConsumers()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        var packagePath = PackagePath(fixture, "legacy.pkg");
        await fixture.Application.Data.ExportPackageAsync(1, packagePath, "0.3.0");
        var inspection = await fixture.Application.Data.InspectPackageAsync(packagePath);
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;

        var tokenError = await Assert.ThrowsAsync<WikiPackageException>(() =>
            fixture.Application.Data.ImportPackageAsync(packagePath, "wrong-token"));
        var legacyError = await Assert.ThrowsAsync<WikiPackageException>(() =>
            fixture.Application.Data.ImportPackageAsync(packagePath, inspection.InspectionToken));
        var imported = await fixture.Application.Data.ImportPackageAsync(
            packagePath,
            inspection.InspectionToken,
            allowLegacyPackage: true);

        Assert.Equal("inspection-token-mismatch", tokenError.Code);
        Assert.Equal("legacy-confirmation-required", legacyError.Code);
        Assert.Equal("0.3.0", imported.Inspection.Metadata.AppVersion);
        Assert.Equal(pauses + 3, fixture.Consumers.PauseCount);
        Assert.Equal(resumes + 3, fixture.Consumers.ResumeCount);
    }

    [Fact]
    public async Task NonActiveExportAndImportDoNotPauseActiveConsumers()
    {
        await using var fixture = await ApplicationFixture.CreateAsync(activeWikiId: 1);
        await fixture.Application.ActiveWiki.SwitchAsync(2);
        await fixture.State.ContentDatabase.SaveItemAsync(Page("Calamity Package Page"));
        await fixture.Application.ActiveWiki.SwitchAsync(1);
        var packagePath = PackagePath(fixture, "non-active.pkg");
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;

        await fixture.Application.Data.ExportPackageAsync(2, packagePath, "1.0.0");
        var inspection = await fixture.Application.Data.InspectPackageAsync(packagePath);
        var imported = await fixture.Application.Data.ImportPackageAsync(
            packagePath,
            inspection.InspectionToken);

        Assert.Equal(2, imported.WikiBook.Id);
        Assert.Equal(pauses, fixture.Consumers.PauseCount);
        Assert.Equal(resumes, fixture.Consumers.ResumeCount);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
        Assert.True((await fixture.Application.Query.ReadAsync("Terraria Wiki")).IsFound);
    }

    [Fact]
    public async Task InvalidPackageDatabaseLeavesActiveSourceUsableAndResumesConsumers()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        await fixture.State.ContentDatabase.SaveItemAsync(Page("Still Available"));
        var invalidDatabase = Path.Combine(fixture.Root, "invalid-data.db");
        await File.WriteAllBytesAsync(invalidDatabase, [1, 2, 3, 4]);
        var packagePath = PackagePath(fixture, "invalid.pkg");
        await WikiPackageWriter.WriteAsync(
            packagePath,
            new WikiPackageDescriptor(1, "Invalid", false, false, DateTime.UtcNow, "1.0.0"),
            [new WikiPackageSourceFile("data.db", invalidDatabase)]);
        var inspection = await fixture.Application.Data.InspectPackageAsync(packagePath);
        var pauses = fixture.Consumers.PauseCount;
        var resumes = fixture.Consumers.ResumeCount;

        await Assert.ThrowsAsync<WikiPackageException>(() => fixture.Application.Data.ImportPackageAsync(
            packagePath,
            inspection.InspectionToken));

        Assert.Equal(pauses + 1, fixture.Consumers.PauseCount);
        Assert.Equal(resumes + 1, fixture.Consumers.ResumeCount);
        Assert.True((await fixture.Application.Query.ReadAsync("Still Available")).IsFound);
        Assert.Equal(1, (await fixture.Application.ActiveWiki.GetActiveAsync()).Id);
    }

    [Fact]
    public async Task SharedGateAndCancellationErrorsArePreserved()
    {
        await using var fixture = await ApplicationFixture.CreateAsync();
        using var occupied = await fixture.State.OperationGate.TryAcquireAsync();

        var busy = await Assert.ThrowsAsync<WikiOperationException>(
            () => fixture.Application.Data.VacuumAsync());
        Assert.Equal("operation-in-progress", busy.Code);

        occupied!.Dispose();
        var packagePath = PackagePath(fixture, "cancel.pkg");
        await fixture.Application.Data.ExportPackageAsync(1, packagePath, "1.0.0");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.Data.DownloadAsync(false, cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Application.Data.InspectPackageAsync(packagePath, cancellationToken: cancellation.Token));
    }

    private static async Task ConfigureFixtureBookAsync(ApplicationFixture fixture)
    {
        var active = fixture.State.ActiveBook!;
        active.ApiBaseUrl = "https://wiki.test/api.php";
        active.BaseUrl = "https://wiki.test";
        active.RedirectListUrl = "/redirects";
        active.MainNamespace = 0;
        active.AdditionalNamespaces = string.Empty;
        await fixture.State.ManagerDatabase.SaveItemAsync(active);
    }

    private static WikiPage Page(string title) => new()
    {
        Title = title,
        Content = $"<p>{title}</p>",
        LastModified = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc),
    };

    private static string PackagePath(ApplicationFixture fixture, string fileName)
    {
        var directory = Path.Combine(fixture.Root, "exports");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
