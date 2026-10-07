using Avalonia.Input;
using Terraria_Wiki.Windows.Hosting;
using Terraria_Wiki.Windows.Presentation.Infrastructure;

namespace Terraria_Wiki.Windows.Presentation.Settings;

internal sealed class QuickAccessSettingsViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<QuickAccessPreferences>> _load;
    private readonly Func<QuickAccessPreferences, CancellationToken, Task<bool>> _save;
    private readonly Func<bool, Task<bool>> _suspendHotkeys;
    private bool? _captureShow;
    private bool _captureReady;
    private int _captureVersion;
    private int _pageIndex = 1;
    private string _showHotkey = new QuickAccessPreferences().ShowHotkey;
    private string _hideHotkey = new QuickAccessPreferences().HideHotkey;
    private bool _closeToTray = new QuickAccessPreferences().CloseToTray;
    private bool _topmost = new QuickAccessPreferences().Topmost;
    private string _status = string.Empty;
    private bool _busy;
    private bool _applying;
    private bool _finishingCapture;
    private bool _disposed;
    private int _editVersion;
    private QuickAccessPreferences? _saved;
    private Task? _saveTask;
    private readonly CancellationTokenSource _lifetime = new();

    public QuickAccessSettingsViewModel(
        Func<CancellationToken, Task<QuickAccessPreferences>> load,
        Func<QuickAccessPreferences, CancellationToken, Task<bool>> save,
        Func<bool, Task<bool>>? suspendHotkeys = null)
    {
        _load = load;
        _save = save;
        _suspendHotkeys = suspendHotkeys ?? (_ => Task.FromResult(true));
        RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => !Busy && !IsRecordingHotkey && !_finishingCapture);
        SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => !Busy && !IsRecordingHotkey && !_finishingCapture);
    }

    public int PageIndex { get => _pageIndex; set { if (SetProperty(ref _pageIndex, value)) QueueSave(); } }
    public string ShowHotkey
    {
        get => _showHotkey;
        set { if (SetProperty(ref _showHotkey, value ?? string.Empty)) { OnPropertyChanged(nameof(ShowHotkeyLabel)); QueueSave(); } }
    }
    public string HideHotkey
    {
        get => _hideHotkey;
        set { if (SetProperty(ref _hideHotkey, value ?? string.Empty)) { OnPropertyChanged(nameof(HideHotkeyLabel)); QueueSave(); } }
    }
    public bool IsRecordingHotkey => _captureShow is not null;
    public string ShowHotkeyLabel => Text.Render(_captureShow == true ? UiTextCatalog.Message("PressAShortcut") : DisplayHotkey(ShowHotkey));
    public string HideHotkeyLabel => Text.Render(_captureShow == false ? UiTextCatalog.Message("PressAShortcut") : DisplayHotkey(HideHotkey));
    private static string DisplayHotkey(string value) => string.IsNullOrWhiteSpace(value) ? UiTextCatalog.Message("NotSet") : value;
    public bool CloseToTray { get => _closeToTray; set { if (SetProperty(ref _closeToTray, value)) QueueSave(); } }
    public bool Topmost { get => _topmost; set { if (SetProperty(ref _topmost, value)) QueueSave(); } }
    public string Status { get => Text.Render(_status); private set => SetProperty(ref _status, UiTextCatalog.FromSafeMessage(value)); }
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (!SetProperty(ref _busy, value)) return;
            RefreshCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }

    public async Task BeginHotkeyCaptureAsync(bool show)
    {
        if (Busy || _finishingCapture || _disposed) return;
        if (_saved is null) await RefreshAsync();
        if (Busy || _saved is null || _disposed) return;
        var version = ++_captureVersion;
        _captureShow = show;
        _captureReady = false;
        Status = string.Empty;
        RefreshCaptureProperties();
        try
        {
            var suspended = await _suspendHotkeys(true);
            if (version != _captureVersion) return;
            if (suspended) _captureReady = true;
            else
            {
                await CancelHotkeyCaptureAsync();
                Status = UiTextCatalog.Message("CouldNotStartRecordingTheShortcutRetry");
            }
        }
        catch
        {
            if (version != _captureVersion) return;
            await CancelHotkeyCaptureAsync();
            Status = UiTextCatalog.Message("CouldNotStartRecordingTheShortcutRetry");
        }
    }

    public async Task CaptureKeyAsync(Key key, KeyModifiers modifiers)
    {
        if (!IsRecordingHotkey) return;
        if (key == Key.Escape && modifiers == KeyModifiers.None || key == Key.Tab)
        {
            await CancelHotkeyCaptureAsync();
            return;
        }
        if (!_captureReady || key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var value = string.Empty;
        if (!(modifiers == KeyModifiers.None && key is Key.Back or Key.Delete)
            && !GlobalHotkey.TryFromKey(key, modifiers, out value))
        {
            Status = UiTextCatalog.Message("ThisKeyIsUnsupportedUseALetterNumber");
            return;
        }
        var show = _captureShow == true;
        // Restore registration before saving so conflict detection runs with hotkeys enabled.
        if (!await EndCaptureAsync()) return;
        if (_disposed) return;
        if (show) ShowHotkey = value;
        else HideHotkey = value;
        await SaveAsync();
    }

    public async Task CancelHotkeyCaptureAsync() => await EndCaptureAsync();

    private async Task<bool> EndCaptureAsync()
    {
        if (!IsRecordingHotkey) return !_finishingCapture;
        ++_captureVersion;
        _captureShow = null;
        _captureReady = false;
        _finishingCapture = true;
        RefreshCaptureProperties();
        try
        {
            if (await _suspendHotkeys(false)) return true;
        }
        catch { }
        finally { _finishingCapture = false; RefreshCaptureProperties(); }
        Status = UiTextCatalog.Message("CouldNotRestoreTheOriginalShortcutsRestartThe");
        return false;
    }
    private void RefreshCaptureProperties()
    {
        OnPropertyChanged(nameof(IsRecordingHotkey));
        OnPropertyChanged(nameof(ShowHotkeyLabel));
        OnPropertyChanged(nameof(HideHotkeyLabel));
        RefreshCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    public async Task RefreshAsync()
    {
        if (_saveTask is { } pending) await pending;
        if (Busy || IsRecordingHotkey || _finishingCapture || _disposed) return;
        Busy = true;
        var version = _editVersion;
        try
        {
            var value = await _load(_lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            _saved = value;
            if (version == _editVersion) Apply(value);
            Status = string.Empty;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch { Status = UiTextCatalog.Message("CouldNotReadQuickAskSettings"); }
        finally { Busy = false; }
        if (_saved is not null && Build(_saved) != _saved) _ = SaveAsync();
    }

    private void Apply(QuickAccessPreferences value)
    {
        _applying = true;
        try
        {
            PageIndex = value.DefaultPage == "wiki" ? 1 : 0;
            ShowHotkey = value.ShowHotkey;
            HideHotkey = value.HideHotkey;
            CloseToTray = value.CloseToTray;
            Topmost = value.Topmost;
        }
        finally { _applying = false; }
    }

    private QuickAccessPreferences Build(QuickAccessPreferences existing) => existing with
    {
        DefaultPage = PageIndex == 1 ? "wiki" : "chat",
        ShowHotkey = ShowHotkey.Trim(),
        HideHotkey = HideHotkey.Trim(),
        CloseToTray = CloseToTray,
        Topmost = Topmost,
    };

    private void QueueSave()
    {
        if (_applying || _disposed) return;
        ++_editVersion;
        if (_saved is not null && !IsRecordingHotkey) _ = SaveAsync();
    }

    public Task SaveAsync()
    {
        if (_saveTask is { } pending) return pending;
        if (Busy || IsRecordingHotkey || _finishingCapture || _disposed || _saved is null)
            return Task.CompletedTask;
        Busy = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _saveTask = completion.Task;
        _ = SavePendingAsync(completion);
        return completion.Task;
    }

    private async Task SavePendingAsync(TaskCompletionSource completion)
    {
        await Task.Yield();
        try
        {
            while (!_disposed && _saved is { } saved && Build(saved) != saved)
            {
                var version = _editVersion;
                try
                {
                    // Merge current window position and host-only fields at the existing storage boundary.
                    var existing = await _load(_lifetime.Token);
                    var requested = Build(existing);
                    if (!await _save(requested, _lifetime.Token))
                    {
                        if (version == _editVersion) Apply(saved);
                        Status = UiTextCatalog.Message("ShortcutInvalidOrAlreadyInUseTheOriginal");
                        if (version == _editVersion) break;
                        continue;
                    }
                    _lifetime.Token.ThrowIfCancellationRequested();
                    _saved = requested;
                    Status = string.Empty;
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
                catch
                {
                    if (version == _editVersion) Apply(saved);
                    Status = UiTextCatalog.Message("QuickAskSettingsFailedToSaveOriginalSettings");
                    if (version == _editVersion) break;
                }
            }
        }
        finally { _saveTask = null; Busy = false; completion.TrySetResult(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = CancelHotkeyCaptureAsync();
        _lifetime.Cancel();
    }
}
