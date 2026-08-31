namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// The single declaration of the roles that exist. Nothing else in the solution may
/// spell a role name as a literal - authorisation policies reference these constants.
/// </summary>
/// <remarks>
/// Roles are seeded, never database-editable: a role name appears in a policy in
/// code, so adding one is a code change rather than a data change. Same reasoning
/// that made TicketPriority a fixed enum in story 03.
/// </remarks>
public static class RoleNames
{
    /// <summary>Manages users; full ticket access.</summary>
    public const string Admin = "Admin";

    /// <summary>Works the queue: read all, create, update, transition, assign.</summary>
    public const string Agent = "Agent";

    /// <summary>Raises and follows their own tickets only.</summary>
    public const string Customer = "Customer";

    public static IReadOnlyList<string> All { get; } = [Admin, Agent, Customer];
}
