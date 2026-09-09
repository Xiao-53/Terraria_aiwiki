using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phase00.Probes;

/// <summary>
/// A read-only compatibility and safety oracle for the legacy WIKIDATA package.
/// It deliberately does not share the production importer or exporter.
/// </summary>
public static class PackageOracle
{
    public const string Header = "WIKIDATA";
    public const int HeaderLength = 8;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static async Task<PackageValidationResult> ValidateAsync(
        string packagePath,
        string stagingDirectory,
        PackageOracleOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PackageOracleOptions();

        var packageFullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(packageFullPath))
            throw new PackageValidationException("package-not-found", $"Package does not exist: {packageFullPath}");

        var packageInfo = new FileInfo(packageFullPath);
        if (packageInfo.Length == 0)
            throw new PackageValidationException("empty-package", "The package is empty.");

        var stagingFullPath = Path.GetFullPath(stagingDirectory);
        EnsureSafeStagingDirectory(stagingFullPath, packageFullPath);
        var sourceSha256Before = await ComputeSha256Async(packageFullPath, cancellationToken);

        var createdStaging = !Directory.Exists(stagingFullPath);
        Directory.CreateDirectory(stagingFullPath);

        try
        {
            await using var stream = new FileStream(
                packageFullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                options: FileOptions.SequentialScan | FileOptions.Asynchronous);

            var headerBytes = new byte[HeaderLength];
            await ReadExactlyAsync(stream, headerBytes, cancellationToken);
            if (!headerBytes.AsSpan().SequenceEqual("WIKIDATA"u8))
                throw new PackageValidationException("invalid-header", "Package header is not WIKIDATA.");

            var lengthBytes = new byte[sizeof(int)];
            await ReadExactlyAsync(stream, lengthBytes, cancellationToken);
            var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (metadataLength < 0)
                throw new PackageValidationException("negative-metadata-length", "Metadata length is negative.");
            if (metadataLength > options.MaxMetadataBytes)
                throw new PackageValidationException("metadata-too-large", $"Metadata length {metadataLength} exceeds {options.MaxMetadataBytes} bytes.");
            if (metadataLength > stream.Length - stream.Position)
                throw new PackageValidationException("truncated-metadata", "Package ends before metadata is complete.");

            var metadataBytes = ArrayPool<byte>.Shared.Rent(Math.Max(metadataLength, 1));
            WikiPackageInfoDto metadata;
            try
            {
                await ReadExactlyAsync(stream, metadataBytes.AsMemory(0, metadataLength), cancellationToken);
                metadata = DeserializeMetadata(metadataBytes.AsSpan(0, metadataLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(metadataBytes, clearArray: true);
            }

            if (metadata.Files is null)
                throw new PackageValidationException("missing-files", "Metadata Files is missing or null.");
            if (metadata.Files.Count > options.MaxFileCount)
                throw new PackageValidationException("file-count-too-large", $"File count {metadata.Files.Count} exceeds {options.MaxFileCount}.");

            var plans = ValidateFilePlans(metadata.Files, stagingFullPath, options);
            var payloadStart = stream.Position;
            long expectedLength;
            try { expectedLength = checked(payloadStart + plans.TotalBytes); }
            catch (OverflowException ex)
            {
                throw new PackageValidationException("total-size-too-large", "Declared payload length overflows Int64.", ex);
            }
            if (stream.Length < expectedLength)
                throw new PackageValidationException("truncated-payload", $"Package length {stream.Length} is shorter than expected {expectedLength}.");
            if (options.RejectTrailingBytes && stream.Length != expectedLength)
                throw new PackageValidationException("trailing-data", $"Package contains {stream.Length - expectedLength} trailing bytes.");

            var extracted = new List<ExtractedPackageFile>(plans.Files.Count);
            foreach (var plan in plans.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetPath = plan.FullPath;
                var partialPath = targetPath + ".phase00-part";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await using var output = new FileStream(
                        partialPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1024 * 1024,
                        options: FileOptions.SequentialScan | FileOptions.Asynchronous);

                    using var md5 = MD5.Create();
                    var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(options.CopyBufferBytes, 4 * 1024 * 1024));
                    try
                    {
                        long remaining = plan.Size;
                        while (remaining > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var requested = (int)Math.Min(buffer.Length, remaining);
                            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                            if (read == 0)
                                throw new PackageValidationException("truncated-payload", $"Payload for {plan.RelativePath} ended early.");

                            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            md5.TransformBlock(buffer, 0, read, null, 0);
                            remaining -= read;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    await output.FlushAsync(cancellationToken);
                    if (!string.Equals(Convert.ToHexString(md5.Hash!).ToLowerInvariant(), plan.Md5, StringComparison.Ordinal))
                        throw new PackageValidationException("hash-mismatch", $"MD5 mismatch for {plan.RelativePath}.");

                    await output.DisposeAsync();
                    File.Move(partialPath, targetPath, overwrite: false);
                    extracted.Add(new ExtractedPackageFile(plan.RelativePath, plan.Size, plan.Md5, targetPath));
                }
                catch
                {
                    TryDelete(partialPath);
                    throw;
                }
            }

            if (stream.Position != expectedLength)
                throw new PackageValidationException("payload-length-mismatch", "Payload stream did not end at the declared boundary.");

            if (options.RejectTrailingBytes && stream.ReadByte() != -1)
                throw new PackageValidationException("trailing-data", "Package contains bytes after the declared payload.");

            var sourceSha256After = await ComputeSha256Async(packageFullPath, cancellationToken);

            return new PackageValidationResult(
                packageFullPath,
                packageInfo.Length,
                sourceSha256Before,
                sourceSha256After,
                string.Equals(sourceSha256Before, sourceSha256After, StringComparison.OrdinalIgnoreCase),
                metadataLength,
                metadata,
                extracted,
                plans.TotalBytes,
                stagingFullPath);
        }
        catch
        {
            // The caller owns the staging directory and may inspect/clean it. No
            // path outside that directory is ever opened for writing by this oracle.
            if (createdStaging && Directory.Exists(stagingFullPath))
            {
                try { Directory.Delete(stagingFullPath, recursive: true); } catch { /* best effort */ }
            }

            throw;
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Serializes a metadata object with the same camel-case policy as
    /// the old source-generated System.Text.Json context. This helper is only
    /// for the runner's disposable synthetic fixture.</summary>
    public static byte[] SerializeMetadataForFixture(WikiPackageInfoDto metadata)
    {
        var options = new JsonSerializerOptions(JsonOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        return JsonSerializer.SerializeToUtf8Bytes(metadata, options);
    }

    private static WikiPackageInfoDto DeserializeMetadata(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            throw new PackageValidationException("empty-metadata", "Metadata JSON is empty.");

        try
        {
            var value = JsonSerializer.Deserialize<WikiPackageInfoDto>(bytes, JsonOptions);
            if (value is null)
                throw new PackageValidationException("invalid-json", "Metadata JSON is null.");
            return value;
        }
        catch (PackageValidationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new PackageValidationException("invalid-json", $"Metadata JSON is invalid: {ex.Message}", ex);
        }
    }

    private static PackageFilePlanSet ValidateFilePlans(
        IReadOnlyList<FileMetaDto> files,
        string stagingDirectory,
        PackageOracleOptions options)
    {
        var root = Path.GetFullPath(stagingDirectory);
        var rootWithSeparator = EnsureTrailingSeparator(root);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planned = new List<PackageFilePlan>(files.Count);
        long total = 0;

        foreach (var file in files)
        {
            if (file is null)
                throw new PackageValidationException("null-file-entry", "Metadata contains a null file entry.");
            if (file.Size < 0)
                throw new PackageValidationException("negative-file-size", "A file size is negative.");
            if (file.Size > options.MaxFileBytes)
                throw new PackageValidationException("file-too-large", $"File {file.RelativePath} exceeds {options.MaxFileBytes} bytes.");
            if (file.Md5 is null || file.Md5.Length != 32 || file.Md5.Any(c => !IsLowerHex(c)))
                throw new PackageValidationException("invalid-md5", $"File {file.RelativePath} does not contain a lowercase MD5 hash.");

            var normalized = NormalizeRelativePath(file.RelativePath);
            if (!seen.Add(normalized))
                throw new PackageValidationException("duplicate-path", $"Duplicate package path (case-insensitive): {file.RelativePath}.");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                throw new PackageValidationException("invalid-path", $"Invalid package path: {file.RelativePath}.", ex);
            }

            if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new PackageValidationException("path-escape", $"Package path escapes staging directory: {file.RelativePath}.");

            foreach (var other in planned)
            {
                if (fullPath.StartsWith(EnsureTrailingSeparator(other.FullPath), StringComparison.OrdinalIgnoreCase)
                    || other.FullPath.StartsWith(EnsureTrailingSeparator(fullPath), StringComparison.OrdinalIgnoreCase))
                    throw new PackageValidationException("path-conflict", $"Package paths overlap as file and directory: {file.RelativePath}.");
            }

            try { total = checked(total + file.Size); }
            catch (OverflowException ex)
            {
                throw new PackageValidationException("total-size-too-large", "Declared payload size overflows Int64.", ex);
            }
            if (total > options.MaxTotalBytes)
                throw new PackageValidationException("total-size-too-large", $"Package payload exceeds {options.MaxTotalBytes} bytes.");

            var parent = Path.GetDirectoryName(fullPath);
            if (parent is null || (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)
                && !parent.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)))
                throw new PackageValidationException("path-escape", $"Package path escapes staging directory: {file.RelativePath}.");
            planned.Add(new PackageFilePlan(normalized, fullPath, file.Size, file.Md5));
        }

        return new PackageFilePlanSet(planned, total);
    }

    private static string NormalizeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PackageValidationException("empty-path", "A package path is empty.");
        if (path.IndexOf('\0') >= 0)
            throw new PackageValidationException("invalid-path", "A package path contains a NUL character.");

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.StartsWith("//", StringComparison.Ordinal)
            || normalized.StartsWith("\\\\", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
            || Path.IsPathRooted(path))
            throw new PackageValidationException("absolute-path", $"Package path must be relative: {path}.");

        var parts = normalized.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".."))
            throw new PackageValidationException("invalid-path", $"Package path contains an empty, '.' or '..' segment: {path}.");
        if (parts.Any(part => part.IndexOf(':') >= 0))
            throw new PackageValidationException("drive-path", $"Package path contains a drive separator: {path}.");

        return string.Join('/', parts);
    }

    private static void EnsureSafeStagingDirectory(string stagingDirectory, string packagePath)
    {
        var root = Path.GetFullPath(stagingDirectory);
        var package = Path.GetFullPath(packagePath);
        if (string.Equals(root, package, StringComparison.OrdinalIgnoreCase))
            throw new PackageValidationException("input-output-same", "Package path cannot be the staging directory.");
        if (File.Exists(root))
            throw new PackageValidationException("staging-is-file", "Staging path is an existing file.");

        if (Directory.Exists(root))
        {
            if (Directory.EnumerateFileSystemEntries(root).Any())
                throw new PackageValidationException("staging-not-empty", "Staging directory must be empty.");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new PackageValidationException("staging-reparse-point", "Staging directory cannot be a reparse point.");
        }
    }

    private static bool IsLowerHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static string EnsureTrailingSeparator(string path)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        return path.EndsWith(separator, StringComparison.Ordinal) ? path : path + separator;
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        var readTotal = 0;
        while (readTotal < destination.Length)
        {
            var read = await stream.ReadAsync(destination[readTotal..], cancellationToken);
            if (read == 0)
                throw new PackageValidationException("truncated-package", "Package ended unexpectedly.");
            readTotal += read;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private sealed record PackageFilePlan(string RelativePath, string FullPath, long Size, string Md5);
    private sealed record PackageFilePlanSet(IReadOnlyList<PackageFilePlan> Files, long TotalBytes);
}

