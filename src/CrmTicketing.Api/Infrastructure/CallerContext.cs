using System.Security.Claims;
using CrmTicketing.Api.Configuration;
using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Identity;

namespace CrmTicketing.Api.Infrastructure;

/// <summary>
/// Reads the acting user out of the authenticated principal.
/// </summary>
internal static class CallerContext
{
    /// <summary>
    /// The caller's user id.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The principal carries no usable id. Every route reading this is behind
    /// <c>[Authorize]</c>, so an absent id means the token was issued wrong rather
    /// than that the caller is anonymous — fail loudly rather than substitute
    /// <see cref="Guid.Empty"/>, which the domain would then reject with a confusing
    /// message about an actor id.
    /// </exception>
    public static Guid UserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(AuthenticationSetup.UserIdClaimType);

        return Guid.TryParse(value, out var userId) && userId != Guid.Empty
            ? userId
            : throw new InvalidOperationException(
                $"The authenticated principal carries no valid '{AuthenticationSetup.UserIdClaimType}' claim.");
    }

    /// <summary>
    /// Which tickets this caller may see. The single place a role becomes data
    /// visibility — everything downstream takes the <see cref="TicketAccess"/>.
    /// </summary>
    public static TicketAccess Access(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Staff see everything. A Customer is confined to tickets they raised, and
        // anyone holding no known role is treated as a Customer: the safer default
        // when a role is missing is less visibility, not more.
        return principal.IsInRole(RoleNames.Admin) || principal.IsInRole(RoleNames.Agent)
            ? TicketAccess.All()
            : TicketAccess.OwnedBy(principal.UserId());
    }

    /// <summary>Whether the caller is staff, and so exempt from requester-only rules.</summary>
    public static bool IsStaff(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.IsInRole(RoleNames.Admin) || principal.IsInRole(RoleNames.Agent);
    }

    /// <summary>
    /// Which comments this caller may see. The counterpart to <see cref="Access"/>:
    /// that one confines which tickets, this one confines which comments on them.
    /// </summary>
    /// <remarks>
    /// Built on <see cref="IsStaff"/> rather than on a fresh role check, so there stays
    /// exactly one declaration of what staff means. Anyone holding no known role sees
    /// public comments only - the same "less visibility, not more" default
    /// <see cref="Access"/> takes.
    /// </remarks>
    public static CommentVisibility CommentVisibility(this ClaimsPrincipal principal) =>
        principal.IsStaff()
            ? Domain.Tickets.CommentVisibility.All()
            : Domain.Tickets.CommentVisibility.PublicOnly();
}
