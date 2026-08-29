namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// Body of <c>POST /api/tickets</c>.
/// </summary>
/// <param name="Title">Short summary of the problem. Trimmed, 1-200 characters.</param>
/// <param name="Description">Full description. Trimmed, 1-10000 characters.</param>
/// <param name="RequesterId">Who raised the ticket. Must not be empty.</param>
/// <param name="Priority">Priority name, for example <c>High</c>. Null means <c>Normal</c>.</param>
/// <param name="Category">Optional free-text category, at most 100 characters.</param>
public sealed record CreateTicketRequest(
    string Title,
    string Description,
    Guid RequesterId,
    string? Priority,
    string? Category);
