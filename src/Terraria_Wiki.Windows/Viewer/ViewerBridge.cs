using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Terraria_Wiki.Application;

namespace Terraria_Wiki.Windows.Viewer;

internal interface IExternalUriLauncher
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

internal sealed class WindowsExternalUriLauncher : IExternalUriLauncher
{
    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}

internal sealed class ViewerBridge
{
    public const string MessageSource = "terraria-wiki-viewer";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly WikiApplication _application;
    private readonly IExternalUriLauncher _externalUris;
    private int _sideButtonBackEnabled;
    private readonly ConcurrentDictionary<string, byte> _activeRequestIds = new(StringComparer.Ordinal);
    private readonly object _historyGate = new();
    private readonly List<ViewerHistoryEntry> _history = [];

    public ViewerBridge(
        WikiApplication application,
        IExternalUriLauncher externalUris,
        bool sideButtonBackEnabled = true)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _externalUris = externalUris ?? throw new ArgumentNullException(nameof(externalUris));
        _sideButtonBackEnabled = sideButtonBackEnabled ? 1 : 0;
    }

    public IReadOnlyList<ViewerHistoryEntry> History
    {
        get { lock (_historyGate) return _history.ToArray(); }
    }

    public event EventHandler<ViewerBridgeRequestCompleted>? RequestCompleted;

    public async Task<ViewerDispatchResult?> HandleOuterMessageAsync(
        string body,
        Uri expectedBaseUri,
        CancellationToken cancellationToken = default)
    {
        if (!ViewerProtocol.TryParseOuter(body, expectedBaseUri, out var envelope) || envelope!.Type != "req")
            return null;
        if (!_activeRequestIds.TryAdd(envelope.Id, 0))
            return Error(envelope.Id, "duplicate-request-id", "The Viewer request ID is already active.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result));
            return result;
        }
        catch (OperationCanceledException)
        {
            var result = Error(envelope.Id, "cancelled", "The Viewer operation was cancelled.");
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result));
            return result;
        }
        catch (ApplicationUseCaseException exception)
        {
            var result = Error(envelope.Id, exception.Code, exception.Message);
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result));
            return result;
        }
        catch (Exception exception)
        {
            var result = Error(envelope.Id, "viewer-operation-failed", exception.Message);
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result));
            return result;
        }
        finally
        {
            _activeRequestIds.TryRemove(envelope.Id, out _);
        }
    }

    private async Task<ViewerDispatchResult> DispatchAsync(
        ViewerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var data = GetStringData(envelope.Data);
        switch (envelope.Method)
        {
            case "GetRedirectedTitleAndAnchorAsync":
            {
                RequireData(data, envelope.Method);
                var read = await _application.Query.ReadAsync(data!, cancellationToken).ConfigureAwait(false);
                if (!read.IsFound || string.IsNullOrWhiteSpace(read.Title))
                    return Error(envelope.Id, "page-not-found", $"Wiki page '{data}' was not found.");
                var payload = JsonSerializer.Serialize(new ViewerTitleAnchor(read.Title, read.Anchor), JsonOptions);
                return Success(envelope.Id, payload);
            }
            case "PageRedirectAsync":
            {
                RequireData(data, envelope.Method);
                var read = await _application.Query.ReadAsync(data!, cancellationToken).ConfigureAwait(false);
                if (!read.IsFound || string.IsNullOrWhiteSpace(read.Title) || read.Content is null)
                    return Error(envelope.Id, "page-not-found", $"Wiki page '{data}' was not found.");

                await _application.Library.RecordVisitAsync(read.Title, cancellationToken).ConfigureAwait(false);
                var page = new ViewerPage(
                    read.Title,
                    read.Content,
                    read.LastModified?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty);
                return Success(envelope.Id, JsonSerializer.Serialize(page, JsonOptions));
            }
            case "SaveToTabHistory":
            {
                RequireData(data, envelope.Method);
                var entry = JsonSerializer.Deserialize<ViewerHistoryEntry>(data!, JsonOptions)
                    ?? throw new InvalidDataException("The Viewer history entry is invalid.");
                if (!string.IsNullOrWhiteSpace(entry.Title))
                {
                    lock (_historyGate)
                        _history.Add(entry);
                }
                return Success(envelope.Id, "true");
            }
            case "WikiBackAsync":
            {
                if (Volatile.Read(ref _sideButtonBackEnabled) == 0)
                    return Error(envelope.Id, "side-button-back-disabled", "Mouse side-button navigation is disabled in Application settings.");
                var command = TryTakeBackCommand();
                if (command is null)
                    return Error(envelope.Id, "viewer-history-empty", "There is no previous Viewer page.");
                return Success(envelope.Id, "true", command);
            }
            case "OpenExternalWebsite":
            {
                RequireData(data, envelope.Method);
                if (!Uri.TryCreate(data, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return Error(envelope.Id, "external-uri-not-allowed", "Only absolute HTTP(S) links can be opened.");
                await _externalUris.OpenAsync(uri, cancellationToken).ConfigureAwait(false);
                return Success(envelope.Id, "true");
            }
            case "ChangeTheme":
                return Success(envelope.Id, "true");
            case "ViewerReady":
                return Success(envelope.Id, "true");
            case "OpenInNewTab":
                return Error(envelope.Id, "not-supported", "Opening Viewer tabs is not available in Phase 04.");
            case "CopyImageToClipboard":
                return Error(envelope.Id, "not-supported", "Copying Viewer images is not available in Phase 04.");
            case "CopyTextToClipboard":
                return Error(envelope.Id, "not-supported", "The full Viewer context menu is not available in Phase 04.");
            default:
                return Error(envelope.Id, "unknown-viewer-method", $"Viewer method '{envelope.Method}' is not supported.");
        }
    }

    public void SetSideButtonBackEnabled(bool enabled) =>
        Volatile.Write(ref _sideButtonBackEnabled, enabled ? 1 : 0);

    public ViewerOutboundCommand? TryTakeBackCommand()
    {
        ViewerHistoryEntry? entry = null;
        lock (_historyGate)
        {
            if (_history.Count > 0)
            {
                entry = _history[^1];
                _history.RemoveAt(_history.Count - 1);
            }
        }

        return entry is null
            ? null
            : new ViewerOutboundCommand("BackToPage", JsonSerializer.Serialize(entry, JsonOptions));
    }

    private static void RequireData(string? data, string method)
    {
        if (string.IsNullOrWhiteSpace(data))
            throw new InvalidDataException($"Viewer method '{method}' requires string data.");
    }

    private static string? GetStringData(JsonElement data) =>
        data.ValueKind == JsonValueKind.String ? data.GetString() : null;

    private static ViewerDispatchResult Success(string id, string data, ViewerOutboundCommand? command = null) =>
        new(JsonSerializer.Serialize(new ViewerResponseEnvelope("res", id, data, null), JsonOptions), command, null);

    private static ViewerDispatchResult Error(string id, string code, string message) =>
        new(
            JsonSerializer.Serialize(
                new ViewerResponseEnvelope("res", id, "null", new ViewerProtocolError(code, message)),
                JsonOptions),
            null,
            new ViewerProtocolError(code, message));
}

internal static class ViewerProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryParseOuter(string body, Uri expectedBaseUri, out ViewerEnvelope? envelope)
    {
        envelope = null;
        if (string.IsNullOrWhiteSpace(body) || expectedBaseUri is null)
            return false;

        try
        {
            var outer = JsonSerializer.Deserialize<OuterViewerMessage>(body, JsonOptions);
            var expectedOrigin = expectedBaseUri.GetLeftPart(UriPartial.Authority);
            if (outer is null
                || !string.Equals(outer.Source, ViewerBridge.MessageSource, StringComparison.Ordinal)
                || !string.Equals(outer.Origin, expectedOrigin, StringComparison.Ordinal)
                || outer.Envelope.ValueKind != JsonValueKind.Object)
                return false;

            var parsed = outer.Envelope.Deserialize<ViewerEnvelope>(JsonOptions);
            if (parsed is null
                || (parsed.Type != "req" && parsed.Type != "res")
                || string.IsNullOrWhiteSpace(parsed.Id)
                || parsed.Id.Length > 128
                || (parsed.Type == "req" && string.IsNullOrWhiteSpace(parsed.Method)))
                return false;
            envelope = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed record OuterViewerMessage(string? Source, string? Origin, JsonElement Envelope);
internal sealed record ViewerEnvelope(string Type, string Id, string? Method, JsonElement Data, ViewerProtocolError? Error = null);
internal sealed record ViewerResponseEnvelope(string Type, string Id, string Data, ViewerProtocolError? Error);
internal sealed record ViewerProtocolError(string Code, string Message);
internal sealed record ViewerDispatchResult(string ResponseJson, ViewerOutboundCommand? Command, ViewerProtocolError? Error);
internal sealed record ViewerBridgeRequestCompleted(string Method, ViewerDispatchResult Result);
internal sealed record ViewerOutboundCommand(string Method, string Data);
internal sealed record ViewerPage(string Title, string Content, string LastModified);
internal sealed record ViewerTitleAnchor(string Title, string? Anchor);
internal sealed record ViewerHistoryEntry(string? Title, double Position);
