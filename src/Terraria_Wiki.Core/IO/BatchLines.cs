using System.Text;

namespace Terraria_Wiki.Core.IO;

internal sealed class BatchLineWriter : IDisposable
{
    private readonly string _filePath;
    private readonly int _batchSize;
    private readonly List<string> _buffer;
    private readonly object _gate = new();

    public BatchLineWriter(string filePath, int batchSize = 200)
    {
        _filePath = filePath;
        _batchSize = batchSize;
        _buffer = new List<string>(batchSize);
    }

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_gate)
        {
            _buffer.Add(line);
            if (_buffer.Count >= _batchSize) FlushCore();
        }
    }

    public void Flush()
    {
        lock (_gate) FlushCore();
    }

    public void Dispose() => Flush();

    private void FlushCore()
    {
        if (_buffer.Count == 0) return;
        File.AppendAllLines(_filePath, _buffer);
        _buffer.Clear();
    }
}

internal sealed class BatchLineProvider : IDisposable
{
    private readonly StreamReader? _reader;
    private readonly object _readerGate = new();

    public BatchLineProvider(string filePath, int batchSize = 50)
    {
        _ = batchSize;
        if (File.Exists(filePath))
        {
            _reader = new StreamReader(
                new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
        }
    }

    public string? GetNextLine()
    {
        lock (_readerGate)
        {
            while (_reader?.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line)) return line;
            }
            return null;
        }
    }

    public void Dispose()
    {
        lock (_readerGate)
        {
            _reader?.Dispose();
        }
    }
}
