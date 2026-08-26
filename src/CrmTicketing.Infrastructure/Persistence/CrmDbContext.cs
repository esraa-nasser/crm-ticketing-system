using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the CRM ticketing database.
/// </summary>
/// <remarks>
/// The model is intentionally empty. Aggregates register themselves through
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes in Configurations/,
/// discovered by ApplyConfigurationsFromAssembly. No DbSet is declared here.
/// </remarks>
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
        modelBuilder.ApplySnakeCaseNames();
    }
}
