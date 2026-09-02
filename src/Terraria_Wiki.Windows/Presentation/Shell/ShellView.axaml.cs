using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Terraria_Wiki.Windows.Presentation.Reader;

namespace Terraria_Wiki.Windows.Presentation.Shell;

internal sealed partial class ShellView : UserControl
{
    internal const double MainPaneMinWidth = 460;
    internal const double ReaderPaneMinWidth = 420;

    private Grid _workspace = null!;
    private ReaderView _readerHost = null!;
    private bool _inlineLayoutApplied;
    private TopLevel? _topLevel;
    private ShellViewModel? _subscribedViewModel;

    public ShellView()
    {
        AvaloniaXamlLoader.Load(this);
        _workspace = this.FindControl<Grid>("WorkspaceGrid")!;
        _readerHost = this.FindControl<ReaderView>("ReaderHost")!;
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
        if (args.PropertyName is nameof(ShellViewModel.IsWideLayout)
            or nameof(ShellViewModel.IsReaderVisible)
            or nameof(ShellViewModel.CurrentSection)
            or nameof(ShellViewModel.ReaderState))
        {
            UpdateResponsiveLayout();
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (DataContext is not ShellViewModel viewModel)
            return;
        var width = _topLevel?.ClientSize.Width ?? Bounds.Width;
        if (width > 0)
            viewModel.SetViewportWidth(width);

        if (viewModel.IsReaderInlineVisible)
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
