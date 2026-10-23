namespace RonSijm.RepoGen.Abstractions;

/// <summary>Items and metadata for a one-based, counted page.</summary>
public sealed class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int Page { get; }
    public int PageSize { get; }
}

/// <summary>A request for a cursor page. A missing cursor means the first page.</summary>
public sealed class CursorPageRequest<TCursor>
{
    public CursorPageRequest(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        PageSize = pageSize;
    }

    public CursorPageRequest(TCursor cursor, int pageSize) : this(pageSize)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        Cursor = cursor;
        HasCursor = true;
    }

    public int PageSize { get; }
    public bool HasCursor { get; }
    public TCursor? Cursor { get; }
}

/// <summary>A page fetched without a separate total-count query.</summary>
public sealed class CursorPagedResult<T, TCursor>
{
    public CursorPagedResult(IReadOnlyList<T> items, bool hasMore, TCursor? nextCursor, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        Items = items;
        HasMore = hasMore;
        NextCursor = nextCursor;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }
    public bool HasMore { get; }
    public TCursor? NextCursor { get; }
    public int PageSize { get; }
}