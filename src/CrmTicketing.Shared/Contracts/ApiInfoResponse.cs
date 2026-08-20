namespace CrmTicketing.Shared.Contracts;

/// <summary>
/// Returned by <c>GET /api/system/info</c>. Used by the client to confirm which
/// API build it is talking to and by smoke tests to assert the wiring works.
/// </summary>
/// <param name="Name">Logical service name.</param>
/// <param name="Version">Assembly informational version of the API.</param>
/// <param name="Environment">ASP.NET Core environment name.</param>
/// <param name="ServerTimeUtc">Server clock at the time of the request.</param>
public sealed record ApiInfoResponse(
    string Name,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc);
