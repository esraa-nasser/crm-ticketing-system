namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// Body of <c>POST /api/tickets/{id}/assignee</c>.
/// </summary>
/// <param name="AssigneeId">
/// Who to assign the ticket to. <c>null</c> unassigns it - there is no separate
/// delete route.
/// </param>
public sealed record AssignTicketRequest(Guid? AssigneeId);
