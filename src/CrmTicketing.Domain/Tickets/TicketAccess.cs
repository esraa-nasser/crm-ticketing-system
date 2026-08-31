namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Which tickets a caller may see.
/// </summary>
/// <remarks>
/// Deliberately not an enum of role names: the repository must not know what a role
/// is, only whether this caller is confined to their own rows. Roles map to a
/// <see cref="TicketAccess"/> at the API boundary, which is the single place that
/// translation happens.
/// </remarks>
public sealed record TicketAccess
{
    private TicketAccess(Guid? restrictedToRequesterId) =>
        RestrictedToRequesterId = restrictedToRequesterId;

    /// <summary>
    /// The requester whose tickets are the only visible ones, or null when every
    /// ticket is visible.
    /// </summary>
    public Guid? RestrictedToRequesterId { get; }

    /// <summary>Unrestricted visibility. Staff.</summary>
    // The cast disambiguates from the record's implicit copy constructor.
    public static TicketAccess All() => new((Guid?)null);

    /// <summary>Visibility confined to tickets raised by <paramref name="requesterId"/>.</summary>
    public static TicketAccess OwnedBy(Guid requesterId)
    {
        if (requesterId == Guid.Empty)
        {
            // An empty id would match no rows, which reads as "this customer has no
            // tickets" rather than as the bug it is.
            throw new ArgumentException("Requester id must not be empty.", nameof(requesterId));
        }

        return new TicketAccess(requesterId);
    }
}
