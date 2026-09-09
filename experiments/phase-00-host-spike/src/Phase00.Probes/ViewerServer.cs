using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using SQLite;

namespace Phase00.Probes;

/// <summary>
/// Small loopback transport used only by the Phase 00 host spike. It binds an
/// ephemeral IPv4 loopback port by default and intentionally exposes only the
/// GET/HEAD routes required by the existing Viewer.
/// </summary>
public sealed class ViewerServer : IAsyncDisposable
{
    private readonly ViewerServerOptions _options;
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _stopSource;
    private Task? _acceptTask;
    private int _port;
    private readonly ConcurrentDictionary<Task, byte> _clientTasks = new();
    private readonly ConcurrentDictionary<string, int> _requestCounts = new(StringComparer.Ordinal);

    public ViewerServer(ViewerServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.StaticRoot) || !Directory.Exists(_options.StaticRoot))
            throw new DirectoryNotFoundException($"Viewer static root does not exist: {_options.StaticRoot}");
        if (_options.Port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), _options.Port, "Viewer port must be between 0 and 65535.");
    }

    public bool IsRunning { get { lock (_gate) return _listener is not null; } }
    public int Port { get { lock (_gate) return _port; } }
    public int ActiveClientCount => _clientTasks.Count;
    public int GetRequestCount(string path) => _requestCounts.TryGetValue(path, out var count) ? count : 0;
    public Uri? BaseUri { get { lock (_gate) return _listener is null ? null : new Uri($"http://127.0.0.1:{_port}/"); } }

    public Task<ViewerServerStatus> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_listener is not null)
                return Task.FromResult(new ViewerServerStatus(true, _port, BaseUri!.ToString(), null));

            var listener = new TcpListener(IPAddress.Loopback, _options.Port);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                listener.Stop();
                throw new ViewerServerStartException(_options.Port, ex);
            }

            _listener = listener;
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _acceptTask = AcceptLoopAsync(listener, _stopSource.Token);
            return Task.FromResult(new ViewerServerStatus(true, _port, $"http://127.0.0.1:{_port}/", null));
        }
    }

    public async Task StopAsync()
    {
        Task? acceptTask;
        CancellationTokenSource? source;
        lock (_gate)
        {
            if (_listener is null) return;
            source = _stopSource;
            acceptTask = _acceptTask;
            _listener.Stop();
            _listener = null;
            _port = 0;
            _acceptTask = null;
            _stopSource = null;
        }

        try { source?.Cancel(); } catch (ObjectDisposedException) { }
        if (acceptTask is not null)
        {
            try { await acceptTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        while (!_clientTasks.IsEmpty)
        {
            var clients = _clientTasks.Keys.ToArray();
            try { await Task.WhenAll(clients).ConfigureAwait(false); } catch { /* individual client errors are handled below */ }
        }
        source?.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (SocketException) { continue; }

            var clientTask = ProcessClientAsync(client, cancellationToken);
            _clientTasks.TryAdd(clientTask, 0);
            _ = clientTask.ContinueWith(
                static (completed, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(completed, out _),
                _clientTasks,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken serverCancellation)
    {
        using (client)
        {
            client.NoDelay = true;
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
            requestCancellation.CancelAfter(_options.RequestTimeout);
            await using var stream = client.GetStream();
            ViewerHttpRequest? request = null;
            ViewerHttpResponse? response = null;
            try
            {
                request = await ReadRequestAsync(stream, requestCancellation.Token).ConfigureAwait(false);
                response = await RouteAsync(request, requestCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A disconnected WebView or a stopped server is not an error
                // worth surfacing as a process-level failure.
                return;
            }
            catch (Exception ex)
            {
                _options.ErrorSink?.Invoke(ex);
                response = ViewerHttpResponse.Text(500, "Internal viewer error", "text/plain; charset=utf-8");
            }

            if (response is not null)
            {
                try
                {
                    await WriteResponseAsync(stream, request?.Method ?? "GET", response, CancellationToken.None).ConfigureAwait(false);
                }
                catch { /* client may have disconnected */ }
            }
        }
    }

    private async Task<ViewerHttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new MemoryStream();
        var one = new byte[1];
        while (bytes.Length <= _options.MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(one.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new IOException("Client disconnected before sending a request.");
            bytes.WriteByte(one[0]);
            if (bytes.Length >= 4)
            {
                var tail = bytes.GetBuffer();
                var length = (int)bytes.Length;
                if (tail[length - 4] == '\r' && tail[length - 3] == '\n' && tail[length - 2] == '\r' && tail[length - 1] == '\n') break;
            }
        }

        if (bytes.Length > _options.MaxHeaderBytes)
            return new ViewerHttpRequest("", "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), false, "request-too-large");

        var text = Encoding.ASCII.GetString(bytes.ToArray());
        var lines = text.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0) return new ViewerHttpRequest("", "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), false, "malformed-request");
        var requestParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length != 3 || !requestParts[2].StartsWith("HTTP/1.", StringComparison.OrdinalIgnoreCase))
            return new ViewerHttpRequest("", "", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), false, "malformed-request");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) break;
            var colon = line.IndexOf(':');
            if (colon <= 0) return new ViewerHttpRequest("", "", headers, false, "malformed-header");
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return new ViewerHttpRequest(requestParts[0], requestParts[1], headers, true, null);
    }

    private async Task<ViewerHttpResponse> RouteAsync(ViewerHttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.IsWellFormed)
            return ViewerHttpResponse.Text(400, request.Error ?? "Bad request", "text/plain; charset=utf-8");

        if (!string.Equals(request.Headers.GetValueOrDefault("Host"), $"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Headers.GetValueOrDefault("Host"), "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            return ViewerHttpResponse.Text(400, "Host is not allowed", "text/plain; charset=utf-8");

        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            return ViewerHttpResponse.Text(405, "Method not allowed", "text/plain; charset=utf-8", new Dictionary<string, string> { ["Allow"] = "GET, HEAD" });

        string path;
        try
        {
            var target = request.Target;
            if (target.Contains("://", StringComparison.Ordinal))
                return ViewerHttpResponse.Text(400, "Absolute request targets are not allowed", "text/plain; charset=utf-8");
            var queryIndex = target.IndexOf('?');
            var rawPath = queryIndex >= 0 ? target[..queryIndex] : target;
            path = Uri.UnescapeDataString(rawPath);
        }
        catch (UriFormatException)
        {
            return ViewerHttpResponse.Text(400, "Invalid URL", "text/plain; charset=utf-8");
        }

        if (!path.StartsWith('/') || path.Contains('\0') || path.Contains('\\') || path.Contains(':'))
            return ViewerHttpResponse.Text(400, "Invalid path", "text/plain; charset=utf-8");
        if (path.Split('/').Any(x => x is "." or ".."))
            return ViewerHttpResponse.Text(400, "Path traversal is not allowed", "text/plain; charset=utf-8");

        _requestCounts.AddOrUpdate(path, 1, static (_, count) => count + 1);

        if (path.Equals("/health", StringComparison.Ordinal))
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, port = Port, origin = $"http://127.0.0.1:{Port}" });
            return new ViewerHttpResponse(200, "application/json; charset=utf-8", body, null);
        }

        if (path.StartsWith("/src/", StringComparison.Ordinal))
        {
            var name = path[5..];
            if (string.IsNullOrEmpty(name) || name.Split('/').Any(x => x.Length == 0 || x is "." or ".."))
                return ViewerHttpResponse.Text(400, "Invalid asset path", "text/plain; charset=utf-8");
            if (_options.DataSource is null)
                return ViewerHttpResponse.Text(404, "Asset not found", "text/plain; charset=utf-8");
            var asset = await _options.DataSource.GetAssetAsync(name, cancellationToken).ConfigureAwait(false);
            return asset is null
                ? ViewerHttpResponse.Text(404, "Asset not found", "text/plain; charset=utf-8")
                : new ViewerHttpResponse(200, asset.MimeType, asset.Data, null);
        }

        var staticRelative = MapStaticPath(path);
        if (staticRelative is null)
            return ViewerHttpResponse.Text(404, "Not found", "text/plain; charset=utf-8");
        var root = Path.GetFullPath(_options.StaticRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, staticRelative.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            return ViewerHttpResponse.Text(404, "Not found", "text/plain; charset=utf-8");

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return new ViewerHttpResponse(200, GetMimeType(fullPath), bytes, null);
    }

    private static string? MapStaticPath(string path)
    {
        if (path.Equals("/", StringComparison.Ordinal)) return "index.html";
        if (path.Equals("/_common/iframe-bridge.js", StringComparison.OrdinalIgnoreCase)) return "_common/iframe-bridge.js";
        if (path.StartsWith("/_common/", StringComparison.OrdinalIgnoreCase)) return "Web/_common/" + path[9..];
        if (path.StartsWith("/wiki/", StringComparison.OrdinalIgnoreCase)) return "Web/" + path[6..];
        if (path.StartsWith("/Terraria_Wiki_", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Calamity_Wiki_", StringComparison.OrdinalIgnoreCase)) return "Web" + path;
        if (path.StartsWith("/outer", StringComparison.OrdinalIgnoreCase)) return path[1..];
        return null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string method, ViewerHttpResponse response, CancellationToken cancellationToken)
    {
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(StatusText(response.StatusCode)).Append("\r\n")
            .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
            .Append("Content-Length: ").Append(response.Body?.LongLength ?? 0).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n");
        foreach (var header in response.Headers ?? new Dictionary<string, string>())
            headers.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        headers.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) && response.Body is { Length: > 0 } body)
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static string StatusText(int statusCode) => statusCode switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "Response"
    };

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".flac" => "audio/flac",
        ".webm" => "audio/webm",
        _ => "application/octet-stream"
    };
}

