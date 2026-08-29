namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// How urgently a ticket needs attention. Values are explicit so a later
/// reorder cannot silently change their meaning.
/// </summary>
public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}
