using Terraria_Wiki.Application.Internal;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application;

public sealed class WikiDataUseCases
{
    private readonly ApplicationState _state;

    internal WikiDataUseCases(ApplicationState state) => _state = state;

    public async Task<WikiBookSnapshot> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        return await RunCoordinatedAsync(async active =>
        {
            using var operation = await EnterAsync(cancellationToken);
            var refreshed = await _state.Lifecycle.RefreshBookStatisticsAsync(active, cancellationToken);
            _state.ActiveBook = refreshed;
            return ApplicationMapper.ToSnapshot(refreshed);
        }, cancellationToken);
    }

    public Task<bool> HasFailuresAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = _state.RequireActiveBook();
        return Task.FromResult(_state.Lifecycle.HasFailures(Paths(active)));
    }

    public async Task<WikiOperationSummary> DownloadAsync(
        bool includeResources,
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunCoordinatedAsync(async active =>
        {
            var result = await _state.Lifecycle.DownloadAsync(
                active,
                Paths(active),
                includeResources,
                Options(),
                progress,
                cancellationToken);
            await _state.RefreshActiveBookAsync(cancellationToken);
            return result;
        }, cancellationToken);
    }

    public async Task<WikiOperationSummary> DownloadResourcesAsync(
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunCoordinatedAsync(async active =>
        {
            var result = await _state.Lifecycle.DownloadResourcesAsync(
                active,
                Paths(active),
                Options(),
                progress,
                cancellationToken);
            await _state.RefreshActiveBookAsync(cancellationToken);
            return result;
        }, cancellationToken);
    }

    public async Task<WikiOperationSummary> UpdateAsync(
        bool includeResources,
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunCoordinatedAsync(async active =>
        {
            var result = await _state.Lifecycle.UpdateAsync(
                active,
                Paths(active),
                includeResources,
                Options(),
                progress,
                cancellationToken);
            await _state.RefreshActiveBookAsync(cancellationToken);
            return result;
        }, cancellationToken);
    }

    public async Task<WikiOperationSummary> RetryFailuresAsync(
        IProgress<WikiProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await RunCoordinatedAsync(async active =>
        {
            var result = await _state.Lifecycle.RetryFailuresAsync(
                active,
                Paths(active),
                Options(),
                progress,
                cancellationToken);
            await _state.RefreshActiveBookAsync(cancellationToken);
            return result;
        }, cancellationToken);
    }

    public async Task ClearFailuresAsync(CancellationToken cancellationToken = default)
    {
        await RunCoordinatedAsync(async active =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _state.Lifecycle.ClearFailuresAsync(Paths(active));
            return true;
        }, cancellationToken);
    }

    public async Task VacuumAsync(CancellationToken cancellationToken = default)
    {
        await RunCoordinatedAsync(async _ =>
        {
            await _state.Lifecycle.VacuumAsync(cancellationToken);
            await _state.RefreshActiveBookAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<WikiOperationSummary> DeleteResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        return await RunCoordinatedAsync(async active =>
        {
            var result = await _state.Lifecycle.DeleteResourcesAsync(active, cancellationToken);
            await _state.RefreshActiveBookAsync(cancellationToken);
            return result;
        }, cancellationToken);
    }

    public async Task<WikiOperationSummary> DeleteWikiDataAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            var active = _state.RequireActiveBook();
            await using var consumers = await _state.ConsumerCoordinator.PauseAsync(cancellationToken);
            var result = await _state.Lifecycle.DeleteWikiDataAsync(
                active,
                Paths(active),
                cancellationToken);
            await _state.RefreshActiveBookAsync(CancellationToken.None);
            return result;
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    public async Task<WikiPackageInspection> InspectPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        _state.RequireActiveBook();
        return await _state.Packages.InspectAsync(packagePath, cancellationToken: cancellationToken);
    }

    public async Task<WikiPackageImportResponse> ImportPackageAsync(
        string packagePath,
        string inspectionToken,
        bool allowLegacyPackage = false,
        int? mapToWikiId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inspectionToken))
            throw new ArgumentException("An inspection token is required.", nameof(inspectionToken));

        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            var active = _state.RequireActiveBook();
            var inspection = await _state.Packages.InspectAsync(
                packagePath,
                cancellationToken: cancellationToken);
            var targetId = mapToWikiId ?? inspection.Metadata.Id;
            IAsyncDisposable? consumers = null;
            if (targetId == active.Id)
                consumers = await _state.ConsumerCoordinator.PauseAsync(cancellationToken);

            await using (consumers)
            {
                var result = await _state.Packages.ImportAsync(
                    new WikiPackageImportRequest(
                        packagePath,
                        _state.DataRoot,
                        inspectionToken,
                        mapToWikiId,
                        allowLegacyPackage),
                    cancellationToken: cancellationToken);
                if (result.WikiBook.Id == active.Id)
                    _state.ActiveBook = ApplicationState.CloneBook(result.WikiBook);
                return ApplicationMapper.ToResponse(result);
            }
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    public async Task<WikiPackageExportResponse> ExportPackageAsync(
        int wikiId,
        string packagePath,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appVersion))
            throw new ArgumentException("An application version is required.", nameof(appVersion));

        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            var active = _state.RequireActiveBook();
            var book = await _state.ManagerDatabase.GetItemAsync<WikiBook>(wikiId, cancellationToken)
                ?? throw new ApplicationUseCaseException("unknown-wiki", $"WikiBook {wikiId} is not registered.");
            var paths = Paths(book);
            if (!File.Exists(paths.ContentDatabasePath))
                throw new WikiPackageException(
                    "content-database-not-found",
                    $"Content database does not exist: {paths.ContentDatabasePath}.");

            ContentDbService? temporary = null;
            try
            {
                var source = active.Id == wikiId
                    ? _state.ContentDatabase
                    : temporary = new ContentDbService(paths.ContentDatabasePath);
                var result = await _state.Packages.ExportAsync(
                    book,
                    paths,
                    packagePath,
                    appVersion,
                    source,
                    cancellationToken: cancellationToken);
                return ApplicationMapper.ToResponse(result);
            }
            finally
            {
                if (temporary is not null)
                {
                    try
                    {
                        await temporary.CloseConnection();
                    }
                    catch
                    {
                    }
                }
            }
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    public async Task<WikiDatabaseInspection> InspectDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        _state.RequireActiveBook();
        return await _state.Packages.InspectDatabaseAsync(databasePath, cancellationToken);
    }

    public async Task<WikiDatabaseImportResponse> ImportDatabaseAsync(
        string databasePath,
        string inspectionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inspectionToken))
            throw new ArgumentException("An inspection token is required.", nameof(inspectionToken));
        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            var active = _state.RequireActiveBook();
            await using var consumers = await _state.ConsumerCoordinator.PauseAsync(cancellationToken);
            var result = await _state.Packages.ImportDatabaseAsync(
                new WikiDatabaseImportRequest(
                    databasePath,
                    _state.DataRoot,
                    active.Id,
                    inspectionToken),
                cancellationToken);
            _state.ActiveBook = ApplicationState.CloneBook(result.WikiBook);
            return ApplicationMapper.ToResponse(result);
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    public async Task<WikiDatabaseExportResponse> ExportDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            var active = _state.RequireActiveBook();
            var result = await _state.Packages.ExportDatabaseAsync(
                Paths(active),
                databasePath,
                _state.ContentDatabase,
                cancellationToken);
            return ApplicationMapper.ToResponse(result);
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    private WikiOperationOptions Options()
    {
        var preferences = _state.RequirePreferences();
        return new WikiOperationOptions(
            preferences.SearchLimit,
            preferences.PageConcurrency,
            preferences.ResourceConcurrency,
            preferences.RetryAttempts).Validate();
    }

    private WikiDataPaths Paths(WikiBook book) => WikiDataPaths.Create(_state.DataRoot, book);

    private async Task<WikiDataOperationGate.WikiDataOperationLease> EnterAsync(
        CancellationToken cancellationToken)
    {
        var operation = await _state.OperationGate.TryAcquireAsync(cancellationToken);
        return operation ?? throw new ApplicationUseCaseException(
            "operation-in-progress",
            "Another Wiki data operation is already running.");
    }

    private async Task<T> RunCoordinatedAsync<T>(
        Func<WikiBook, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            return await action(_state.RequireActiveBook());
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    private static ApplicationUseCaseException OperationInProgress() => new(
        "operation-in-progress",
        "Another Wiki data operation is already running.");
}
