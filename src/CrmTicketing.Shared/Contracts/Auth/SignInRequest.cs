namespace CrmTicketing.Shared.Contracts.Auth;

/// <summary>
/// Body of <c>POST /api/auth/signin</c>.
/// </summary>
/// <param name="Email">The account's email address.</param>
/// <param name="Password">The account's password.</param>
public sealed record SignInRequest(string Email, string Password);
