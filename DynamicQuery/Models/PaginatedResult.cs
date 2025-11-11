using System.Text.Json.Serialization;

namespace DynamicQuery.Models;

public class PaginatedResult<T>
{
    public IEnumerable<T>? Data { get; private set; }

    public PageMetadata<T>? PageMetadata { get; private set; }

    public PaginatedResult(IEnumerable<T> data, int totalItemCount, int currentPage, int pageSize)
    {
        Data = data;
        PageMetadata = new PageMetadata<T>(totalItemCount, currentPage, pageSize);
    }

    public PaginatedResult(
        IEnumerable<T> data,
        int totalItemCount,
        int pageSize,
        string? previousCursor = null,
        string? nextCursor = null
    )
    {
        Data = data;
        PageMetadata = new PageMetadata<T>(totalItemCount, pageSize, previousCursor, nextCursor);
    }
}

public class PageMetadata<T>
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentPage { get; set; }

    public int PageSize { get; set; }

    public int TotalPage { get; set; }

    public bool? HasNextPage { get; set; }

    public bool? HasPreviousPage { get; set; }

    public string? Before { get; set; }

    public string? After { get; set; }

    public PageMetadata(int totalItemCount, int currentPage = 1, int pageSize = 10)
    {
        CurrentPage = currentPage;
        PageSize = pageSize;
        TotalPage = (int)Math.Ceiling(totalItemCount / (double)pageSize);

        HasNextPage = CurrentPage < TotalPage;
        HasPreviousPage = currentPage > 1;
    }

    public PageMetadata(
        int totalItemCount,
        int pageSize = 10,
        string? previousCursor = null,
        string? nextCursor = null
    )
    {
        PageSize = pageSize;
        TotalPage = (int)Math.Ceiling(totalItemCount / (double)pageSize);
        After = nextCursor;
        HasNextPage = nextCursor != null;
        Before = previousCursor;
        HasPreviousPage = previousCursor != null;
    }
}
