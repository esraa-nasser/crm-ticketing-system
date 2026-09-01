using CrmTicketing.Domain.Common;

namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// One comment on a ticket. Its own aggregate, referencing a ticket by id.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an owned collection on <see cref="Ticket"/>. Comments grow
/// without bound, and an owned collection means every ticket read drags the whole
/// history - or means configuring EF not to, which is the same decision made less
/// visibly. Reading a ticket loads no comments and returns no comment count.
/// </para>
/// <para>
/// The rule about <em>when</em> a comment may be written stays inside the ticket:
/// see <see cref="Ticket.EnsureCanBeCommentedOn"/>. That is the compromise the split
/// requires.
/// </para>
/// <para>
/// Append-only. There is no edit and no delete: an edited comment raises what the
/// audit trail should show, and a deleted one raises whether the thread should say
/// something was removed. Both are product questions and neither has been asked.
/// </para>
/// </remarks>
public sealed class TicketComment : Entity
{
    public const int MaxBodyLength = 5000;

    private TicketComment(
        Guid id,
        Guid ticketId,
        Guid authorId,
        string body,
        bool isInternal,
        DateTimeOffset createdAt)
        : base(id)
    {
        TicketId = ticketId;
        AuthorId = authorId;
        Body = body;
        IsInternal = isInternal;
        CreatedAt = createdAt;
    }

    // EF Core materialises entities through this constructor. It bypasses every
    // invariant by design, which is safe only because EF is its sole caller;
    // nothing else may construct a TicketComment reflectively. The id handed to the
    // base is a placeholder EF overwrites with the value from the row; the base
    // rejects Guid.Empty, so it cannot simply be left unset.
    private TicketComment()
        : base(Guid.NewGuid()) =>
        Body = null!;

    /// <summary>The ticket this comment belongs to. An id, never a navigation property.</summary>
    public Guid TicketId { get; private set; }

    /// <summary>Who wrote it. The first field in this solution whose value is the whole point.</summary>
    public Guid AuthorId { get; private set; }

    public string Body { get; private set; }

    /// <summary>Staff-only when true. Set once, at construction; there is no mutator.</summary>
    public bool IsInternal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Writes a comment. Named for the business event rather than for construction,
    /// matching <see cref="Ticket.Open"/>.
    /// </summary>
    /// <remarks>
    /// Does not consult the ticket's status: <see cref="TicketComment"/> is a separate
    /// aggregate and cannot load one. The caller invokes
    /// <see cref="Ticket.EnsureCanBeCommentedOn"/> first, having already loaded the
    /// ticket to check the caller may see it at all.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The ticket id or author id is empty, or the body is empty or too long after trimming.
    /// </exception>
    public static TicketComment Write(
        Guid id,
        Guid ticketId,
        Guid authorId,
        string body,
        bool isInternal,
        DateTimeOffset at)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket id must not be empty.", nameof(ticketId));
        }

        if (authorId == Guid.Empty)
        {
            // A comment with no author is the one thing this type must never hold.
            throw new ArgumentException("Author id must not be empty.", nameof(authorId));
        }

        return new TicketComment(id, ticketId, authorId, NormaliseBody(body), isInternal, at);
    }

    private static string NormaliseBody(string body)
    {
        var trimmed = body?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Comment body must not be empty.", nameof(body));
        }

        if (trimmed.Length > MaxBodyLength)
        {
            throw new ArgumentException(
                $"Comment body must be at most {MaxBodyLength} characters.",
                nameof(body));
        }

        return trimmed;
    }
}
