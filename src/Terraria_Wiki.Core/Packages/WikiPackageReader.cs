using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terraria_Wiki.Core.Packages;

public static class WikiPackageReader
{
    public const string Header = "WIKIDATA";
    public const int HeaderLength = 8;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.Strict,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public static async Task<WikiPackageInspection> InspectAsync(
        string packagePath,
        WikiPackageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = (options ?? new WikiPackageOptions()).Validate();
        var fullPath = ValidatePackageInput(packagePath);

        await using var stream = OpenPackage(fullPath, options.CopyBufferBytes);
        return await ReadInspectionAsync(stream, fullPath, options, cancellationToken);
    }

    public static async Task<WikiPackageExtractionResult> ExtractAsync(
        string packagePath,
        string stagingDirectory,
        string? expectedInspectionToken = null,
        WikiPackageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = (options ?? new WikiPackageOptions()).Validate();
        var fullPath = ValidatePackageInput(packagePath);
        if (string.IsNullOrWhiteSpace(stagingDirectory) || !Path.IsPathFullyQualified(stagingDirectory))
            throw Error("staging-path-not-absolute", "The staging directory must be absolute.");
        var staging = Path.GetFullPath(stagingDirectory);
        WikiPackagePathPolicy.EnsureSafeStaging(staging, fullPath, out var createdStaging);
        Directory.CreateDirectory(staging);

        try
        {
            await using var stream = OpenPackage(fullPath, options.CopyBufferBytes);
            var inspection = await ReadInspectionAsync(stream, fullPath, options, cancellationToken);
            if (expectedInspectionToken is not null
                && !string.Equals(expectedInspectionToken, inspection.InspectionToken, StringComparison.Ordinal))
                throw Error("package-changed", "The package metadata changed after it was inspected.");

            var files = inspection.Metadata.Files!;
            var normalizedPaths = WikiPackagePathPolicy.ValidateAndNormalize(files, options, out _);
            var extracted = new List<WikiPackageExtractedFile>(files.Count);
            var buffer = ArrayPool<byte>.Shared.Rent(options.CopyBufferBytes);
            try
            {
                for (var index = 0; index < files.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var metadata = files[index];
                    var relativePath = normalizedPaths[index];
                    var targetPath = WikiPackagePathPolicy.ResolveUnderRoot(staging, relativePath);
                    var partialPath = targetPath + ".wiki-package-part";

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                        await using var output = new FileStream(
                            partialPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            options.CopyBufferBytes,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

                        long remaining = metadata.Size;
                        while (remaining > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var requested = (int)Math.Min(buffer.Length, remaining);
                            var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                            if (read == 0)
                                throw Error("truncated-payload", $"Payload for {relativePath} ended early.");

                            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            hash.AppendData(buffer, 0, read);
                            remaining -= read;
                        }

                        await output.FlushAsync(cancellationToken);
                        var calculatedHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                        if (!string.Equals(calculatedHash, metadata.Md5, StringComparison.Ordinal))
                            throw Error("hash-mismatch", $"MD5 mismatch for {relativePath}.");

                        await output.DisposeAsync();
                        File.Move(partialPath, targetPath, overwrite: false);
                        extracted.Add(new WikiPackageExtractedFile(relativePath, targetPath, metadata.Size, metadata.Md5!));
                    }
                    catch
                    {
                        TryDeleteFile(partialPath);
                        throw;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var declaredBoundary = checked(HeaderLength + sizeof(int) + inspection.MetadataBytes + inspection.PayloadBytes);
            if (stream.Position != declaredBoundary)
                throw Error("payload-length-mismatch", "Payload stream did not end at the declared boundary.");
            if (options.RejectTrailingBytes && stream.ReadByte() != -1)
                throw Error("trailing-data", "Package contains bytes after the declared payload.");

            return new WikiPackageExtractionResult(inspection, staging, extracted);
        }
        catch
        {
            CleanupStaging(staging, createdStaging);
            throw;
        }
    }

    private static async Task<WikiPackageInspection> ReadInspectionAsync(
        FileStream stream,
        string fullPath,
        WikiPackageOptions options,
        CancellationToken cancellationToken)
    {
        if (stream.Length == 0)
            throw Error("empty-package", "The package is empty.");

        var headerBytes = new byte[HeaderLength];
        await ReadExactlyAsync(stream, headerBytes, "truncated-package", cancellationToken);
        if (!headerBytes.AsSpan().SequenceEqual("WIKIDATA"u8))
            throw Error("invalid-header", "Package header is not WIKIDATA.");

        var lengthBytes = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, lengthBytes, "truncated-package", cancellationToken);
        var metadataLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (metadataLength < 0)
            throw Error("negative-metadata-length", "Metadata length is negative.");
        if (metadataLength == 0)
            throw Error("empty-metadata", "Metadata JSON is empty.");
        if (metadataLength > options.MaxMetadataBytes)
            throw Error("metadata-too-large", $"Metadata length {metadataLength} exceeds {options.MaxMetadataBytes} bytes.");
        if (metadataLength > stream.Length - stream.Position)
            throw Error("truncated-metadata", "Package ends before metadata is complete.");

        var metadataBytes = ArrayPool<byte>.Shared.Rent(metadataLength);
        try
        {
            await ReadExactlyAsync(
                stream,
                metadataBytes.AsMemory(0, metadataLength),
                "truncated-metadata",
                cancellationToken);
            var metadata = DeserializeMetadata(metadataBytes.AsSpan(0, metadataLength));
            if (metadata.Files is null)
                throw Error("missing-files", "Metadata files is missing or null.");

            WikiPackagePathPolicy.ValidateAndNormalize(metadata.Files, options, out var totalBytes);
            long expectedLength;
            try
            {
                expectedLength = checked(stream.Position + totalBytes);
            }
            catch (OverflowException ex)
            {
                throw Error("total-size-too-large", "Declared payload length overflows Int64.", ex);
            }

            if (stream.Length < expectedLength)
                throw Error("truncated-payload", $"Package length {stream.Length} is shorter than expected {expectedLength}.");
            if (options.RejectTrailingBytes && stream.Length != expectedLength)
                throw Error("trailing-data", $"Package contains {stream.Length - expectedLength} trailing bytes.");

            var inspectionToken = ComputeInspectionToken(
                headerBytes,
                lengthBytes,
                metadataBytes.AsSpan(0, metadataLength),
                stream.Length);
            return new WikiPackageInspection(
                fullPath,
                stream.Length,
                metadataLength,
                totalBytes,
                inspectionToken,
                metadata);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(metadataBytes, clearArray: true);
        }
    }

    private static WikiPackageMetadata DeserializeMetadata(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<WikiPackageMetadata>(bytes, JsonOptions)
                ?? throw Error("invalid-json", "Metadata JSON is null.");
        }
        catch (WikiPackageException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw Error("invalid-json", $"Metadata JSON is invalid: {ex.Message}", ex);
        }
    }

    private static string ComputeInspectionToken(
        ReadOnlySpan<byte> header,
        ReadOnlySpan<byte> length,
        ReadOnlySpan<byte> metadata,
        long packageLength)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(length);
        hash.AppendData(metadata);
        Span<byte> packageLengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(packageLengthBytes, packageLength);
        hash.AppendData(packageLengthBytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string ValidatePackageInput(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("A package path is required.", nameof(packagePath));
        if (!Path.IsPathFullyQualified(packagePath))
            throw Error("package-path-not-absolute", "The package path must be absolute.");

        var fullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPath))
            throw Error("package-not-found", $"Package does not exist: {fullPath}.");
        return fullPath;
    }

    private static FileStream OpenPackage(string fullPath, int bufferSize) =>
        new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async ValueTask ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = await stream.ReadAsync(destination[total..], cancellationToken);
            if (read == 0)
                throw Error(errorCode, "Package ended unexpectedly.");
            total += read;
        }
    }

    private static void CleanupStaging(string staging, bool created)
    {
        try
        {
            if (!Directory.Exists(staging)) return;
            if (created)
            {
                Directory.Delete(staging, recursive: true);
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(staging))
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
        }
        catch
        {
            // The original package exception remains authoritative. The caller
            // owns a pre-existing staging directory and can inspect cleanup.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static WikiPackageException Error(string code, string message, Exception? inner = null) =>
        new(code, message, inner);
}
