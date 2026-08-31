using Microsoft.AspNetCore.Identity;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// An application role, keyed by <see cref="Guid"/> to match
/// <see cref="ApplicationUser"/>. The three roles that exist are declared in
/// <see cref="RoleNames"/> and seeded by <see cref="IdentitySeeder"/>.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>;
