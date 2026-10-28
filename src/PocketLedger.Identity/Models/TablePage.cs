namespace PocketLedger.Models.ViewModels;

public sealed record TablePage<T>(IReadOnlyList<T> Items, int Page, int TotalPages, int TotalCount, string Sort, bool Descending);

// In-memory sorting and paging for admin and account tables whose columns cannot be queried in the database (encrypted or remote data).
public static class TablePaging
{
    public static TablePage<T> Apply<T>(IEnumerable<T> items, string? sort, bool descending, IReadOnlyDictionary<string, Func<T, IComparable?>> sortKeys, string defaultSort, int page, int pageSize)
    {
        var all = items.ToList();
        var sortName = sort is not null && sortKeys.ContainsKey(sort) ? sort : defaultSort;
        var key = sortKeys[sortName];
        // Missing values always go last, whatever the direction.
        var sorted = descending
            ? all.OrderBy(item => key(item) is null).ThenByDescending(key, Comparer<IComparable?>.Default).ToList()
            : all.OrderBy(item => key(item) is null).ThenBy(key, Comparer<IComparable?>.Default).ToList();
        var totalPages = Math.Max(1, (int)Math.Ceiling(sorted.Count / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        return new TablePage<T>(sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList(), page, totalPages, sorted.Count, sortName, descending);
    }
}

public sealed record SortableHeaderViewModel(string Label, string Url, bool IsActive, bool Descending);
