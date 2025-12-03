using System.Text.Json.Serialization;

namespace DynamicQuery.Models;

public class PaginatedResult<T>
{
    public IEnumerable<T>? Data { get; private set; }

    public PageMetadata<T>? PageMetadata { get; private set; }

    public PaginatedResult(
        IEnumerable<T> data,
        long totalItemCount,
        long currentPage,
        long pageSize
    )
    {
        Data = data;
        PageMetadata = new PageMetadata<T>(totalItemCount, currentPage, pageSize);
    }

    public PaginatedResult(
        IEnumerable<T> data,
        long totalItemCount,
        long pageSize,
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
    public long? CurrentPage { get; set; }

    public long PageSize { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalItems { get; set; }

    public long TotalPage { get; set; }

    public bool? HasNextPage { get; set; }

    public bool? HasPreviousPage { get; set; }

    public string? Before { get; set; }

    public string? After { get; set; }

    public PageMetadata(long totalItemCount, long currentPage = 1, long pageSize = 100)
    {
        CurrentPage = currentPage;
        PageSize = pageSize;
        TotalPage = (long)Math.Ceiling(totalItemCount / (double)pageSize);
        TotalItems = totalItemCount;

        HasNextPage = CurrentPage < TotalPage;
        HasPreviousPage = currentPage > 1;
    }

    public PageMetadata(
        long totalItemCount,
        long pageSize = 100,
        string? previousCursor = null,
        string? nextCursor = null
    )
    {
        PageSize = pageSize;
        TotalPage = (long)Math.Ceiling(totalItemCount / (double)pageSize);
        After = nextCursor;
        HasNextPage = nextCursor != null;
        Before = previousCursor;
        HasPreviousPage = previousCursor != null;
    }
}
