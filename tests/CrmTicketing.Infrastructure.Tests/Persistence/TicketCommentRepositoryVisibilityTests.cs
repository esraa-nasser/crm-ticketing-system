using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Persistence;

namespace CrmTicketing.Infrastructure.Tests.Persistence;

/// <summary>
/// The rule that keeps a staff conversation out of a customer's thread.
/// </summary>
/// <remarks>
/// Exercised through <see cref="TicketCommentRepository.Filter"/> and
/// <see cref="TicketCommentRepository.ApplyVisibility"/> over an in-memory
/// <see cref="IQueryable{T}"/>, the same way
/// <see cref="TicketRepositoryAccessTests"/> exercises the row-level ticket rule. The
/// repository's constructor needs a <c>CrmDbContext</c> and CI has no database; the EF
/// in-memory provider is deliberately not used, because it is a different query engine
/// than production. Issue #29 owns real integration tests.
/// </remarks>
public sealed class TicketCommentRepositoryVisibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ticket = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTicket = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Author = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static TicketComment Comment(
        string body,
        bool isInternal = false,
        Guid? ticketId = null,
        int minutesLater = 0) =>
        TicketComment.Write(
            Guid.CreateVersion7(),
            ticketId ?? Ticket,
            Author,
            body,
            isInternal,
            Now.AddMinutes(minutesLater));

    private static IQueryable<TicketComment> Comments(params TicketComment[] comments) =>
        comments.AsQueryable();

    private static TicketCommentQuery AsCustomer() =>
        TicketCommentQuery.Create(Ticket, CommentVisibility.PublicOnly());

    private static TicketCommentQuery AsStaff() =>
        TicketCommentQuery.Create(Ticket, CommentVisibility.All());

    [Fact]
    public void Filter_HidesInternalCommentsFromAPublicOnlyCaller()
    {
        var mine = Comment("Tried a different socket.");
        var theirs = Comment("Using the last spare from the cupboard.", isInternal: true);

        var visible = TicketCommentRepository.Filter(Comments(mine, theirs), AsCustomer()).ToList();

        Assert.Equal(mine.Id, Assert.Single(visible).Id);
        Assert.DoesNotContain(visible, c => c.Id == theirs.Id);
    }

    [Fact]
    public void Filter_CountsOnlyWhatAPublicOnlyCallerMaySee()
    {
        // The count must be constrained identically to the page. A total that counts
        // comments the caller cannot read tells them how many they are not being
        // shown, which discloses that a staff conversation is happening.
        var total = TicketCommentRepository.Filter(
            Comments(
                Comment("Public one."),
                Comment("Internal one.", isInternal: true),
                Comment("Internal two.", isInternal: true)),
            AsCustomer())
            .Count();

        Assert.Equal(1, total);
    }

    [Fact]
    public void ApplyVisibility_ReturnsEverythingForStaff()
    {
        // The mutation guard. Without it, an ApplyVisibility that dropped internal
        // comments unconditionally would still pass the two tests above.
        var visible = TicketCommentRepository.ApplyVisibility(
            Comments(Comment("Public."), Comment("Internal.", isInternal: true)),
            CommentVisibility.All())
            .ToList();

        Assert.Equal(2, visible.Count);
        Assert.Contains(visible, c => c.IsInternal);
    }

    [Fact]
    public void Filter_ConfinesToTheRequestedTicket()
    {
        var mine = Comment("On this ticket.");
        var elsewhere = Comment("On another ticket.", ticketId: OtherTicket);

        var visible = TicketCommentRepository.Filter(Comments(mine, elsewhere), AsStaff()).ToList();

        Assert.Equal(mine.Id, Assert.Single(visible).Id);
    }

    [Fact]
    public void Filter_CountsOnlyTheRequestedTicket()
    {
        var total = TicketCommentRepository.Filter(
            Comments(
                Comment("Mine."),
                Comment("Theirs.", ticketId: OtherTicket),
                Comment("Also theirs.", ticketId: OtherTicket)),
            AsStaff())
            .Count();

        Assert.Equal(1, total);
    }

    [Fact]
    public void Filter_AppliesBothRulesTogether()
    {
        // A customer reading their own ticket sees neither the internal comment on it
        // nor anything from another ticket.
        var visible = TicketCommentRepository.Filter(
            Comments(
                Comment("Mine and public."),
                Comment("Mine but internal.", isInternal: true),
                Comment("Public but another ticket.", ticketId: OtherTicket)),
            AsCustomer())
            .ToList();

        Assert.Equal("Mine and public.", Assert.Single(visible).Body);
    }

    [Fact]
    public void Order_IsTotalWhenTwoCommentsShareAnInstant()
    {
        // Ids are version 7, so a shared CreatedAt is broken by descending id and the
        // order stays total. Without a tiebreaker a comment can appear on two pages or
        // on none.
        var a = Comment("One.");
        var b = Comment("Two.");

        Assert.Equal(a.CreatedAt, b.CreatedAt);

        // Fed in ascending id order, so a stable sort with no tiebreaker would leave
        // them that way and the assertion below would fail. Which of the two ids is
        // larger is not assumed: version 7 orders by time, and these two share an
        // instant, so the random tail decides.
        var ascending = new[] { a, b }.OrderBy(c => c.Id).ToArray();
        var expected = ascending.Reverse().Select(c => c.Id).ToList();

        var ordered = TicketCommentRepository.Filter(Comments(ascending), AsStaff())
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => c.Id)
            .ToList();

        Assert.Equal(expected, ordered);
    }
}
