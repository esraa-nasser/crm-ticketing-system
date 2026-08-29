using CrmTicketing.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITicketRepository"/>. Internal because
/// nothing outside Infrastructure may name a persistence type.
/// </summary>
internal sealed class TicketRepository(CrmDbContext context) : ITicketRepository
{
    public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Ticket>().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task AddAsync(Ticket ticket, CancellationToken cancellationToken) =>
        await context.Set<Ticket>().AddAsync(ticket, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Ticket>> ListAsync(
        TicketQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // ThenBy(t => t.Id) is not decoration: without a tiebreaker two tickets
        // sharing a CreatedAt can repeat or vanish across pages.
        return await Filter(context.Set<Ticket>().AsNoTracking(), query)
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> CountAsync(TicketQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Filter(context.Set<Ticket>().AsNoTracking(), query).CountAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    // Shared by ListAsync and CountAsync so a filter can never apply to the page
    // but not the total.
    private static IQueryable<Ticket> Filter(IQueryable<Ticket> tickets, TicketQuery query)
    {
        if (query.Status is { } status)
        {
            tickets = tickets.Where(t => t.Status == status);
        }

        if (query.Priority is { } priority)
        {
            tickets = tickets.Where(t => t.Priority == priority);
        }

        if (query.AssigneeId is { } assigneeId)
        {
            tickets = tickets.Where(t => t.AssigneeId == assigneeId);
        }

        if (query.RequesterId is { } requesterId)
        {
            tickets = tickets.Where(t => t.RequesterId == requesterId);
        }

        return tickets;
    }
}
