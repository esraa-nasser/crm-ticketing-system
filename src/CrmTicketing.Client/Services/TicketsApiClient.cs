using System.Net.Http.Json;
using System.Text.Json;
using CrmTicketing.Shared.Contracts.Tickets;

namespace CrmTicketing.Client.Services;

/// <summary>
/// Typed client for the ticket endpoints. Components never call
/// <see cref="HttpClient"/> directly (see docs/constitution.md).
/// </summary>
/// <remarks>
/// Deliberately does not use <c>GetFromJsonAsync</c>: that helper throws
/// <see cref="HttpRequestException"/> and discards the response body, which is where
/// the problem-details message lives.
/// </remarks>
public sealed class TicketsApiClient(HttpClient httpClient) : ITicketsApiClient
{
    /// <summary>
    /// The page size this client requests. What the server actually served is on
    /// <see cref="PagedResponse{T}.PageSize"/>, and only that may drive the UI.
    /// </summary>
    internal const int PageSize = 25;

    private const string GenericFailure = "The API could not complete the request.";

    public Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(
        string? status,
        string? priority,
        int page,
        CancellationToken cancellationToken)
    {
        // Absent filters are omitted entirely. Sending "status=" makes the API parse
        // the empty string, fail to match a declared name, and return 400.
        var query = new List<string>(4) { $"page={page}", $"pageSize={PageSize}" };

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            query.Add($"priority={Uri.EscapeDataString(priority)}");
        }

        return GetAsync<PagedResponse<TicketSummaryResponse>>(
            $"api/tickets?{string.Join('&', query)}",
            cancellationToken);
    }

    public Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken) =>
        GetAsync<TicketMetadataResponse>("api/tickets/metadata", cancellationToken);

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken cancellationToken)
    {
        // HttpRequestException (the API is unreachable) propagates; the page catches
        // it. An unreachable host and a rejected request are different conditions.
        var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ApiRequestException(
                await ReadFailureMessageAsync(response, cancellationToken).ConfigureAwait(false),
                (int)response.StatusCode);
        }

        return await response.Content
            .ReadFromJsonAsync<T>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ApiRequestException(
                "The API returned an empty response.",
                (int)response.StatusCode);
    }

    private static async Task<string> ReadFailureMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ApiProblem? problem;

        try
        {
            problem = await response.Content
                .ReadFromJsonAsync<ApiProblem>(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A proxy or dev-server can answer a 502 with HTML. A failed parse of the
            // error body must not mask the status code the caller needs.
            return GenericFailure;
        }

        if (problem?.Errors is { Count: > 0 } errors)
        {
            // Positionally, never by key: a bad filter in the query string keys this
            // "status", the same value in a request body keys it "Status". The field
            // name is not rendered, so the casing never reaches the user.
            foreach (var entry in errors)
            {
                if (entry.Value is { Length: > 0 } messages)
                {
                    return messages[0];
                }
            }
        }

        return string.IsNullOrWhiteSpace(problem?.Title) ? GenericFailure : problem.Title;
    }
}
