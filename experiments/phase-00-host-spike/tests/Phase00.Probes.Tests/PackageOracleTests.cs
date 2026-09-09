using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Phase00.Probes;

namespace Phase00.Probes.Tests;

public sealed class PackageOracleTests
{
    [Fact]
    public async Task IndependentSyntheticWriterOutputIsAcceptedByOracle()
    {
        using var temp = new TestDirectory();
        var package = Path.Combine(temp.Path, "writer-output.pkg");
        await SyntheticPackageWriter.WriteAsync(package, [
            new SyntheticPackageFile("failed_pages.txt", "none\n"u8.ToArray()),
            new SyntheticPackageFile("nested/data.bin", Enumerable.Range(0, 32).Select(i => (byte)i).ToArray())
        ]);

        var result = await PackageOracle.ValidateAsync(package, Path.Combine(temp.Path, "staging"));

        Assert.Equal(2, result.Files.Count);
        Assert.Equal("failed_pages.txt", result.Files[0].RelativePath);
        Assert.Equal("nested/data.bin", result.Files[1].RelativePath);
        Assert.True(result.SourceUnchanged);
        Assert.Equal(result.SourceSha256Before, result.SourceSha256After);
    }

    [Fact]
    public async Task AcceptsLegacyCamelCaseAndExtractsInFileOrder()
    {
        using var temp = new TestDirectory();
        var first = Encoding.UTF8.GetBytes("first");
        var second = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var package = Path.Combine(temp.Path, "valid.pkg");
        await WritePackageAsync(package, [
            ("failed_pages.txt", first),
            ("nested/data.bin", second)
        ]);

        var result = await PackageOracle.ValidateAsync(package, Path.Combine(temp.Path, "staging"));

        Assert.Equal(2, result.Files.Count);
        Assert.Equal("failed_pages.txt", result.Files[0].RelativePath);
        Assert.Equal("nested/data.bin", result.Files[1].RelativePath);
        Assert.Equal(first, await File.ReadAllBytesAsync(result.Files[0].FullPath));
        Assert.Equal(second, await File.ReadAllBytesAsync(result.Files[1].FullPath));
        Assert.True(result.SourceUnchanged);
        Assert.Equal(result.SourceSha256Before, result.SourceSha256After);
        Assert.Equal("mD5", JsonPropertyNameOf<FileMetaDto>(nameof(FileMetaDto.Md5)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("//server/share/file.txt")]
    [InlineData("C:/file.txt")]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    [InlineData("nested/./file.txt")]
    public async Task RejectsUnsafeRelativePathsWithoutWritingOutsideStaging(string path)
    {
        using var temp = new TestDirectory();
        var package = Path.Combine(temp.Path, "unsafe.pkg");
        await WritePackageAsync(package, [(path, [1, 2, 3])]);
        var outside = Path.Combine(temp.Path, "escape.txt");

        var exception = await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(package, Path.Combine(temp.Path, "staging")));

        Assert.Contains(exception.Code, new[] { "empty-path", "absolute-path", "unc-path", "drive-path", "invalid-path", "path-escape" });
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task RejectsCaseInsensitiveDuplicatesAndFileDirectoryConflicts()
    {
        using var temp = new TestDirectory();
        var duplicate = Path.Combine(temp.Path, "duplicate.pkg");
        await WritePackageAsync(duplicate, [("A.txt", [1]), ("a.TXT", [2])]);
        var conflict = Path.Combine(temp.Path, "conflict.pkg");
        await WritePackageAsync(conflict, [("a", [1]), ("a/b", [2])]);

        var duplicateError = await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(duplicate, Path.Combine(temp.Path, "duplicate-stage")));
        var conflictError = await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(conflict, Path.Combine(temp.Path, "conflict-stage")));
        Assert.Equal("duplicate-path", duplicateError.Code);
        Assert.Equal("path-conflict", conflictError.Code);
    }

