namespace CrmTicketing.Shared.Contracts.Auth;

/// <summary>
/// A successful sign-in. The token is a bearer token to send as
/// <c>Authorization: Bearer &lt;token&gt;</c>.
/// </summary>
/// <param name="AccessToken">The signed bearer token.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
/// <param name="Email">The signed-in account's email.</param>
/// <param name="Roles">The roles the account holds.</param>
public sealed record SignInResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string Email,
    IReadOnlyList<string> Roles);
