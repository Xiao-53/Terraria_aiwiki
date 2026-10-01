using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Input;
using InputKey = Avalonia.Input.Key;
using Avalonia.Threading;

namespace Terraria_Wiki.Windows.Hosting;

internal readonly record struct GlobalHotkey(uint Modifiers, uint Key)
{
    public static bool TryParse(string? value, out GlobalHotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        uint modifiers = 0;
        uint key = 0;
        foreach (var raw in value.Split('+'))
        {
            var part = raw.Trim().ToUpperInvariant();
            var modifier = part switch
            {
                "ALT" => 1u,
                "CTRL" or "CONTROL" => 2u,
                "SHIFT" => 4u,
                "WIN" => 8u,
                _ => 0u,
            };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0)
                    return false;
                modifiers |= modifier;
            }
            else if (key == 0 && part.Length == 1 && char.IsAsciiLetterOrDigit(part[0]))
                key = part[0];
            else if (key == 0 && part.Length >= 2 && part[0] == 'F'
                     && int.TryParse(part.AsSpan(1), out var number) && number is >= 1 and <= 24)
                key = (uint)(0x70 + number - 1);
            else if (key == 0 && NamedKey(part) is { } named && named != 0)
                key = named;
            else
                return false;
        }
        if (key == 0)
            return false;
        hotkey = new GlobalHotkey(modifiers | 0x4000, key); // MOD_NOREPEAT
        return true;
    }

    private static uint NamedKey(string name) => name switch
    {
        "ENTER" => 0x0D, "SPACE" => 0x20, "TAB" => 0x09, "ESCAPE" => 0x1B,
        "BACK" => 0x08, "DELETE" => 0x2E, "INSERT" => 0x2D,
        "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22,
        "LEFT" => 0x25, "UP" => 0x26, "RIGHT" => 0x27, "DOWN" => 0x28,
        _ => 0,
    };

    internal static bool TryFromKey(InputKey key, KeyModifiers modifiers, out string value)
    {
        var name = key switch
        {
            >= InputKey.D0 and <= InputKey.D9 => ((int)(key - InputKey.D0)).ToString(),
            InputKey.Return => "Enter",
            _ => key.ToString(),
        };
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(name);
        value = string.Join('+', parts);
        return TryParse(value, out var parsed) && parsed.Key != 0;
    }
}

internal sealed class GlobalHotkeys : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint WmWork = 0x8001;
    private const uint WmQuit = 0x0012;
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly Thread _thread;
    private readonly TaskCompletionSource<uint> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action _show;
    private readonly Action _hide;
    private uint _threadId;
    private GlobalHotkey _showKey;
    private GlobalHotkey _hideKey;
    private bool _disposed;
    private volatile bool _suspended;
    private long _activationVersion;

    public GlobalHotkeys(Action show, Action hide)
    {
        _show = show;
        _hide = hide;
        _thread = new Thread(Run) { IsBackground = true, Name = "Quick Ask hotkeys" };
        _thread.Start();
    }

    public async Task<bool> TrySetAsync(string show, string hide)
    {
        if (!GlobalHotkey.TryParse(show, out var showKey)
            || !GlobalHotkey.TryParse(hide, out var hideKey)
            || (showKey.Key != 0 && showKey == hideKey))
            return false;
        await _started.Task;
        if (_disposed)
            return false;
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            var oldShow = _showKey;
            var oldHide = _hideKey;
            Interlocked.Increment(ref _activationVersion);
            UnregisterHotKey(0, 1);
            UnregisterHotKey(0, 2);
            if (_suspended || (Register(showKey, 1) && Register(hideKey, 2)))
            {
                _showKey = showKey;
                _hideKey = hideKey;
                result.TrySetResult(true);
                return;
            }
            UnregisterHotKey(0, 1);
            UnregisterHotKey(0, 2);
            Register(oldShow, 1);
            Register(oldHide, 2);
            result.TrySetResult(false);
        });
        PostThreadMessage(_threadId, WmWork, 0, 0);
        return await result.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public async Task<bool> SetSuspendedAsync(bool suspended)
    {
        await _started.Task;
        if (_disposed) return false;
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            Interlocked.Increment(ref _activationVersion);
            _suspended = suspended;
            UnregisterHotKey(0, 1);
            UnregisterHotKey(0, 2);
            if (suspended) result.TrySetResult(true);
            else
            {
                // Try both independently so one occupied binding does not disable the other.
                var showRestored = Register(_showKey, 1);
                var hideRestored = Register(_hideKey, 2);
                result.TrySetResult(showRestored && hideRestored);
            }
        });
        PostThreadMessage(_threadId, WmWork, 0, 0);
        return await result.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static bool Register(GlobalHotkey key, int id) =>
        key.Key == 0 || RegisterHotKey(0, id, key.Modifiers, key.Key);

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, 0); // create the thread message queue
        _started.TrySetResult(_threadId);
        while (GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.message == WmWork)
            {
                while (_work.TryDequeue(out var action)) action();
            }
            else if (message.message == WmHotkey)
            {
                if (_suspended) continue;
                var action = message.wParam == 1 ? _show : _hide;
                var version = Interlocked.Read(ref _activationVersion);
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed && !_suspended && version == Interlocked.Read(ref _activationVersion)) action();
                });
            }
        }
        UnregisterHotKey(0, 1);
        UnregisterHotKey(0, 2);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, 0, 0);
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
        public uint privateValue;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
