namespace Nexora.Application.Common;

/// <summary>
/// Uniform list envelope. Offset paging fills <see cref="TotalCount"/>/<see cref="TotalPages"/>;
/// keyset (cursor) paging leaves them null and fills <see cref="NextCursor"/> instead.
/// <see cref="TotalCountCapped"/> is true when counting stopped at a limit: there are more rows than
/// <see cref="TotalCount"/> (shown as "10,000+").
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long? TotalCount,
    int? TotalPages,
    string? NextCursor = null,
    bool TotalCountCapped = false)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, long total, bool capped = false) =>
        new(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize), null, capped);
}

public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));

    /// <summary>Escapes LIKE wildcards so user input is matched literally.</summary>
    public static string ToContainsPattern(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
