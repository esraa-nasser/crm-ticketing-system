using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Domain.Tests.Tickets;

public sealed class TicketCommentQueryTests
{
    private static readonly Guid TicketId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Create_ClampsThePage(int requested, int expected) =>
        Assert.Equal(
            expected,
            TicketCommentQuery.Create(TicketId, CommentVisibility.All(), page: requested).Page);

    [Theory]
    [InlineData(0, TicketCommentQuery.DefaultPageSize)]
    [InlineData(-1, TicketCommentQuery.DefaultPageSize)]
    [InlineData(10, 10)]
    [InlineData(1000, TicketCommentQuery.MaxPageSize)]
    public void Create_ClampsThePageSize(int requested, int expected) =>
        Assert.Equal(
            expected,
            TicketCommentQuery.Create(TicketId, CommentVisibility.All(), pageSize: requested).PageSize);

    [Fact]
    public void Skip_IsNeverNegative()
    {
        // Follows from the page clamp, and is the value that reaches the database.
        var query = TicketCommentQuery.Create(TicketId, CommentVisibility.All(), page: -3);

        Assert.Equal(0, query.Skip);
    }

    [Fact]
    public void Skip_CountsWholePages()
    {
        var query = TicketCommentQuery.Create(TicketId, CommentVisibility.All(), page: 3, pageSize: 10);

        Assert.Equal(20, query.Skip);
    }

    [Fact]
    public void Create_RejectsAnEmptyTicketId()
    {
        // An empty id would match no rows, which reads as an empty thread rather than
        // as the bug it is.
        var ex = Assert.Throws<ArgumentException>(() =>
            TicketCommentQuery.Create(Guid.Empty, CommentVisibility.All()));

        Assert.Equal("ticketId", ex.ParamName);
    }

    [Fact]
    public void Create_RejectsANullVisibility() =>
        Assert.Throws<ArgumentNullException>(() => TicketCommentQuery.Create(TicketId, null!));

    [Fact]
    public void Visibility_IsCarriedThrough()
    {
        Assert.False(TicketCommentQuery.Create(TicketId, CommentVisibility.PublicOnly()).Visibility.IncludesInternal);
        Assert.True(TicketCommentQuery.Create(TicketId, CommentVisibility.All()).Visibility.IncludesInternal);
    }
}
