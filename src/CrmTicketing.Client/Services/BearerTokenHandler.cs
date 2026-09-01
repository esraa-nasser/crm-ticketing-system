using System.Net.Http.Headers;

namespace CrmTicketing.Client.Services;

/// <summary>
/// Attaches the stored bearer token to outgoing API requests.
/// </summary>
/// <remarks>
/// A handler rather than a per-call argument so no caller can forget it, and so the
/// typed clients stay unaware that authentication exists.
/// </remarks>
public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
{
    /// <summary>
    /// The store this handler reads from. Exposed so a composition test can assert it
    /// is the same instance a component receives — if the two ever diverge, the token
    /// is set in one place and read from another, and every call goes out anonymous.
    /// </summary>
    internal TokenStore Tokens => tokens;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (tokens.AccessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