public sealed class PackageOracleOptions
{
    public int MaxMetadataBytes { get; init; } = 16 * 1024 * 1024;
    public int MaxFileCount { get; init; } = 100_000;
    public long MaxFileBytes { get; init; } = 8L * 1024 * 1024 * 1024;
    public long MaxTotalBytes { get; init; } = 16L * 1024 * 1024 * 1024;
    public int CopyBufferBytes { get; init; } = 1024 * 1024;
    public bool RejectTrailingBytes { get; init; } = true;
}

public sealed class PackageValidationException : Exception
{
    public PackageValidationException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

public sealed record WikiPackageInfoDto
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("isPageDownloaded")]
    public bool IsPageDownloaded { get; init; }

    [JsonPropertyName("isResourceDownloaded")]
    public bool IsResourceDownloaded { get; init; }

    [JsonPropertyName("updateTime")]
    public DateTime UpdateTime { get; init; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; init; }

    [JsonPropertyName("files")]
    public List<FileMetaDto>? Files { get; init; }
}

public sealed record FileMetaDto
{
    [JsonPropertyName("relativePath")]
    public string? RelativePath { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    // System.Text.Json's camel-case policy serializes the legacy MD5 property as mD5.
    [JsonPropertyName("mD5")]
    public string? Md5 { get; init; }
}

public sealed record ExtractedPackageFile(string RelativePath, long Size, string Md5, string FullPath);

public sealed record PackageValidationResult(
    string PackagePath,
    long PackageBytes,
    string SourceSha256Before,
    string SourceSha256After,
    bool SourceUnchanged,
    int MetadataBytes,
    WikiPackageInfoDto Metadata,
    IReadOnlyList<ExtractedPackageFile> Files,
    long PayloadBytes,
    string StagingDirectory);
