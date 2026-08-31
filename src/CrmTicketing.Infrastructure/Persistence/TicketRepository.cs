using CrmTicketing.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITicketRepository"/>. Internal because
/// nothing outside Infrastructure may name a persistence type.
/// </summary>
internal sealed class TicketRepository(CrmDbContext context) : ITicketRepository
{
    public Task<Ticket?> GetAsync(Guid id, TicketAccess access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);

        // The access predicate is applied here rather than left to the controller, so
        // a ticket the caller may not see comes back as null and the existing 404 path
        // handles it. A caller-side check would be one refactor away from being dropped.
        // Routed through the same ApplyAccess as Filter, so read-one and read-many
        // cannot disagree about who may see what.
        return ApplyAccess(context.Set<Ticket>().Where(t => t.Id == id), access)
            .FirstOrDefaultAsync(cancellationToken);
    }

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
    /// <summary>
    /// Confines the query to what <paramref name="access"/> permits. The one place
    /// the row-level rule is expressed; <see cref="GetAsync"/> and
    /// <see cref="Filter"/> both route through it.
    /// </summary>
    /// <remarks>Internal so it can be tested over an in-memory queryable, with no database.</remarks>
    internal static IQueryable<Ticket> ApplyAccess(IQueryable<Ticket> tickets, TicketAccess access) =>
        access.RestrictedToRequesterId is { } restrictedTo
            ? tickets.Where(t => t.RequesterId == restrictedTo)
            : tickets;

    /// <summary>
    /// Applies access and the caller's filters. Shared by <see cref="ListAsync"/> and
    /// <see cref="CountAsync"/> so a rule cannot constrain the page but not the count.
    /// </summary>
    /// <remarks>Internal so it can be tested over an in-memory queryable, with no database.</remarks>
    internal static IQueryable<Ticket> Filter(IQueryable<Ticket> tickets, TicketQuery query)
    {
        // Access first. A total that counts rows the caller cannot see discloses how
        // many tickets exist.
        tickets = ApplyAccess(tickets, query.Access);

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