    [Fact]
    public async Task RejectsNegativeLengthHashMismatchTruncationAndTrailingData()
    {
        using var temp = new TestDirectory();
        var negative = Path.Combine(temp.Path, "negative.pkg");
        await WritePackageAsync(negative, [("x", [1])], sizeOverride: -1);
        var hash = Path.Combine(temp.Path, "hash.pkg");
        await WritePackageAsync(hash, [("x", [1])], md5Override: "00000000000000000000000000000000");
        var trunc = Path.Combine(temp.Path, "trunc.pkg");
        await WritePackageAsync(trunc, [("x", [1, 2, 3])], truncatePayload: true);
        var trailing = Path.Combine(temp.Path, "trailing.pkg");
        await WritePackageAsync(trailing, [("x", [1])], trailing: [9]);

        Assert.Equal("negative-file-size", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(negative, Path.Combine(temp.Path, "s1")))).Code);
        Assert.Equal("hash-mismatch", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(hash, Path.Combine(temp.Path, "s2")))).Code);
        Assert.Equal("truncated-payload", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(trunc, Path.Combine(temp.Path, "s3")))).Code);
        Assert.Equal("trailing-data", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(trailing, Path.Combine(temp.Path, "s4")))).Code);
    }

    [Fact]
    public async Task EnforcesMetadataCountAndTotalLimitsBeforePayloadWrites()
    {
        using var temp = new TestDirectory();
        var package = Path.Combine(temp.Path, "limits.pkg");
        await WritePackageAsync(package, [("x", [1, 2, 3])]);
        var countError = await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(package, Path.Combine(temp.Path, "count"), new PackageOracleOptions { MaxFileCount = 0 }));
        var totalError = await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(package, Path.Combine(temp.Path, "total"), new PackageOracleOptions { MaxTotalBytes = 2 }));
        Assert.Equal("file-count-too-large", countError.Code);
        Assert.Equal("total-size-too-large", totalError.Code);
        Assert.True(!Directory.Exists(Path.Combine(temp.Path, "count")) || !Directory.EnumerateFileSystemEntries(Path.Combine(temp.Path, "count")).Any());
        Assert.True(!Directory.Exists(Path.Combine(temp.Path, "total")) || !Directory.EnumerateFileSystemEntries(Path.Combine(temp.Path, "total")).Any());
    }

    [Fact]
    public async Task RejectsOversizedAndMalformedMetadataLength()
    {
        using var temp = new TestDirectory();
        var negative = Path.Combine(temp.Path, "negative-length.pkg");
        await File.WriteAllBytesAsync(negative, BuildHeaderWithLength(-1));
        var tooLarge = Path.Combine(temp.Path, "large-length.pkg");
        await File.WriteAllBytesAsync(tooLarge, BuildHeaderWithLength(17));
        var overLimit = Path.Combine(temp.Path, "over-limit.pkg");
        await WritePackageAsync(overLimit, [("x", [1])]);

        Assert.Equal("negative-metadata-length", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(negative, Path.Combine(temp.Path, "s1")))).Code);
        Assert.Equal("truncated-metadata", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(tooLarge, Path.Combine(temp.Path, "s2")))).Code);
        Assert.Equal("metadata-too-large", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(overLimit, Path.Combine(temp.Path, "s3"), new PackageOracleOptions { MaxMetadataBytes = 1 }))).Code);
    }

    [Fact]
    public async Task RejectsEmptyAndInvalidHeaders()
    {
        using var temp = new TestDirectory();
        var empty = Path.Combine(temp.Path, "empty.pkg");
        await File.WriteAllBytesAsync(empty, []);
        var invalid = Path.Combine(temp.Path, "invalid-header.pkg");
        await File.WriteAllBytesAsync(invalid, "NOTDATAX"u8.ToArray());

        Assert.Equal("empty-package", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(empty, Path.Combine(temp.Path, "s1")))).Code);
        Assert.Equal("invalid-header", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(invalid, Path.Combine(temp.Path, "s2")))).Code);
    }

    [Fact]
    public async Task RefusesNonEmptyStagingAndInputOutputCollision()
    {
        using var temp = new TestDirectory();
        var package = Path.Combine(temp.Path, "valid.pkg");
        await WritePackageAsync(package, [("x", [1])]);
        var staging = Path.Combine(temp.Path, "staging");
        Directory.CreateDirectory(staging);
        await File.WriteAllTextAsync(Path.Combine(staging, "preexisting.txt"), "must remain");

        Assert.Equal("staging-not-empty", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(package, staging))).Code);
        Assert.True(File.Exists(Path.Combine(staging, "preexisting.txt")));
        Assert.Equal("input-output-same", (await Assert.ThrowsAsync<PackageValidationException>(() => PackageOracle.ValidateAsync(package, package))).Code);
    }

    private static async Task WritePackageAsync(
        string path,
        IReadOnlyList<(string RelativePath, byte[] Data)> files,
        long? sizeOverride = null,
        string? md5Override = null,
        bool truncatePayload = false,
        byte[]? trailing = null)
    {
        var entries = files.Select(file => new
        {
            relativePath = file.RelativePath,
            size = file.RelativePath == files[0].RelativePath && sizeOverride.HasValue ? sizeOverride.Value : file.Data.LongLength,
            mD5 = file.RelativePath == files[0].RelativePath && md5Override is not null
                ? md5Override
                : Convert.ToHexString(MD5.HashData(file.Data)).ToLowerInvariant()
        }).ToArray();
        var metadata = new
        {
            id = 1,
            title = "Synthetic",
            isPageDownloaded = true,
            isResourceDownloaded = true,
            updateTime = "2026-08-20T00:00:00Z",
            appVersion = "0.3.0.3",
            files = entries
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(metadata);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync("WIKIDATA"u8.ToArray());
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
        await stream.WriteAsync(length);
        await stream.WriteAsync(json);
        foreach (var file in files)
        {
            if (truncatePayload && file == files[^1])
            {
                if (file.Data.Length > 1) await stream.WriteAsync(file.Data.AsMemory(0, file.Data.Length - 1));
            }
            else await stream.WriteAsync(file.Data);
        }
        if (trailing is not null) await stream.WriteAsync(trailing);
    }

    private static byte[] BuildHeaderWithLength(int length)
    {
        var bytes = new byte[12];
        "WIKIDATA"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), length);
        return bytes;
    }

    private static string? JsonPropertyNameOf<T>(string propertyName) =>
        typeof(T).GetProperty(propertyName)?.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false).Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().SingleOrDefault()?.Name;

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "phase00-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
