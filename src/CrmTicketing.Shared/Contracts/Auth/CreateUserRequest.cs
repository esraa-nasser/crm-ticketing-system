namespace CrmTicketing.Shared.Contracts.Auth;

/// <summary>
/// Body of <c>POST /api/auth/users</c>. Admin only — this is the only way an
/// account comes into existence, since there is no self-registration.
/// </summary>
/// <param name="Email">The new account's email address. Must be unique.</param>
/// <param name="Password">The initial password.</param>
/// <param name="Role">One of the declared role names.</param>
public sealed record CreateUserRequest(string Email, string Password, string Role);
