namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Which comments to fetch, and how they are paged. Clamping lives here rather than
/// in a caller so the rule is unit-testable without a web framework and cannot drift
/// between callers.
/// </summary>
public sealed record TicketCommentQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;

    private TicketCommentQuery(
        Guid ticketId,
        CommentVisibility visibility,
        int page,
        int pageSize)
    {
        TicketId = ticketId;
        Visibility = visibility;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>The ticket whose thread this is.</summary>
    public Guid TicketId { get; }

    /// <summary>Which comments the caller may see. Applied by the repository.</summary>
    public CommentVisibility Visibility { get; }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>Rows to skip for the requested page. Never negative.</summary>
    public int Skip => (Page - 1) * PageSize;

    /// <summary>
    /// Builds a query, clamping paging rather than rejecting it: an oversized page is
    /// a caller being optimistic, not a caller being wrong.
    /// </summary>
    /// <param name="ticketId">Required. An empty id would match no rows, which reads as an empty thread.</param>
    /// <param name="visibility">
    /// Required and without a default. A caller that forgets it must fail to compile
    /// rather than silently receive every internal comment.
    /// </param>
    public static TicketCommentQuery Create(
        Guid ticketId,
        CommentVisibility visibility,
        int page = 1,
        int pageSize = DefaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(visibility);

        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id must not be empty.", nameof(ticketId));
        }

        var clampedPage = page < 1 ? 1 : page;

        var clampedPageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        return new TicketCommentQuery(ticketId, visibility, clampedPage, clampedPageSize);
    }
}
