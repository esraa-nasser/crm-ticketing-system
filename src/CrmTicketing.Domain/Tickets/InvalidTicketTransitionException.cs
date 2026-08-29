namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Thrown when a caller attempts a status move that <see cref="TicketStatusTransitions"/>
/// does not allow. <see cref="From"/> and <see cref="To"/> let a caller map this to an
/// HTTP status without parsing the message.
/// </summary>
public sealed class InvalidTicketTransitionException : InvalidOperationException
{
    public InvalidTicketTransitionException()
    {
    }

    public InvalidTicketTransitionException(string message)
        : base(message)
    {
    }

    public InvalidTicketTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InvalidTicketTransitionException(TicketStatus from, TicketStatus to)
        : base($"A ticket cannot move from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    public TicketStatus From { get; }

    public TicketStatus To { get; }
}
