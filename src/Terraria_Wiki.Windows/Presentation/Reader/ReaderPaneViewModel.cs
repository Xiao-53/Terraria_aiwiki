using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Reader;

internal enum ReaderPaneState
{
    Closed,
    Home,
    ChatSource,
    Wiki,
}

internal sealed record ReaderPageRequest(string Title, string? Anchor = null)
{
    public string Target => string.IsNullOrWhiteSpace(Anchor)
        ? Title
        : $"{Title}#{Anchor.TrimStart('#')}";
}

internal sealed class ReaderPaneViewModel : ObservableObject
{
    private Func<string, CancellationToken, Task>? _openPage;
    private Func<CancellationToken, Task>? _goBack;
    private Func<string, Task<bool>>? _isFavorite;
    private Func<string, bool, Task<bool>>? _setFavorite;
    private string _title = "Wiki";
    private string _location = "等待本地 Viewer…";
    private string _statusMessage = "Reader 正在启动。";
    private bool _isBusy;
    private bool _isReady;
    private string? _currentPageTitle;
    private bool _isWikiFavorite;

    public ReaderPaneViewModel()
    {
        BackCommand = new AsyncCommand(_ => BackAsync(), _ => IsReady && !IsBusy);
        ToggleFavoriteCommand = new AsyncCommand(_ => ToggleFavoriteAsync(), _ => CanFavorite);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Location
    {
        get => _location;
        private set => SetProperty(ref _location, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                BackCommand.NotifyCanExecuteChanged();
                ToggleFavoriteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanFavorite));
            }
        }
    }

    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (SetProperty(ref _isReady, value))
            {
                BackCommand.NotifyCanExecuteChanged();
                ToggleFavoriteCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanFavorite));
            }
        }
    }

    public AsyncCommand BackCommand { get; }
    public AsyncCommand ToggleFavoriteCommand { get; }
    public bool CanFavorite => IsReady && !IsBusy && _setFavorite is not null && _currentPageTitle is not null;

    public bool IsWikiFavorite
    {
        get => _isWikiFavorite;
        private set => SetProperty(ref _isWikiFavorite, value);
    }

    public void Attach(
        Func<string, CancellationToken, Task> openPage,
        Func<CancellationToken, Task> goBack)
    {
        _openPage = openPage ?? throw new ArgumentNullException(nameof(openPage));
        _goBack = goBack ?? throw new ArgumentNullException(nameof(goBack));
        IsReady = true;
        StatusMessage = "本地 Viewer 已就绪。";
        Location = "本地 Wiki 首页";
    }

    public void Detach()
    {
        _openPage = null;
        _goBack = null;
        IsReady = false;
        IsBusy = false;
        StatusMessage = "Reader 不可用。";
        Location = "等待本地 Viewer…";
        _currentPageTitle = null;
        IsWikiFavorite = false;
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
    }

    public void AttachFavorites(
        Func<string, Task<bool>> isFavorite,
        Func<string, bool, Task<bool>> setFavorite)
    {
        _isFavorite = isFavorite ?? throw new ArgumentNullException(nameof(isFavorite));
        _setFavorite = setFavorite ?? throw new ArgumentNullException(nameof(setFavorite));
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
    }

    public void DetachFavorites()
    {
        _isFavorite = null;
        _setFavorite = null;
        IsWikiFavorite = false;
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
    }

    public void SetStatus(string message) => StatusMessage = message;

    public void SetTransportReady(bool isReady)
    {
        IsReady = isReady && _openPage is not null;
        StatusMessage = IsReady
            ? "本地 Viewer 已就绪。"
            : "正在准备本地 Wiki 内容…";
    }

    public async Task SetHomePageAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return;
        _currentPageTitle = title.Trim();
        Title = "Wiki";
        Location = "本地 Wiki 首页";
        try
        {
            await RefreshFavoriteAsync();
        }
        catch
        {
            IsWikiFavorite = false;
            ToggleFavoriteCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanFavorite));
            StatusMessage = "无法读取 Wiki 收藏状态。";
        }
    }

    public async Task OpenAsync(ReaderPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_openPage is null)
        {
            StatusMessage = "Reader 尚未就绪。";
            return;
        }

        IsBusy = true;
        Title = request.Title;
        Location = request.Target;
        StatusMessage = $"正在打开 {request.Title}…";
        try
        {
            await _openPage(request.Target, cancellationToken);
            _currentPageTitle = request.Title;
            await RefreshFavoriteAsync();
            StatusMessage = $"已打开 {request.Title}。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "打开页面已取消。";
        }
        catch
        {
            StatusMessage = "无法打开该 Wiki 页面。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task BackAsync()
    {
        if (_goBack is null)
            return;
        IsBusy = true;
        try
        {
            await _goBack(CancellationToken.None);
            _currentPageTitle = null;
            IsWikiFavorite = false;
            ToggleFavoriteCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanFavorite));
            StatusMessage = "已返回上一页。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "返回操作已取消。";
        }
        catch
        {
            StatusMessage = "无法返回上一页。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ToggleFavoriteAsync()
    {
        if (_setFavorite is null || _currentPageTitle is null || !CanFavorite)
            return;
        var requested = !IsWikiFavorite;
        IsWikiFavorite = await _setFavorite(_currentPageTitle, requested);
        StatusMessage = IsWikiFavorite == requested
            ? requested ? $"已收藏 {_currentPageTitle}。" : $"已取消收藏 {_currentPageTitle}。"
            : "无法更新 Wiki 收藏。";
    }

    private async Task RefreshFavoriteAsync()
    {
        IsWikiFavorite = _isFavorite is not null && _currentPageTitle is not null
            && await _isFavorite(_currentPageTitle);
        ToggleFavoriteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanFavorite));
    }
}
