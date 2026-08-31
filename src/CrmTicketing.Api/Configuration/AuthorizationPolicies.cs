using CrmTicketing.Infrastructure.Identity;

namespace CrmTicketing.Api.Configuration;

/// <summary>
/// Authorisation policies for the ticket endpoints.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Admin or Agent. Excludes Customer.</summary>
    public const string StaffOnly = "StaffOnly";

    /// <summary>
    /// Registers the ticket policies. Role names come from
    /// <see cref="RoleNames"/>; no policy spells one as a literal.
    /// </summary>
    public static IServiceCollection AddTicketAuthorization(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddPolicy(StaffOnly, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Agent))
            .Services;
}
