namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// The workflow vocabulary, published so a client never hardcodes it. Returned by
/// <c>GET /api/tickets/metadata</c>.
/// </summary>
/// <param name="Statuses">Every status name, in declaration order.</param>
/// <param name="Priorities">Every priority name, in declaration order.</param>
/// <param name="Transitions">
/// Status name to the names it may legally move to. A terminal status maps to an
/// empty list.
/// </param>
public sealed record TicketMetadataResponse(
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> Priorities,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Transitions);
