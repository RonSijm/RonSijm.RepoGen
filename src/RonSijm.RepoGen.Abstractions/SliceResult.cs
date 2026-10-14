namespace RonSijm.RepoGen.Abstractions;

/// <summary>An offset page fetched without a total-count query. Page numbers are one-based.</summary>
public sealed class SliceResult<T>
{
    public SliceResult(IReadOnlyList<T> items, int page, int pageSize, bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        if (items.Count > pageSize)
        {
            throw new ArgumentException("A slice cannot contain more than PageSize items.", nameof(items));
        }

        Items = items;
        Page = page;
        PageSize = pageSize;
        HasMore = hasMore;
    }

    public IReadOnlyList<T> Items { get; }
    public int Page { get; }
    public int PageSize { get; }
    public bool HasMore { get; }

    /// <summary>The next zero-based offset, or null when this slice exhausts the query.</summary>
    public long? NextOffset => HasMore ? (long)Page * PageSize : null;
}