namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Thrown when an operation is attempted on a closed ticket that a closed ticket
/// does not permit. Distinct from <see cref="InvalidTicketTransitionException"/>:
/// no status change was attempted, so a caller mapping this to HTTP reports the
/// operation rather than a from/to pair.
/// </summary>
public sealed class TicketClosedException : InvalidOperationException
{
    public TicketClosedException()
    {
    }

    public TicketClosedException(string message)
        : base(message)
    {
    }

    public TicketClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TicketClosedException(TicketStatus status, string operation)
        : base($"A ticket with status {status} cannot be {operation}.")
    {
        Operation = operation;
    }

    /// <summary>The refused operation, for example <c>assigned</c>.</summary>
    public string Operation { get; } = string.Empty;
}
