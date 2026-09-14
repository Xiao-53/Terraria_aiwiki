using System.Diagnostics;
using Terraria_Wiki.Application.Internal;
using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Models;
using Terraria_Wiki.Services;

namespace Terraria_Wiki.Application;

public sealed class ActiveWikiUseCases
{
    private readonly ApplicationState _state;
    private readonly ApplicationSettingsUseCases _settings;

    internal ActiveWikiUseCases(ApplicationState state, ApplicationSettingsUseCases settings)
    {
        _state = state;
        _settings = settings;
    }

    public Task<WikiBookSnapshot> InitializeAsync(CancellationToken cancellationToken = default) =>
        InitializeAsync(startupObserver: null, cancellationToken);

    internal async Task<WikiBookSnapshot> InitializeAsync(
        Action<ApplicationStartupTiming>? startupObserver,
        CancellationToken cancellationToken = default)
    {
        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            if (_state.IsInitialized && _state.ActiveBook is not null)
                return ApplicationMapper.ToSnapshot(_state.ActiveBook);

            Report(startupObserver, ApplicationStartupStage.DataLayoutPreflight, 0);

            List<WikiBook> books;
            using (Measure(ApplicationStartupStage.ManagerInitializationAndRecovery, startupObserver))
            {
                await _state.ManagerDatabase.Init(cancellationToken: cancellationToken);
                books = await _state.ManagerDatabase.GetItemsAsync<WikiBook>(cancellationToken);
                foreach (var book in books.OrderBy(book => book.Id))
                    await _state.DatabaseTransfer.RecoverPendingImportAsync(_state.DataRoot, book, cancellationToken);

                books = await _state.ManagerDatabase.GetItemsAsync<WikiBook>(cancellationToken);
                if (books.Count == 0)
                    throw new ApplicationUseCaseException("no-wikis", "No WikiBook is registered in Manager.db.");
            }

            WikiBook selected;
            bool repairStoredId;
            using (Measure(ApplicationStartupStage.Settings, startupObserver))
            {
                await _settings.LoadAndRepairAsync(cancellationToken);
                var storedId = await _state.SettingsStore.LoadActiveWikiIdAsync(cancellationToken);
                selected = SelectBook(books, storedId);
                repairStoredId = storedId != selected.Id;
            }

            await using var consumers = await _state.ConsumerCoordinator.PauseAsync(cancellationToken);
            using var operation = await _state.OperationGate.TryAcquireAsync(cancellationToken)
                ?? throw OperationInProgress();

            using (Measure(ApplicationStartupStage.CompatibilityPreparation, startupObserver))
                PrepareTarget(selected, cancellationToken);
            var paths = WikiDataPaths.Create(_state.DataRoot, selected);
            using (Measure(ApplicationStartupStage.ContentInitialization, startupObserver))
            {
                await _state.ContentDatabase.SwitchDatabaseAsync(paths.ContentDatabasePath, cancellationToken);
                await _state.ContentDatabase.Init(seedBook: selected, cancellationToken: cancellationToken);
            }

            // Manager.db is the durable startup snapshot. Fresh counts remain
            // available through Data.GetStatisticsAsync and lifecycle operations.
            Report(startupObserver, ApplicationStartupStage.StatisticsSnapshot, 0);

            if (repairStoredId)
                await _state.SettingsStore.SaveActiveWikiIdAsync(selected.Id, cancellationToken);

            _state.ActiveBook = selected;
            _state.IsInitialized = true;
            return ApplicationMapper.ToSnapshot(selected);
        }
        catch (OperationCanceledException)
        {
            await ResetFailedInitializationAsync();
            throw;
        }
        catch (ApplicationUseCaseException)
        {
            await ResetFailedInitializationAsync();
            throw;
        }
        catch (WikiDatabaseFormatException exception)
        {
            await ResetFailedInitializationAsync();
            throw new ApplicationUseCaseException(exception.Code, exception.Message, exception);
        }
        catch (Exception exception)
        {
            await ResetFailedInitializationAsync();
            throw new ApplicationUseCaseException(
                "initialization-failed",
                "Application initialization failed.",
                exception);
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    public async Task<IReadOnlyList<WikiBookSnapshot>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        _state.RequireActiveBook();
        var books = await _state.ManagerDatabase.GetItemsAsync<WikiBook>(cancellationToken);
        return books
            .OrderBy(book => book.Id)
            .Select(ApplicationMapper.ToSnapshot)
            .ToArray();
    }

