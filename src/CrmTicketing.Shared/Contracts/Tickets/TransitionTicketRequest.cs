namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// Body of <c>POST /api/tickets/{id}/status</c>.
/// </summary>
/// <param name="Status">
/// Target status name, for example <c>Resolved</c>. Names only - a numeric value
/// is rejected with 400. The legal moves are published by
/// <c>GET /api/tickets/metadata</c>.
/// </param>
public sealed record TransitionTicketRequest(string Status);
