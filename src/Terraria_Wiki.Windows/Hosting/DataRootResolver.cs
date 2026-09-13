namespace Terraria_Wiki.Windows.Hosting;

internal enum DataRootSource
{
    CommandLine,
    SavedSettings,
    ManagedDefault,
}

internal sealed record DataRootResolution(string Path, DataRootSource Source);

internal sealed class DataRootResolver
{
    private readonly HostSettingsStore _settings;

    public DataRootResolver(HostSettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<DataRootResolution> ResolveAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var explicitPath = ParseExplicitPath(arguments);
        if (explicitPath is not null)
        {
            var validated = ValidateForInitialization(explicitPath, "invalid-explicit-data-root");
            await _settings.SaveDataRootAsync(validated, cancellationToken).ConfigureAwait(false);
            return new DataRootResolution(validated, DataRootSource.CommandLine);
        }

        var saved = await _settings.LoadDataRootAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(saved) && TryPrepareForInitialization(saved, out var savedPath, out _))
            return new DataRootResolution(savedPath!, DataRootSource.SavedSettings);

        if (!string.IsNullOrWhiteSpace(saved))
        {
            throw new DataRootResolutionException(
                "data-root-required",
                "已保存的 Wiki 数据目录不可用。请恢复该目录后重试，或清除无效的主机设置以使用默认目录。");
        }

        var defaultPath = Path.Combine(
            Path.GetDirectoryName(_settings.FilePath)!,
            "WikiData");
        try
        {
            Directory.CreateDirectory(defaultPath);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            throw new DataRootResolutionException(
                "default-data-root-unavailable",
                "无法创建默认 Wiki 数据目录。请检查目录权限和磁盘状态后重试。",
                exception);
        }

        if (!TryPrepareForInitialization(defaultPath, out var normalizedDefault, out _))
        {
            throw new DataRootResolutionException(
                "default-data-root-unavailable",
                "默认 Wiki 数据目录包含无法识别的数据。请先恢复或清理该目录后重试。");
        }

        await _settings.SaveDataRootAsync(normalizedDefault!, cancellationToken).ConfigureAwait(false);
        return new DataRootResolution(normalizedDefault!, DataRootSource.ManagedDefault);
    }

    internal static string? ParseExplicitPath(IReadOnlyList<string> arguments)
    {
        string? value = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            string? candidate = null;
            if (string.Equals(argument, "--data-root", StringComparison.Ordinal))
            {
                if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                    throw new DataRootResolutionException("missing-data-root-argument", "--data-root requires an absolute path.");
                candidate = arguments[index];
            }
            else if (argument.StartsWith("--data-root=", StringComparison.Ordinal))
            {
                candidate = argument["--data-root=".Length..];
                if (string.IsNullOrWhiteSpace(candidate))
                    throw new DataRootResolutionException("missing-data-root-argument", "--data-root requires an absolute path.");
            }

            if (candidate is null)
                continue;
            if (value is not null)
                throw new DataRootResolutionException("duplicate-data-root-argument", "--data-root may only be supplied once.");
            value = candidate;
        }

        return value;
    }

    internal static bool TryPrepareForInitialization(
        string candidate,
        out string? normalized,
        out string? error)
    {
        normalized = null;
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
            {
                error = "The Wiki data folder must be an absolute path.";
                return false;
            }

            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (!Directory.Exists(fullPath))
            {
                error = $"The Wiki data folder does not exist: {fullPath}";
                return false;
            }

            var managerPath = Path.Combine(fullPath, "Manager.db");
            if (!File.Exists(managerPath))
            {
                if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    normalized = fullPath;
                    return true;
                }
                error = $"The selected folder does not contain Manager.db: {fullPath}";
                return false;
            }

            using var stream = new FileStream(
                managerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.RandomAccess);
            if (stream.Length < 100)
            {
                error = $"Manager.db is not a valid SQLite database: {managerPath}";
                return false;
            }
            Span<byte> header = stackalloc byte[16];
            if (stream.Read(header) != header.Length
                || !header.SequenceEqual("SQLite format 3\0"u8))
            {
                error = $"Manager.db is not a valid SQLite database: {managerPath}";
                return false;
            }
            normalized = fullPath;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error = $"The Wiki data folder is not readable: {exception.Message}";
            return false;
        }
    }

    private static string ValidateForInitialization(string candidate, string code)
    {
        if (TryPrepareForInitialization(candidate, out var normalized, out var error))
            return normalized!;
        throw new DataRootResolutionException(code, error ?? "The Wiki data folder is invalid.");
    }
}

internal sealed class DataRootResolutionException : IOException
{
    public DataRootResolutionException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
