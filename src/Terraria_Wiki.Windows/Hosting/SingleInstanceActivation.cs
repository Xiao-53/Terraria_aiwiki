using System.IO.Pipes;
using Avalonia.Threading;

namespace Terraria_Wiki.Windows.Hosting;

internal static class SingleInstanceActivation
{
    private const string MutexName = @"Local\TerrariaAIWiki.Windows.Activation";
    private const string PipeName = "TerrariaAIWiki.Windows.Activation";
    private static readonly object Gate = new();
    private static Mutex? _owner;
    private static Action? _handler;
    private static bool _pending;

    public static bool TryNotifyExisting()
    {
        var mutex = new Mutex(true, MutexName, out var created);
        if (created)
        {
            _owner = mutex;
            _ = ListenAsync();
            return false;
        }
        mutex.Dispose();
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch { return false; } // existing runtime data lock remains authoritative
    }

    public static void SetHandler(Action? handler)
    {
        lock (Gate)
        {
            _handler = handler;
            if (!_pending || handler is null) return;
            _pending = false;
            Dispatcher.UIThread.Post(handler);
        }
    }

    private static async Task ListenAsync()
    {
        while (_owner is not null)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                if (server.ReadByte() != 1) continue;
                lock (Gate)
                {
                    if (_handler is { } handler) Dispatcher.UIThread.Post(handler);
                    else _pending = true;
                }
            }
            catch { await Task.Delay(250); }
        }
    }
}
