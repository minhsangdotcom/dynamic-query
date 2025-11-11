namespace DynamicQuery.Models;

public class CursorPaginationRequest(
    string? beforeCursor,
    string? afterCursor,
    int size,
    string? sort,
    string uniqueSort
)
{
    public string? Before { get; private set; } = beforeCursor;

    public string? After { get; private set; } = afterCursor;

    public int Size { get; private set; } = size;

    public string? Sort { get; private set; } = sort;

    public string UniqueSort { get; private set; } =
        !string.IsNullOrWhiteSpace(uniqueSort)
            ? uniqueSort
            : throw new ArgumentException(
                "UniqueSort cannot be null, empty, or whitespace.",
                nameof(uniqueSort)
            );
}
