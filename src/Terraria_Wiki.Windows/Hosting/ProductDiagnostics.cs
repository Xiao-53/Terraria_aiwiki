using System.Text;
using System.Text.Json;
using Terraria_Wiki.Windows.Updates;
using Terraria_Wiki.Core.Operations;

namespace Terraria_Wiki.Windows.Hosting;

internal enum ProductDiagnosticEvent
{
    ApplicationStarted,
    StartupStageCompleted,
    RuntimeReady,
    StartupFailed,
    WebView2RecoveryOpened,
    ApplicationStopped,
    QuickAccessStageCompleted,
    TrayInitializationFailed,
}

internal enum QuickAccessStage
{
    FirstWindowShown,
    AdapterCreated,
    DomReady,
    FirstPageCommitted,
    Reopened,
}

internal sealed class ProductDiagnostics
{
    internal const long DefaultMaxLogBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedStartupFailureCodes = new(StringComparer.Ordinal)
    {
        "anchor-migration-cleanup-failed",
        "anchor-migration-commit-invalid",
        "anchor-migration-journal-invalid",
        "anchor-migration-rollback-failed",
        "client-already-running",
        "content-database-not-found",
        "data-directory-conflict",
        "data-directory-migration-failed",
        "data-directory-not-found",
        "data-directory-reparse-point",
        "data-directory-rollback-failed",
        "data-root-in-use",
        "data-root-not-found",
        "data-root-required",
        "database-checkpoint-failed",
        "database-family-conflict",
        "database-family-journal-invalid",
        "database-family-normalization-failed",
        "database-family-rollback-failed",
        "database-not-checkpointed",
        "default-data-root-unavailable",
        "duplicate-data-root-argument",
        "duplicate-host-settings-argument",
        "host-settings-invalid",
        "host-settings-write-failed",
        "initialization-failed",
        "invalid-host-settings-argument",
        "local-app-data-unavailable",
        "missing-data-root-argument",
        "missing-host-settings-argument",
        "no-wikis",
        "orphan-database-sidecar",
        "unknown-startup-error",
        "viewer-assets-missing",
        "viewer-bind-failed",
        "viewer-dom-ready-timeout",
        "viewer-navigation-failed",
        "viewer-not-ready",
        "viewer-server-start-failed",
        "webview2-missing",
    };
    private readonly object _gate = new();
    private readonly long _maxLogBytes;

    internal ProductDiagnostics(string? logDirectory, long maxLogBytes = DefaultMaxLogBytes)
    {
        if (logDirectory is not null && !Path.IsPathFullyQualified(logDirectory))
            throw new ArgumentException("The diagnostics directory must be absolute.", nameof(logDirectory));
        if (maxLogBytes < 256)
            throw new ArgumentOutOfRangeException(nameof(maxLogBytes));

        LogDirectory = logDirectory is null ? null : Path.GetFullPath(logDirectory);
        _maxLogBytes = maxLogBytes;
    }

    public string? LogDirectory { get; }
    internal string? LogPath => LogDirectory is null ? null : Path.Combine(LogDirectory, "application.log");
    internal string? PreviousLogPath => LogPath is null ? null : LogPath + ".previous";
    internal string? WikiLogPath => LogDirectory is null ? null : Path.Combine(LogDirectory, "wiki-sync.log");
    private static readonly HashSet<string> AllowedWikiStages = new(StringComparer.Ordinal)
    {
        "download-start", "download-redirects-start", "download-page-list-start", "update-start", "redirect-list", "page-list", "page-download", "page-skipped",
        "resource-downloaded", "resource-skipped", "http-retry", "download-item-missing", "download-item-failed",
        "sync-discovery", "sync-full-scan", "sync-capability-unavailable", "sync-pages-start", "sync-page-failed",
        "sync-delete", "sync-index-maintenance", "sync-page-cursor-committed", "sync-orphan-media-retained",
        "sync-media-start", "sync-media-failed", "resource-source-resolved",
        "viewer-rebind", "viewer-rebind-navigation", "viewer-rebind-quick"
    };

    internal void WriteWikiOperation(Guid operationId, int? wikiId, string operationEvent,
        WikiProgress? progress = null, Exception? error = null, string? result = null)
    {
        if (operationEvent is not ("started" or "progress" or "completed" or "failed" or "cancelled")) return;
        // Deliberately record structured exception codes, never Message/ToString or
        // stack traces: those can contain user paths, response bodies or credentials.
        var errors = new List<object>();
        for (var current = error ?? progress?.Error; current is not null && errors.Count < 4; current = current.InnerException)
            errors.Add(new
            {
                type = current.GetType().Name, hResult = current.HResult,
                httpStatus = current is HttpRequestException http ? (int?)http.StatusCode : null,
                sqliteResult = current is SQLite.SQLiteException sqlite ? sqlite.Result.ToString() : null,
                socketError = current is System.Net.Sockets.SocketException socket ? socket.SocketErrorCode.ToString() : null
            });
        WriteEntry(new
        {
            timestampUtc = DateTimeOffset.UtcNow, applicationVersion = ProductMetadata.Version,
            operationId, wikiId, operationEvent, stage = progress is not null && AllowedWikiStages.Contains(progress.Code) ? progress.Code : null,
            current = progress?.Current, total = progress?.Total,
            detail = progress is not null && AllowedWikiStages.Contains(progress.Code) ? SafeWikiDetail(progress.Detail) : null,
            result = SafeWikiDetail(result), errors
        }, WikiLogPath);
    }

