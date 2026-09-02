using System.Text.Json;
using Terraria_Wiki.Application;

namespace Terraria_Wiki.Windows.Hosting;

internal sealed class HostSettingsStore : IApplicationSettingsStore
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public HostSettingsStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A host settings path is required.", nameof(filePath));
        if (!Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("The host settings path must be absolute.", nameof(filePath));

        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }
    internal string BackupPath => FilePath + ".backup";

    public static HostSettingsStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new HostSettingsException("local-app-data-unavailable", "Windows LocalApplicationData is unavailable.");
        return new HostSettingsStore(Path.Combine(localAppData, "Terraria AI Wiki", "host-settings.json"));
    }

    public Task<ApplicationPreferences?> LoadPreferencesAsync(CancellationToken cancellationToken = default) =>
        ReadValueAsync(static document => document.Preferences, cancellationToken);

    public Task<int?> LoadActiveWikiIdAsync(CancellationToken cancellationToken = default) =>
        ReadValueAsync(static document => document.ActiveWikiId, cancellationToken);

    public Task<string?> LoadDataRootAsync(CancellationToken cancellationToken = default) =>
        ReadValueAsync(static document => document.DataRoot, cancellationToken);

    public Task SavePreferencesAsync(ApplicationPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return UpdateAsync(document => document with { Preferences = preferences }, cancellationToken);
    }

    public Task SaveActiveWikiIdAsync(int wikiId, CancellationToken cancellationToken = default) =>
        UpdateAsync(document => document with { ActiveWikiId = wikiId }, cancellationToken);

    public Task SaveDataRootAsync(string dataRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
            throw new ArgumentException("The data root must be absolute.", nameof(dataRoot));
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataRoot));
        return UpdateAsync(document => document with { DataRoot = normalized }, cancellationToken);
    }

    private async Task<T> ReadValueAsync<T>(
        Func<HostSettingsDocument, T> selector,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            return selector(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpdateAsync(
        Func<HostSettingsDocument, HostSettingsDocument> update,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            await WriteDocumentAsync(update(current) with { Version = CurrentVersion }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HostSettingsDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath) && !File.Exists(BackupPath))
            return new HostSettingsDocument();

        var primary = await TryReadAsync(FilePath, cancellationToken).ConfigureAwait(false);
        if (primary is not null)
            return primary;

        var backup = await TryReadAsync(BackupPath, cancellationToken).ConfigureAwait(false);
        if (backup is null)
        {
            throw new HostSettingsException(
                "host-settings-invalid",
                $"Neither '{FilePath}' nor its backup contains valid host settings.");
        }

        await RestoreBackupAsync(cancellationToken).ConfigureAwait(false);
        return backup;
    }

    private static async Task<HostSettingsDocument?> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<HostSettingsDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return document is { Version: CurrentVersion } ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task RestoreBackupAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await CopyDurablyAsync(BackupPath, tempPath, cancellationToken).ConfigureAwait(false);
            if (File.Exists(FilePath))
                File.Replace(tempPath, FilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, FilePath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private async Task WriteDocumentAsync(HostSettingsDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(FilePath))
                File.Replace(tempPath, FilePath, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new HostSettingsException("host-settings-write-failed", "Host settings could not be saved.", exception);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static async Task CopyDurablyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}

internal sealed record HostSettingsDocument
{
    public int Version { get; init; } = 1;
    public ApplicationPreferences? Preferences { get; init; }
    public int? ActiveWikiId { get; init; }
    public string? DataRoot { get; init; }
}

internal sealed class HostSettingsException : IOException
{
    public HostSettingsException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
