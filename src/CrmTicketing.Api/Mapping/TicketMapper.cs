using CrmTicketing.Domain.Tickets;
using CrmTicketing.Shared.Contracts.Tickets;

namespace CrmTicketing.Api.Mapping;

/// <summary>
/// Translates between the ticket aggregate and the wire contracts. Lives in the
/// API because <c>Shared</c> holds no behaviour and <c>Domain</c> knows nothing
/// about contracts (docs/constitution.md §II).
/// </summary>
internal static class TicketMapper
{
    public static TicketResponse ToResponse(Ticket ticket) => new(
        Id: ticket.Id,
        Title: ticket.Title.Value,
        Description: ticket.Description,
        Status: ticket.Status.ToString(),
        Priority: ticket.Priority.ToString(),
        Category: ticket.Category,
        RequesterId: ticket.RequesterId,
        AssigneeId: ticket.AssigneeId,
        CreatedAt: ticket.CreatedAt,
        UpdatedAt: ticket.UpdatedAt);

    public static TicketSummaryResponse ToSummary(Ticket ticket) => new(
        Id: ticket.Id,
        Title: ticket.Title.Value,
        Status: ticket.Status.ToString(),
        Priority: ticket.Priority.ToString(),
        Category: ticket.Category,
        RequesterId: ticket.RequesterId,
        AssigneeId: ticket.AssigneeId,
        CreatedAt: ticket.CreatedAt,
        UpdatedAt: ticket.UpdatedAt);

    public static bool TryParseStatus(string? value, out TicketStatus status) =>
        TryParseName(value, out status);

    public static bool TryParsePriority(string? value, out TicketPriority priority) =>
        TryParseName(value, out priority);

    /// <summary>
    /// Matches <paramref name="value"/> against the declared enum names, case
    /// insensitively.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Enum.TryParse</c>: that accepts numeric text, so "3"
    /// would silently mean the third member and "99" would produce an undeclared
    /// value. Statuses and priorities cross the wire as names, never as numbers.
    /// </remarks>
    private static bool TryParseName<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;

        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                parsed = Enum.Parse<TEnum>(name);
                return true;
            }
        }

        return false;
    }
}
