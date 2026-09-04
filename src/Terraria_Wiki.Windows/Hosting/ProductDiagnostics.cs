using System.Text;
using System.Text.Json;
using Terraria_Wiki.Windows.Updates;

namespace Terraria_Wiki.Windows.Hosting;

internal enum ProductDiagnosticEvent
{
    ApplicationStarted,
    RuntimeReady,
    StartupFailed,
    WebView2RecoveryOpened,
    ApplicationStopped,
}

internal sealed class ProductDiagnostics
{
    internal const long DefaultMaxLogBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        var logPath = LogPath;
        if (logPath is null)
            return;

        var entry = new ProductDiagnosticEntry(
            DateTimeOffset.UtcNow,
            ProductMetadata.Version,
            diagnosticEvent.ToString(),
            SanitizeVersion(componentVersion));
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

    private sealed record ProductDiagnosticEntry(
        DateTimeOffset TimestampUtc,
        string ApplicationVersion,
        string Event,
        string? ComponentVersion);
}
