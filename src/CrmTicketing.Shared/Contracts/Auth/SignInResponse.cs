namespace CrmTicketing.Shared.Contracts.Auth;

/// <summary>
/// A successful sign-in. The token is a bearer token to send as
/// <c>Authorization: Bearer &lt;token&gt;</c>.
/// </summary>
/// <param name="AccessToken">The signed bearer token.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
/// <param name="Email">The signed-in account's email.</param>
/// <param name="UserId">
/// The signed-in account's id. Carried explicitly so a client never has to read it
/// out of the token: the claim type is pinned in server configuration, and a client
/// decoding it would break silently if that ever changed.
/// </param>
/// <param name="Roles">The roles the account holds.</param>
/// <param name="IsStaff">
/// Whether the account may act as staff: the server's own answer, not a role name the
/// client is left to interpret. Carried explicitly because the grouping - which role
/// names count as staff - is a policy, and the single declaration of it lives in the
/// API. A client cannot reach that declaration without an edge the layer graph forbids,
/// and a second copy would drift silently the day a fourth role appears.
///
/// A display hint, never an authorisation decision. The API refuses what a caller may
/// not do regardless of what this said, which matters because a token outlives a role
/// change.
/// </param>
public sealed record SignInResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string Email,
    Guid UserId,
    IReadOnlyList<string> Roles,
    bool IsStaff);
