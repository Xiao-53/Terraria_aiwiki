using System.Collections.ObjectModel;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Wiki;

internal sealed class WikiSearchResultViewModel : ObservableObject
{
    private readonly Func<SearchResultItem, Task> _open;

    public WikiSearchResultViewModel(SearchResultItem result, Func<SearchResultItem, Task> open)
    {
        Result = result;
        _open = open;
        Title = result.Title;
        RedirectText = string.IsNullOrWhiteSpace(result.RedirectTo) ? string.Empty : "→ " + result.RedirectTo;
        Snippet = result.Snippet;
        SectionText = result.SectionPath;
        MatchText = result.EvidenceKind switch
        {
            WikiSearchEvidenceKind.ExactTitle => UiTextCatalog.Message("ExactTitle"),
            WikiSearchEvidenceKind.ExactRedirect => UiTextCatalog.Message("ExactRedirect"),
            WikiSearchEvidenceKind.ExactCommunityAlias => UiTextCatalog.Message("CommunityAlias"),
            WikiSearchEvidenceKind.Heading => UiTextCatalog.Message("SectionHeading"),
            WikiSearchEvidenceKind.TableRow => UiTextCatalog.Message("TableContent"),
            WikiSearchEvidenceKind.ListItem => UiTextCatalog.Message("ListContent"),
            WikiSearchEvidenceKind.Definition => UiTextCatalog.Message("Definition"),
            WikiSearchEvidenceKind.Infobox => UiTextCatalog.Message("Infobox"),
            WikiSearchEvidenceKind.Generic => UiTextCatalog.Message("PageExcerpt"),
            _ => result.Scope == WikiSearchScope.Partial ? UiTextCatalog.Message("PartialMatch") : UiTextCatalog.Message("PageContentMatch"),
        };
        OpenCommand = new AsyncCommand(_ => OpenAsync());
    }

    public SearchResultItem Result { get; }
    public string Title { get; }
    public string RedirectText { get; }
    public string Snippet { get; }
    public string SectionText { get; }
    private string _localizedMatchText = string.Empty;
    public string MatchText { get => Text.Render(_localizedMatchText); private set => _localizedMatchText = value; }
    public AsyncCommand OpenCommand { get; }

    internal Task OpenAsync() => _open(Result);
}

internal sealed class WikiViewModel : ObservableObject, IDisposable
{
    protected override void OnTextCatalogChanged()
    {
        foreach (var item in Results) item.UseTextCatalog(Text);
    }
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);
    private readonly Func<SearchResultItem, Task> _openResult;
    private Func<string, CancellationToken, Task<IReadOnlyList<SearchResultItem>>>? _search;
    private CancellationTokenSource? _searchCancellation;
    private string _query = string.Empty;
    private string _statusMessage = UiTextCatalog.Message("PreparingTheLocalWiki");
    private bool _isSearching;
    private bool _isAvailable;
    private bool _disposed;

    public WikiViewModel(Func<SearchResultItem, Task> openResult)
    {
        _openResult = openResult ?? throw new ArgumentNullException(nameof(openResult));
        SearchCommand = new AsyncCommand(_ => SearchNowAsync(), _ => IsAvailable && !IsSearching);
    }

    public ObservableCollection<WikiSearchResultViewModel> Results { get; } = [];

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value))
                return;
            _ = BeginDebouncedSearchAsync();
        }
    }

    public string StatusMessage
    {
        get => Text.Render(_statusMessage);
        private set => SetProperty(ref _statusMessage, UiTextCatalog.FromSafeMessage(value));
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
                SearchCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
                SearchCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasResults => Results.Count > 0;
    public bool IsEmpty => !IsSearching && Query.Trim().Length > 0 && Results.Count == 0;

    public AsyncCommand SearchCommand { get; }

    public void Attach(Func<string, CancellationToken, Task<IReadOnlyList<SearchResultItem>>> search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        IsAvailable = true;
        StatusMessage = UiTextCatalog.Message("SearchTheLocalWikiResultsOpenInThe");
    }

    public void Detach()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        _search = null;
        IsAvailable = false;
        IsSearching = false;
        StatusMessage = UiTextCatalog.Message("TheLocalWikiDataSourceIsUnavailable");
    }

    public void ResetForDataSourceChange()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        Results.Clear();
        Query = string.Empty;
        IsSearching = false;
        StatusMessage = IsAvailable
            ? UiTextCatalog.Message("TheActiveWikiHasChangedEnterANew")
            : UiTextCatalog.Message("TheLocalWikiDataSourceIsUnavailable");
        RefreshResultProperties();
    }

    internal Task SearchNowAsync() => SearchCoreAsync(useDebounce: false);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
    }

    private Task BeginDebouncedSearchAsync() => SearchCoreAsync(useDebounce: true);

    private async Task SearchCoreAsync(bool useDebounce)
    {
        var search = _search;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var current = _searchCancellation;
        var token = current.Token;
        var query = Query.Trim();

        if (query.Length == 0)
        {
            Results.Clear();
            IsSearching = false;
            StatusMessage = UiTextCatalog.Message("EnterAPageNameOrKeywords");
            RefreshResultProperties();
            return;
        }
        if (search is null)
        {
            Results.Clear();
            IsSearching = false;
            StatusMessage = UiTextCatalog.Message("TheLocalWikiDataSourceIsUnavailable");
            RefreshResultProperties();
            return;
        }

        IsSearching = true;
        StatusMessage = UiTextCatalog.Message("Searching");
        try
        {
            if (useDebounce)
                await Task.Delay(SearchDelay, token);
            var results = await search(query, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(current, _searchCancellation))
                return;
            Results.Clear();
            foreach (var result in results)
                Results.Add(new WikiSearchResultViewModel(result, _openResult).WithText(Text));
            StatusMessage = results.Count == 0 ? UiTextCatalog.Message("NoResultsFound") : UiTextCatalog.Message("FoundResults", results.Count);
            RefreshResultProperties();
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(current, _searchCancellation))
                StatusMessage = UiTextCatalog.Message("SearchCancelled");
        }
        catch (WikiQueryException exception) when (exception.Code == WikiQueryErrorCode.DataSourceUnavailable)
        {
            Results.Clear();
            StatusMessage = UiTextCatalog.Message("TheLocalWikiDataSourceIsUnavailableTry");
            RefreshResultProperties();
        }
        catch
        {
            Results.Clear();
            StatusMessage = UiTextCatalog.Message("SearchFailedLocalDataIsUnchanged");
            RefreshResultProperties();
        }
        finally
        {
            if (ReferenceEquals(current, _searchCancellation))
            {
                IsSearching = false;
                current.Dispose();
                _searchCancellation = null;
                RefreshResultProperties();
            }
        }
    }

    private void RefreshResultProperties()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
