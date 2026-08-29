namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Filters and paging for a ticket search. Clamping lives here rather than in a
/// caller so the rule is unit-testable without a web framework and cannot drift
/// between callers.
/// </summary>
public sealed record TicketQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;

    private TicketQuery(
        TicketStatus? status,
        TicketPriority? priority,
        Guid? assigneeId,
        Guid? requesterId,
        int page,
        int pageSize)
    {
        Status = status;
        Priority = priority;
        AssigneeId = assigneeId;
        RequesterId = requesterId;
        Page = page;
        PageSize = pageSize;
    }

    public TicketStatus? Status { get; }

    public TicketPriority? Priority { get; }

    public Guid? AssigneeId { get; }

    public Guid? RequesterId { get; }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>Rows to skip for the requested page. Never negative.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Builds a query, clamping paging rather than rejecting it: an oversized
    /// page is a caller being optimistic, not a caller being wrong.
    /// </summary>
    public static TicketQuery Create(
        TicketStatus? status = null,
        TicketPriority? priority = null,
        Guid? assigneeId = null,
        Guid? requesterId = null,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        var clampedPage = page < 1 ? 1 : page;

        var clampedPageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        return new TicketQuery(status, priority, assigneeId, requesterId, clampedPage, clampedPageSize);
    }
}
