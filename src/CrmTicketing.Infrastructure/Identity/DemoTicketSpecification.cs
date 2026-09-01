using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Infrastructure.Identity;

/// <summary>Which seeded user raised a ticket.</summary>
internal enum DemoRequester
{
    Customer,
    Agent,
}

/// <summary>
/// One seeded ticket, declared rather than constructed.
/// </summary>
/// <remarks>
/// Separated from the code that executes it so the shape of the demo set is a pure
/// value a test can assert on without a database, a scope, or Identity.
/// </remarks>
/// <param name="Title">Obviously synthetic. Demo data that reads as real gets mistaken for real.</param>
/// <param name="Description">Body text, equally synthetic.</param>
/// <param name="Category">Free-text category, or null to exercise the nullable column.</param>
/// <param name="Priority">Priority the ticket ends on.</param>
/// <param name="Requester">Which seeded user raised it.</param>
/// <param name="TargetStatus">Status the ticket ends on, reached only through legal moves.</param>
/// <param name="AssignToAgent">Whether the Agent ends up assigned. Never true for a closed ticket.</param>
/// <param name="AgeInDays">How long ago it was raised, as an offset from the current instant.</param>
internal sealed record DemoTicketSpecification(
    string Title,
    string Description,
    string? Category,
    TicketPriority Priority,
    DemoRequester Requester,
    TicketStatus TargetStatus,
    bool AssignToAgent,
    int AgeInDays);

/// <summary>
/// One seeded comment, declared rather than constructed.
/// </summary>
/// <remarks>
/// Separated from the code that executes it for the same reason as
/// <see cref="DemoTicketSpecification"/>: the shape of the demo thread is a pure value
/// a test can assert on without a database, a scope, or Identity.
/// </remarks>
/// <param name="TicketIndex">Index into <c>DemoDataSeeder.Specifications</c>.</param>
/// <param name="Author">Which seeded user wrote it.</param>
/// <param name="Body">Obviously synthetic, like every other seeded string.</param>
/// <param name="IsInternal">Whether it is staff-only. A customer never receives one.</param>
/// <param name="HoursAfterTicket">Offset from the ticket's creation instant.</param>
internal sealed record DemoCommentSpecification(
    int TicketIndex,
    DemoRequester Author,
    string Body,
    bool IsInternal,
    int HoursAfterTicket);
