using CrmTicketing.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="ITicketCommentRepository"/>. Internal because
/// nothing outside Infrastructure may name a persistence type.
/// </summary>
internal sealed class TicketCommentRepository(CrmDbContext context) : ITicketCommentRepository
{
    public async Task AddAsync(TicketComment comment, CancellationToken cancellationToken) =>
        await context.Set<TicketComment>().AddAsync(comment, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<TicketComment>> ListAsync(
        TicketCommentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Newest first. ThenByDescending(c => c.Id) is not decoration: without a
        // tiebreaker two comments sharing a CreatedAt can repeat or vanish across
        // pages. Ids are version 7, so descending id is descending creation order and
        // the tiebreaker never contradicts the sort.
        return await Filter(context.Set<TicketComment>().AsNoTracking(), query)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> CountAsync(TicketCommentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Filter(context.Set<TicketComment>().AsNoTracking(), query).CountAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Confines the query to what <paramref name="visibility"/> permits. The one place
    /// the internal-comment rule is expressed.
    /// </summary>
    /// <remarks>Internal so it can be tested over an in-memory queryable, with no database.</remarks>
    internal static IQueryable<TicketComment> ApplyVisibility(
        IQueryable<TicketComment> comments,
        CommentVisibility visibility) =>
        visibility.IncludesInternal ? comments : comments.Where(c => !c.IsInternal);

    /// <summary>
    /// Applies visibility and confines the query to one ticket. Shared by
    /// <see cref="ListAsync"/> and <see cref="CountAsync"/> so a rule cannot constrain
    /// the page but not the total.
    /// </summary>
    /// <remarks>Internal so it can be tested over an in-memory queryable, with no database.</remarks>
    internal static IQueryable<TicketComment> Filter(
        IQueryable<TicketComment> comments,
        TicketCommentQuery query)
    {
        // Visibility first. A total that counts comments the caller cannot read tells
        // them how many they are not being shown, which discloses that a staff
        // conversation is happening.
        comments = ApplyVisibility(comments, query.Visibility);

        var ticketId = query.TicketId;

        return comments.Where(c => c.TicketId == ticketId);
    }
}
