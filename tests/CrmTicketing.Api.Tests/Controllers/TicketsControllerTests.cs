using CrmTicketing.Api.Controllers;
using CrmTicketing.Api.Infrastructure;
using CrmTicketing.Domain.Tickets;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CrmTicketing.Api.Tests.Controllers;

public sealed class TicketsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Mirrors TicketRepository's filter, order, and page behaviour in memory, so
    // these tests need no database and no query provider.
    private sealed class FakeTicketRepository : ITicketRepository
    {
        private readonly List<Ticket> tickets = [];

        public int SaveChangesCount { get; private set; }

        public IReadOnlyList<Ticket> Tickets => tickets;

        public void Seed(params Ticket[] seeded) => tickets.AddRange(seeded);

        public Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(tickets.Find(t => t.Id == id));

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken)
        {
            tickets.Add(ticket);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Ticket>> ListAsync(
            TicketQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Ticket>>(
                [.. Filter(query)
                    .OrderByDescending(t => t.CreatedAt)
                    .ThenBy(t => t.Id)
                    .Skip(query.Skip)
                    .Take(query.PageSize)]);

        public Task<int> CountAsync(TicketQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(Filter(query).Count());

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }

        private IEnumerable<Ticket> Filter(TicketQuery query) => tickets.Where(t =>
            (query.Status is null || t.Status == query.Status)
            && (query.Priority is null || t.Priority == query.Priority)
            && (query.AssigneeId is null || t.AssigneeId == query.AssigneeId)
            && (query.RequesterId is null || t.RequesterId == query.RequesterId));
    }

    // ControllerBase resolves this from HttpContext.RequestServices, which a unit
    // test does not have. Assigning it directly keeps Problem() and
    // ValidationProblem() working without a host.
    private sealed class TestProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext,
            ModelStateDictionary modelStateDictionary,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null) =>
            new(modelStateDictionary)
            {
                Status = statusCode ?? StatusCodes.Status400BadRequest,
                Title = title,
                Type = type,
                Detail = detail,
                Instance = instance,
            };
    }

    private static TicketsController CreateController(FakeTicketRepository repository) =>
        new(repository, new FixedTimeProvider(Now))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            ProblemDetailsFactory = new TestProblemDetailsFactory(),
        };

    private static Ticket SeededTicket(
        TicketStatus status = TicketStatus.New,
        TicketPriority priority = TicketPriority.Normal,
        Guid? requesterId = null,
        Guid? assigneeId = null,
        DateTimeOffset? createdAt = null)
    {
        var ticket = Ticket.Open(
            Guid.CreateVersion7(),
            TicketTitle.Create("Printer is on fire"),
            "It started smoking after the firmware update.",
            requesterId ?? Guid.NewGuid(),
            createdAt ?? Now,
            priority);

        switch (status)
        {
            case TicketStatus.New:
                break;
            case TicketStatus.Open:
                ticket.TransitionTo(TicketStatus.Open, Now);
                break;
            case TicketStatus.Pending:
                ticket.TransitionTo(TicketStatus.Open, Now);
                ticket.TransitionTo(TicketStatus.Pending, Now);
                break;
            case TicketStatus.Resolved:
                ticket.TransitionTo(TicketStatus.Open, Now);
                ticket.TransitionTo(TicketStatus.Resolved, Now);
                break;
            case TicketStatus.Closed:
                ticket.TransitionTo(TicketStatus.Closed, Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (assigneeId is { } assignee && status != TicketStatus.Closed)
        {
            ticket.Assign(assignee, Now);
        }

        return ticket;
    }

    [Fact]
    public async Task Create_ReturnsCreatedPointingAtGetById()
    {
        var repository = new FakeTicketRepository();
        var controller = CreateController(repository);
        var requester = Guid.NewGuid();

        var result = await controller.Create(
            new CreateTicketRequest("  Printer is on fire  ", "  Smoke everywhere.  ", requester, null, "  "),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(TicketsController.GetById), created.ActionName);
        var response = Assert.IsType<TicketResponse>(created.Value);
        Assert.Equal(response.Id, Assert.IsType<Guid>(created.RouteValues!["id"]));

        Assert.Equal("Printer is on fire", response.Title);
        Assert.Equal("Smoke everywhere.", response.Description);
        Assert.Equal("New", response.Status);
        Assert.Equal("Normal", response.Priority);
        Assert.Null(response.Category);
        Assert.Equal(requester, response.RequesterId);
        Assert.Equal(Now, response.CreatedAt);
        Assert.Equal(response.CreatedAt, response.UpdatedAt);

        Assert.Single(repository.Tickets);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task Create_UnknownPriority_Returns400()
    {
        var repository = new FakeTicketRepository();
        var controller = CreateController(repository);

        var result = await controller.Create(
            new CreateTicketRequest("Printer is on fire", "Smoke.", Guid.NewGuid(), "Screaming", null),
            CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Empty(repository.Tickets);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404()
    {
        var controller = CreateController(new FakeTicketRepository());

        var result = await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsTheTicket()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket();
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var result = await controller.GetById(ticket.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(ticket.Id, Assert.IsType<TicketResponse>(ok.Value).Id);
    }

    [Fact]
    public async Task Transition_IllegalMove_ThrowsAndMapsTo409()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket(TicketStatus.Closed);
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var ex = await Assert.ThrowsAsync<InvalidTicketTransitionException>(
            () => controller.Transition(
                ticket.Id,
                new TransitionTicketRequest("Open"),
                CancellationToken.None));

        Assert.Equal(TicketStatus.Closed, ex.From);
        Assert.Equal(TicketStatus.Open, ex.To);
        Assert.Equal(StatusCodes.Status409Conflict, DomainExceptionHandler.MapStatusCode(ex));
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task Transition_LegalMove_Returns200()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket();
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var result = await controller.Transition(
            ticket.Id,
            new TransitionTicketRequest("open"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Open", Assert.IsType<TicketResponse>(ok.Value).Status);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Theory]
    [InlineData("Frozen")]
    [InlineData("3")]
    [InlineData(" 3 ")]
    [InlineData("99")]
    [InlineData("")]
    public async Task Transition_UnknownStatusString_Returns400(string status)
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket();
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var result = await controller.Transition(
            ticket.Id,
            new TransitionTicketRequest(status),
            CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task Transition_UnknownId_Returns404()
    {
        var controller = CreateController(new FakeTicketRepository());

        var result = await controller.Transition(
            Guid.NewGuid(),
            new TransitionTicketRequest("Open"),
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task List_PageSize500_IsClampedTo100()
    {
        var repository = new FakeTicketRepository();
        repository.Seed(SeededTicket(), SeededTicket());
        var controller = CreateController(repository);

        var result = await controller.List(CancellationToken.None, pageSize: 500);

        var page = Assert.IsType<PagedResponse<TicketSummaryResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(TicketQuery.MaxPageSize, page.PageSize);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task List_FiltersByStatusAndAssignee()
    {
        var assignee = Guid.NewGuid();
        var repository = new FakeTicketRepository();
        var wanted = SeededTicket(TicketStatus.Open, assigneeId: assignee);
        repository.Seed(
            wanted,
            SeededTicket(TicketStatus.Open),
            SeededTicket(TicketStatus.Pending, assigneeId: assignee),
            SeededTicket());
        var controller = CreateController(repository);

        var result = await controller.List(
            CancellationToken.None,
            status: "Open",
            assigneeId: assignee);

        var page = Assert.IsType<PagedResponse<TicketSummaryResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        // TotalCount reflects the filter, not the page.
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(wanted.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task List_PageBeyondTheEnd_ReturnsEmptyItemsAndTheTrueTotal()
    {
        var repository = new FakeTicketRepository();
        repository.Seed(SeededTicket(), SeededTicket(), SeededTicket());
        var controller = CreateController(repository);

        var result = await controller.List(CancellationToken.None, page: 99, pageSize: 10);

        var page = Assert.IsType<PagedResponse<TicketSummaryResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(99, page.Page);
    }

    [Fact]
    public async Task List_UnknownStatusString_Returns400()
    {
        var controller = CreateController(new FakeTicketRepository());

        var result = await controller.List(CancellationToken.None, status: "Frozen");

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Patch_UnknownId_Returns404()
    {
        var controller = CreateController(new FakeTicketRepository());

        var result = await controller.Update(
            Guid.NewGuid(),
            new UpdateTicketRequest("New title", "New body", null, null),
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Patch_UpdatesTitleDescriptionAndPriority()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket();
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var result = await controller.Update(
            ticket.Id,
            new UpdateTicketRequest("Printer still smoking", "Now worse.", "Facilities", "Urgent"),
            CancellationToken.None);

        var response = Assert.IsType<TicketResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Printer still smoking", response.Title);
        Assert.Equal("Now worse.", response.Description);
        Assert.Equal("Facilities", response.Category);
        Assert.Equal("Urgent", response.Priority);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task Patch_UnknownPriority_Returns400AndLeavesTheTicketAlone()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket();
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var result = await controller.Update(
            ticket.Id,
            new UpdateTicketRequest("Changed", "Changed body", null, "Screaming"),
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);
        Assert.Equal("Printer is on fire", ticket.Title.Value);
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public async Task Assign_SetsAssignee()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket();
        repository.Seed(ticket);
        var controller = CreateController(repository);
        var assignee = Guid.NewGuid();

        var result = await controller.Assign(
            ticket.Id,
            new AssignTicketRequest(assignee),
            CancellationToken.None);

        var response = Assert.IsType<TicketResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(assignee, response.AssigneeId);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task Assign_NullAssigneeId_Unassigns()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket(assigneeId: Guid.NewGuid());
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var result = await controller.Assign(
            ticket.Id,
            new AssignTicketRequest(null),
            CancellationToken.None);

        var response = Assert.IsType<TicketResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Null(response.AssigneeId);
    }

    [Fact]
    public async Task Assign_ClosedTicket_ThrowsTicketClosedAndMapsTo409()
    {
        var repository = new FakeTicketRepository();
        var ticket = SeededTicket(TicketStatus.Closed);
        repository.Seed(ticket);
        var controller = CreateController(repository);

        var ex = await Assert.ThrowsAsync<TicketClosedException>(
            () => controller.Assign(
                ticket.Id,
                new AssignTicketRequest(Guid.NewGuid()),
                CancellationToken.None));

        Assert.Equal("assigned", ex.Operation);
        Assert.Equal(StatusCodes.Status409Conflict, DomainExceptionHandler.MapStatusCode(ex));
        Assert.Equal(0, repository.SaveChangesCount);
    }

    [Fact]
    public void Metadata_ReturnsTransitionMapFromDomain()
    {
        var controller = CreateController(new FakeTicketRepository());

        var metadata = Assert.IsType<TicketMetadataResponse>(
            Assert.IsType<OkObjectResult>(controller.GetMetadata().Result).Value);

        Assert.Equal(5, metadata.Statuses.Count);
        Assert.Equal(4, metadata.Priorities.Count);
        Assert.Equal(5, metadata.Transitions.Count);

        Assert.Empty(metadata.Transitions["Closed"]);

        // Count plus membership, not sequence equality: the domain set has no
        // guaranteed order.
        var fromNew = metadata.Transitions["New"];
        Assert.Equal(2, fromNew.Count);
        Assert.Contains("Open", fromNew);
        Assert.Contains("Closed", fromNew);
    }

    [Fact]
    public void Metadata_MatchesTheDomainTableForEveryStatus()
    {
        var controller = CreateController(new FakeTicketRepository());

        var metadata = Assert.IsType<TicketMetadataResponse>(
            Assert.IsType<OkObjectResult>(controller.GetMetadata().Result).Value);

        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            var published = metadata.Transitions[status.ToString()];
            var expected = TicketStatusTransitions.AllowedFrom(status);

            Assert.Equal(expected.Count, published.Count);

            foreach (var target in expected)
            {
                Assert.Contains(target.ToString(), published);
            }
        }
    }
}
