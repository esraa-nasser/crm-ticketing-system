using System.Reflection;
using CrmTicketing.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace CrmTicketing.Api.Controllers;

/// <summary>
/// Non-domain endpoints used to verify that the client, the API, and the
/// deployment environment are wired together correctly.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class SystemController(IHostEnvironment environment, TimeProvider timeProvider)
    : ControllerBase
{
    private static readonly string AssemblyVersion =
        typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Reports the running API build, environment, and server clock.</summary>
    [HttpGet("info")]
    [ProducesResponseType<ApiInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<ApiInfoResponse> GetInfo() => Ok(new ApiInfoResponse(
        Name: "CrmTicketing.Api",
        Version: AssemblyVersion,
        Environment: environment.EnvironmentName,
        ServerTimeUtc: timeProvider.GetUtcNow()));
}
