using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Terraria_Wiki.Windows.Viewer;

internal sealed class ViewerServer : IAsyncDisposable
{
    private readonly ViewerServerOptions _options;
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<Task, byte> _clients = new();
    private readonly ConcurrentDictionary<TcpClient, byte> _connections = new();
    private readonly ConcurrentDictionary<string, int> _requestCounts = new(StringComparer.Ordinal);
    private TcpListener? _listener;
    private CancellationTokenSource? _stopSource;
    private Task? _acceptTask;
    private int _port;

    public ViewerServer(ViewerServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.StaticRoot) || !Directory.Exists(options.StaticRoot))
            throw new DirectoryNotFoundException($"Viewer static root does not exist: {options.StaticRoot}");
        if (!File.Exists(Path.Combine(options.StaticRoot, "index.html")))
            throw new FileNotFoundException("The Viewer outer host asset is missing.", Path.Combine(options.StaticRoot, "index.html"));
        if (string.IsNullOrWhiteSpace(options.WikiDataFolder)
            || !string.Equals(Path.GetFileName(options.WikiDataFolder), options.WikiDataFolder, StringComparison.Ordinal)
            || options.WikiDataFolder is "." or "..")
            throw new ArgumentException("The active Wiki data folder is invalid.", nameof(options));
        if (options.Port is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(options), options.Port, "Viewer port must be between 0 and 65535.");
        if (options.Theme is not ("light" or "dark" or "original"))
            throw new ArgumentException("The Viewer theme is invalid.", nameof(options));
    }

    public bool IsRunning { get { lock (_stateGate) return _listener is not null; } }
    public int Port { get { lock (_stateGate) return _port; } }
    public int ActiveClientCount => _connections.Count;
    public int GetRequestCount(string path) => _requestCounts.TryGetValue(path, out var count) ? count : 0;
    public Uri? BaseUri { get { lock (_stateGate) return _listener is null ? null : new Uri($"http://127.0.0.1:{_port}/"); } }

    public Task<ViewerServerStatus> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            if (_listener is not null)
                return Task.FromResult(new ViewerServerStatus(_port, BaseUri!));

            var listener = new TcpListener(IPAddress.Loopback, _options.Port);
            try
            {
                listener.Start();
            }
            catch (SocketException exception)
            {
                listener.Stop();
                throw new ViewerServerStartException(_options.Port, exception);
            }

            _listener = listener;
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _acceptTask = AcceptLoopAsync(listener, _stopSource.Token);
            return Task.FromResult(new ViewerServerStatus(_port, new Uri($"http://127.0.0.1:{_port}/")));
        }
    }

    public async Task StopAsync()
    {
        Task? acceptTask;
        CancellationTokenSource? stopSource;
        lock (_stateGate)
        {
            if (_listener is null)
                return;
            stopSource = _stopSource;
            acceptTask = _acceptTask;
            _listener.Stop();
            _listener = null;
            _stopSource = null;
            _acceptTask = null;
            _port = 0;
        }

        try { stopSource?.Cancel(); } catch (ObjectDisposedException) { }
        foreach (var client in _connections.Keys)
        {
            try { client.Dispose(); } catch { }
        }
        if (acceptTask is not null)
        {
            try { await acceptTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }

        var clients = _clients.Keys.ToArray();
        if (clients.Length > 0)
        {
            try
            {
                await Task.WhenAll(clients)
                    .WaitAsync(_options.ShutdownTimeout)
                    .ConfigureAwait(false);
            }
            catch { }
        }
        _connections.Clear();
        _clients.Clear();
        stopSource?.Dispose();
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
            catch (SocketException exception)
            {
                _options.ErrorSink?.Invoke(exception);
                continue;
            }

            _connections.TryAdd(client, 0);
            var task = ProcessClientAsync(client, cancellationToken);
            _clients.TryAdd(task, 0);
            _ = task.ContinueWith(
                static (completed, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(completed, out _),
                _clients,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken serverCancellation)
    {
        try
        {
            using (client)
            using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation))
            {
                client.NoDelay = true;
                requestCancellation.CancelAfter(_options.RequestTimeout);
                await using var stream = client.GetStream();
                ViewerHttpRequest? request = null;
                ViewerHttpResponse? response;
                try
                {
                    request = await ReadRequestAsync(stream, requestCancellation.Token).ConfigureAwait(false);
                    response = await RouteAsync(request, requestCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _options.ErrorSink?.Invoke(exception);
                    response = ViewerHttpResponse.Text(500, "Internal viewer error");
                }

                try
                {
                    await WriteResponseAsync(
                            stream,
                            request?.Method ?? "GET",
                            response,
                            requestCancellation.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        finally
        {
            _connections.TryRemove(client, out _);
        }
    }

    private async Task<ViewerHttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var one = new byte[1];
        while (bytes.Length <= _options.MaxHeaderBytes)
        {
            var read = await stream.ReadAsync(one.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Client disconnected before sending a request.");
            bytes.WriteByte(one[0]);
            if (bytes.Length >= 4)
            {
                var tail = bytes.GetBuffer();
                var length = (int)bytes.Length;
                if (tail[length - 4] == '\r' && tail[length - 3] == '\n' && tail[length - 2] == '\r' && tail[length - 1] == '\n')
                    break;
            }
        }

        if (bytes.Length > _options.MaxHeaderBytes)
            return ViewerHttpRequest.Invalid("request-too-large");

        var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n", StringSplitOptions.None);
        var requestParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length != 3 || !requestParts[2].StartsWith("HTTP/1.", StringComparison.OrdinalIgnoreCase))
            return ViewerHttpRequest.Invalid("malformed-request");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
                break;
            var colon = line.IndexOf(':');
            if (colon <= 0)
                return ViewerHttpRequest.Invalid("malformed-header");
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return new ViewerHttpRequest(requestParts[0], requestParts[1], headers, null);
    }

    private async Task<ViewerHttpResponse> RouteAsync(ViewerHttpRequest request, CancellationToken cancellationToken)
    {
        if (request.Error is not null)
            return ViewerHttpResponse.Text(400, request.Error);
        if (!string.Equals(request.Headers.GetValueOrDefault("Host"), $"127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase))
            return ViewerHttpResponse.Text(400, "Host is not allowed");
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            return ViewerHttpResponse.Text(405, "Method not allowed", new Dictionary<string, string> { ["Allow"] = "GET, HEAD" });

        string path;
        try
        {
            if (request.Target.Contains("://", StringComparison.Ordinal))
                return ViewerHttpResponse.Text(400, "Absolute request targets are not allowed");
            var query = request.Target.IndexOf('?');
            path = Uri.UnescapeDataString(query < 0 ? request.Target : request.Target[..query]);
        }
        catch (UriFormatException)
        {
            return ViewerHttpResponse.Text(400, "Invalid URL");
        }

        if (!path.StartsWith('/') || path.Contains('\0') || path.Contains('\\') || path.Contains(':'))
            return ViewerHttpResponse.Text(400, "Invalid path");
        if (path.Split('/').Any(segment => segment is "." or ".."))
            return ViewerHttpResponse.Text(400, "Path traversal is not allowed");

        _requestCounts.AddOrUpdate(path, 1, static (_, count) => count + 1);

        if (path.Equals("/health", StringComparison.Ordinal))
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, port = Port, origin = BaseUri!.GetLeftPart(UriPartial.Authority) });
            return new ViewerHttpResponse(200, "application/json; charset=utf-8", body, null);
        }

        if (path.StartsWith("/src/", StringComparison.Ordinal))
        {
            var fileName = path[5..];
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
                return ViewerHttpResponse.Text(400, "Invalid asset path");
            var asset = await _options.DataSource.ReadAssetAsync(fileName, cancellationToken).ConfigureAwait(false);
            return asset is null
                ? ViewerHttpResponse.Text(404, "Asset not found")
                : new ViewerHttpResponse(200, SafeContentType(asset.MimeType), asset.Data, null);
        }

        if (path.Equals("/", StringComparison.Ordinal))
        {
            var template = await File.ReadAllTextAsync(Path.Combine(_options.StaticRoot, "index.html"), cancellationToken).ConfigureAwait(false);
            var folder = Uri.EscapeDataString(_options.WikiDataFolder);
            var theme = _options.Theme;
            var wikiUri = $"/wiki/{folder}/index.html?theme={theme}&isMobile=False&host=Avalonia";
            var body = Encoding.UTF8.GetBytes(template.Replace("{{WIKI_URI}}", HtmlEncoder.Default.Encode(wikiUri), StringComparison.Ordinal));
            return new ViewerHttpResponse(
                200,
                "text/html; charset=utf-8",
                body,
                new Dictionary<string, string>
                {
                    ["Content-Security-Policy"] = "default-src 'self' data:; frame-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; media-src 'self'",
                });
        }

        var relative = MapStaticPath(path);
        if (relative is null)
            return ViewerHttpResponse.Text(404, "Not found");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_options.StaticRoot));
        var fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            return ViewerHttpResponse.Text(404, "Not found");

        return new ViewerHttpResponse(
            200,
            MimeType(fullPath),
            await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false),
            null);
    }

    private string? MapStaticPath(string path)
    {
        if (path.Equals("/_common/iframe-bridge.js", StringComparison.OrdinalIgnoreCase))
            return "_common/iframe-bridge.js";
        if (path.StartsWith("/_common/", StringComparison.OrdinalIgnoreCase))
            return "Web/_common/" + path[9..];

        var activePrefix = "/wiki/" + _options.WikiDataFolder + "/";
        if (path.StartsWith(activePrefix, StringComparison.OrdinalIgnoreCase))
            return "Web/" + _options.WikiDataFolder + "/" + path[activePrefix.Length..];
        return null;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string method,
        ViewerHttpResponse response,
        CancellationToken cancellationToken)
    {
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(StatusText(response.StatusCode)).Append("\r\n")
            .Append("Content-Type: ").Append(response.ContentType).Append("\r\n")
            .Append("Content-Length: ").Append(response.Body.LongLength).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Connection: close\r\n");
        foreach (var header in response.Headers ?? new Dictionary<string, string>())
            headers.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        headers.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), cancellationToken).ConfigureAwait(false);
        if (!string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase) && response.Body.Length > 0)
            await stream.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
    }

    private static string SafeContentType(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n')
            ? "application/octet-stream"
            : value;

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
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
        _ => "application/octet-stream",
    };

    private static string StatusText(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        405 => "Method Not Allowed",
        500 => "Internal Server Error",
        _ => "Response",
    };
}

