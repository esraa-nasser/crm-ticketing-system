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

    [Fact]
    public void Assign_ClosedTicket_ThrowsTicketClosed()
    {
        var ticket = TicketInStatus(TicketStatus.Closed);

        // The exact type matters: DomainExceptionHandler maps TicketClosedException
        // to 409 and anything else to 500, so a looser assertion would let a revert
        // to a plain InvalidOperationException pass while the endpoint regressed.
        var ex = Assert.Throws<TicketClosedException>(() => ticket.Assign(Guid.NewGuid(), LaterAt));

        Assert.Equal("assigned", ex.Operation);
        Assert.Equal("A ticket with status Closed cannot be assigned.", ex.Message);
    }

    [Fact]
    public void Unassign_ClosedTicket_ThrowsTicketClosed()
    {
        var ticket = TicketInStatus(TicketStatus.Closed);

        var ex = Assert.Throws<TicketClosedException>(() => ticket.Unassign(LaterAt));

        Assert.Equal("unassigned", ex.Operation);
        Assert.Equal("A ticket with status Closed cannot be unassigned.", ex.Message);
    }

    [Fact]
    public void UpdateDetails_ReplacesFieldsAndAdvancesUpdatedAt()
    {
        var ticket = OpenTicket("Hardware");

        ticket.UpdateDetails(
            TicketTitle.Create("  Printer still smoking  "),
            "  Now with more smoke.  ",
            "  Facilities  ",
            LaterAt);

        Assert.Equal("Printer still smoking", ticket.Title.Value);
        Assert.Equal("Now with more smoke.", ticket.Description);
        Assert.Equal("Facilities", ticket.Category);
        Assert.Equal(LaterAt, ticket.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_RejectsNullTitle()
    {
        var ticket = OpenTicket();

        Assert.Throws<ArgumentNullException>(
            () => ticket.UpdateDetails(null!, "Still broken.", null, LaterAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_RejectsMissingDescription(string? description)
    {
        var ticket = OpenTicket();

        var ex = Assert.Throws<ArgumentException>(() => ticket.UpdateDetails(
            TicketTitle.Create("Printer is on fire"),
            description!,
            null,
            LaterAt));

        Assert.Equal("description", ex.ParamName);
    }

    [Fact]
    public void UpdateDetails_RejectsOversizedDescriptionAndCategory()
    {
        var ticket = OpenTicket();
        var title = TicketTitle.Create("Printer is on fire");

        var description = Assert.Throws<ArgumentException>(() => ticket.UpdateDetails(
            title,
            new string('x', Ticket.MaxDescriptionLength + 1),
            null,
            LaterAt));
        Assert.Equal("description", description.ParamName);

        var category = Assert.Throws<ArgumentException>(() => ticket.UpdateDetails(
            title,
            "Still broken.",
            new string('x', Ticket.MaxCategoryLength + 1),
            LaterAt));
        Assert.Equal("category", category.ParamName);
    }

    [Fact]
    public void UpdateDetails_LeavesTicketUntouchedWhenDescriptionIsInvalid()
    {
        var ticket = OpenTicket("Hardware");
        var originalTitle = ticket.Title.Value;

        Assert.Throws<ArgumentException>(() => ticket.UpdateDetails(
            TicketTitle.Create("A brand new title"),
            "",
            "Facilities",
            LaterAt));

        Assert.Equal(originalTitle, ticket.Title.Value);
        Assert.Equal("Hardware", ticket.Category);
        Assert.Equal(CreatedAt, ticket.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_SucceedsOnAClosedTicket()
    {
        var ticket = TicketInStatus(TicketStatus.Closed);

        ticket.UpdateDetails(
            TicketTitle.Create("Corrected after closing"),
            "Typo fixed.",
            null,
            LaterAt);

        Assert.Equal("Corrected after closing", ticket.Title.Value);
        Assert.Equal(TicketStatus.Closed, ticket.Status);
    }
}
