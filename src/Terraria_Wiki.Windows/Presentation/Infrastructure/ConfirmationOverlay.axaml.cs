using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Terraria_Wiki.Windows.Presentation.Infrastructure;

internal sealed partial class ConfirmationOverlay : UserControl
{
    private static readonly ConditionalWeakTable<Window, ConfirmationOverlay> Active = new();
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<(NativeWebView View, bool Visible)> _webViews = [];
    private Window? _owner;
    private Control? _background;
    private OverlayLayer? _layer;
    private IInputElement? _previousFocus;
    private bool _backgroundEnabled;

    public ConfirmationOverlay(UiTextCatalog text)
    {
        AvaloniaXamlLoader.Load(this);
        text.Attach(Resources);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
    }

    internal void Update(string title, string message, string action, bool dangerous, bool canConfirm)
    {
        this.FindControl<TextBlock>("Heading")!.Text = title;
        this.FindControl<TextBlock>("Message")!.Text = message;
        var button = this.FindControl<Button>("ConfirmButton")!;
        button.Content = action;
        button.Classes.Set("danger-action", dangerous);
        button.Classes.Set("primary-action", !dangerous);
        button.IsEnabled = canConfirm;
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetName(button, action);
    }

    internal Task<bool> ShowAsync(Window owner)
    {
        if (_completion.Task.IsCompleted || _owner is not null) return _completion.Task;
        if (!owner.IsVisible || owner.Content is not Control background || Active.TryGetValue(owner, out _))
        {
            Close(false);
            return _completion.Task;
        }
        var layer = OverlayLayer.GetOverlayLayer(background);
        if (layer is null) { Close(false); return _completion.Task; }
        _owner = owner;
        _background = background;
        _layer = layer;
        _backgroundEnabled = background.IsEnabled;
        _previousFocus = owner.FocusManager?.GetFocusedElement();
        Active.Add(owner, this);
        // Native WebView HWNDs cannot safely share an Avalonia overlay's airspace.
        foreach (var view in background.GetVisualDescendants().OfType<NativeWebView>())
        {
            _webViews.Add((view, view.IsVisible));
            view.SetCurrentValue(IsVisibleProperty, false);
        }
        background.SetCurrentValue(IsEnabledProperty, false);
        ZIndex = int.MaxValue;
        Resize();
        layer.Children.Add(this);
        layer.SizeChanged += LayerSizeChanged;
        owner.Closed += OwnerClosed;
        background.DetachedFromVisualTree += BackgroundDetached;
        owner.AddHandler(KeyDownEvent, OnOwnerKeyDown, RoutingStrategies.Tunnel);
        owner.AddHandler(GotFocusEvent, OnOwnerGotFocus, RoutingStrategies.Tunnel);
        Dispatcher.UIThread.Post(() => { if (_owner is not null) CancelAction.Focus(); }, DispatcherPriority.Input);
        return _completion.Task;
    }

    private Button CancelAction => this.FindControl<Button>("CancelButton")!;
    private Button ConfirmAction => this.FindControl<Button>("ConfirmButton")!;
    private void Resize() { Width = _layer!.Bounds.Width; Height = _layer.Bounds.Height; }
    private void LayerSizeChanged(object? sender, SizeChangedEventArgs args) => Resize();
    private void OwnerClosed(object? sender, EventArgs args) => Close(false);
    private void BackgroundDetached(object? sender, VisualTreeAttachmentEventArgs args) => Close(false);

    private void OnOwnerGotFocus(object? sender, RoutedEventArgs args)
    {
        if (args.Source is Visual source && !ReferenceEquals(source, this) && !this.IsVisualAncestorOf(source))
            CancelAction.Focus();
    }

    private void OnOwnerKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape) { args.Handled = true; Close(false); return; }
        if (args.Key == Key.Tab)
        {
            args.Handled = true;
            var focus = _owner?.FocusManager?.GetFocusedElement();
            if (ReferenceEquals(focus, CancelAction) && ConfirmAction.IsEnabled) ConfirmAction.Focus();
            else CancelAction.Focus();
        }
    }

    internal void Close(bool approved)
    {
        if (_completion.Task.IsCompleted) return;
        if (_owner is { } owner)
        {
            owner.RemoveHandler(KeyDownEvent, OnOwnerKeyDown);
            owner.RemoveHandler(GotFocusEvent, OnOwnerGotFocus);
            owner.Closed -= OwnerClosed;
            _background!.DetachedFromVisualTree -= BackgroundDetached;
            _layer!.SizeChanged -= LayerSizeChanged;
            _layer.Children.Remove(this);
            _background.SetCurrentValue(IsEnabledProperty, _backgroundEnabled);
            foreach (var (view, visible) in _webViews) view.SetCurrentValue(IsVisibleProperty, visible);
            _webViews.Clear();
            Active.Remove(owner);
            _owner = null;
            if (owner.IsVisible) _previousFocus?.Focus();
        }
        _completion.TrySetResult(approved);
    }

    private void CancelClicked(object? sender, RoutedEventArgs args) => Close(false);
    private void ConfirmClicked(object? sender, RoutedEventArgs args) => Close(true);
}