internal sealed class ViewerServerOptions
{
    public required string StaticRoot { get; init; }
    public required string WikiDataFolder { get; init; }
    public required IViewerDataSource DataSource { get; init; }
    public string Theme { get; init; } = "light";
    public int Port { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public int MaxHeaderBytes { get; init; } = 32 * 1024;
    public Action<Exception>? ErrorSink { get; init; }
}

internal interface IViewerDataSource
{
    Task<ViewerAsset?> ReadAssetAsync(string fileName, CancellationToken cancellationToken = default);
}

internal sealed record ViewerAsset(string FileName, byte[] Data, string MimeType);
internal sealed record ViewerServerStatus(int Port, Uri BaseUri);
internal sealed record ViewerHttpRequest(string Method, string Target, IReadOnlyDictionary<string, string> Headers, string? Error)
{
    public static ViewerHttpRequest Invalid(string error) => new("", "", new Dictionary<string, string>(), error);
}
internal sealed record ViewerHttpResponse(int StatusCode, string ContentType, byte[] Body, IReadOnlyDictionary<string, string>? Headers)
{
    public static ViewerHttpResponse Text(int status, string text, IReadOnlyDictionary<string, string>? headers = null) =>
        new(status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text), headers);
}

internal sealed class ViewerServerStartException : IOException
{
    public ViewerServerStartException(int requestedPort, Exception innerException)
        : base($"Could not bind loopback Viewer port {requestedPort}.", innerException) => RequestedPort = requestedPort;

    public int RequestedPort { get; }
}
