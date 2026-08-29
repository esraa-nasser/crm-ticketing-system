namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// One page of results plus the totals a caller needs to render paging.
/// </summary>
/// <typeparam name="T">Item type carried by the page.</typeparam>
/// <param name="Items">The rows on this page. Empty when the page is past the end.</param>
/// <param name="Page">The one-based page actually served, after clamping.</param>
/// <param name="PageSize">The page size actually served, after clamping.</param>
/// <param name="TotalCount">Rows matching the filter, ignoring paging.</param>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
