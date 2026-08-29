namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// Body of <c>PATCH /api/tickets/{id}</c>.
/// </summary>
/// <param name="Title">Replacement title. Required - the aggregate has no empty title.</param>
/// <param name="Description">Replacement description. Required.</param>
/// <param name="Category">Replacement category. Null or blank clears it.</param>
/// <param name="Priority">Priority name. Null leaves the priority unchanged.</param>
public sealed record UpdateTicketRequest(
    string Title,
    string Description,
    string? Category,
    string? Priority);
