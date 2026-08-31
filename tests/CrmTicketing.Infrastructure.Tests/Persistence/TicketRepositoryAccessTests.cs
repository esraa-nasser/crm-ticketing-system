using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Persistence;

namespace CrmTicketing.Infrastructure.Tests.Persistence;

/// <summary>
/// The row-level rule that makes <c>GET /api/tickets</c> a security boundary.
/// </summary>
/// <remarks>
/// Exercised through <see cref="TicketRepository.Filter"/> and
/// <see cref="TicketRepository.ApplyAccess"/> over an in-memory
/// <see cref="IQueryable{T}"/>. The repository's constructor needs a
/// <c>CrmDbContext</c> and CI has no database; the EF in-memory provider is
/// deliberately not used, because it is a different query engine than production.
/// Issue #29 owns real integration tests.
/// </remarks>
public sealed class TicketRepositoryAccessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Customer = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid OtherCustomer = Guid.Parse("dddddddd-0000-0000-0000-000000000004");
    private static readonly Guid Actor = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static Ticket TicketFor(Guid requesterId, string title, TicketPriority priority = TicketPriority.Normal) =>
        Ticket.Open(
            Guid.CreateVersion7(),
            TicketTitle.Create(title),
            "It started smoking after the firmware update.",
            requesterId,
            Now,
            Actor,
            priority);

    private static IQueryable<Ticket> Tickets(params Ticket[] tickets) => tickets.AsQueryable();

    [Fact]
    public void Filter_ConfinesACustomerToTheirOwnTickets()
    {
        var mine = TicketFor(Customer, "Mine");
        var theirs = TicketFor(OtherCustomer, "Theirs");
        var query = TicketQuery.Create(TicketAccess.OwnedBy(Customer));

        var visible = TicketRepository.Filter(Tickets(mine, theirs), query).ToList();

        Assert.Equal(mine.Id, Assert.Single(visible).Id);
        Assert.DoesNotContain(visible, t => t.Id == theirs.Id);
    }

    [Fact]
    public void Filter_CountsOnlyWhatTheCustomerMaySee()
    {
        // The count must be constrained identically to the page. A total that counts
        // rows the caller cannot see discloses how many tickets exist.
        var query = TicketQuery.Create(TicketAccess.OwnedBy(Customer));

        var total = TicketRepository.Filter(
            Tickets(
                TicketFor(Customer, "Mine"),
                TicketFor(OtherCustomer, "Theirs"),
                TicketFor(OtherCustomer, "Also theirs")),
            query).Count();

        Assert.Equal(1, total);
    }

    [Fact]
    public void ApplyAccess_HidesAnotherCustomersTicketFromReadOne()
    {
        var theirs = TicketFor(OtherCustomer, "Theirs");

        // This is what makes GetById answer 404 rather than 403: the row simply is
        // not there, so the existing not-found path handles it.
        var found = TicketRepository
            .ApplyAccess(Tickets(theirs).Where(t => t.Id == theirs.Id), TicketAccess.OwnedBy(Customer))
            .FirstOrDefault();

        Assert.Null(found);
    }

    [Fact]
    public void ApplyAccess_ReturnsTheCustomersOwnTicketFromReadOne()
    {
        var mine = TicketFor(Customer, "Mine");

        var found = TicketRepository
            .ApplyAccess(Tickets(mine).Where(t => t.Id == mine.Id), TicketAccess.OwnedBy(Customer))
            .FirstOrDefault();

        Assert.NotNull(found);
        Assert.Equal(mine.Id, found.Id);
    }

    [Fact]
    public void Filter_WithUnrestrictedAccess_ReturnsEveryTicket()
    {
        var mine = TicketFor(Customer, "Mine");
        var theirs = TicketFor(OtherCustomer, "Theirs");
        var query = TicketQuery.Create(TicketAccess.All());

        var visible = TicketRepository.Filter(Tickets(mine, theirs), query).ToList();

        Assert.Equal(2, visible.Count);
        Assert.Contains(visible, t => t.Id == mine.Id);
        Assert.Contains(visible, t => t.Id == theirs.Id);
    }

    [Fact]
    public void Filter_AppliesAccessAndTheCallersFiltersTogether()
    {
        // A customer filtering by priority must not thereby see another customer's
        // tickets of that priority.
        var mineHigh = TicketFor(Customer, "Mine high", TicketPriority.High);
        var query = TicketQuery.Create(TicketAccess.OwnedBy(Customer), priority: TicketPriority.High);

        var visible = TicketRepository.Filter(
            Tickets(
                mineHigh,
                TicketFor(Customer, "Mine normal"),
                TicketFor(OtherCustomer, "Theirs high", TicketPriority.High)),
            query).ToList();

        Assert.Equal(mineHigh.Id, Assert.Single(visible).Id);
    }
}
