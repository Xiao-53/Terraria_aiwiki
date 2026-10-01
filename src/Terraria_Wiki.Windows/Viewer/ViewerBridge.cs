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
    private readonly ConcurrentDictionary<string, PendingPageIdentity> _pendingPages = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _pendingPageOrder = new();
    private readonly ConcurrentDictionary<string, string> _committedPageTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _committedTokenOrder = new();
    private readonly object _historyGate = new();
    private readonly object _commitGate = new();
    private readonly List<ViewerHistoryEntry> _history = [];
    private readonly List<ViewerHistoryEntry> _forwardHistory = [];
    private long _generation;
    private int _wikiId;
    private string _wikiDisplayTitle = string.Empty;
    private long _lastCommittedNavigationVersion;
    private long _lastSavedNavigationVersion;
    private string? _currentPageToken;

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
    public bool HasHistory
    {
        get { lock (_historyGate) return _history.Count > 0; }
    }
    public IReadOnlyList<ViewerHistoryEntry> ForwardHistory
    {
        get { lock (_historyGate) return _forwardHistory.ToArray(); }
    }
    public bool HasForwardHistory
    {
        get { lock (_historyGate) return _forwardHistory.Count > 0; }
    }

    public event EventHandler<ViewerBridgeRequestCompleted>? RequestCompleted;
    public event EventHandler<ViewerPageIdentity>? PageCommitted;
    public event EventHandler<bool>? HistoryChanged;

    public void BeginSession(int wikiId, string wikiDisplayTitle, long generation)
    {
        if (wikiId <= 0)
            throw new ArgumentOutOfRangeException(nameof(wikiId));
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (string.IsNullOrWhiteSpace(wikiDisplayTitle))
            throw new ArgumentException("A Wiki display title is required.", nameof(wikiDisplayTitle));

        lock (_commitGate)
        {
            Volatile.Write(ref _wikiId, wikiId);
            Volatile.Write(ref _wikiDisplayTitle, wikiDisplayTitle.Trim());
            Volatile.Write(ref _generation, generation);
            Volatile.Write(ref _lastCommittedNavigationVersion, 0);
            Volatile.Write(ref _lastSavedNavigationVersion, 0);
            Volatile.Write(ref _currentPageToken, null);
            _pendingPages.Clear();
            _pendingPageOrder.Clear();
            _committedPageTokens.Clear();
            _committedTokenOrder.Clear();
        }
        lock (_historyGate)
        {
            _history.Clear();
            _forwardHistory.Clear();
        }
        HistoryChanged?.Invoke(this, false);
    }

    public async Task<ViewerDispatchResult?> HandleOuterMessageAsync(
        string body,
        Uri expectedBaseUri,
        CancellationToken cancellationToken = default)
    {
        if (!ViewerProtocol.TryParseOuter(body, expectedBaseUri, out var envelope) || envelope!.Type != "req")
            return null;
        return await HandleOuterMessageAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ViewerDispatchResult> HandleOuterMessageAsync(
        ViewerEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Type != "req" || string.IsNullOrWhiteSpace(envelope.Id) || string.IsNullOrWhiteSpace(envelope.Method))
            throw new ArgumentException("A validated Viewer request envelope is required.", nameof(envelope));
        var requestStarted = Stopwatch.GetTimestamp();
        ViewerNavigationTrace.Write(envelope.Id, null, "bridge-received", requestStarted);
        if (!_activeRequestIds.TryAdd(envelope.Id, 0))
            return Error(envelope.Id, "duplicate-request-id", "The Viewer request ID is already active.");

        var requestGeneration = Volatile.Read(ref _generation);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            ViewerNavigationTrace.Write(envelope.Id, null, "response-serialized", requestStarted);
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result, requestGeneration));
            return result;
        }
        catch (OperationCanceledException)
        {
            var result = Error(envelope.Id, "cancelled", "The Viewer operation was cancelled.");
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result, requestGeneration));
            return result;
        }
        catch (ViewerProtocolException exception)
        {
            var result = Error(envelope.Id, exception.Code, exception.Message);
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result, requestGeneration));
            return result;
        }
        catch (ApplicationUseCaseException exception)
        {
            var result = Error(envelope.Id, exception.Code, exception.Message);
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result, requestGeneration));
            return result;
        }
        catch (Exception exception)
        {
            var result = Error(envelope.Id, "viewer-operation-failed", exception.Message);
            RequestCompleted?.Invoke(this, new ViewerBridgeRequestCompleted(envelope.Method!, result, requestGeneration));
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
                long generation;
                int wikiId;
                lock (_commitGate)
                {
                    generation = _generation;
                    wikiId = _wikiId;
                }
                if (generation <= 0 || wikiId <= 0)
                    return Error(envelope.Id, "viewer-session-unbound", "The Viewer session is not bound to a Wiki.");
                var readStarted = Stopwatch.GetTimestamp();
                var read = await _application.Query.ReadAsync(data!, cancellationToken).ConfigureAwait(false);
                ViewerNavigationTrace.Write(envelope.Id, null, "read-completed reads=1", readStarted);
                if (!read.IsFound || string.IsNullOrWhiteSpace(read.Title) || read.Content is null)
                    return Error(envelope.Id, "page-not-found", $"Wiki page '{data}' was not found.");
                var readToken = Guid.NewGuid().ToString("N");
                lock (_commitGate)
                {
                    if (generation != _generation || wikiId != _wikiId)
                        return Error(envelope.Id, "viewer-session-expired", "The Viewer session changed while the page was being read.");
                    _pendingPages[readToken] = new PendingPageIdentity(generation, wikiId, read.Title);
                    _pendingPageOrder.Enqueue(readToken);
                    TrimPendingPages();
                }
                var page = new ViewerPage(
                    read.Title,
                    read.Anchor,
                    read.Content,
                    read.LastModified?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
                    readToken);
                ViewerNavigationTrace.Write(envelope.Id, readToken, "page-constructed", readStarted);
                return Success(envelope.Id, page);
            }
            case "ViewerPageCommitted":
            {
                RequireData(data, envelope.Method);
                var commit = JsonSerializer.Deserialize<ViewerPageCommit>(data!, JsonOptions)
                    ?? throw new InvalidDataException("The Viewer page commit is invalid.");
                if (string.IsNullOrWhiteSpace(commit.ReadToken)
                    || commit.NavigationVersion <= 0
                    || !_pendingPages.TryRemove(commit.ReadToken, out var pending))
                    return Error(envelope.Id, "viewer-page-commit-invalid", "The Viewer page commit is unknown or expired.");

                ViewerPageIdentity identity;
                lock (_commitGate)
                {
                    var generation = Volatile.Read(ref _generation);
                    var wikiId = Volatile.Read(ref _wikiId);
                    if (pending.Generation != generation || pending.WikiId != wikiId)
                        return Error(envelope.Id, "viewer-page-commit-expired", "The Viewer page commit belongs to an expired session.");

                    var previous = Volatile.Read(ref _lastCommittedNavigationVersion);
                    if (commit.NavigationVersion <= previous)
                        return Error(envelope.Id, "viewer-page-commit-stale", "The Viewer page commit is older than the displayed page.");
                    Volatile.Write(ref _lastCommittedNavigationVersion, commit.NavigationVersion);
                    Volatile.Write(ref _currentPageToken, commit.ReadToken);
                    _committedPageTokens[commit.ReadToken] = pending.CanonicalTitle;
                    _committedTokenOrder.Enqueue(commit.ReadToken);
                    while (_committedPageTokens.Count > 64 && _committedTokenOrder.TryDequeue(out var expiredToken))
                        _committedPageTokens.TryRemove(expiredToken, out _);
                    identity = new ViewerPageIdentity(
                        generation,
                        wikiId,
                        Volatile.Read(ref _wikiDisplayTitle),
                        pending.CanonicalTitle,
                        commit.NavigationVersion);
                }
                PageCommitted?.Invoke(this, identity);
                ViewerNavigationTrace.Arrival(envelope.Id, commit.ReadToken, "page-committed");
                return Success(envelope.Id, "true");
            }
            case "SaveToTabHistory":
            {
                RequireData(data, envelope.Method);
                var entry = JsonSerializer.Deserialize<ViewerHistoryEntry>(data!, JsonOptions)
                    ?? throw new InvalidDataException("The Viewer history entry is invalid.");
                lock (_commitGate)
                {
                    if (!TryNormalizeHistoryEntry(entry, requireCurrentNavigation: true, out var normalized))
                        return Error(envelope.Id, "viewer-history-expired", "The Viewer history entry belongs to an expired page.");
                    if (normalized.Title is not null
                        && entry.NavigationVersion != Volatile.Read(ref _lastSavedNavigationVersion))
                    {
                        lock (_historyGate)
                        {
                            _history.Add(normalized);
                            _forwardHistory.Clear();
                        }
                        Volatile.Write(ref _lastSavedNavigationVersion, entry.NavigationVersion);
                        HistoryChanged?.Invoke(this, true);
                    }
                }
                return Success(envelope.Id, "true");
            }
            case "WikiBackAsync":
            {
                if (Volatile.Read(ref _sideButtonBackEnabled) == 0)
                    return Error(envelope.Id, "side-button-back-disabled", "Mouse side-button navigation is disabled in Application settings.");
                var command = string.IsNullOrWhiteSpace(data)
                    ? TryTakeBackCommand()
                    : TryTakeBackCommand(ParseCurrentHistoryEntry(data!, envelope.Method));
                if (command is null)
                    return Error(envelope.Id, "viewer-history-empty", "There is no previous Viewer page.");
                return Success(envelope.Id, "true", command);
            }
            case "WikiForwardAsync":
            {
                if (Volatile.Read(ref _sideButtonBackEnabled) == 0)
                    return Error(envelope.Id, "side-button-forward-disabled", "Mouse side-button navigation is disabled in Application settings.");
                if (string.IsNullOrWhiteSpace(data))
                    return Error(envelope.Id, "viewer-forward-state-invalid", "The current Viewer page state is required for forward navigation.");
                var command = TryTakeForwardCommand(ParseCurrentHistoryEntry(data!, envelope.Method));
                if (command is null)
                    return Error(envelope.Id, "viewer-forward-history-empty", "There is no next Viewer page.");
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
                RequireData(data, envelope.Method);
                return string.Equals(data, Volatile.Read(ref _currentPageToken), StringComparison.Ordinal)
                    ? Success(envelope.Id, "true")
                    : Error(envelope.Id, "viewer-ready-expired", "The Viewer ready signal belongs to an expired page.");
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

        HistoryChanged?.Invoke(this, HasHistory);

        return entry is null
            ? null
            : new ViewerOutboundCommand("BackToPage", JsonSerializer.Serialize(entry, JsonOptions));
    }

    public ViewerOutboundCommand? TryTakeBackCommand(ViewerHistoryEntry currentEntry)
    {
        var current = ValidateCurrentHistoryEntry(currentEntry);
        ViewerHistoryEntry? target;
        lock (_historyGate)
        {
            if (_history.Count == 0)
                return null;
            target = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            _forwardHistory.Add(current);
        }

        HistoryChanged?.Invoke(this, HasHistory);
        return new ViewerOutboundCommand(
            "BackToPage",
            JsonSerializer.Serialize(target, JsonOptions),
            new ViewerNavigationTransition("back", current, target, Volatile.Read(ref _generation)));
    }

    public ViewerOutboundCommand? TryTakeForwardCommand(ViewerHistoryEntry currentEntry)
    {
        var current = ValidateCurrentHistoryEntry(currentEntry);
        ViewerHistoryEntry? target;
        lock (_historyGate)
        {
            if (_forwardHistory.Count == 0)
                return null;
            target = _forwardHistory[^1];
            _forwardHistory.RemoveAt(_forwardHistory.Count - 1);
            _history.Add(current);
        }

        HistoryChanged?.Invoke(this, HasHistory);
        return new ViewerOutboundCommand(
            "ForwardToPage",
            JsonSerializer.Serialize(target, JsonOptions),
            new ViewerNavigationTransition("forward", current, target, Volatile.Read(ref _generation)));
    }

    public void RestoreBackCommand(ViewerOutboundCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Transition is not null)
        {
            RestoreNavigation(command.Transition);
            return;
        }
        if (!string.Equals(command.Method, "BackToPage", StringComparison.Ordinal))
            return;
        var entry = JsonSerializer.Deserialize<ViewerHistoryEntry>(command.Data, JsonOptions);
        if (entry is null || string.IsNullOrWhiteSpace(entry.Title))
            return;
        lock (_commitGate)
        {
            if (string.IsNullOrWhiteSpace(entry.ReadToken)
                || !_committedPageTokens.ContainsKey(entry.ReadToken))
                return;
            lock (_historyGate)
                _history.Add(entry);
        }
        HistoryChanged?.Invoke(this, true);
    }

    public void RestoreNavigation(ViewerNavigationTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        lock (_commitGate)
        {
            if (transition.Generation != Volatile.Read(ref _generation)
                || !string.Equals(Volatile.Read(ref _currentPageToken), transition.Current.ReadToken, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(transition.Current.ReadToken)
                || !_committedPageTokens.ContainsKey(transition.Current.ReadToken))
            {
                return;
            }

            lock (_historyGate)
            {
                if (string.Equals(transition.Direction, "back", StringComparison.Ordinal))
                {
                    RemoveLastMatching(_forwardHistory, transition.Current);
                    _history.Add(transition.Target);
                }
                else if (string.Equals(transition.Direction, "forward", StringComparison.Ordinal))
                {
                    RemoveLastMatching(_history, transition.Current);
                    _forwardHistory.Add(transition.Target);
                }
                else
                {
                    return;
                }
            }
        }
        HistoryChanged?.Invoke(this, HasHistory);
    }

    private void TrimPendingPages()
    {
        while ((_pendingPages.Count > 32 || _pendingPageOrder.Count > 64)
               && _pendingPageOrder.TryDequeue(out var expiredToken))
            _pendingPages.TryRemove(expiredToken, out _);
    }

    private static void RequireData(string? data, string method)
    {
        if (string.IsNullOrWhiteSpace(data))
            throw new InvalidDataException($"Viewer method '{method}' requires string data.");
    }

    private static string? GetStringData(JsonElement data) =>
        data.ValueKind == JsonValueKind.String ? data.GetString() : null;

    private ViewerHistoryEntry ParseCurrentHistoryEntry(string data, string method)
    {
        try
        {
            return JsonSerializer.Deserialize<ViewerHistoryEntry>(data, JsonOptions)
                ?? throw new ViewerProtocolException(
                    "viewer-history-current-invalid",
                    "The current Viewer page state is invalid.");
        }
        catch (JsonException)
        {
            throw new ViewerProtocolException(
                "viewer-history-current-invalid",
                $"Viewer method '{method}' received invalid page state.");
        }
    }

    private ViewerHistoryEntry ValidateCurrentHistoryEntry(ViewerHistoryEntry entry)
    {
        lock (_commitGate)
        {
            if (!TryNormalizeHistoryEntry(entry, requireCurrentNavigation: true, out var normalized)
                || !string.Equals(entry.ReadToken, Volatile.Read(ref _currentPageToken), StringComparison.Ordinal))
            {
                throw new ViewerProtocolException(
                    "viewer-history-current-expired",
                    "The current Viewer page state is expired.");
            }
            return normalized;
        }
    }

    private bool TryNormalizeHistoryEntry(
        ViewerHistoryEntry entry,
        bool requireCurrentNavigation,
        out ViewerHistoryEntry normalized)
    {
        normalized = entry;
        if (string.IsNullOrWhiteSpace(entry.ReadToken)
            || !double.IsFinite(entry.Position)
            || entry.Position < 0
            || !_committedPageTokens.TryGetValue(entry.ReadToken, out var canonicalTitle))
            return false;
        if (requireCurrentNavigation && entry.NavigationVersion != _lastCommittedNavigationVersion)
            return false;
        normalized = entry with
        {
            Title = canonicalTitle,
            NavigationVersion = _lastCommittedNavigationVersion,
        };
        return true;
    }

    private static void RemoveLastMatching(
        List<ViewerHistoryEntry> entries,
        ViewerHistoryEntry expected)
    {
        if (entries.Count > 0 && entries[^1] == expected)
            entries.RemoveAt(entries.Count - 1);
    }

    private static ViewerDispatchResult Success(string id, object? data, ViewerOutboundCommand? command = null) =>
        new(JsonSerializer.Serialize(new ViewerResponseEnvelope("res", id, data, null), JsonOptions), command, null);

    internal static string CreateErrorResponse(string id, ViewerProtocolError error) =>
        JsonSerializer.Serialize(
            new ViewerResponseEnvelope("res", id, null, error),
            JsonOptions);

    private static ViewerDispatchResult Error(string id, string code, string message) =>
        new(
            JsonSerializer.Serialize(
                new ViewerResponseEnvelope("res", id, null, new ViewerProtocolError(code, message)),
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

internal static class ViewerNavigationTrace
{
    [Conditional("DEBUG")]
    public static void Write(string requestId, string? readToken, string stage, long started)
    {
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Debug.WriteLine(
            $"[ViewerNavigation] request={requestId} readToken={readToken ?? "-"} stage={stage} elapsedMs={elapsed:F2}");
    }

    [Conditional("DEBUG")]
    public static void Arrival(string requestId, string? readToken, string stage) =>
        Debug.WriteLine(
            $"[ViewerNavigation] request={requestId} readToken={readToken ?? "-"} stage={stage} timestamp={Stopwatch.GetTimestamp()}");
}

internal sealed record OuterViewerMessage(string? Source, string? Origin, JsonElement Envelope);
internal sealed record ViewerEnvelope(string Type, string Id, string? Method, JsonElement Data, ViewerProtocolError? Error = null);
internal sealed record ViewerResponseEnvelope(string Type, string Id, object? Data, ViewerProtocolError? Error);
internal sealed record ViewerProtocolError(string Code, string Message);
internal sealed record ViewerDispatchResult(string ResponseJson, ViewerOutboundCommand? Command, ViewerProtocolError? Error);
internal sealed record ViewerBridgeRequestCompleted(string Method, ViewerDispatchResult Result, long Generation);
internal sealed record ViewerOutboundCommand(
    string Method,
    string Data,
    ViewerNavigationTransition? Transition = null);
internal sealed record ViewerNavigationTransition(
    string Direction,
    ViewerHistoryEntry Current,
    ViewerHistoryEntry Target,
    long Generation);
internal sealed record ViewerPage(string Title, string? Anchor, string Content, string LastModified, string ReadToken);
internal sealed record ViewerTitleAnchor(string Title, string? Anchor);
internal sealed record ViewerHistoryEntry(
    string? Title,
    double Position,
    string? ReadToken = null,
    long NavigationVersion = 0);
internal sealed record ViewerPageCommit(string ReadToken, long NavigationVersion);
internal sealed record PendingPageIdentity(long Generation, int WikiId, string CanonicalTitle);
