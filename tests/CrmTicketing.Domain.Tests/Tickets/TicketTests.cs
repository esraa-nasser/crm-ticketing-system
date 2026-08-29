using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Domain.Tests.Tickets;

public sealed class TicketTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 5, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterAt = CreatedAt.AddHours(2);

    private static Ticket OpenTicket(string? category = null) => Ticket.Open(
        Guid.NewGuid(),
        TicketTitle.Create("Printer is on fire"),
        "It started smoking after the firmware update.",
        Guid.NewGuid(),
        CreatedAt,
        category: category);

    // Walks the ticket to the requested status using only legal moves, so the
    // fixture itself never depends on an illegal transition being tolerated.
    private static Ticket TicketInStatus(TicketStatus status)
    {
        var ticket = OpenTicket();

        switch (status)
        {
            case TicketStatus.New:
                break;
            case TicketStatus.Open:
                ticket.TransitionTo(TicketStatus.Open, CreatedAt);
                break;
            case TicketStatus.Pending:
                ticket.TransitionTo(TicketStatus.Open, CreatedAt);
                ticket.TransitionTo(TicketStatus.Pending, CreatedAt);
                break;
            case TicketStatus.Resolved:
                ticket.TransitionTo(TicketStatus.Open, CreatedAt);
                ticket.TransitionTo(TicketStatus.Resolved, CreatedAt);
                break;
            case TicketStatus.Closed:
                ticket.TransitionTo(TicketStatus.Closed, CreatedAt);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return ticket;
    }

    [Fact]
    public void Open_RejectsEmptyRequester()
    {
        var ex = Assert.Throws<ArgumentException>(() => Ticket.Open(
            Guid.NewGuid(),
            TicketTitle.Create("Printer is on fire"),
            "Smoke.",
            Guid.Empty,
            CreatedAt));

        Assert.Equal("requesterId", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_RejectsMissingDescription(string? description)
    {
        var ex = Assert.Throws<ArgumentException>(() => Ticket.Open(
            Guid.NewGuid(),
            TicketTitle.Create("Printer is on fire"),
            description!,
            Guid.NewGuid(),
            CreatedAt));

        Assert.Equal("description", ex.ParamName);
    }

    [Fact]
    public void Open_RejectsDescriptionOverMaxLength()
    {
        var ex = Assert.Throws<ArgumentException>(() => Ticket.Open(
            Guid.NewGuid(),
            TicketTitle.Create("Printer is on fire"),
            new string('x', Ticket.MaxDescriptionLength + 1),
            Guid.NewGuid(),
            CreatedAt));

        Assert.Equal("description", ex.ParamName);
    }

    [Fact]
    public void Open_AcceptsDescriptionAtMaxLength()
    {
        var description = new string('x', Ticket.MaxDescriptionLength);

        var ticket = Ticket.Open(
            Guid.NewGuid(),
            TicketTitle.Create("Printer is on fire"),
            description,
            Guid.NewGuid(),
            CreatedAt);

        Assert.Equal(description, ticket.Description);
    }

    [Fact]
    public void Open_RejectsCategoryOverMaxLength()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => OpenTicket(new string('x', Ticket.MaxCategoryLength + 1)));

        Assert.Equal("category", ex.ParamName);
    }

    [Fact]
    public void Open_StartsNewWithNormalPriorityAndEqualTimestamps()
    {
        var ticket = OpenTicket();

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(TicketPriority.Normal, ticket.Priority);
        Assert.Equal(CreatedAt, ticket.CreatedAt);
        Assert.Equal(ticket.CreatedAt, ticket.UpdatedAt);
        Assert.Null(ticket.AssigneeId);
        Assert.Null(ticket.Category);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Open_NormalisesBlankCategoryToNull(string category) =>
        Assert.Null(OpenTicket(category).Category);

    [Fact]
    public void Open_TrimsCategory() => Assert.Equal("Hardware", OpenTicket("  Hardware  ").Category);

    [Theory]
    [MemberData(
        nameof(TicketStatusTransitionsTests.AllPairs),
        MemberType = typeof(TicketStatusTransitionsTests))]
    public void TransitionTo_HonoursTheTransitionTable(TicketStatus from, TicketStatus to)
    {
        var ticket = TicketInStatus(from);

        if (TicketStatusTransitionsTests.IsLegal(from, to))
        {
            ticket.TransitionTo(to, LaterAt);

            Assert.Equal(to, ticket.Status);
            Assert.Equal(LaterAt, ticket.UpdatedAt);
        }
        else
        {
            var ex = Assert.Throws<InvalidTicketTransitionException>(
                () => ticket.TransitionTo(to, LaterAt));

            Assert.Equal(from, ex.From);
            Assert.Equal(to, ex.To);
            Assert.Equal(from, ticket.Status);
            Assert.Equal(CreatedAt, ticket.UpdatedAt);
        }
    }

    [Fact]
    public void Assign_RejectsEmptyAssignee()
    {
        var ex = Assert.Throws<ArgumentException>(() => OpenTicket().Assign(Guid.Empty, LaterAt));

        Assert.Equal("assigneeId", ex.ParamName);
    }

    [Fact]
    public void Assign_RejectsClosedTicket()
    {
        var ticket = TicketInStatus(TicketStatus.Closed);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ticket.Assign(Guid.NewGuid(), LaterAt));

        Assert.Contains(nameof(TicketStatus.Closed), ex.Message, StringComparison.Ordinal);
        Assert.IsNotType<InvalidTicketTransitionException>(ex);
    }

    [Fact]
    public void Assign_SetsAssigneeAndAdvancesUpdatedAt()
    {
        var ticket = OpenTicket();
        var assignee = Guid.NewGuid();

        ticket.Assign(assignee, LaterAt);

        Assert.Equal(assignee, ticket.AssigneeId);
        Assert.Equal(LaterAt, ticket.UpdatedAt);
    }

    [Fact]
    public void Unassign_ClearsAssignee()
    {
        var ticket = OpenTicket();
        ticket.Assign(Guid.NewGuid(), LaterAt);

        ticket.Unassign(LaterAt.AddMinutes(5));

        Assert.Null(ticket.AssigneeId);
        Assert.Equal(LaterAt.AddMinutes(5), ticket.UpdatedAt);
    }

    [Fact]
    public void ChangePriority_SetsPriorityAndAdvancesUpdatedAt()
    {
        var ticket = OpenTicket();

        ticket.ChangePriority(TicketPriority.Urgent, LaterAt);

        Assert.Equal(TicketPriority.Urgent, ticket.Priority);
        Assert.Equal(LaterAt, ticket.UpdatedAt);
    }
}
