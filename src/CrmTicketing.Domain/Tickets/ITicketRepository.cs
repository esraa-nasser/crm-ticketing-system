namespace CrmTicketing.Domain.Tickets;

/// <summary>
/// Storage for the ticket aggregate, declared by the domain and implemented by
/// Infrastructure.
/// </summary>
/// <remarks>
/// Framework-free by design: no EF type, no <c>IQueryable</c>, nothing that would
/// give <c>CrmTicketing.Domain</c> a dependency. <see cref="SaveChangesAsync"/>
/// sits here rather than on a separate unit of work because there is one
/// aggregate and one caller (docs/constitution.md §VII). Split it out when a
/// transaction must span two aggregates.
/// </remarks>
public interface ITicketRepository
{
    /// <summary>
    /// The ticket, or null when it does not exist <em>or</em> the caller may not see
    /// it. The two are deliberately indistinguishable: telling a customer a ticket
    /// exists but is not theirs leaks the existence of other customers' tickets.
    /// </summary>
    Task<Ticket?> GetAsync(Guid id, TicketAccess access, CancellationToken cancellationToken);

    Task AddAsync(Ticket ticket, CancellationToken cancellationToken);

    Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken cancellationToken);

    Task<int> CountAsync(TicketQuery query, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
