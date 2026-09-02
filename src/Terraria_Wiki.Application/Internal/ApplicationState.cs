using Terraria_Wiki.Core.Data;
using Terraria_Wiki.Core.Net;
using Terraria_Wiki.Core.Operations;
using Terraria_Wiki.Core.Packages;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;

namespace Terraria_Wiki.Application.Internal;

internal sealed class ApplicationState
{
    public ApplicationState(
        string dataRoot,
        IApplicationSettingsStore settingsStore,
        IWikiDataConsumerCoordinator consumerCoordinator,
        IWikiHttpTransport httpTransport,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("A data root is required.", nameof(dataRoot));
        if (!Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("The data root must be absolute.", nameof(dataRoot));

        DataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        SettingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        ConsumerCoordinator = consumerCoordinator ?? throw new ArgumentNullException(nameof(consumerCoordinator));
        TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(httpTransport);

        ManagerDatabase = new ManagerDbService(Path.Combine(DataRoot, "Manager.db"));
        ContentDatabase = new ContentDbService(Path.Combine(DataRoot, ".application-uninitialized.db"));
        OperationGate = new WikiDataOperationGate();
        Lifecycle = new WikiDataLifecycle(ManagerDatabase, ContentDatabase, httpTransport, OperationGate);
        Packages = new WikiPackageTransferService(ManagerDatabase, ContentDatabase, OperationGate);
        Reader = new WikiReader(ContentDatabase);
    }

    public string DataRoot { get; }
    public IApplicationSettingsStore SettingsStore { get; }
    public IWikiDataConsumerCoordinator ConsumerCoordinator { get; }
    public TimeProvider TimeProvider { get; }
    public ManagerDbService ManagerDatabase { get; }
    public ContentDbService ContentDatabase { get; }
    public WikiDataOperationGate OperationGate { get; }
    public WikiDataLifecycle Lifecycle { get; }
    public WikiPackageTransferService Packages { get; }
    public WikiReader Reader { get; }
    public SemaphoreSlim CoordinationGate { get; } = new(1, 1);
    public ApplicationPreferences? Preferences { get; set; }
    public WikiBook? ActiveBook { get; set; }
    public bool IsInitialized { get; set; }

    public WikiBook RequireActiveBook()
    {
        if (!IsInitialized || ActiveBook is null)
            throw new ApplicationUseCaseException("not-initialized", "The Application use cases have not been initialized.");
        return ActiveBook;
    }

    public ApplicationPreferences RequirePreferences()
    {
        if (Preferences is null)
            throw new ApplicationUseCaseException("not-initialized", "Application preferences have not been loaded.");
        return Preferences;
    }

    public async Task RefreshActiveBookAsync(CancellationToken cancellationToken)
    {
        var active = RequireActiveBook();
        ActiveBook = await ManagerDatabase.GetItemAsync<WikiBook>(active.Id, cancellationToken)
            ?? throw new ApplicationUseCaseException("unknown-wiki", $"WikiBook {active.Id} is no longer registered.");
    }

    public static WikiBook CloneBook(WikiBook source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Description = source.Description,
        IsPageDownloaded = source.IsPageDownloaded,
        IsResourceDownloaded = source.IsResourceDownloaded,
        PageCount = source.PageCount,
        ResourceCount = source.ResourceCount,
        RedirectCount = source.RedirectCount,
        DataSize = source.DataSize,
        UpdateTime = source.UpdateTime,
        ApiBaseUrl = source.ApiBaseUrl,
        BaseUrl = source.BaseUrl,
        RedirectListUrl = source.RedirectListUrl,
        MainNamespace = source.MainNamespace,
        AdditionalNamespaces = source.AdditionalNamespaces,
        JunkXPath = source.JunkXPath,
        DataFolder = source.DataFolder,
        DefaultPageContent = source.DefaultPageContent,
        DefaultPageTitle = source.DefaultPageTitle,
    };
}
