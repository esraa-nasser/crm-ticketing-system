namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// One comment on a ticket.
/// </summary>
/// <param name="Id">The comment's own id.</param>
/// <param name="TicketId">The ticket it belongs to.</param>
/// <param name="AuthorId">
/// Who wrote it, as an opaque id. There is no author name because no endpoint lists
/// users; a client renders this the way it already renders a requester or an assignee.
/// </param>
/// <param name="Body">The comment text, trimmed.</param>
/// <param name="IsInternal">True when the comment is staff-only. A customer never receives one.</param>
/// <param name="CreatedAt">When it was written. UTC on the wire; a client renders it locally.</param>
public sealed record TicketCommentResponse(
    Guid Id,
    Guid TicketId,
    Guid AuthorId,
    string Body,
    bool IsInternal,
    DateTimeOffset CreatedAt);
