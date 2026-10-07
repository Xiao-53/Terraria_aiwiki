using System.ComponentModel;
using Terraria_Wiki.Windows.AI;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Chat;
using Terraria_Wiki.Windows.Presentation.Conversations;
using Terraria_Wiki.Windows.Presentation.Infrastructure;
using Terraria_Wiki.Windows.Presentation.Markdown;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Presentation.Wiki;
using Terraria_Wiki.Windows.Viewer;
using Terraria_Wiki.Core.Query;

namespace Terraria_Wiki.Windows.Presentation.QuickAccess;

internal sealed class QuickAccessViewModel : ObservableObject, IDisposable
{
    protected override void OnTextCatalogChanged()
    {
        Chat.UseTextCatalog(Text);
        Wiki.UseTextCatalog(Text);
        Reader.UseTextCatalog(Text);
    }

    private readonly Func<string, Task> _openArticle;
    private readonly Func<Task> _restorePage;
    private readonly Action _retryViewer;
    private bool _isWiki;
    private bool _wikiReading;
    private bool _sourceReading;
    private string _viewerStatus = UiTextCatalog.Message("PreparingTheLocalWiki");
    private string _homeTitle = "Terraria Wiki";
    private bool _viewerFailed;
    private bool _viewerReady;

    public QuickAccessViewModel(
        IChatConversationStore store,
        Func<string, Task> openArticle,
        Func<Task> restorePage,
        Action retryViewer)
    {
        _openArticle = openArticle;
        _restorePage = restorePage;
        _retryViewer = retryViewer;
        Chat = new ChatViewModel(new SafeMarkdownParser(),
            source => OpenArticleAsync(source.ResolvedTitle +
                (string.IsNullOrWhiteSpace(source.Anchor) ? string.Empty : "#" + source.Anchor)),
            () => IsWiki = true, store);
        Wiki = new WikiViewModel(result =>
        {
            var title = !string.IsNullOrWhiteSpace(result.CanonicalTitle)
                ? result.CanonicalTitle
                : !string.IsNullOrWhiteSpace(result.RedirectTo) ? result.RedirectTo : result.Title;
            return OpenArticleAsync(title +
                (string.IsNullOrWhiteSpace(result.Anchor) ? string.Empty : "#" + result.Anchor));
        });
        Wiki.PropertyChanged += OnWikiChanged;
        Reader = new ReaderPaneViewModel();
        ShowChatCommand = new RelayCommand(_ => { IsWiki = false; _ = _restorePage(); });
        ShowWikiCommand = new RelayCommand(_ => { IsWiki = true; _ = _restorePage(); });
        BackCommand = new RelayCommand(_ => { SetReading(false); _ = _restorePage(); });
        RetryViewerCommand = new RelayCommand(_ => _retryViewer());
    }

    public ChatViewModel Chat { get; }
    public WikiViewModel Wiki { get; }
    public ReaderPaneViewModel Reader { get; }
    public RelayCommand ShowChatCommand { get; }
    public RelayCommand ShowWikiCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand RetryViewerCommand { get; }

