using System.Security.Claims;
using System.Text;
using CrmTicketing.Api.Configuration;
using CrmTicketing.Infrastructure.Identity;
using CrmTicketing.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CrmTicketing.Api.Controllers;

/// <summary>
/// Sign-in and account creation.
/// </summary>
/// <remarks>
/// There is no self-registration: <c>AddIdentityCore</c> plus this hand-written
/// controller means no register endpoint exists to disable. Accounts are created by
/// an Admin through <see cref="CreateUser"/>.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    JwtOptions jwtOptions,
    TimeProvider timeProvider)
    : ControllerBase
{
    // Verifying a real hash is deliberately slow. An unknown email is verified
    // against this instead, so the response time does not disclose which accounts
    // exist any more than the response body does.
    private static readonly string DecoyHash =
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "decoy-password");

    private const string SignInFailed = "The email or password is incorrect.";

    /// <summary>Exchanges credentials for a bearer token.</summary>
    [HttpPost("signin")]
    [AllowAnonymous]
    [ProducesResponseType<SignInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SignInResponse>> SignIn(
        SignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(request.Email).ConfigureAwait(false);

        if (user is null)
        {
            // Burn comparable time, then fail identically to a wrong password.
            userManager.PasswordHasher.VerifyHashedPassword(
                new ApplicationUser(),
                DecoyHash,
                request.Password);

            return SignInProblem();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password).ConfigureAwait(false))
        {
            return SignInProblem();
        }

        var roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(jwtOptions.LifetimeMinutes);

        return Ok(new SignInResponse(
            AccessToken: CreateToken(user, roles, expiresAt),
            ExpiresAt: expiresAt,
            Email: user.Email ?? string.Empty,
            UserId: user.Id,
            Roles: [.. roles],
            // From RoleNames, never a literal, and the same two roles CallerContext
            // treats as staff. A display hint for the client; the API still enforces.
            IsStaff: roles.Contains(RoleNames.Admin) || roles.Contains(RoleNames.Agent)));
    }

    /// <summary>Creates an account in a role. The only way an account comes to exist.</summary>
    [HttpPost("users")]
    [Authorize(Roles = RoleNames.Admin)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateUser(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!RoleNames.All.Contains(request.Role, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(request.Role), $"'{request.Role}' is not a recognised role.");
            return ValidationProblem(ModelState);
        }

        var user = new ApplicationUser { UserName = request.Email, Email = request.Email };
        var created = await userManager.CreateAsync(user, request.Password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            return IdentityProblem(created);
        }

        var assigned = await userManager.AddToRoleAsync(user, request.Role).ConfigureAwait(false);

        if (!assigned.Succeeded)
        {
            // The account exists but holds no role, which would let it authenticate
            // and then be refused everywhere. Remove it rather than leave that behind.
            await userManager.DeleteAsync(user).ConfigureAwait(false);
            return IdentityProblem(assigned);
        }

        return StatusCode(StatusCodes.Status201Created);
    }

    private string CreateToken(ApplicationUser user, IEnumerable<string> roles, DateTimeOffset expiresAt)
    {
        // Claim types come from AuthenticationSetup so the writer and the reader
        // agree. A mismatch here makes every [Authorize(Roles = ...)] reject a user
        // who genuinely holds the role.
        var claims = new List<Claim>
        {
            new(AuthenticationSetup.UserIdClaimType, user.Id.ToString()),
            new(AuthenticationSetup.NameClaimType, user.Email ?? string.Empty),
        };

        claims.AddRange(roles.Select(role => new Claim(AuthenticationSetup.RoleClaimType, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwtOptions.Issuer,
            Audience = jwtOptions.Audience,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
    }

    // One body for both failure paths. Branching the message would disclose which
    // emails have accounts.
    private ObjectResult SignInProblem() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: SignInFailed);

    private ActionResult IdentityProblem(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }

        return ValidationProblem(ModelState);
    }
}
