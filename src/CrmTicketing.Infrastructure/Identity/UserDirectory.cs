using CrmTicketing.Domain.Tickets;
using Microsoft.AspNetCore.Identity;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// <see cref="IUserDirectory"/> over Identity's <see cref="UserManager{TUser}"/>.
/// Internal so the API depends on the domain interface and never names Identity.
/// </summary>
internal sealed class UserDirectory(UserManager<ApplicationUser> userManager) : IUserDirectory
{
    public async Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is not null;
    }
}
