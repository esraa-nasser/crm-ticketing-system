using System.Net.Http.Json;
using CrmTicketing.Shared.Contracts;

namespace CrmTicketing.Client.Services;

/// <summary>
/// Typed client for the API's non-domain endpoints. Every API area gets its own
/// typed client in this folder; components never call <see cref="HttpClient"/>
/// directly (see docs/constitution.md).
/// </summary>
public sealed class SystemApiClient(HttpClient httpClient)
{
    public async Task<ApiInfoResponse?> GetInfoAsync(CancellationToken cancellationToken = default) =>
        await httpClient.GetFromJsonAsync<ApiInfoResponse>("api/system/info", cancellationToken);
}
