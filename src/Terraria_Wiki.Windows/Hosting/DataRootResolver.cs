namespace Terraria_Wiki.Windows.Hosting;

internal interface IDataRootPicker
{
    Task<string?> PickAsync(CancellationToken cancellationToken = default);
}

internal enum DataRootSource
{
    CommandLine,
    SavedSettings,
    FolderPicker,
}

internal sealed record DataRootResolution(string Path, DataRootSource Source);

internal sealed class DataRootResolver
{
    private readonly HostSettingsStore _settings;
    private readonly IDataRootPicker _picker;

    public DataRootResolver(HostSettingsStore settings, IDataRootPicker picker)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
    }

    public async Task<DataRootResolution> ResolveAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var explicitPath = ParseExplicitPath(arguments);
        if (explicitPath is not null)
        {
            var validated = Validate(explicitPath, "invalid-explicit-data-root");
            await _settings.SaveDataRootAsync(validated, cancellationToken).ConfigureAwait(false);
            return new DataRootResolution(validated, DataRootSource.CommandLine);
        }

        var saved = await _settings.LoadDataRootAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(saved) && TryValidate(saved, out var savedPath, out _))
            return new DataRootResolution(savedPath!, DataRootSource.SavedSettings);

        var selected = await _picker.PickAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(selected))
        {
            throw new DataRootResolutionException(
                "data-root-selection-cancelled",
                "No Wiki data folder was selected. Choose the folder that contains Manager.db and retry.");
        }

        var selectedPath = Validate(selected, "invalid-selected-data-root");
        await _settings.SaveDataRootAsync(selectedPath, cancellationToken).ConfigureAwait(false);
        return new DataRootResolution(selectedPath, DataRootSource.FolderPicker);
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

    internal static bool TryValidate(string candidate, out string? normalized, out string? error)
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

    private static string Validate(string candidate, string code)
    {
        if (TryValidate(candidate, out var normalized, out var error))
            return normalized!;
        throw new DataRootResolutionException(code, error ?? "The Wiki data folder is invalid.");
    }
}

internal sealed class DataRootResolutionException : IOException
{
    public DataRootResolutionException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
