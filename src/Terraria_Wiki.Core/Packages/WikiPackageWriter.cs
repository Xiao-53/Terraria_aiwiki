using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Terraria_Wiki.Core.Packages;

public static class WikiPackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task<WikiPackageWriteResult> WriteAsync(
        string packagePath,
        WikiPackageDescriptor descriptor,
        IReadOnlyList<WikiPackageSourceFile> sourceFiles,
        WikiPackageOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        options = (options ?? new WikiPackageOptions()).Validate();

        var outputPath = ValidateOutputPath(packagePath);
        var sources = await PrepareSourcesAsync(sourceFiles, outputPath, options, cancellationToken);
        var metadata = new WikiPackageMetadata
        {
            Id = descriptor.Id,
            Title = descriptor.Title,
            IsPageDownloaded = descriptor.IsPageDownloaded,
            IsResourceDownloaded = descriptor.IsResourceDownloaded,
            UpdateTime = descriptor.UpdateTime,
            AppVersion = descriptor.AppVersion,
            Files = sources.Select(source => new WikiPackageFileMetadata
            {
                RelativePath = source.RelativePath,
                Size = source.Size,
                Md5 = source.Md5,
            }).ToList(),
        };
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions);
        if (metadataBytes.Length > options.MaxMetadataBytes)
            throw Error("metadata-too-large", $"Metadata length {metadataBytes.Length} exceeds {options.MaxMetadataBytes} bytes.");

        var buffer = ArrayPool<byte>.Shared.Rent(options.CopyBufferBytes);
        var outputCreated = false;
        try
        {
            await using var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                options.CopyBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            outputCreated = true;

            await output.WriteAsync("WIKIDATA"u8.ToArray(), cancellationToken);
            var lengthBytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, metadataBytes.Length);
            await output.WriteAsync(lengthBytes, cancellationToken);
            await output.WriteAsync(metadataBytes, cancellationToken);

            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CopyAndVerifySourceAsync(source, output, buffer, cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            if (outputCreated) TryDelete(outputPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var packageBytes = new FileInfo(outputPath).Length;
        return new WikiPackageWriteResult(
            outputPath,
            packageBytes,
            metadataBytes.Length,
            sources.Sum(source => source.Size),
            metadata);
    }

    private static async Task<IReadOnlyList<PreparedSource>> PrepareSourcesAsync(
        IReadOnlyList<WikiPackageSourceFile> sources,
        string outputPath,
        WikiPackageOptions options,
        CancellationToken cancellationToken)
    {
        if (sources.Count > options.MaxFileCount)
            throw Error("file-count-too-large", $"File count {sources.Count} exceeds {options.MaxFileCount}.");

        var provisional = new List<WikiPackageFileMetadata>(sources.Count);
        var fullSourcePaths = new List<string>(sources.Count);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source is null)
                throw Error("null-file-entry", "The source list contains a null file entry.");
            if (string.IsNullOrWhiteSpace(source.SourcePath) || !Path.IsPathFullyQualified(source.SourcePath))
                throw Error("source-path-not-absolute", $"Source path must be absolute: {source.SourcePath}.");

            var fullSourcePath = Path.GetFullPath(source.SourcePath);
            if (string.Equals(fullSourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
                throw Error("input-output-same", "A package source cannot also be the package output.");
            if (!File.Exists(fullSourcePath))
                throw Error("source-not-found", $"Package source does not exist: {fullSourcePath}.");
            if ((File.GetAttributes(fullSourcePath) & FileAttributes.ReparsePoint) != 0)
                throw Error("source-reparse-point", $"Package source cannot be a reparse point: {fullSourcePath}.");

            var info = new FileInfo(fullSourcePath);
            provisional.Add(new WikiPackageFileMetadata
            {
                RelativePath = source.RelativePath,
                Size = info.Length,
                Md5 = new string('0', 32),
            });
            fullSourcePaths.Add(fullSourcePath);
        }

        var normalizedPaths = WikiPackagePathPolicy.ValidateAndNormalize(provisional, options, out _);
        var prepared = new List<PreparedSource>(sources.Count);
        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = provisional[index].Size;
            var md5 = await ComputeMd5Async(fullSourcePaths[index], length, options.CopyBufferBytes, cancellationToken);
            prepared.Add(new PreparedSource(normalizedPaths[index], fullSourcePaths[index], length, md5));
        }
        return prepared;
    }

    private static async Task<string> ComputeMd5Async(
        string sourcePath,
        long expectedLength,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        await using var source = OpenSource(sourcePath, bufferSize);
        if (source.Length != expectedLength)
            throw Error("source-changed", $"Package source changed while metadata was prepared: {sourcePath}.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                total = checked(total + read);
            }

            if (total != expectedLength || source.Length != expectedLength)
                throw Error("source-changed", $"Package source changed while it was hashed: {sourcePath}.");
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task CopyAndVerifySourceAsync(
        PreparedSource source,
        Stream output,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await using var input = OpenSource(source.SourcePath, buffer.Length);
        if (input.Length != source.Size)
            throw Error("source-changed", $"Package source changed before it was copied: {source.SourcePath}.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        long remaining = source.Size;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
            if (read == 0)
                throw Error("source-changed", $"Package source ended early: {source.SourcePath}.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }

        if (input.ReadByte() != -1 || input.Length != source.Size)
            throw Error("source-changed", $"Package source grew while it was copied: {source.SourcePath}.");
        var copiedHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(copiedHash, source.Md5, StringComparison.Ordinal))
            throw Error("source-changed", $"Package source changed between hashing and copying: {source.SourcePath}.");
    }

    private static FileStream OpenSource(string path, int bufferSize) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static string ValidateOutputPath(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("A package output path is required.", nameof(packagePath));
        if (!Path.IsPathFullyQualified(packagePath))
            throw Error("output-path-not-absolute", "The package output path must be absolute.");

        var fullPath = Path.GetFullPath(packagePath);
        var parent = Path.GetDirectoryName(fullPath);
        if (parent is null || !Directory.Exists(parent))
            throw Error("output-directory-not-found", "The package output directory does not exist.");
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
            throw Error("output-exists", "The package output path already exists.");
        return fullPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static WikiPackageException Error(string code, string message) => new(code, message);

    private sealed record PreparedSource(string RelativePath, string SourcePath, long Size, string Md5);
}
