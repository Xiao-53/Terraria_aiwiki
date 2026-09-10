using System.Text;
using System.Text.Json;
using Terraria_Wiki.Windows.Updates;

namespace Terraria_Wiki.Windows.Hosting;

internal enum ProductDiagnosticEvent
{
    ApplicationStarted,
    StartupStageCompleted,
    RuntimeReady,
    StartupFailed,
    WebView2RecoveryOpened,
    ApplicationStopped,
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
        "legacy-client-running",
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
            Math.Max(0, durationMilliseconds)));
    }

    private void WriteEntry(ProductDiagnosticEntry entry)
    {
        var logPath = LogPath;
        if (logPath is null)
            return;

        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetByteCount(line);

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(LogDirectory!);
                if (File.Exists(logPath) && new FileInfo(logPath).Length + bytes > _maxLogBytes)
                    File.Move(logPath, PreviousLogPath!, overwrite: true);
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
        long? DurationMilliseconds);
}
