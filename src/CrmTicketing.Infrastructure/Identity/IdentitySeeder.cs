using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// Seeds the roles declared in <see cref="RoleNames"/>, and the first Admin account
/// when one is configured.
/// </summary>
/// <remarks>
/// Internal by design: the API reaches this through
/// <c>DependencyInjection.SeedIdentityAsync</c>, so <see cref="RoleManager{TRole}"/>,
/// <see cref="UserManager{TUser}"/>, and <see cref="ApplicationRole"/> stay invisible
/// to <c>CrmTicketing.Api</c>.
/// </remarks>
internal static class IdentitySeeder
{
    private const string BootstrapAdminSection = "Identity:BootstrapAdmin";

    /// <summary>
    /// Seeds roles, then the bootstrap Admin. Idempotent throughout: re-running
    /// startup neither duplicates anything nor throws.
    /// </summary>
    /// <param name="services">
    /// An <em>already scoped</em> provider. <see cref="RoleManager{TRole}"/> and
    /// <see cref="UserManager{TUser}"/> are scoped and cannot be resolved from the
    /// root provider; the caller owns the scope.
    /// </param>
    /// <param name="cancellationToken">Cancels the seed.</param>
    internal static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await SeedRolesAsync(services, cancellationToken).ConfigureAwait(false);
        await SeedBootstrapAdminAsync(services, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SeedRolesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var roleName in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await roleManager.RoleExistsAsync(roleName).ConfigureAwait(false))
            {
                continue;
            }

            var result = await roleManager
                .CreateAsync(new ApplicationRole { Name = roleName })
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                // Fail loudly at startup: a missing role means every policy naming it
                // rejects every caller, which is far harder to diagnose later.
                throw new InvalidOperationException(
                    $"Could not seed the '{roleName}' role: " + IdentityErrors.Describe(result));
            }
        }
    }

    /// <summary>
    /// Creates the first Admin from configuration, so a freshly migrated database is
    /// reachable. Without it nobody can sign in and nobody can create an account,
    /// because <c>POST /api/auth/users</c> itself requires an Admin.
    /// </summary>
    private static async Task SeedBootstrapAdminAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuration = services.GetRequiredService<IConfiguration>();
        var email = configuration[$"{BootstrapAdminSection}:Email"];
        var password = configuration[$"{BootstrapAdminSection}:Password"];

        // Absent configuration is not an error. A deployment that manages accounts
        // another way must not be forced to carry a bootstrap account.
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        // Idempotent by existence check, like the roles. Never resets the password of
        // an account that already exists.
        if (await userManager.FindByEmailAsync(email).ConfigureAwait(false) is not null)
        {
            return;
        }

        var admin = new ApplicationUser { UserName = email, Email = email };
        var created = await userManager.CreateAsync(admin, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            // Present-but-weak is an error. Skipping silently would leave an operator
            // believing an Admin account exists when none does.
            throw new InvalidOperationException(
                $"Could not create the bootstrap Admin from '{BootstrapAdminSection}': " + IdentityErrors.Describe(created));
        }

        var assigned = await userManager.AddToRoleAsync(admin, RoleNames.Admin).ConfigureAwait(false);

        if (!assigned.Succeeded)
        {
            // An account with no role can authenticate and then be refused everywhere.
            await userManager.DeleteAsync(admin).ConfigureAwait(false);

            throw new InvalidOperationException(
                $"Could not place the bootstrap Admin in the '{RoleNames.Admin}' role: " + IdentityErrors.Describe(assigned));
        }
    }
}
