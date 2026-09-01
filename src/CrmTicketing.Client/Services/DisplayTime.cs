using System.Globalization;

namespace CrmTicketing.Client.Services;

/// <summary>
/// Formats an instant for display in the browser's own time zone.
/// </summary>
/// <remarks>
/// The API stores and returns <see cref="DateTimeOffset"/> in UTC, which is correct
/// on the wire and wrong on a screen: a user at UTC+03 reading a raw value sees a
/// ticket they just created as three hours old. Shared by the list and the detail
/// view so the two cannot drift into different formats.
/// </remarks>
internal static class DisplayTime
{
    private const string Format = "yyyy-MM-dd HH:mm";

    /// <summary>
    /// The instant in the viewer's local zone. Deliberately carries no "Z" or offset
    /// suffix — it is local, and a UTC marker would be a lie.
    /// </summary>
    internal static string Local(DateTimeOffset value) =>
        value.ToLocalTime().ToString(Format, CultureInfo.InvariantCulture);
}
