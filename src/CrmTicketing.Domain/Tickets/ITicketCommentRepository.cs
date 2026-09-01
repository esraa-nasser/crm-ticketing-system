namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Storage for the ticket comment aggregate, declared by the domain and implemented
/// by Infrastructure.
/// </summary>
/// <remarks>
/// A separate interface rather than more methods on <see cref="ITicketRepository"/>.
/// Comments are their own aggregate with their own paging; hanging them off the ticket
/// repository would make the split true in the domain and false in the storage
/// contract.
///
/// There is no <c>GetAsync</c> for a single comment because nothing reads one - no
/// edit, no delete, no permalink. Framework-free by design, like
/// <see cref="ITicketRepository"/>: no EF type, no <c>IQueryable</c>.
/// </remarks>
public interface ITicketCommentRepository
{
    Task AddAsync(TicketComment comment, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketComment>> ListAsync(
        TicketCommentQuery query,
        CancellationToken cancellationToken);

    Task<int> CountAsync(TicketCommentQuery query, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