    public Task<WikiBookSnapshot> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplicationMapper.ToSnapshot(_state.RequireActiveBook()));
    }

    public async Task<WikiBookSnapshot> SwitchAsync(
        int wikiId,
        CancellationToken cancellationToken = default)
    {
        var current = _state.RequireActiveBook();
        if (current.Id == wikiId)
            return ApplicationMapper.ToSnapshot(current);

        if (!await _state.CoordinationGate.WaitAsync(0, cancellationToken))
            throw OperationInProgress();
        try
        {
            current = _state.RequireActiveBook();
            if (current.Id == wikiId)
                return ApplicationMapper.ToSnapshot(current);

            var target = await _state.ManagerDatabase.GetItemAsync<WikiBook>(wikiId, cancellationToken)
                ?? throw new ApplicationUseCaseException("unknown-wiki", $"WikiBook {wikiId} is not registered.");
            var previous = ApplicationState.CloneBook(current);
            var previousPath = _state.ContentDatabase.DatabasePath;

            await using var consumers = await _state.ConsumerCoordinator.PauseAsync(cancellationToken);
            using var operation = await _state.OperationGate.TryAcquireAsync(cancellationToken)
                ?? throw OperationInProgress();

            try
            {
                await _state.ContentDatabase.CloseConnection(cancellationToken);
                PrepareTarget(target, cancellationToken);
                var targetPaths = WikiDataPaths.Create(_state.DataRoot, target);
                await _state.ContentDatabase.SwitchDatabaseAsync(targetPaths.ContentDatabasePath, cancellationToken);
                await _state.ContentDatabase.Init(seedBook: target, cancellationToken: cancellationToken);
                target = await _state.Lifecycle.RefreshBookStatisticsAsync(target, cancellationToken);
                await _state.SettingsStore.SaveActiveWikiIdAsync(target.Id, cancellationToken);
                _state.ActiveBook = target;
                return ApplicationMapper.ToSnapshot(target);
            }
            catch (Exception switchException)
            {
                try
                {
                    await _state.ContentDatabase.CloseConnection();
                    await _state.ContentDatabase.SwitchDatabaseAsync(previousPath);
                    await _state.ContentDatabase.Init(seedBook: previous);
                    await _state.SettingsStore.SaveActiveWikiIdAsync(previous.Id);
                    _state.ActiveBook = previous;
                }
                catch (Exception rollbackException)
                {
                    throw new ApplicationUseCaseException(
                        "active-wiki-rollback-failed",
                        "The active Wiki switch failed and the previous data source could not be restored.",
                        new AggregateException(switchException, rollbackException));
                }

                if (switchException is OperationCanceledException cancellation)
                    throw cancellation;
                throw new ApplicationUseCaseException(
                    "active-wiki-switch-failed",
                    $"Switching to WikiBook {wikiId} failed and the previous data source was restored.",
                    switchException);
            }
        }
        finally
        {
            _state.CoordinationGate.Release();
        }
    }

    private void PrepareTarget(
        WikiBook book,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paths = WikiDataPaths.Create(_state.DataRoot, book);
        paths.EnsureDataDirectory();
    }

    private async Task ResetFailedInitializationAsync()
    {
        _state.ActiveBook = null;
        _state.IsInitialized = false;
        try
        {
            await _state.ContentDatabase.CloseConnection();
        }
        catch
        {
        }
    }

    internal static WikiBook SelectBook(IReadOnlyList<WikiBook> books, int? storedId) =>
        books.FirstOrDefault(book => book.Id == storedId)
        ?? books.FirstOrDefault(book => book.Id == ApplicationPreferences.DefaultActiveWikiId)
        ?? books.OrderBy(book => book.Id).First();

    private static ApplicationUseCaseException OperationInProgress() => new(
        "operation-in-progress",
        "Another Wiki data operation is already running.");

    private static IDisposable Measure(
        ApplicationStartupStage stage,
        Action<ApplicationStartupTiming>? observer) =>
        new StartupMeasurement(stage, observer);

    private static void Report(
        Action<ApplicationStartupTiming>? observer,
        ApplicationStartupStage stage,
        long durationMilliseconds)
    {
        if (observer is null)
            return;
        try { observer(new ApplicationStartupTiming(stage, durationMilliseconds)); }
        catch { }
    }

    private sealed class StartupMeasurement : IDisposable
    {
        private readonly ApplicationStartupStage _stage;
        private readonly Action<ApplicationStartupTiming>? _observer;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public StartupMeasurement(
            ApplicationStartupStage stage,
            Action<ApplicationStartupTiming>? observer)
        {
            _stage = stage;
            _observer = observer;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            Report(_observer, _stage, _stopwatch.ElapsedMilliseconds);
        }
    }
}

internal enum ApplicationStartupStage
{
    DataLayoutPreflight,
    ManagerInitializationAndRecovery,
    Settings,
    CompatibilityPreparation,
    ContentInitialization,
    StatisticsSnapshot,
}

internal readonly record struct ApplicationStartupTiming(
    ApplicationStartupStage Stage,
    long DurationMilliseconds);
