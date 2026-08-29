using System.Collections.Frozen;

namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// The single declaration of which status moves are legal. Nothing else in the
/// solution may encode this - not the API, not the client, not the database.
/// </summary>
public static class TicketStatusTransitions
{
    private static readonly FrozenDictionary<TicketStatus, FrozenSet<TicketStatus>> Allowed;

    private static readonly FrozenSet<TicketStatus> None = FrozenSet<TicketStatus>.Empty;

    static TicketStatusTransitions()
    {
        Allowed = new Dictionary<TicketStatus, FrozenSet<TicketStatus>>
        {
            [TicketStatus.New] = new[] { TicketStatus.Open, TicketStatus.Closed }.ToFrozenSet(),
            [TicketStatus.Open] = new[] { TicketStatus.Pending, TicketStatus.Resolved, TicketStatus.Closed }.ToFrozenSet(),
            [TicketStatus.Pending] = new[] { TicketStatus.Open, TicketStatus.Resolved, TicketStatus.Closed }.ToFrozenSet(),
            [TicketStatus.Resolved] = new[] { TicketStatus.Open, TicketStatus.Closed }.ToFrozenSet(),
            [TicketStatus.Closed] = None,
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// Whether a ticket may move from <paramref name="from"/> to <paramref name="to"/>.
    /// A move to the same status is not allowed: it is a no-op that hides caller bugs.
    /// </summary>
    public static bool IsAllowed(TicketStatus from, TicketStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>
    /// The statuses reachable from <paramref name="from"/>. Empty for a terminal status.
    /// Exists so a caller can render only legal actions without duplicating the table.
    /// </summary>
    public static IReadOnlySet<TicketStatus> AllowedFrom(TicketStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : None;
}
