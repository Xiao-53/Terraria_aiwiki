using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Phase00.Probes;

/// <summary>
/// Disposable fixture-only writer. Its DTOs and serializer are intentionally
/// independent from PackageOracle's legacy metadata DTO and deserializer; the
/// resulting bytes are accepted only when the oracle can parse them.
/// </summary>
public static class SyntheticPackageWriter
{
    public static async Task WriteAsync(
        string packagePath,
        IReadOnlyList<SyntheticPackageFile> files,
        CancellationToken cancellationToken = default)
    {
        var metadata = new SyntheticMetadata
        {
            id = 1,
            title = "Phase 00 Fixture",
            isPageDownloaded = true,
            isResourceDownloaded = true,
            updateTime = "2026-08-20T00:00:00Z",
            appVersion = "0.3.0.3",
            files = files.Select(file => new SyntheticFileMetadata
            {
                relativePath = file.RelativePath,
                size = file.Data.LongLength,
                mD5 = Convert.ToHexString(MD5.HashData(file.Data)).ToLowerInvariant()
            }).ToArray()
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(metadata);

        var fullPath = Path.GetFullPath(packagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var output = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        await output.WriteAsync("WIKIDATA"u8.ToArray(), cancellationToken);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
        await output.WriteAsync(length, cancellationToken);
        await output.WriteAsync(json, cancellationToken);
        foreach (var file in files)
            await output.WriteAsync(file.Data, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private sealed class SyntheticMetadata
    {
        public int id { get; init; }
        public string? title { get; init; }
        public bool isPageDownloaded { get; init; }
        public bool isResourceDownloaded { get; init; }
        public string? updateTime { get; init; }
        public string? appVersion { get; init; }
        public IReadOnlyList<SyntheticFileMetadata> files { get; init; } = Array.Empty<SyntheticFileMetadata>();
    }

    private sealed class SyntheticFileMetadata
    {
        public string? relativePath { get; init; }
        public long size { get; init; }
        public string? mD5 { get; init; }
    }
}

public sealed record SyntheticPackageFile(string RelativePath, byte[] Data);
