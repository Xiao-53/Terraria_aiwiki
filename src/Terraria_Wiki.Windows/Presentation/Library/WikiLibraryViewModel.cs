using System.Collections.ObjectModel;
using Terraria_Wiki.Application;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Library;

internal enum WikiLibrarySection
{
    Pages,
    History,
}

internal sealed class WikiPageItemViewModel
{
    private readonly Func<string, Task> _open;

    public WikiPageItemViewModel(WikiPageListItem item, Func<string, Task> open)
    {
        Item = item;
        _open = open;
        OpenCommand = new AsyncCommand(_ => OpenAsync());
    }

    public WikiPageListItem Item { get; }
    public string Title => Item.Title;
    public string ModifiedText => Item.LastModified.ToString("yyyy-MM-dd HH:mm");
    public AsyncCommand OpenCommand { get; }

    internal Task OpenAsync() => _open(Title);
}

internal sealed class WikiHistoryItemViewModel
{
    private readonly Func<string, Task> _open;

    public WikiHistoryItemViewModel(
        WikiHistoryItem item,
        bool showDateHeader,
        string dateLabel,
        Func<string, Task> open)
    {
        Item = item;
        ShowDateHeader = showDateHeader;
        DateLabel = dateLabel;
        _open = open;
        OpenCommand = new AsyncCommand(_ => OpenAsync());
    }

    public WikiHistoryItem Item { get; }
    public string Title => Item.WikiTitle;
    public string TimeText => Item.ReadAt.ToString("HH:mm");
    public bool ShowDateHeader { get; }
    public string DateLabel { get; }
    public AsyncCommand OpenCommand { get; }

    internal Task OpenAsync() => _open(Title);
}

internal sealed class WikiLibraryViewModel : ObservableObject, IDisposable
{
    internal const int PageSize = 100;
    private static readonly TimeSpan FilterDelay = TimeSpan.FromMilliseconds(180);

    private readonly Func<string, Task> _openPage;
    private Func<PageRequest, CancellationToken, Task<PageSlice<WikiPageListItem>>>? _getPages;
    private Func<PageRequest, CancellationToken, Task<PageSlice<WikiHistoryItem>>>? _getHistory;
    private Func<CancellationToken, Task>? _clearHistory;
    private CancellationTokenSource? _loadCancellation;
    private WikiLibrarySection _currentSection = WikiLibrarySection.Pages;
    private string _query = string.Empty;
    private string _statusMessage = "正在准备 Wiki 资料库…";
    private bool _isAvailable;
    private bool _isBusy;
    private bool _isClearConfirmationVisible;
    private int _totalCount;
    private bool _disposed;

