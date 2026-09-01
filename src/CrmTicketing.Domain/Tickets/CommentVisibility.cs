namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Which comments a caller may see.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="TicketAccess"/>: that one confines which tickets,
/// this one confines which comments on them. Deliberately not an enum of role names
/// and deliberately not a bare <see cref="bool"/> parameter - the repository must not
/// know what a role is, and a <c>bool includeInternal</c> argument is silently
/// invertible at a call site in a way two named factories are not. Roles map to a
/// <see cref="CommentVisibility"/> at the API boundary, which is the single place that
/// translation happens.
/// </remarks>
public sealed record CommentVisibility
{
    private CommentVisibility(bool includesInternal) => IncludesInternal = includesInternal;

    /// <summary>Whether staff-only comments are visible.</summary>
    public bool IncludesInternal { get; }

    /// <summary>Internal and public alike. Staff.</summary>
    public static CommentVisibility All() => new(true);

    /// <summary>Public comments only. Everyone else.</summary>
    public static CommentVisibility PublicOnly() => new(false);
}