    private static string? SafeWikiDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Contains('<') || value.Contains('\\') ||
            value.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("api_key", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '/') return null;
        if (value.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is not ("http" or "https")) return null;
            value = uri.GetLeftPart(UriPartial.Path); // No URL query, user info or fragment.
            if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        }
        return new string(value.Where(character => !char.IsControl(character)).Take(300).ToArray());
    }

    internal static ProductDiagnostics CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? new ProductDiagnostics(null)
            : new ProductDiagnostics(Path.Combine(localAppData, "Terraria AI Wiki", "logs"));
    }

    public void Write(ProductDiagnosticEvent diagnosticEvent, string? componentVersion = null)
    {
        var detail = diagnosticEvent switch
        {
            ProductDiagnosticEvent.RuntimeReady => SanitizeVersion(componentVersion),
            ProductDiagnosticEvent.StartupFailed => SanitizeFailureCode(componentVersion),
            _ => null,
        };
        WriteEntry(new ProductDiagnosticEntry(
            DateTimeOffset.UtcNow,
            ProductMetadata.Version,
            diagnosticEvent.ToString(),
            detail,
            null,
            null,
            null,
            null));
    }

    public void WriteStartupStage(
        WindowsStartupStage stage,
        long elapsedMilliseconds,
        long durationMilliseconds)
    {
        WriteEntry(new ProductDiagnosticEntry(
            DateTimeOffset.UtcNow,
            ProductMetadata.Version,
            ProductDiagnosticEvent.StartupStageCompleted.ToString(),
            null,
            WindowsStartupClock.Name(stage),
            Math.Max(0, elapsedMilliseconds),
            Math.Max(0, durationMilliseconds),
            null));
    }

    public void WriteQuickAccessStage(QuickAccessStage stage, long milliseconds)
    {
        WriteEntry(new ProductDiagnosticEntry(
            DateTimeOffset.UtcNow,
            ProductMetadata.Version,
            ProductDiagnosticEvent.QuickAccessStageCompleted.ToString(),
            null,
            stage.ToString(),
            null,
            Math.Max(0, milliseconds),
            null));
    }

    public void WriteShutdown(
        long webViewStopMilliseconds,
        long aiStopMilliseconds,
        long viewerStopMilliseconds,
        long databaseCloseMilliseconds,
        long runtimeDisposeMilliseconds,
        long closeTotalMilliseconds)
    {
        WriteEntry(new ProductDiagnosticEntry(
            DateTimeOffset.UtcNow,
            ProductMetadata.Version,
            ProductDiagnosticEvent.ApplicationStopped.ToString(),
            null,
            null,
            null,
            null,
            new ProductShutdownDiagnostic(
                Math.Max(0, webViewStopMilliseconds),
                Math.Max(0, aiStopMilliseconds),
                Math.Max(0, viewerStopMilliseconds),
                Math.Max(0, databaseCloseMilliseconds),
                Math.Max(0, runtimeDisposeMilliseconds),
                Math.Max(0, closeTotalMilliseconds))));
    }

    internal void WriteInvestigation(StartupInvestigationEntry entry) => WriteEntry(entry);

    private void WriteEntry<T>(T entry) => WriteEntry(entry, LogPath);

    private void WriteEntry<T>(T entry, string? logPath)
    {
        if (logPath is null)
            return;

        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetByteCount(line);
        if (bytes > _maxLogBytes) return;

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogDirectory!);
                if (File.Exists(logPath) && new FileInfo(logPath).Length + bytes > _maxLogBytes)
                    File.Move(logPath, logPath + ".previous", overwrite: true);
                File.AppendAllText(logPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
            // Diagnostics must never prevent startup, recovery, or shutdown.
        }
    }

    private static string? SanitizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var sanitized = new string(value
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+')
            .Take(64)
            .ToArray());
        return sanitized.Length == 0 ? null : sanitized;
    }

    private static string? SanitizeFailureCode(string? value) =>
        value is not null && AllowedStartupFailureCodes.Contains(value) ? value : null;

    private sealed record ProductDiagnosticEntry(
        DateTimeOffset TimestampUtc,
        string ApplicationVersion,
        string Event,
        string? ComponentVersion,
        string? Stage,
        long? ElapsedMilliseconds,
        long? DurationMilliseconds,
        ProductShutdownDiagnostic? Shutdown);

    private sealed record ProductShutdownDiagnostic(
        long WebViewStopMilliseconds,
        long AiStopMilliseconds,
        long ViewerStopMilliseconds,
        long DatabaseCloseMilliseconds,
        long RuntimeDisposeMilliseconds,
        long CloseTotalMilliseconds);
}
