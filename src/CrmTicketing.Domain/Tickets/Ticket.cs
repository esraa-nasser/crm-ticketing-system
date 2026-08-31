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

    /// <summary>Who opened the ticket. Never changes after <see cref="Open"/>.</summary>
    /// <remarks>
    /// A private setter rather than a get-only property: <see cref="Open"/> is a
    /// static factory assigning after the private constructor has run, which a
    /// get-only property does not allow. The property stays closed to callers, which
    /// is what the invariant actually requires.
    /// </remarks>
    public Guid CreatedBy { get; private set; }

    /// <summary>Who made the most recent change.</summary>
    public Guid UpdatedBy { get; private set; }

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
        Guid actorId,
        TicketPriority priority = TicketPriority.Normal,
        string? category = null)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (requesterId == Guid.Empty)
        {
            throw new ArgumentException("Requester id must not be empty.", nameof(requesterId));
        }

        RequireActor(actorId);

        var ticket = new Ticket(
            id,
            title,
            NormaliseDescription(description),
            requesterId,
            createdAt,
            priority,
            NormaliseCategory(category));

        ticket.CreatedBy = actorId;
        ticket.UpdatedBy = actorId;

        return ticket;
    }

    /// <summary>
    /// Moves the ticket to <paramref name="target"/> if the transition table allows it.
    /// </summary>
    /// <exception cref="InvalidTicketTransitionException">The move is not legal.</exception>
    public void TransitionTo(TicketStatus target, DateTimeOffset at, Guid actorId)
    {
        RequireActor(actorId);

        if (!TicketStatusTransitions.IsAllowed(Status, target))
        {
            throw new InvalidTicketTransitionException(Status, target);
        }

        Status = target;
        Touch(at, actorId);
    }

    /// <summary>
    /// Assigns the ticket to an agent. A closed ticket cannot be assigned.
    /// </summary>
    public void Assign(Guid assigneeId, DateTimeOffset at, Guid actorId)
    {
        if (assigneeId == Guid.Empty)
        {
            throw new ArgumentException("Assignee id must not be empty.", nameof(assigneeId));
        }

        RequireActor(actorId);

        if (Status == TicketStatus.Closed)
        {
            throw new TicketClosedException(Status, "assigned");
        }

        AssigneeId = assigneeId;
        Touch(at, actorId);
    }

    /// <summary>
    /// Removes the current assignee. Legal in any status other than closed.
    /// </summary>
    public void Unassign(DateTimeOffset at, Guid actorId)
    {
        RequireActor(actorId);

        if (Status == TicketStatus.Closed)
        {
            throw new TicketClosedException(Status, "unassigned");
        }

        AssigneeId = null;
        Touch(at, actorId);
    }

    public void ChangePriority(TicketPriority priority, DateTimeOffset at, Guid actorId)
    {
        RequireActor(actorId);

        Priority = priority;
        Touch(at, actorId);
    }

    /// <summary>
    /// Corrects the descriptive fields. Legal in any status - a closed ticket may
    /// still have a typo fixed.
    /// </summary>
    public void UpdateDetails(
        TicketTitle title,
        string description,
        string? category,
        DateTimeOffset at,
        Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(title);
        RequireActor(actorId);

        // Normalise into locals first: a bad description must not leave the ticket
        // holding a new title and the old body.
        var normalisedDescription = NormaliseDescription(description);
        var normalisedCategory = NormaliseCategory(category);

        Title = title;
        Description = normalisedDescription;
        Category = normalisedCategory;
        Touch(at, actorId);
    }

    // Who changed it and when move together; separating them is how an audit trail
    // drifts from the thing it audits.
    private void Touch(DateTimeOffset at, Guid actorId)
    {
        UpdatedAt = at;
        UpdatedBy = actorId;
    }

    private static void RequireActor(Guid actorId)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("Actor id must not be empty.", nameof(actorId));
        }
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
