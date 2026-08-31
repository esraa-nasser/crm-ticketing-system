using Microsoft.AspNetCore.Identity;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>
/// Renders an <see cref="IdentityResult"/>'s failures for an exception message.
/// </summary>
/// <remarks>
/// One implementation, shared by <see cref="IdentitySeeder"/> and
/// <see cref="DemoDataSeeder"/>. Deliberately not duplicated: this is the single
/// line that must never print a configured password, and two copies is two places
/// for that to stop being true (docs/constitution.md §VI).
/// </remarks>
internal static class IdentityErrors
{
    /// <summary>Identity's own error descriptions, joined. Never the credentials.</summary>
    internal static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
