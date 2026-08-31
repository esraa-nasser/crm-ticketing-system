namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Answers whether a user id refers to a real account.
/// </summary>
/// <remarks>
/// Exists because <c>ticket.requester_id</c> now carries a foreign key: an id that
/// refers to nobody makes the database reject the insert, which surfaces as a 500.
/// The API checks first and answers 400 instead. Deliberately not a <c>Ticket</c>
/// invariant — the existence of a user is not something the aggregate can know, and
/// <c>Ticket</c> still holds an opaque <see cref="Guid"/>. Framework-free like
/// <see cref="ITicketRepository"/>, so no Identity type reaches the domain.
/// </remarks>
public interface IUserDirectory
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken);
}
