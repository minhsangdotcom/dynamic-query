namespace DynamicQuery.Models;

public class CursorPaginationRequest
{
    public string? Before { get; private set; }
    public string? After { get; private set; }
    public int Size { get; private set; }
    public string Sort { get; private set; }
    public string UniqueSort { get; private set; }

    public CursorPaginationRequest(
        string? beforeCursor,
        string? afterCursor,
        int size,
        string? sort,
        string uniqueSort
    )
    {
        if (string.IsNullOrWhiteSpace(uniqueSort))
        {
            throw new ArgumentException(
                "UniqueSort cannot be null, empty, or whitespace.",
                nameof(uniqueSort)
            );
        }

        Before = beforeCursor;
        After = afterCursor;
        Size = size;
        UniqueSort = uniqueSort;

        Sort = string.IsNullOrWhiteSpace(sort) ? UniqueSort : sort;
    }
}
