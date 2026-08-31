using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Identity;
using CrmTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Infrastructure;

/// <summary>
/// Composition seam for this project. The API registers persistence through this
/// class and never names a persistence type itself.
/// </summary>
public static class DependencyInjection
{
    private const string ConnectionStringName = "CrmDatabase";

    /// <summary>
    /// Registers the persistence layer. The caller supplies configuration and
    /// learns nothing about EF Core, Npgsql, or CrmDbContext.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail closed: a default would let a developer run against the wrong database.
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not configured. "
                + $"Set it with: dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" "
                + "\"Host=localhost;Database=crm_ticketing;Username=...;Password=...\" "
                + "--project src/CrmTicketing.Api");
        }

        services.AddDbContext<CrmDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITicketRepository, TicketRepository>();

        // AddIdentityCore, not AddIdentity: the latter wires cookie authentication,
        // which this API does not use and which would add a second scheme nobody
        // asked for.
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<CrmDbContext>();

        services.AddScoped<IUserDirectory, UserDirectory>();

        return services;
    }

    /// <summary>
    /// Seeds the application roles and, when one is configured, the first Admin
    /// account. Idempotent: safe to call on every startup.
    /// </summary>
    /// <remarks>
    /// A sibling to <see cref="AddPersistence"/> so the API composition root keeps
    /// naming exactly one Infrastructure extension and no Identity type. Opens its own
    /// scope because <c>RoleManager</c> and <c>UserManager</c> are scoped and the root
    /// provider cannot resolve them.
    /// </remarks>
    public static async Task SeedIdentityAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();

        await IdentitySeeder
            .SeedAsync(scope.ServiceProvider, cancellationToken)
            .ConfigureAwait(false);
    }
}
