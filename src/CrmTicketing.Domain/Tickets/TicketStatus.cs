namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// The lifecycle states a ticket may occupy. Values are explicit so a later
/// reorder cannot silently change their meaning.
/// </summary>
public enum TicketStatus
{
    New = 0,
    Open = 1,
    Pending = 2,
    Resolved = 3,
    Closed = 4,
}
