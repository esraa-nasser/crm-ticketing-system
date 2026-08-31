using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CrmTicketing.Api.Configuration;

/// <summary>
/// Bearer-token authentication for the API.
/// </summary>
public static class AuthenticationSetup
{
    /// <summary>
    /// The claim type carrying a role. Pinned explicitly here and used by the sign-in
    /// endpoint when it writes the token.
    /// </summary>
    /// <remarks>
    /// Role claims in a JWT do not map to <see cref="ClaimsIdentity.RoleClaimType"/>
    /// by default, so without pinning this, every <c>[Authorize(Roles = ...)]</c>
    /// rejects a user who genuinely holds the role. The symptom is a 403 for correct
    /// credentials, which reads as a policy bug rather than a claim-mapping one. The
    /// token writer and the token reader must agree, so both use these constants.
    /// </remarks>
    public const string RoleClaimType = ClaimTypes.Role;

    /// <summary>The claim type carrying the user's name. Pinned for the same reason.</summary>
    public const string NameClaimType = ClaimTypes.Name;

    /// <summary>
    /// Registers JWT bearer authentication from the <c>Jwt</c> configuration section.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        if (options.SigningKey.Length < JwtOptions.MinimumSigningKeyLength)
        {
            // Fail closed, like AddPersistence does for its connection string. A short
            // or absent key otherwise fails deep inside the JWT library with a far
            // worse message.
            throw new InvalidOperationException(
                $"Configuration '{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}' is missing or "
                + $"shorter than {JwtOptions.MinimumSigningKeyLength} characters. "
                + $"Set it with: dotnet user-secrets set \"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}\" "
                + "\"<32+ character value>\" --project src/CrmTicketing.Api");
        }

        services.AddSingleton(options);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt => jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),

                // The five-minute default would make an expiry test either slow or a lie.
                ClockSkew = TimeSpan.FromSeconds(30),

                RoleClaimType = RoleClaimType,
                NameClaimType = NameClaimType,
            });

        return services;
    }
}
