namespace CrmTicketing.Client.Services;

/// <summary>
/// Holds the bearer token for the current browser session.
/// </summary>
/// <remarks>
/// In memory, deliberately not <c>localStorage</c>. Persisting a bearer token to
/// storage readable by any script on the origin is an XSS-amplification decision
/// that deserves its own story; holding it in memory only means a page refresh
/// returns the user to sign-in.
/// </remarks>
public sealed class TokenStore
{
    public string? AccessToken { get; private set; }

    public string? Email { get; private set; }

    public IReadOnlyList<string> Roles { get; private set; } = [];

    public bool IsSignedIn => !string.IsNullOrEmpty(AccessToken);

    public void Set(string accessToken, string email, IReadOnlyList<string> roles)
    {
        AccessToken = accessToken;
        Email = email;
        Roles = roles;
    }

    public void Clear()
    {
        AccessToken = null;
        Email = null;
        Roles = [];
    }
}
