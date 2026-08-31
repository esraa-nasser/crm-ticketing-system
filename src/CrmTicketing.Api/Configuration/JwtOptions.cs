namespace CrmTicketing.Api.Configuration;

/// <summary>
/// Bearer-token settings, bound from the <c>Jwt</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> has no default and never appears in a checked-in file
/// (docs/constitution.md §VI). Supply it through user secrets locally and through
/// the environment in CI.
/// </remarks>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>The minimum key length accepted, in characters.</summary>
    public const int MinimumSigningKeyLength = 32;

    public string Issuer { get; set; } = "CrmTicketing";

    public string Audience { get; set; } = "CrmTicketing.Client";

    public string SigningKey { get; set; } = string.Empty;

    public int LifetimeMinutes { get; set; } = 60;
}
