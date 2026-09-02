namespace Terraria_Wiki.Application;

public sealed record PageRequest
{
    public PageRequest(int offset, int limit, string? filterText = null)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "The offset cannot be negative.");
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "The limit must be between 1 and 200.");

        Offset = offset;
        Limit = limit;
        FilterText = filterText;
    }

    public int Offset { get; }
    public int Limit { get; }
    public string? FilterText { get; }
}

public sealed record PageSlice<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Offset,
    int Limit);