    public WikiLibraryViewModel(Func<string, Task> openPage)
    {
        _openPage = openPage ?? throw new ArgumentNullException(nameof(openPage));
        SelectPagesCommand = new AsyncCommand(_ => SelectSectionAsync(WikiLibrarySection.Pages));
        SelectHistoryCommand = new AsyncCommand(_ => SelectSectionAsync(WikiLibrarySection.History));
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => IsAvailable && !IsBusy);
        LoadMoreCommand = new AsyncCommand(_ => LoadMoreAsync(), _ => CanLoadMore);
        RequestClearHistoryCommand = new RelayCommand(_ => RequestClearHistory(), _ => CanRequestClearHistory);
        CancelClearHistoryCommand = new RelayCommand(_ => CancelClearHistory(), _ => IsClearConfirmationVisible);
        ConfirmClearHistoryCommand = new AsyncCommand(
            _ => ConfirmClearHistoryAsync(),
            _ => IsClearConfirmationVisible && IsAvailable && !IsBusy);
    }

    public ObservableCollection<WikiPageItemViewModel> Pages { get; } = [];
    public ObservableCollection<WikiHistoryItemViewModel> History { get; } = [];

    public WikiLibrarySection CurrentSection
    {
        get => _currentSection;
        private set
        {
            if (!SetProperty(ref _currentSection, value))
                return;
            OnPropertyChanged(nameof(IsPagesSelected));
            OnPropertyChanged(nameof(IsHistorySelected));
            OnPropertyChanged(nameof(IsPagesUnselected));
            OnPropertyChanged(nameof(IsHistoryUnselected));
            OnPropertyChanged(nameof(IsPagesVisible));
            OnPropertyChanged(nameof(IsHistoryVisible));
            RefreshViewState();
        }
    }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
                _ = LoadAsync(reset: true, useFilterDelay: true);
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
                RefreshViewState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                RefreshViewState();
        }
    }

    public bool IsClearConfirmationVisible
    {
        get => _isClearConfirmationVisible;
        private set
        {
            if (!SetProperty(ref _isClearConfirmationVisible, value))
                return;
            CancelClearHistoryCommand.NotifyCanExecuteChanged();
            ConfirmClearHistoryCommand.NotifyCanExecuteChanged();
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
                RefreshViewState();
        }
    }

    public bool IsPagesSelected => CurrentSection == WikiLibrarySection.Pages;
    public bool IsHistorySelected => CurrentSection == WikiLibrarySection.History;
    public bool IsPagesUnselected => !IsPagesSelected;
    public bool IsHistoryUnselected => !IsHistorySelected;
    public bool IsPagesVisible => IsPagesSelected;
    public bool IsHistoryVisible => IsHistorySelected;
    public bool HasItems => CurrentItemCount > 0;
    public bool IsEmpty => IsAvailable && !IsBusy && CurrentItemCount == 0;
    public bool CanLoadMore => IsAvailable && !IsBusy && CurrentItemCount < TotalCount;
    public bool CanRequestClearHistory => IsHistorySelected && IsAvailable && !IsBusy && History.Count > 0;

    public AsyncCommand SelectPagesCommand { get; }
    public AsyncCommand SelectHistoryCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand LoadMoreCommand { get; }
    public RelayCommand RequestClearHistoryCommand { get; }
    public RelayCommand CancelClearHistoryCommand { get; }
    public AsyncCommand ConfirmClearHistoryCommand { get; }

    private int CurrentItemCount => IsPagesSelected ? Pages.Count : History.Count;

    public void Attach(WikiLibraryUseCases library)
    {
        ArgumentNullException.ThrowIfNull(library);
        Attach(library.GetPagesAsync, library.GetHistoryAsync, library.ClearHistoryAsync);
    }

    internal void Attach(
        Func<PageRequest, CancellationToken, Task<PageSlice<WikiPageListItem>>> getPages,
        Func<PageRequest, CancellationToken, Task<PageSlice<WikiHistoryItem>>> getHistory,
        Func<CancellationToken, Task> clearHistory)
    {
        _getPages = getPages ?? throw new ArgumentNullException(nameof(getPages));
        _getHistory = getHistory ?? throw new ArgumentNullException(nameof(getHistory));
        _clearHistory = clearHistory ?? throw new ArgumentNullException(nameof(clearHistory));
        IsAvailable = true;
        StatusMessage = "浏览全部页面或最近阅读记录。";
    }

    public void Detach()
    {
        CancelCurrentLoad();
        _getPages = null;
        _getHistory = null;
        _clearHistory = null;
        IsAvailable = false;
        IsBusy = false;
        IsClearConfirmationVisible = false;
        Pages.Clear();
        History.Clear();
        TotalCount = 0;
        StatusMessage = "本地 Wiki 数据源不可用。";
        RefreshViewState();
    }

    internal async Task SelectSectionAsync(WikiLibrarySection section)
    {
        if (CurrentSection == section)
            return;
        CurrentSection = section;
        IsClearConfirmationVisible = false;
        await LoadAsync(reset: true, useFilterDelay: false);
    }

    internal Task RefreshAsync() => LoadAsync(reset: true, useFilterDelay: false);

    internal Task LoadMoreAsync() =>
        CanLoadMore ? LoadAsync(reset: false, useFilterDelay: false) : Task.CompletedTask;

    internal void RequestClearHistory()
    {
        if (CanRequestClearHistory)
            IsClearConfirmationVisible = true;
    }

    internal void CancelClearHistory() => IsClearConfirmationVisible = false;

    internal async Task ConfirmClearHistoryAsync()
    {
        var clearHistory = _clearHistory;
        if (!IsClearConfirmationVisible || clearHistory is null || IsBusy)
            return;

        CancelCurrentLoad();
        var current = new CancellationTokenSource();
        _loadCancellation = current;
        IsBusy = true;
        var cleared = false;
        try
        {
            await clearHistory(current.Token);
            current.Token.ThrowIfCancellationRequested();
            cleared = true;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        catch
        {
            StatusMessage = "无法清空浏览历史；现有记录未被更改。";
        }
        finally
        {
            if (ReferenceEquals(current, _loadCancellation))
            {
                _loadCancellation = null;
                current.Dispose();
                IsBusy = false;
                IsClearConfirmationVisible = false;
            }
        }

        if (cleared && await LoadAsync(reset: true, useFilterDelay: false))
            StatusMessage = "浏览历史已清空。";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Detach();
    }

    private async Task<bool> LoadAsync(bool reset, bool useFilterDelay)
    {
        var getPages = _getPages;
        var getHistory = _getHistory;
        if (!IsAvailable || (IsPagesSelected ? getPages is null : getHistory is null))
            return false;

        CancelCurrentLoad();
        var current = new CancellationTokenSource();
        _loadCancellation = current;
        var token = current.Token;
        var section = CurrentSection;
        var query = Query.Trim();
        var offset = reset ? 0 : CurrentItemCount;
        IsBusy = true;
        StatusMessage = section == WikiLibrarySection.Pages ? "正在载入全部页面…" : "正在载入浏览历史…";

        try
        {
            if (useFilterDelay)
                await Task.Delay(FilterDelay, token);

            if (section == WikiLibrarySection.Pages)
            {
                var slice = await getPages!(new PageRequest(offset, PageSize, query), token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(current, _loadCancellation) || CurrentSection != section)
                    return false;
                ApplyPages(slice, reset);
            }
            else
            {
                var slice = await getHistory!(new PageRequest(offset, PageSize, query), token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(current, _loadCancellation) || CurrentSection != section)
                    return false;
                ApplyHistory(slice, reset);
            }
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            if (ReferenceEquals(current, _loadCancellation))
                StatusMessage = "资料库暂时不可用；本地数据未被修改。";
            return false;
        }
        finally
        {
            if (ReferenceEquals(current, _loadCancellation))
            {
                _loadCancellation = null;
                current.Dispose();
                IsBusy = false;
                RefreshViewState();
            }
        }
    }

    private void ApplyPages(PageSlice<WikiPageListItem> slice, bool reset)
    {
        if (reset)
            Pages.Clear();
        foreach (var item in slice.Items)
            Pages.Add(new WikiPageItemViewModel(item, _openPage));
        TotalCount = slice.TotalCount;
        StatusMessage = TotalCount == 0
            ? BuildEmptyMessage("页面")
            : $"已显示 {Pages.Count} / {TotalCount} 个页面";
        RefreshViewState();
    }

    private void ApplyHistory(PageSlice<WikiHistoryItem> slice, bool reset)
    {
        if (reset)
            History.Clear();
        var previousDateKey = History.LastOrDefault()?.Item.DateKey;
        foreach (var item in slice.Items)
        {
            var showDateHeader = !string.Equals(previousDateKey, item.DateKey, StringComparison.Ordinal);
            History.Add(new WikiHistoryItemViewModel(
                item,
                showDateHeader,
                FormatDateLabel(item.ReadAt),
                _openPage));
            previousDateKey = item.DateKey;
        }
        TotalCount = slice.TotalCount;
        StatusMessage = TotalCount == 0
            ? BuildEmptyMessage("历史记录")
            : $"已显示 {History.Count} / {TotalCount} 条历史记录";
        RefreshViewState();
    }

    private string BuildEmptyMessage(string subject) =>
        Query.Trim().Length == 0 ? $"还没有{subject}。" : $"没有匹配“{Query.Trim()}”的{subject}。";

    private static string FormatDateLabel(DateTime value)
    {
        var date = value.Date;
        if (date == DateTime.Today)
            return "今天";
        if (date == DateTime.Today.AddDays(-1))
            return "昨天";
        return date.ToString("yyyy年M月d日");
    }

    private void CancelCurrentLoad()
    {
        var current = _loadCancellation;
        _loadCancellation = null;
        if (current is null)
            return;
        current.Cancel();
        current.Dispose();
    }

    private void RefreshViewState()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanLoadMore));
        OnPropertyChanged(nameof(CanRequestClearHistory));
        RefreshCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged();
        RequestClearHistoryCommand.NotifyCanExecuteChanged();
        ConfirmClearHistoryCommand.NotifyCanExecuteChanged();
    }
}
