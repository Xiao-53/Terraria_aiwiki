using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Terraria_Wiki.Windows.Hosting;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Terraria_Wiki.Windows.Presentation.Favorites;
using Terraria_Wiki.Windows.Presentation.Library;
using Terraria_Wiki.Windows.Presentation.Management;
using Terraria_Wiki.Windows.Presentation.Reader;
using Terraria_Wiki.Windows.Presentation.Settings;
using Terraria_Wiki.Windows.Presentation.Wiki;

namespace Terraria_Wiki.Windows.Presentation.Shell;

internal sealed partial class ShellView : UserControl
{
    internal const double MainPaneMinWidth = 460;
    internal const double ReaderPaneMinWidth = 420;

    private Grid _workspace = null!;
    private Grid _wikiHost = null!;
    private Grid _favoritesHost = null!;
    private Grid _libraryHost = null!;
    private Grid _managementHost = null!;
    private Grid _settingsHost = null!;
    private ReaderView _readerHost = null!;
    private Control? _wikiView;
    private Control? _favoritesView;
    private Control? _libraryView;
    private Control? _managementView;
    private Control? _settingsView;
    private bool _inlineLayoutApplied;
    private TopLevel? _topLevel;
    private ShellViewModel? _subscribedViewModel;

    public ShellView()
    {
        using var timing = WindowsStartupInvestigation.Measure(StartupInvestigationPhase.ShellViewConstructor);
        AvaloniaXamlLoader.Load(this);
        _workspace = this.FindControl<Grid>("WorkspaceGrid")!;
        _wikiHost = this.FindControl<Grid>("WikiHost")!;
        _favoritesHost = this.FindControl<Grid>("FavoritesHost")!;
        _libraryHost = this.FindControl<Grid>("LibraryHost")!;
        _managementHost = this.FindControl<Grid>("ManagementHost")!;
        _settingsHost = this.FindControl<Grid>("SettingsHost")!;
        _readerHost = this.FindControl<ReaderView>("ReaderHost")!;
        var wikiSearchInput = this.FindControl<TextBox>("WikiSearchInput")!;
        wikiSearchInput.AddHandler(
            InputElement.KeyDownEvent,
            OnWikiSearchKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public NativeWebView WebView => _readerHost.WebView;

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_subscribedViewModel is not null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = DataContext as ShellViewModel;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateLazyViewDataContexts();
        EnsureCurrentSectionView();
        UpdateResponsiveLayout();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.PropertyChanged += OnTopLevelPropertyChanged;
            _topLevel.KeyDown += OnTopLevelKeyDown;
        }
        UpdateResponsiveLayout();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_topLevel is not null)
        {
            _topLevel.PropertyChanged -= OnTopLevelPropertyChanged;
            _topLevel.KeyDown -= OnTopLevelKeyDown;
            _topLevel = null;
        }
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TopLevel.ClientSizeProperty)
            UpdateResponsiveLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.CurrentSection))
            EnsureCurrentSectionView();

        if (args.PropertyName is nameof(ShellViewModel.IsWideLayout)
            or nameof(ShellViewModel.IsReaderVisible)
            or nameof(ShellViewModel.CurrentSection)
            or nameof(ShellViewModel.ReaderState)
            or nameof(ShellViewModel.WikiMode)
            or nameof(ShellViewModel.IsWikiSurfaceVisible))
        {
            UpdateResponsiveLayout();
        }
    }

    private void EnsureCurrentSectionView()
    {
        if (_subscribedViewModel is not { } viewModel)
            return;

        switch (viewModel.CurrentSection)
        {
            case ShellSection.Wiki:
                AttachLazyView(_wikiHost, ref _wikiView, static () => new WikiView(), viewModel.Wiki);
                break;
            case ShellSection.Favorites:
                AttachLazyView(_favoritesHost, ref _favoritesView, static () => new FavoritesView(), viewModel.Favorites);
                break;
            case ShellSection.Library:
                AttachLazyView(_libraryHost, ref _libraryView, static () => new WikiLibraryView(), viewModel.Library);
                break;
            case ShellSection.Management:
                AttachLazyView(_managementHost, ref _managementView, static () => new WikiManagementView(), viewModel.Management);
                break;
            case ShellSection.Settings:
                AttachLazyView(_settingsHost, ref _settingsView, static () => new SettingsView(), viewModel.Settings);
                break;
        }
    }

    private void UpdateLazyViewDataContexts()
    {
        if (_subscribedViewModel is not { } viewModel)
            return;
        if (_wikiView is not null)
            _wikiView.DataContext = viewModel.Wiki;
        if (_favoritesView is not null)
            _favoritesView.DataContext = viewModel.Favorites;
        if (_libraryView is not null)
            _libraryView.DataContext = viewModel.Library;
        if (_managementView is not null)
            _managementView.DataContext = viewModel.Management;
        if (_settingsView is not null)
            _settingsView.DataContext = viewModel.Settings;
    }

    private static void AttachLazyView(
        Grid host,
        ref Control? view,
        Func<Control> create,
        object dataContext)
    {
        view ??= create();
        view.DataContext = dataContext;
        if (!host.Children.Contains(view))
            host.Children.Add(view);
    }

    private void UpdateResponsiveLayout()
    {
        if (DataContext is not ShellViewModel viewModel)
            return;
        var width = _topLevel?.ClientSize.Width ?? Bounds.Width;
        if (width > 0)
            viewModel.SetViewportWidth(width);

        if (viewModel.IsWikiSurfaceVisible)
        {
            _inlineLayoutApplied = false;
            _workspace.ColumnDefinitions[0].MinWidth = 0;
            _workspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _workspace.ColumnDefinitions[1].Width = new GridLength(0);
            _workspace.ColumnDefinitions[2].MinWidth = 0;
            _workspace.ColumnDefinitions[2].Width = new GridLength(0);
            Grid.SetColumn(_readerHost, 0);
            Grid.SetColumnSpan(_readerHost, 3);
            _readerHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            _readerHost.Width = double.NaN;
            _readerHost.Margin = new Thickness(0);
            _readerHost.ZIndex = 1;
        }
        else if (viewModel.IsReaderInlineVisible)
        {
            if (!_inlineLayoutApplied)
            {
                _workspace.ColumnDefinitions[0].MinWidth = MainPaneMinWidth;
                _workspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                _workspace.ColumnDefinitions[1].Width = new GridLength(12);
                _workspace.ColumnDefinitions[2].MinWidth = ReaderPaneMinWidth;
                _workspace.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                _inlineLayoutApplied = true;
            }
            Grid.SetColumn(_readerHost, 2);
            Grid.SetColumnSpan(_readerHost, 1);
            _readerHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            _readerHost.Width = double.NaN;
            _readerHost.Margin = new Thickness(0);
            _readerHost.ZIndex = 1;
        }
        else
        {
            _inlineLayoutApplied = false;
            _workspace.ColumnDefinitions[0].MinWidth = 0;
            _workspace.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _workspace.ColumnDefinitions[1].Width = new GridLength(0);
            _workspace.ColumnDefinitions[2].MinWidth = 0;
            _workspace.ColumnDefinitions[2].Width = new GridLength(0);
            Grid.SetColumn(_readerHost, 0);
            Grid.SetColumnSpan(_readerHost, 3);
            _readerHost.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            _readerHost.Width = Math.Min(720, Math.Max(520, _workspace.Bounds.Width * 0.82));
            _readerHost.Margin = new Thickness(12);
            _readerHost.ZIndex = 3;
        }
    }

    private void OnWikiSearchKeyDown(object? sender, KeyEventArgs args)
    {
        var isComposing = sender is TextBox searchBox
                          && searchBox.GetVisualDescendants()
                              .OfType<TextPresenter>()
                              .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        if (args.Key != Key.Enter
            || args.KeyModifiers.HasFlag(KeyModifiers.Shift)
            || isComposing)
        {
            return;
        }
        if (DataContext is ShellViewModel viewModel
            && viewModel.Wiki.SearchCommand.CanExecute(null))
        {
            viewModel.Wiki.SearchCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape
            && DataContext is ShellViewModel viewModel
            && viewModel.TryCloseOverlay())
        {
            args.Handled = true;
        }
    }

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is ShellViewModel viewModel)
            viewModel.CloseReader();
        args.Handled = true;
    }
}
