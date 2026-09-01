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

    /// <summary>
    /// The signed-in account's id, taken from the sign-in response. Never decoded
    /// from the token: the claim type is pinned in server configuration, and reading
    /// it here would break silently if that changed.
    /// </summary>
    /// <remarks><see cref="Guid.Empty"/> when nobody is signed in.</remarks>
    public Guid UserId { get; private set; }

    public IReadOnlyList<string> Roles { get; private set; } = [];

    /// <summary>
    /// Whether the signed-in account may act as staff, as reported by the sign-in
    /// response.
    /// </summary>
    /// <remarks>
    /// The server's own answer, not a grouping this project computes from
    /// <see cref="Roles"/>: which role names count as staff is a policy, and its single
    /// declaration lives in the API, which the layer graph puts out of this project's
    /// reach.
    ///
    /// A display hint and never an authorisation decision. A token outlives a role
    /// change, so this can be stale in both directions; the API refuses what the caller
    /// may not do regardless of what this says.
    /// </remarks>
    public bool IsStaff { get; private set; }

    public bool IsSignedIn => !string.IsNullOrEmpty(AccessToken);

    public void Set(
        string accessToken,
        string email,
        Guid userId,
        IReadOnlyList<string> roles,
        bool isStaff)
    {
        AccessToken = accessToken;
        Email = email;
        UserId = userId;
        Roles = roles;
        IsStaff = isStaff;
    }

    public void Clear()
    {
        AccessToken = null;
        Email = null;
        UserId = Guid.Empty;
        Roles = [];
        IsStaff = false;
    }
}
