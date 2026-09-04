using System.Diagnostics;
using System.Text;

namespace Terraria_Wiki.Windows.AI;

internal interface IAiSidecarConnection : IAsyncDisposable
{
    ValueTask WriteAsync(byte[] line, CancellationToken cancellationToken = default);
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
    bool HasExited { get; }
    int? ExitCode { get; }
    void KillProcessTree();
}

internal interface IAiSidecarConnectionFactory
{
    Task<IAiSidecarConnection> StartAsync(
        AiRuntimeConfiguration configuration,
        string launcherPath,
        CancellationToken cancellationToken = default);
}

internal sealed class ProcessAiSidecarConnectionFactory : IAiSidecarConnectionFactory
{
    private readonly string? _launcherOverride;

    public ProcessAiSidecarConnectionFactory(string? launcherOverride = null) =>
        _launcherOverride = launcherOverride;

    public Task<IAiSidecarConnection> StartAsync(
        AiRuntimeConfiguration configuration,
        string launcherPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        launcherPath = _launcherOverride ?? launcherPath;
        if (!File.Exists(launcherPath))
            throw new AiRuntimeException("dependency_mismatch");

        var startInfo = CreateStartInfo(configuration, launcherPath);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                throw new AiRuntimeException("runtime_crashed");
            return Task.FromResult<IAiSidecarConnection>(new ProcessAiSidecarConnection(process));
        }
        catch (AiRuntimeException)
        {
            process.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            process.Dispose();
            throw new AiRuntimeException("runtime_crashed", exception);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        AiRuntimeConfiguration configuration,
        string launcherPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = configuration.PythonExecutable,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true),
        };
        startInfo.ArgumentList.Add("-I");
        startInfo.ArgumentList.Add("-B");
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add("utf8");
        startInfo.ArgumentList.Add(launcherPath);
        startInfo.Environment["TERRARIA_AI_API_KEY"] = configuration.ApiKey;
        return startInfo;
    }
}

internal sealed class ProcessAiSidecarConnection : IAiSidecarConnection
{
    private const int MaxCapturedStderrChars = 64 * 1024;
    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly byte[] _readBuffer = new byte[8192];
    private readonly StringBuilder _capturedStderr = new();
    private readonly Task _stderrDrain;
    private int _readOffset;
    private int _readCount;
    private int _disposed;

    public ProcessAiSidecarConnection(Process process)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
        _stderrDrain = DrainStderrAsync(process.StandardError);
    }

    public bool HasExited => _process.HasExited;
    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;
    internal string CapturedStderr => _capturedStderr.ToString();

    public async ValueTask WriteAsync(byte[] line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length > AiSidecarProtocol.MaxLineBytes || line.Length == 0 || line[^1] != (byte)'\n')
            throw new AiRuntimeException("protocol_violation");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _input.WriteAsync(line, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AiRuntimeException("runtime_crashed", exception);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        using var line = new MemoryStream();
        while (true)
        {
            if (_readOffset == _readCount)
            {
                _readCount = await _output.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
                _readOffset = 0;
                if (_readCount == 0)
                {
                    if (line.Length == 0)
                        return null;
                    throw new AiRuntimeException("protocol_violation");
                }
            }

            var newline = Array.IndexOf(_readBuffer, (byte)'\n', _readOffset, _readCount - _readOffset);
            var end = newline >= 0 ? newline : _readCount;
            var count = end - _readOffset;
            if (line.Length + count + (newline >= 0 ? 1 : 0) > AiSidecarProtocol.MaxLineBytes)
                throw new AiRuntimeException("protocol_violation");
            line.Write(_readBuffer, _readOffset, count);
            _readOffset = newline >= 0 ? newline + 1 : _readCount;
            if (newline >= 0)
            {
                try
                {
                    return new UTF8Encoding(false, true).GetString(line.GetBuffer(), 0, checked((int)line.Length));
                }
                catch (DecoderFallbackException exception)
                {
                    throw new AiRuntimeException("protocol_violation", exception);
                }
            }
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _process.WaitForExitAsync(cancellationToken);

    public void KillProcessTree()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cleanup is best effort and must not replace the stable Runtime failure.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            _input.Dispose();
            _output.Dispose();
            await _stderrDrain.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _writeGate.Dispose();
            _process.Dispose();
        }
    }

    private async Task DrainStderrAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                    return;
                var remaining = MaxCapturedStderrChars - _capturedStderr.Length;
                if (remaining > 0)
                    _capturedStderr.Append(buffer, 0, Math.Min(read, remaining));
            }
        }
        catch
        {
        }
    }
}
