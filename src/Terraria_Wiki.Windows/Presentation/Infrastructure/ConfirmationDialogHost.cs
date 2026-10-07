using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Terraria_Wiki.Windows.Presentation.Infrastructure;

// Observes the existing pending confirmation without taking over business state.
internal sealed class ConfirmationDialogHost : IDisposable
{
    private readonly UserControl _view;
    private readonly ObservableObject _model;
    private readonly Func<bool> _pending;
    private readonly Func<(string Title, string Message, string Action, bool Dangerous)> _content;
    private readonly Func<ICommand> _confirm;
    private readonly Action _cancel;
    private ConfirmationOverlay? _dialog;
    private bool _disposed;
    private bool _showing;
    private bool _handled;
    private int _revision;

    internal ConfirmationDialogHost(UserControl view, ObservableObject model, Func<bool> pending,
        Func<(string, string, string, bool)> content, Func<ICommand> confirm, Action cancel)
    {
        _view = view;
        _model = model;
        _pending = pending;
        _content = content;
        _confirm = confirm;
        _cancel = cancel;
        model.PropertyChanged += ModelChanged;
        ScheduleRefresh();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_pending())
        {
            _revision++;
            _handled = false;
            var obsoleteDialog = _dialog;
            if (obsoleteDialog is not null) Dispatcher.UIThread.Post(() => obsoleteDialog.Close(false));
        }
        ScheduleRefresh();
    }
    private void ScheduleRefresh() => Dispatcher.UIThread.Post(Refresh);

    private async void Refresh()
    {
        if (_disposed) return;
        if (!_pending()) { _dialog?.Close(false); return; }
        if (_handled) return;
        if (_dialog is not null) { Update(); return; }
        if (_showing) return;
        if (TopLevel.GetTopLevel(_view) is not Window owner || !owner.IsVisible)
        {
            _cancel();
            return;
        }
        _showing = true;
        var revision = _revision;
        var dialog = new ConfirmationOverlay(_model.Text);
        _dialog = dialog;
        Update();
        try
        {
            var approved = await dialog.ShowAsync(owner);
            if (_disposed || revision != _revision || !_pending()) return;
            var command = _confirm();
            if (approved && command.CanExecute(null)) { _handled = true; command.Execute(null); }
            else _cancel();
        }
        finally
        {
            _dialog = null;
            _showing = false;
            if (!_disposed && !_handled && _pending()) ScheduleRefresh();
        }
    }

    private void Update()
    {
        var content = _content();
        _dialog!.Update(content.Title, content.Message, content.Action, content.Dangerous, _confirm().CanExecute(null));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _model.PropertyChanged -= ModelChanged;
        _dialog?.Close(false);
        if (_pending()) _cancel();
    }
}
