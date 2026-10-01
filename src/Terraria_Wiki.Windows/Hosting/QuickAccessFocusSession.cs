using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Terraria_Wiki.Windows.Hosting;

internal enum QuickAccessShowIntent { GlobalHotkey, Tray }

internal readonly record struct QuickAccessReturnTarget(
    nint Handle, uint ProcessId, uint ThreadId, long ProcessStarted, string WindowClass);

// One visible round, with replaceable native operations for tests that never touch real windows.
internal sealed class QuickAccessFocusSession(
    Func<nint> foreground,
    Func<nint, QuickAccessReturnTarget?> inspect,
    Func<nint, bool> ownsWindow,
    Func<nint, bool> restore)
{
    private QuickAccessReturnTarget? _returnTarget;
    private bool _visible;
    private bool _temporaryTopmost;
    private bool _preferredTopmost;
    private long _focusVersion;

    public bool EffectiveTopmost => _temporaryTopmost || _preferredTopmost;
    public long FocusVersion => _focusVersion;

    public bool Show(QuickAccessShowIntent intent, bool preferredTopmost)
    {
        InvalidateFocus();
        _preferredTopmost = preferredTopmost;
        if (!_visible)
        {
            _returnTarget = intent == QuickAccessShowIntent.GlobalHotkey ? inspect(foreground()) : null;
            _visible = true;
        }
        if (intent == QuickAccessShowIntent.Tray)
        {
            _returnTarget = null;
            _temporaryTopmost = false;
        }
        else _temporaryTopmost = true;
        return intent == QuickAccessShowIntent.Tray;
    }

    public void ApplyTopmostPreference(bool value) => _preferredTopmost = value;
    public void InvalidateFocus() => ++_focusVersion;
    public bool CanFocus(long version, bool active) =>
        _visible && active && version == _focusVersion && ownsWindow(foreground());

    public void Hide(Action hide)
    {
        if (!_visible) return;
        var target = ownsWindow(foreground()) ? _returnTarget : null;
        Clear();
        hide();
        // Inspect again after hiding: closed, reused, minimized or inaccessible targets are skipped.
        if (target is { } expected && inspect(expected.Handle) == expected)
            restore(expected.Handle); // Best effort, once. A system refusal never retries.
    }

    public void Clear()
    {
        InvalidateFocus();
        _visible = false;
        _temporaryTopmost = false;
        _returnTarget = null;
    }
}

internal static class QuickAccessNativeFocus
{
    public static nint Foreground() => GetForegroundWindow();

    public static QuickAccessReturnTarget? Inspect(nint window)
    {
        if (window == 0 || !IsWindow(window) || !IsWindowVisible(window) || IsIconic(window)
            || window == GetDesktopWindow() || window == GetShellWindow()) return null;
        var thread = GetWindowThreadProcessId(window, out var processId);
        if (thread == 0 || processId == 0 || processId == Environment.ProcessId) return null;
        var className = new StringBuilder(256);
        if (GetClassName(window, className, className.Capacity) == 0) return null;
        var name = className.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var started = process.StartTime.ToUniversalTime().Ticks;
            // Process inspection may race window destruction; never retain an unverified identity.
            if (!IsWindow(window) || GetWindowThreadProcessId(window, out var currentId) != thread
                || currentId != processId) return null;
            return new(window, processId, thread, started, name);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    public static bool BelongsTo(nint foreground, nint host) =>
        foreground != 0 && host != 0 && (foreground == host
            || GetAncestor(foreground, 2) == host || GetAncestor(foreground, 3) == host);

    public static bool Restore(nint window) => SetForegroundWindow(window);

    public static void RaiseWithoutActivation(nint window)
    {
        if (window != 0)
            SetWindowPos(window, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder className, int count);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter,
        int x, int y, int width, int height, uint flags);
}