public sealed class ViewerServerOptions
{
    public required string StaticRoot { get; init; }
    public IViewerDataSource? DataSource { get; init; }
    public int Port { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public Action<Exception>? ErrorSink { get; init; }
}

public interface IViewerDataSource
{
    Task<ViewerAsset?> GetAssetAsync(string fileName, CancellationToken cancellationToken = default);
}

public sealed record ViewerAsset(string FileName, byte[] Data, string MimeType);

public sealed class SqliteViewerDataSource : IViewerDataSource
{
    private readonly string _databasePath;

    public SqliteViewerDataSource(string databasePath) => _databasePath = Path.GetFullPath(databasePath);

    public async Task<ViewerAsset?> GetAssetAsync(string fileName, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            using var connection = new SQLiteConnection(_databasePath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            var row = connection.Query<AssetDataRow>("SELECT FileName, Data, MimeType FROM WikiAsset WHERE FileName=? LIMIT 1", fileName).FirstOrDefault();
            return row is null ? null : new ViewerAsset(row.FileName ?? fileName, row.Data ?? Array.Empty<byte>(), row.MimeType ?? "application/octet-stream");
        }, cancellationToken);
    }

    private sealed class AssetDataRow { public string? FileName { get; set; } public byte[]? Data { get; set; } public string? MimeType { get; set; } }
}

public sealed record ViewerServerStatus(bool Running, int Port, string Origin, string? Error);

public sealed class ViewerServerStartException : IOException
{
    public ViewerServerStartException(int requestedPort, Exception innerException)
        : base($"Could not bind loopback Viewer port {requestedPort}.", innerException) => RequestedPort = requestedPort;

    public int RequestedPort { get; }
}

public sealed record ViewerHttpRequest(string Method, string Target, IReadOnlyDictionary<string, string> Headers, bool IsWellFormed, string? Error);

public sealed record ViewerHttpResponse(int StatusCode, string ContentType, byte[]? Body, IReadOnlyDictionary<string, string>? Headers)
{
    public static ViewerHttpResponse Text(int statusCode, string text, string contentType, IReadOnlyDictionary<string, string>? headers = null) =>
        new(statusCode, contentType, Encoding.UTF8.GetBytes(text), headers);
}
