namespace Terraria_Wiki.Core.Packages;

internal static class WikiPackagePathPolicy
{
    public static IReadOnlyList<string> ValidateAndNormalize(
        IReadOnlyList<WikiPackageFileMetadata> files,
        WikiPackageOptions options,
        out long totalBytes)
    {
        if (files.Count > options.MaxFileCount)
            throw Error("file-count-too-large", $"File count {files.Count} exceeds {options.MaxFileCount}.");

        var normalizedPaths = new List<string>(files.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        totalBytes = 0;

        foreach (var file in files)
        {
            if (file is null)
                throw Error("null-file-entry", "Metadata contains a null file entry.");
            if (file.Size < 0)
                throw Error("negative-file-size", "A file size is negative.");
            if (file.Size > options.MaxFileBytes)
                throw Error("file-too-large", $"File {file.RelativePath} exceeds {options.MaxFileBytes} bytes.");
            if (!IsCanonicalMd5(file.Md5))
                throw Error("invalid-md5", $"File {file.RelativePath} does not contain a lowercase MD5 hash.");

            var normalized = Normalize(file.RelativePath);
            if (!seen.Add(normalized))
                throw Error("duplicate-path", $"Duplicate package path (case-insensitive): {file.RelativePath}.");

            foreach (var other in normalizedPaths)
            {
                if (normalized.StartsWith(other + "/", StringComparison.OrdinalIgnoreCase)
                    || other.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))
                    throw Error("path-conflict", $"Package paths overlap as file and directory: {file.RelativePath}.");
            }

            try
            {
                totalBytes = checked(totalBytes + file.Size);
            }
            catch (OverflowException ex)
            {
                throw Error("total-size-too-large", "Declared payload size overflows Int64.", ex);
            }

            if (totalBytes > options.MaxTotalBytes)
                throw Error("total-size-too-large", $"Package payload exceeds {options.MaxTotalBytes} bytes.");

            normalizedPaths.Add(normalized);
        }

        return normalizedPaths;
    }

    public static string ResolveUnderRoot(string root, string normalizedRelativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var rootPrefix = EnsureTrailingSeparator(fullRoot);
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            throw Error("invalid-path", $"Invalid package path: {normalizedRelativePath}.", ex);
        }

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw Error("path-escape", $"Package path escapes staging directory: {normalizedRelativePath}.");

        var parent = Path.GetDirectoryName(fullPath);
        if (parent is null || (!string.Equals(parent, fullRoot, StringComparison.OrdinalIgnoreCase)
            && !parent.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)))
            throw Error("path-escape", $"Package path escapes staging directory: {normalizedRelativePath}.");

        return fullPath;
    }

    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw Error("empty-path", "A package path is empty.");
        if (path.IndexOf('\0') >= 0)
            throw Error("invalid-path", "A package path contains a NUL character.");

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/')
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || normalized.StartsWith("\\\\", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
            || Path.IsPathRooted(path))
            throw Error("absolute-path", $"Package path must be relative: {path}.");

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            throw Error("invalid-path", $"Package path contains an empty, '.' or '..' segment: {path}.");
        if (segments.Any(segment => segment.Contains(':')))
            throw Error("drive-path", $"Package path contains a drive separator: {path}.");

        return string.Join('/', segments);
    }

    public static void EnsureSafeStaging(string stagingDirectory, string packagePath, out bool created)
    {
        var staging = Path.GetFullPath(stagingDirectory);
        var package = Path.GetFullPath(packagePath);
        if (string.Equals(staging, package, StringComparison.OrdinalIgnoreCase))
            throw Error("input-output-same", "Package path cannot be the staging directory.");
        if (File.Exists(staging))
            throw Error("staging-is-file", "Staging path is an existing file.");

        created = !Directory.Exists(staging);
        if (!created)
        {
            if (Directory.EnumerateFileSystemEntries(staging).Any())
                throw Error("staging-not-empty", "Staging directory must be empty.");
            if ((File.GetAttributes(staging) & FileAttributes.ReparsePoint) != 0)
                throw Error("staging-reparse-point", "Staging directory cannot be a reparse point.");
        }
    }

    public static bool IsCanonicalMd5(string? value) =>
        value is { Length: 32 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private static WikiPackageException Error(string code, string message, Exception? inner = null) =>
        new(code, message, inner);
}
