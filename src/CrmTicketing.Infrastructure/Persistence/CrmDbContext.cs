using CrmTicketing.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the CRM ticketing database, and the Identity store.
/// </summary>
/// <remarks>
/// Aggregates register themselves through <see cref="IEntityTypeConfiguration{TEntity}"/>
/// classes in Configurations/, discovered by ApplyConfigurationsFromAssembly. No DbSet
/// is declared here. Identity's own model is built by the base class.
/// </remarks>
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    // The parameter is named to match IdentityDbContext's own declaration (CA1725).
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Order matters. base first: Identity registers its entities there, and the
        // snake_case pass below must run last so it rewrites those names too —
        // AspNetUsers becomes asp_net_users. Identity is deliberately not exempt.
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
        builder.ApplySnakeCaseNames();
    }
}
