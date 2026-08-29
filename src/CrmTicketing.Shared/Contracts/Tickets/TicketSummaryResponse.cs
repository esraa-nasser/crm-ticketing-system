namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// A ticket as it appears in a list. Deliberately omits <c>Description</c>: a
/// page of 100 rows must not ship 100 full descriptions.
/// </summary>
/// <param name="Id">Ticket identifier.</param>
/// <param name="Title">Ticket title.</param>
/// <param name="Status">Status name.</param>
/// <param name="Priority">Priority name.</param>
/// <param name="Category">Category, or null.</param>
/// <param name="RequesterId">Who raised the ticket.</param>
/// <param name="AssigneeId">Who owns it, or null when unassigned.</param>
/// <param name="CreatedAt">When the ticket was opened.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record TicketSummaryResponse(
    Guid Id,
    string Title,
    string Status,
    string Priority,
    string? Category,
    Guid RequesterId,
    Guid? AssigneeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
