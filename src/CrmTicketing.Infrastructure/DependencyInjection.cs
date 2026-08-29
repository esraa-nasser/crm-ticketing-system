using CrmTicketing.Domain.Tickets;
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

        return services;
    }
}
