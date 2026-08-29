namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// A single ticket in full. Returned by every route that acts on one ticket.
/// </summary>
/// <param name="Id">Ticket identifier.</param>
/// <param name="Title">Ticket title.</param>
/// <param name="Description">Full description.</param>
/// <param name="Status">Status name, for example <c>Open</c>.</param>
/// <param name="Priority">Priority name, for example <c>Normal</c>.</param>
/// <param name="Category">Category, or null.</param>
/// <param name="RequesterId">Who raised the ticket.</param>
/// <param name="AssigneeId">Who owns it, or null when unassigned.</param>
/// <param name="CreatedAt">When the ticket was opened.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record TicketResponse(
    Guid Id,
    string Title,
    string Description,
    string Status,
    string Priority,
    string? Category,
    Guid RequesterId,
    Guid? AssigneeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
