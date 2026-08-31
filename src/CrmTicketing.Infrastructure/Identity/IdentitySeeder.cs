using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// Creates the roles declared in <see cref="RoleNames"/> if they are not already
/// present.
/// </summary>
/// <remarks>
/// Internal by design: the API reaches this through
/// <c>DependencyInjection.SeedIdentityRolesAsync</c>, so <see cref="RoleManager{TRole}"/>
/// and <see cref="ApplicationRole"/> stay invisible to <c>CrmTicketing.Api</c>.
/// </remarks>
internal static class IdentitySeeder
{
    /// <summary>
    /// Seeds every role in <see cref="RoleNames.All"/>. Idempotent by construction:
    /// each role is created only when it does not already exist, so re-running
    /// startup neither duplicates nor throws.
    /// </summary>
    /// <param name="services">
    /// An <em>already scoped</em> provider. <see cref="RoleManager{TRole}"/> is scoped
    /// and cannot be resolved from the root provider; the caller owns the scope.
    /// </param>
    /// <param name="cancellationToken">Cancels the seed.</param>
    internal static async Task SeedRolesAsync(
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
                    $"Could not seed the '{roleName}' role: "
                    + string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
