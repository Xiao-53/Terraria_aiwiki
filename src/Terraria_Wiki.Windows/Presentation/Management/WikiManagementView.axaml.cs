using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Management;

internal sealed partial class WikiManagementView : UserControl
{
    private WikiManagementViewModel? _approvalModel;
    private ConfirmationDialogHost? _confirmationHost;
    private ConfirmationOverlay? _fullScanOverlay;
    private readonly List<Visual> _visibilityAncestors = [];

    public WikiManagementView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            _fullScanOverlay?.Close(false);
            _confirmationHost?.Dispose();
            _confirmationHost = null;
            _approvalModel?.Notification.SetPageActive(false);
            if (_approvalModel is not null) _approvalModel.FullScanApproval = null;
            _approvalModel = DataContext as WikiManagementViewModel;
            if (_approvalModel is not null) _approvalModel.FullScanApproval = RequestFullScanApprovalAsync;
            RefreshNotificationVisibility();
            AttachConfirmationHost();
        };
        AttachedToVisualTree += (_, _) => { WatchVisibility(); AttachConfirmationHost(); };
        DetachedFromVisualTree += (_, _) =>
        {
            StopWatchingVisibility();
            _approvalModel?.Notification.SetPageActive(false);
            _fullScanOverlay?.Close(false);
            _confirmationHost?.Dispose();
            _confirmationHost = null;
        };
    }

    private void RefreshNotificationVisibility()
        => _approvalModel?.Notification.SetPageActive(IsEffectivelyVisible);

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == IsVisibleProperty) RefreshNotificationVisibility();
    }

    private void WatchVisibility()
    {
        StopWatchingVisibility();
        _visibilityAncestors.Add(this);
        _visibilityAncestors.AddRange(this.GetVisualAncestors());
        foreach (var visual in _visibilityAncestors) visual.PropertyChanged += OnAncestorPropertyChanged;
        RefreshNotificationVisibility();
    }

    private void StopWatchingVisibility()
    {
        foreach (var visual in _visibilityAncestors) visual.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityAncestors.Clear();
    }

    private void AttachConfirmationHost()
    {
        if (_confirmationHost is not null || _approvalModel is not { } model || TopLevel.GetTopLevel(this) is null) return;
        _confirmationHost = new ConfirmationDialogHost(this, model, () => model.IsConfirmationVisible,
            () => (model.ConfirmationTitle, model.ConfirmationMessage, model.ConfirmationActionText, model.IsDangerousConfirmation),
            () => model.ConfirmCommand, model.CancelConfirmation);
    }

    private Task<bool> RequestFullScanApprovalAsync(string reason, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (_approvalModel is null || TopLevel.GetTopLevel(this) is not Window owner || !owner.IsVisible)
                {
                    completion.TrySetResult(false);
                    return;
                }
                var text = _approvalModel.Text;
                var dialog = new ConfirmationOverlay(text);
                _fullScanOverlay = dialog;
                void RefreshText(object? sender, EventArgs args) => dialog.Update(
                    text.Get("FullScanConfirmationTitle"), text.Get("FullScanConfirmationMessage"),
                    text.Get("ContinueFullScan"), false, true);
                RefreshText(null, EventArgs.Empty);
                text.LanguageChanged += RefreshText;
                try
                {
                    using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
                    var approved = await dialog.ShowAsync(owner);
                    token.ThrowIfCancellationRequested();
                    completion.TrySetResult(approved);
                }
                finally
                {
                    text.LanguageChanged -= RefreshText;
                    dialog.Close(false);
                    if (ReferenceEquals(_fullScanOverlay, dialog)) _fullScanOverlay = null;
                }
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }
}
