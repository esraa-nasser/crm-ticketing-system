using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Domain.Tests.Tickets;

public sealed class TicketQueryTests
{
    [Theory]
    [InlineData(500, TicketQuery.MaxPageSize)]
    [InlineData(101, TicketQuery.MaxPageSize)]
    [InlineData(TicketQuery.MaxPageSize, TicketQuery.MaxPageSize)]
    [InlineData(0, TicketQuery.DefaultPageSize)]
    [InlineData(-5, TicketQuery.DefaultPageSize)]
    [InlineData(10, 10)]
    public void Create_ClampsPageSize(int requested, int expected) =>
        Assert.Equal(expected, TicketQuery.Create(TicketAccess.All(), pageSize: requested).PageSize);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public void Create_ClampsPage(int requested, int expected) =>
        Assert.Equal(expected, TicketQuery.Create(TicketAccess.All(), page: requested).Page);

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(3, 10, 20)]
    [InlineData(0, 500, 0)]
    [InlineData(-4, -1, 0)]
    public void Skip_IsPageOffsetAndNeverNegative(int page, int pageSize, int expected)
    {
        var query = TicketQuery.Create(TicketAccess.All(), page: page, pageSize: pageSize);

        Assert.Equal(expected, query.Skip);
        Assert.True(query.Skip >= 0);
        Assert.Equal((query.Page - 1) * query.PageSize, query.Skip);
    }

    [Fact]
    public void Create_RoundTripsFilters()
    {
        var assignee = Guid.NewGuid();
        var requester = Guid.NewGuid();

        var query = TicketQuery.Create(
            TicketAccess.All(),
            TicketStatus.Pending,
            TicketPriority.Urgent,
            assignee,
            requester,
            page: 2,
            pageSize: 50);

        Assert.Equal(TicketStatus.Pending, query.Status);
        Assert.Equal(TicketPriority.Urgent, query.Priority);
        Assert.Equal(assignee, query.AssigneeId);
        Assert.Equal(requester, query.RequesterId);
        Assert.Equal(2, query.Page);
        Assert.Equal(50, query.PageSize);
    }

    [Fact]
    public void Create_DefaultsToAnUnfilteredFirstPage()
    {
        var query = TicketQuery.Create(TicketAccess.All());

        Assert.Null(query.Status);
        Assert.Null(query.Priority);
        Assert.Null(query.AssigneeId);
        Assert.Null(query.RequesterId);
        Assert.Equal(1, query.Page);
        Assert.Equal(TicketQuery.DefaultPageSize, query.PageSize);
    }
}
