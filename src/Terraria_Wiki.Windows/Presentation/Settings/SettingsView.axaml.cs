using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed partial class SettingsView : UserControl
{
    private Window? _window;
    private readonly List<Visual> _visibilityAncestors = [];

    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private QuickAccessSettingsViewModel? QuickAccess => (DataContext as SettingsViewModel)?.QuickAccess;

    private async void OnRecordShowHotkey(object? sender, RoutedEventArgs args)
    {
        if (QuickAccess is { } quick) await quick.BeginHotkeyCaptureAsync(show: true);
    }

    private async void OnRecordHideHotkey(object? sender, RoutedEventArgs args)
    {
        if (QuickAccess is { } quick) await quick.BeginHotkeyCaptureAsync(show: false);
    }

    private async void OnCaptureKeyDown(object? sender, KeyEventArgs args)
    {
        if (QuickAccess is not { IsRecordingHotkey: true } quick) return;
        args.Handled = args.Key != Key.Tab;
        await quick.CaptureKeyAsync(args.Key, args.KeyModifiers);
    }

    private async void OnRecorderLostFocus(object? sender, RoutedEventArgs args)
    {
        if (QuickAccess is { } quick) await quick.CancelHotkeyCaptureAsync();
    }

    private async void OnWindowDeactivated(object? sender, EventArgs args)
    {
        if (QuickAccess is { } quick) await quick.CancelHotkeyCaptureAsync();
    }

    private void OnPageVisibilityChanged(object? sender, EventArgs args)
    {
        if (DataContext is not SettingsViewModel settings) return;
        settings.Ai.SetPageActive(IsEffectivelyVisible && settings.IsAiSelected);
        if (!IsEffectivelyVisible) _ = settings.QuickAccess.CancelHotkeyCaptureAsync();
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty) OnPageVisibilityChanged(sender, EventArgs.Empty);
    }

    private void WatchVisibility()
    {
        StopWatchingVisibility();
        _visibilityAncestors.Add(this);
        _visibilityAncestors.AddRange(this.GetVisualAncestors());
        foreach (var visual in _visibilityAncestors) visual.PropertyChanged += OnAncestorPropertyChanged;
        OnPageVisibilityChanged(this, EventArgs.Empty);
    }

    private void StopWatchingVisibility()
    {
        foreach (var visual in _visibilityAncestors) visual.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityAncestors.Clear();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
    {
        base.OnAttachedToVisualTree(args);
        WatchVisibility();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.Deactivated += OnWindowDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        if (_window is not null) _window.Deactivated -= OnWindowDeactivated;
        _window = null;
        StopWatchingVisibility();
        if (DataContext is SettingsViewModel settings) settings.Ai.SetPageActive(false);
        if (QuickAccess is { } quick) _ = quick.CancelHotkeyCaptureAsync();
        base.OnDetachedFromVisualTree(args);
    }
}
