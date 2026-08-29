using CrmTicketing.Domain.Common;

namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// A support ticket. The aggregate enforces its own invariants: it cannot be
/// constructed invalid, and it refuses illegal status moves itself.
/// </summary>
/// <remarks>
/// Every mutator takes the current time as a parameter. The domain never reads a
/// clock - the caller owns time (docs/constitution.md §V).
/// </remarks>
public sealed class Ticket : Entity
{
    public const int MaxDescriptionLength = 10000;
    public const int MaxCategoryLength = 100;

    private Ticket(
        Guid id,
        TicketTitle title,
        string description,
        Guid requesterId,
        DateTimeOffset createdAt,
        TicketPriority priority,
        string? category)
        : base(id)
    {
        Title = title;
        Description = description;
        RequesterId = requesterId;
        Priority = priority;
        Category = category;
        Status = TicketStatus.New;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    // EF Core materialises entities through this constructor. It bypasses every
    // invariant by design, which is safe only because EF is its sole caller;
    // nothing else may construct a Ticket reflectively. The id handed to the base
    // is a placeholder EF overwrites with the value from the row; the base rejects
    // Guid.Empty, so it cannot simply be left unset.
    private Ticket()
        : base(Guid.NewGuid())
    {
        Title = null!;
        Description = null!;
    }

    public TicketTitle Title { get; private set; }

    public string Description { get; private set; }

    public TicketStatus Status { get; private set; }

    public TicketPriority Priority { get; private set; }

    public string? Category { get; private set; }

    public Guid RequesterId { get; }

    public Guid? AssigneeId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Opens a new ticket. Named for the business event rather than for construction.
    /// The status is always <see cref="TicketStatus.New"/>; the caller cannot choose it.
    /// </summary>
    public static Ticket Open(
        Guid id,
        TicketTitle title,
        string description,
        Guid requesterId,
        DateTimeOffset createdAt,
        TicketPriority priority = TicketPriority.Normal,
        string? category = null)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (requesterId == Guid.Empty)
        {
            throw new ArgumentException("Requester id must not be empty.", nameof(requesterId));
        }

        return new Ticket(
            id,
            title,
            NormaliseDescription(description),
            requesterId,
            createdAt,
            priority,
            NormaliseCategory(category));
    }

    /// <summary>
    /// Moves the ticket to <paramref name="target"/> if the transition table allows it.
    /// </summary>
    /// <exception cref="InvalidTicketTransitionException">The move is not legal.</exception>
    public void TransitionTo(TicketStatus target, DateTimeOffset at)
    {
        if (!TicketStatusTransitions.IsAllowed(Status, target))
        {
            throw new InvalidTicketTransitionException(Status, target);
        }

        Status = target;
        UpdatedAt = at;
    }

    /// <summary>
    /// Assigns the ticket to an agent. A closed ticket cannot be assigned.
    /// </summary>
    public void Assign(Guid assigneeId, DateTimeOffset at)
    {
        if (assigneeId == Guid.Empty)
        {
            throw new ArgumentException("Assignee id must not be empty.", nameof(assigneeId));
        }

        if (Status == TicketStatus.Closed)
        {
            throw new InvalidOperationException(
                $"A ticket with status {TicketStatus.Closed} cannot be assigned.");
        }

        AssigneeId = assigneeId;
        UpdatedAt = at;
    }

    /// <summary>
    /// Removes the current assignee. Legal in any status other than closed.
    /// </summary>
    public void Unassign(DateTimeOffset at)
    {
        if (Status == TicketStatus.Closed)
        {
            throw new InvalidOperationException(
                $"A ticket with status {TicketStatus.Closed} cannot be unassigned.");
        }

        AssigneeId = null;
        UpdatedAt = at;
    }

    public void ChangePriority(TicketPriority priority, DateTimeOffset at)
    {
        Priority = priority;
        UpdatedAt = at;
    }

    private static string NormaliseDescription(string description)
    {
        var trimmed = description?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Ticket description must not be empty.", nameof(description));
        }

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Ticket description must be at most {MaxDescriptionLength} characters.",
                nameof(description));
        }

        return trimmed;
    }

    private static string? NormaliseCategory(string? category)
    {
        var trimmed = category?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxCategoryLength)
        {
            throw new ArgumentException(
                $"Ticket category must be at most {MaxCategoryLength} characters.",
                nameof(category));
        }

        return trimmed;
    }
}
