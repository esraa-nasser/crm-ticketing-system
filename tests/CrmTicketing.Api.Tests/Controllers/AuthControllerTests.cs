using System.Reflection;
using System.Text.Json;
using CrmTicketing.Api.Configuration;
using CrmTicketing.Api.Controllers;
using CrmTicketing.Infrastructure.Identity;
using CrmTicketing.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CrmTicketing.Api.Tests.Controllers;

public sealed class AuthControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private const string KnownEmail = "agent@example.com";
    private const string CorrectPassword = "correct-horse-battery-staple";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // UserManager's methods are virtual, so the store is never reached. It exists
    // only to satisfy the base constructor.
    private sealed class UnusedUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // No real password hashing: Identity's hasher is deliberately slow, and a suite
    // that pays that cost per test stops being run.
    private sealed class StubUserManager(ApplicationUser? user, bool passwordMatches)
        : UserManager<ApplicationUser>(
            new UnusedUserStore(),
            null!,
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            null!,
            new IdentityErrorDescriber(),
            null!,
            null!)
    {
        public override Task<ApplicationUser?> FindByEmailAsync(string email) => Task.FromResult(user);

        public override Task<bool> CheckPasswordAsync(ApplicationUser candidate, string password) =>
            Task.FromResult(passwordMatches);

        public override Task<IList<string>> GetRolesAsync(ApplicationUser candidate) =>
            Task.FromResult<IList<string>>([RoleNames.Agent]);
    }

    private sealed class TestProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext,
            ModelStateDictionary modelStateDictionary,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode ?? StatusCodes.Status400BadRequest, Title = title };
    }

    private static JwtOptions Options() => new()
    {
        Issuer = "CrmTicketing",
        Audience = "CrmTicketing.Client",
        SigningKey = "test-only-signing-key-0123456789abcdef",
        LifetimeMinutes = 60,
    };

    private static AuthController CreateController(ApplicationUser? user, bool passwordMatches) =>
        new(new StubUserManager(user, passwordMatches), Options(), new FixedTimeProvider(Now))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new TestProblemDetailsFactory(),
        };

    private static ApplicationUser KnownUser() => new()
    {
        Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
        Email = KnownEmail,
        UserName = KnownEmail,
    };

    [Fact]
    public async Task SignIn_WithAnUnknownEmailAndAWrongPassword_ReturnByteIdenticalBodies()
    {
        var unknownEmail = await CreateController(user: null, passwordMatches: false)
            .SignIn(new SignInRequest("nobody@example.com", CorrectPassword), CancellationToken.None);

        var wrongPassword = await CreateController(KnownUser(), passwordMatches: false)
            .SignIn(new SignInRequest(KnownEmail, "wrong"), CancellationToken.None);

        var a = Assert.IsAssignableFrom<ObjectResult>(unknownEmail.Result);
        var b = Assert.IsAssignableFrom<ObjectResult>(wrongPassword.Result);

        Assert.Equal(StatusCodes.Status401Unauthorized, a.StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, b.StatusCode);

        // Byte-identical, not merely both-401: any difference discloses which emails
        // have accounts.
        Assert.Equal(
            JsonSerializer.Serialize(a.Value),
            JsonSerializer.Serialize(b.Value));
    }

    [Fact]
    public async Task SignIn_WithCorrectCredentials_ReturnsATokenAndRoles()
    {
        var controller = CreateController(KnownUser(), passwordMatches: true);

        var result = await controller.SignIn(
            new SignInRequest(KnownEmail, CorrectPassword),
            CancellationToken.None);

        var response = Assert.IsType<SignInResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.Equal(KnownEmail, response.Email);
        Assert.Equal(RoleNames.Agent, Assert.Single(response.Roles));
        Assert.Equal(Now.AddMinutes(60), response.ExpiresAt);
    }

    [Fact]
    public void NoRegisterEndpointExists()
    {
        // No self-registration: AddIdentityCore plus a hand-written controller means
        // there is no register route to disable. A request to it is a 404 because
        // nothing is routed there.
        var routed = typeof(AuthController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template)
            .ToList();

        Assert.DoesNotContain(routed, t => t is not null && t.Contains("register", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("signin", routed);
        Assert.Contains("users", routed);
    }

    [Fact]
    public void CreateUser_RequiresTheAdminRole()
    {
        var createUser = typeof(AuthController).GetMethod(nameof(AuthController.CreateUser));
        Assert.NotNull(createUser);

        var roles = createUser
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Roles)
            .ToList();

        Assert.Contains(roles, r => r == RoleNames.Admin);
    }

    [Fact]
    public void OnlySignInIsAnonymous()
    {
        var type = typeof(AuthController);
        Assert.NotEmpty(type.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        var anonymous = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(nameof(AuthController.SignIn), Assert.Single(anonymous));
    }
}
