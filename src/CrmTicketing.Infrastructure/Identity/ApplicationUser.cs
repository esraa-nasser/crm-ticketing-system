using Microsoft.AspNetCore.Identity;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// The application's user. Keyed by <see cref="Guid"/> to match
/// <c>Ticket.RequesterId</c> and <c>Ticket.AssigneeId</c>, which this story is what
/// finally gives something to point at.
/// </summary>
/// <remarks>
/// Identity types are framework types and stay inside Infrastructure. Nothing in
/// <c>Domain</c>, <c>Shared</c>, or <c>Client</c> may reference this class
/// (docs/constitution.md §II), and <c>Ticket</c> gains no navigation property to it.
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>;
