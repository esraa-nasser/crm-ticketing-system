using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmTicketing.Shared.Contracts.Auth;

namespace CrmTicketing.Client.Services;

/// <summary>
/// Typed client for the sign-in endpoint.
/// </summary>
/// <remarks>
/// Registered without <see cref="BearerTokenHandler"/>: signing in is how the token
/// is obtained, so attaching one would be circular.
/// </remarks>
public sealed class AuthApiClient(HttpClient httpClient)
{
    /// <summary>
    /// Exchanges credentials for a token.
    /// </summary>
    /// <exception cref="ApiRequestException">
    /// The credentials were rejected, or the API answered with a problem.
    /// </exception>
    public async Task<SignInResponse> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        var response = await httpClient
            .PostAsJsonAsync("api/auth/signin", request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException(
                await ReadMessageAsync(response, cancellationToken).ConfigureAwait(false),
                (int)response.StatusCode);
        }

        return await response.Content
            .ReadFromJsonAsync<SignInResponse>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ApiRequestException(
                "The API returned an empty response.",
                (int)response.StatusCode);
    }

    private static async Task<string> ReadMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // The API deliberately returns one body for a bad password and an unknown
        // email; this surfaces it verbatim rather than inventing a friendlier
        // message that might distinguish them.
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<ApiProblem>(cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                return problem.Title;
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic message below.
        }

        return response.StatusCode == HttpStatusCode.Unauthorized
            ? "The email or password is incorrect."
            : "Sign-in could not be completed.";
    }
}
