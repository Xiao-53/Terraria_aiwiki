using System.Collections.ObjectModel;
using Terraria_Wiki.Core.Query;
using Terraria_Wiki.Models;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Wiki;

internal sealed class WikiSearchResultViewModel
{
    private readonly Func<SearchResultItem, Task> _open;

    public WikiSearchResultViewModel(SearchResultItem result, Func<SearchResultItem, Task> open)
    {
        Result = result;
        _open = open;
        Title = result.Title;
        RedirectText = string.IsNullOrWhiteSpace(result.RedirectTo) ? string.Empty : "→ " + result.RedirectTo;
        Snippet = result.Snippet;
        MatchText = result.MatchKind switch
        {
            WikiSearchMatchKind.ExactTitle => "精确标题",
            WikiSearchMatchKind.ExactRedirect => "精确重定向",
            WikiSearchMatchKind.TitlePrefix => "标题前缀",
            WikiSearchMatchKind.RedirectPrefix => "重定向前缀",
            WikiSearchMatchKind.TitleContains => "标题包含",
            WikiSearchMatchKind.RedirectContains => "重定向包含",
            _ => "正文匹配",
        };
        OpenCommand = new AsyncCommand(_ => OpenAsync());
    }

    public SearchResultItem Result { get; }
    public string Title { get; }
    public string RedirectText { get; }
    public string Snippet { get; }
    public string MatchText { get; }
    public AsyncCommand OpenCommand { get; }

    internal Task OpenAsync() => _open(Result);
}

internal sealed class WikiViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(180);
    private readonly Func<SearchResultItem, Task> _openResult;
    private Func<string, CancellationToken, Task<IReadOnlyList<SearchResultItem>>>? _search;
    private CancellationTokenSource? _searchCancellation;
    private string _query = string.Empty;
    private string _statusMessage = "正在准备本地 Wiki…";
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
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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
        StatusMessage = "搜索本地 Wiki，结果会在同一个 Reader 中打开。";
    }

    public void Detach()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = null;
        _search = null;
        IsAvailable = false;
        IsSearching = false;
        StatusMessage = "本地 Wiki 数据源不可用。";
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
            ? "活动 Wiki 已改变；请输入新的搜索内容。"
            : "本地 Wiki 数据源不可用。";
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
            StatusMessage = "输入词条名称或关键词。";
            RefreshResultProperties();
            return;
        }
        if (search is null)
        {
            Results.Clear();
            IsSearching = false;
            StatusMessage = "本地 Wiki 数据源不可用。";
            RefreshResultProperties();
            return;
        }

        IsSearching = true;
        StatusMessage = "正在搜索…";
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
                Results.Add(new WikiSearchResultViewModel(result, _openResult));
            StatusMessage = results.Count == 0 ? "没有找到结果。" : $"找到 {results.Count} 个结果。";
            RefreshResultProperties();
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(current, _searchCancellation))
                StatusMessage = "搜索已取消。";
        }
        catch (WikiQueryException exception) when (exception.Code == WikiQueryErrorCode.DataSourceUnavailable)
        {
            Results.Clear();
            StatusMessage = "本地 Wiki 数据源不可用；请稍后重试。";
            RefreshResultProperties();
        }
        catch
        {
            Results.Clear();
            StatusMessage = "搜索失败；本地数据未被修改。";
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
