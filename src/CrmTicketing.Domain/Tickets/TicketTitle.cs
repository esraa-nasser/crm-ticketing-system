namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// A ticket title: trimmed, non-empty, and no longer than <see cref="MaxLength"/>.
/// </summary>
public sealed record TicketTitle
{
    public const int MaxLength = 200;

    private TicketTitle(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Trims <paramref name="value"/>, then validates it. The only way to obtain a title.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The value is null, empty, whitespace-only, or longer than <see cref="MaxLength"/> after trimming.
    /// </exception>
    public static TicketTitle Create(string? value)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Ticket title must not be empty.", nameof(value));
        }

        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Ticket title must be at most {MaxLength} characters.",
                nameof(value));
        }

        return new TicketTitle(trimmed);
    }

    public override string ToString() => Value;
}