    public bool IsWiki
    {
        get => _isWiki;
        set
        {
            if (SetProperty(ref _isWiki, value)) RefreshModes();
        }
    }
    public bool IsChat => !IsWiki;
    public bool IsReading => IsWiki ? _wikiReading : _sourceReading;
    public bool IsChatVisible => IsChat && !IsReading;
    public bool IsSearchVisible => IsWiki && !IsReading && Wiki.Query.Trim().Length > 0;
    public bool ShowSearchStatus => IsSearchVisible && (Wiki.IsSearching || !Wiki.HasResults);
    public bool IsHomeVisible => IsWiki && !IsReading && !IsSearchVisible;
    public bool IsReaderVisible => IsReading || IsHomeVisible;
    public bool IsBackVisible => IsReading;
    public bool IsNewConversationVisible => IsChat && !IsReading;
    public bool IsSearchBoxVisible => IsWiki;
    public string HomeTitle { get => _homeTitle; private set => SetProperty(ref _homeTitle, value); }
    public string ViewerStatus { get => Text.Render(_viewerStatus); private set => SetProperty(ref _viewerStatus, UiTextCatalog.FromSafeMessage(value)); }
    public bool ViewerFailed
    {
        get => _viewerFailed;
        private set
        {
            if (SetProperty(ref _viewerFailed, value))
                OnPropertyChanged(nameof(ShowViewerOverlay));
        }
    }
    public bool ViewerReady
    {
        get => _viewerReady;
        private set
        {
            if (SetProperty(ref _viewerReady, value))
                OnPropertyChanged(nameof(ShowViewerOverlay));
        }
    }
    public bool ShowViewerOverlay => IsReaderVisible && (!ViewerReady || ViewerFailed);

    public void SelectDefault(string page)
    {
        IsWiki = page == "wiki";
        _ = _restorePage();
    }

    public void AttachRuntime(WindowsClientRuntime runtime)
    {
        var gateway = new WindowsChatRuntimeGateway(runtime);
        Chat.AttachRuntime(gateway, gateway);
        Wiki.Attach((query, token) => runtime.Application.Query.SearchAsync(query, cancellationToken: token));
    }

    public void DetachRuntime()
    {
        Chat.DetachRuntime();
        Wiki.Detach();
        Reader.Detach();
        ViewerReady = false;
        ViewerStatus = UiTextCatalog.Message("RestoringTheLocalWiki");
    }

    public void SetHome(string title, bool resetSearch)
    {
        HomeTitle = title;
        if (resetSearch)
        {
            Wiki.ResetForDataSourceChange();
            _wikiReading = false;
            _sourceReading = false;
            RefreshModes();
        }
    }

    public void SetViewerStatus(string message, bool failed = false)
    {
        ViewerStatus = message;
        ViewerFailed = failed;
    }

    internal void HandleViewerProtocolError(string code)
    {
        if (code is "viewer-history-empty" or "viewer-forward-history-empty"
            or "side-button-back-disabled" or "side-button-forward-disabled") return;
        SetViewerStatus(UiTextCatalog.Message("PageContentIsTemporarilyUnavailableRetry"), failed: true);
    }
    public void SetViewerReady(bool ready) => ViewerReady = ready;

    public async Task OpenArticleAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        SetReading(true);
        await _openArticle(title);
    }

    private void SetReading(bool value)
    {
        if (IsWiki) _wikiReading = value;
        else _sourceReading = value;
        RefreshModes();
    }

    private void OnWikiChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(WikiViewModel.IsSearching) or nameof(WikiViewModel.HasResults))
            OnPropertyChanged(nameof(ShowSearchStatus));
        if (args.PropertyName == nameof(WikiViewModel.Query))
        {
            _wikiReading = false;
            RefreshModes();
            if (Wiki.Query.Trim().Length == 0 && IsWiki)
                _ = _restorePage();
        }
    }

    private void RefreshModes()
    {
        OnPropertyChanged(nameof(IsReading));
        OnPropertyChanged(nameof(IsChat));
        OnPropertyChanged(nameof(IsChatVisible));
        OnPropertyChanged(nameof(IsSearchVisible));
        OnPropertyChanged(nameof(ShowSearchStatus));
        OnPropertyChanged(nameof(IsHomeVisible));
        OnPropertyChanged(nameof(IsReaderVisible));
        OnPropertyChanged(nameof(ShowViewerOverlay));
        OnPropertyChanged(nameof(IsBackVisible));
        OnPropertyChanged(nameof(IsNewConversationVisible));
        OnPropertyChanged(nameof(IsSearchBoxVisible));
    }

    public void Dispose()
    {
        Wiki.PropertyChanged -= OnWikiChanged;
        Chat.Dispose();
        Wiki.Dispose();
    }
}
