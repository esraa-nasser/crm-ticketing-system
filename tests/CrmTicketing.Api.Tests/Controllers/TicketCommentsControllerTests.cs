using System.Reflection;
using System.Security.Claims;
using CrmTicketing.Api.Configuration;
using CrmTicketing.Api.Controllers;
using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Identity;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CrmTicketing.Api.Tests.Controllers;

/// <summary>
/// The comment endpoints. No HTTP, no database: the repositories are hand-rolled
/// fakes and the principal is constructed directly.
/// </summary>
public sealed class TicketCommentsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TicketId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AgentId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CustomerId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid OtherCustomerId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Mirrors the real repository's access behaviour: a ticket the caller may not see
    // comes back null, so the controller answers 404 rather than 403.
    private sealed class FakeTicketRepository : ITicketRepository
    {
        private readonly List<Ticket> tickets = [];

        public void Seed(params Ticket[] seeded) => tickets.AddRange(seeded);

        public Task<Ticket?> GetAsync(Guid id, TicketAccess access, CancellationToken cancellationToken) =>
            Task.FromResult(tickets.Find(t =>
                t.Id == id
                && (access.RestrictedToRequesterId is not { } owner || t.RequesterId == owner)));

        public Task AddAsync(Ticket ticket, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The comment endpoints never create a ticket.");

        public Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(TicketQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Comments are saved through their own repository.");
    }

    // Mirrors TicketCommentRepository's filter, order, and page behaviour in memory.
    private sealed class FakeTicketCommentRepository : ITicketCommentRepository
    {
        private readonly List<TicketComment> comments = [];

        public IReadOnlyList<TicketComment> Comments => comments;

        public int SaveChangesCount { get; private set; }

        /// <summary>The visibility the controller handed the repository, most recent last.</summary>
        public List<CommentVisibility> Visibilities { get; } = [];

        public void Seed(params TicketComment[] seeded) => comments.AddRange(seeded);

        public Task AddAsync(TicketComment comment, CancellationToken cancellationToken)
        {
            comments.Add(comment);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TicketComment>> ListAsync(
            TicketCommentQuery query,
            CancellationToken cancellationToken)
        {
            Visibilities.Add(query.Visibility);

            return Task.FromResult<IReadOnlyList<TicketComment>>(
                [.. Filter(query)
                    .OrderByDescending(c => c.CreatedAt)
                    .ThenByDescending(c => c.Id)
                    .Skip(query.Skip)
                    .Take(query.PageSize)]);
        }

        public Task<int> CountAsync(TicketCommentQuery query, CancellationToken cancellationToken)
        {
            Visibilities.Add(query.Visibility);

            return Task.FromResult(Filter(query).Count());
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }

        private IEnumerable<TicketComment> Filter(TicketCommentQuery query) => comments.Where(c =>
            c.TicketId == query.TicketId
            && (query.Visibility.IncludesInternal || !c.IsInternal));
    }

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
            new(modelStateDictionary) { Status = statusCode ?? StatusCodes.Status400BadRequest, Title = title };
    }

    private static ClaimsPrincipal Principal(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(AuthenticationSetup.UserIdClaimType, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(AuthenticationSetup.RoleClaimType, r)));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "Test",
            nameType: AuthenticationSetup.NameClaimType,
            roleType: AuthenticationSetup.RoleClaimType));
    }

    private static ClaimsPrincipal Agent() => Principal(AgentId, RoleNames.Agent);

    private static ClaimsPrincipal Customer() => Principal(CustomerId, RoleNames.Customer);

    private static TicketCommentsController CreateController(
        FakeTicketRepository tickets,
        FakeTicketCommentRepository comments,
        ClaimsPrincipal? user = null) =>
        new(tickets, comments, new FixedTimeProvider(Now))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user ?? Agent() },
            },
            ProblemDetailsFactory = new TestProblemDetailsFactory(),
        };

    private static Ticket SeededTicket(
        TicketStatus status = TicketStatus.Open,
        Guid? requesterId = null)
    {
        var ticket = Ticket.Open(
            TicketId,
            TicketTitle.Create("Laptop will not charge"),
            "The charger light is on but the battery stays at zero.",
            requesterId ?? CustomerId,
            Now,
            requesterId ?? CustomerId);

        foreach (var step in Path(status))
        {
            ticket.TransitionTo(step, Now, AgentId);
        }

        return ticket;
    }

    private static IReadOnlyList<TicketStatus> Path(TicketStatus target) => target switch
    {
        TicketStatus.New => [],
        TicketStatus.Open => [TicketStatus.Open],
        TicketStatus.Pending => [TicketStatus.Open, TicketStatus.Pending],
        TicketStatus.Resolved => [TicketStatus.Open, TicketStatus.Resolved],
        TicketStatus.Closed => [TicketStatus.Open, TicketStatus.Closed],
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static TicketComment Comment(bool isInternal = false, int minutesLater = 0, Guid? ticketId = null) =>
        TicketComment.Write(
            Guid.CreateVersion7(),
            ticketId ?? TicketId,
            AgentId,
            isInternal ? "Internal note." : "Public reply.",
            isInternal,
            Now.AddMinutes(minutesLater));

    // ---- POST ----

    [Fact]
    public async Task Post_AsACustomer_WithIsInternalTrue_ReturnsForbidAndStoresNothing()
    {
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Customer());

        var result = await controller.Create(
            TicketId,
            new CreateCommentRequest("Let me in.", IsInternal: true),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);

        // Both halves. A 403 that still wrote a row is the defect the ordering in the
        // action exists to prevent.
        Assert.Empty(comments.Comments);
        Assert.Equal(0, comments.SaveChangesCount);
    }

    [Fact]
    public async Task Post_AsACustomer_PublicComment_Succeeds()
    {
        // Without this, the test above passes for a controller that refuses everything.
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Customer());

        var result = await controller.Create(
            TicketId,
            new CreateCommentRequest("Tried a different socket.", IsInternal: false),
            CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);

        var stored = Assert.Single(comments.Comments);
        Assert.False(stored.IsInternal);
        Assert.Equal(CustomerId, stored.AuthorId);
    }

    [Fact]
    public async Task Post_AsStaff_WithIsInternalTrue_StoresAnInternalComment()
    {
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Agent());

        await controller.Create(
            TicketId,
            new CreateCommentRequest("Using the last spare.", IsInternal: true),
            CancellationToken.None);

        Assert.True(Assert.Single(comments.Comments).IsInternal);
    }

    [Fact]
    public async Task Post_ToAClosedTicket_ThrowsTicketClosedException()
    {
        // The controller does not answer 409 itself: it lets the domain exception reach
        // DomainExceptionHandler, which maps it. DomainExceptionHandlerTests pins the
        // other half of that pairing.
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket(TicketStatus.Closed));
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Agent());

        var ex = await Assert.ThrowsAsync<TicketClosedException>(() => controller.Create(
            TicketId,
            new CreateCommentRequest("Too late.", IsInternal: false),
            CancellationToken.None));

        Assert.Equal("commented on", ex.Operation);
        Assert.Empty(comments.Comments);
    }

    [Fact]
    public async Task Post_AsACustomer_InternalOnAClosedTicket_IsForbiddenNotConflicted()
    {
        // The 403 precedes the 409 deliberately: the caller is refused for the reason
        // about them, and is not told the ticket's state.
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket(TicketStatus.Closed));
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Customer());

        var result = await controller.Create(
            TicketId,
            new CreateCommentRequest("Let me in.", IsInternal: true),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task Post_ToATicketTheCallerMayNotSee_Returns404()
    {
        // Another customer's ticket. 404, never 403 - a 403 would confirm it exists.
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket(requesterId: OtherCustomerId));
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Customer());

        var result = await controller.Create(
            TicketId,
            new CreateCommentRequest("Hello?", IsInternal: false),
            CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Empty(comments.Comments);
    }

    [Fact]
    public async Task Post_ToAMissingTicket_Returns404()
    {
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(new FakeTicketRepository(), comments, Agent());

        var result = await controller.Create(
            TicketId,
            new CreateCommentRequest("Hello?", IsInternal: false),
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Post_UsesTheCallerAsAuthor()
    {
        // There is no author field on the request. This pins that no future one is
        // honoured.
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Agent());

        await controller.Create(
            TicketId,
            new CreateCommentRequest("On it.", IsInternal: false),
            CancellationToken.None);

        var stored = Assert.Single(comments.Comments);
        Assert.Equal(AgentId, stored.AuthorId);
        Assert.Equal(TicketId, stored.TicketId);
        Assert.Equal(Now, stored.CreatedAt);
    }

    [Fact]
    public async Task Post_WithAnEmptyBody_ThrowsArgumentException()
    {
        // Reaches DomainExceptionHandler as a 400 carrying the parameter name.
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Agent());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => controller.Create(
            TicketId,
            new CreateCommentRequest("   ", IsInternal: false),
            CancellationToken.None));

        Assert.Equal("body", ex.ParamName);
        Assert.Empty(comments.Comments);
    }

    [Fact]
    public async Task Post_SavesOnce()
    {
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Agent());

        await controller.Create(
            TicketId,
            new CreateCommentRequest("On it.", IsInternal: false),
            CancellationToken.None);

        Assert.Equal(1, comments.SaveChangesCount);
    }

    // ---- GET ----

    [Fact]
    public async Task Get_ReturnsNewestFirstPaged()
    {
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        comments.Seed(Comment(minutesLater: 0), Comment(minutesLater: 10), Comment(minutesLater: 20));
        var controller = CreateController(tickets, comments, Agent());

        var result = await controller.List(TicketId, CancellationToken.None, page: 1, pageSize: 2);

        var page = Assert.IsType<PagedResponse<TicketCommentResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.PageSize);

        // The total ignores paging.
        Assert.Equal(3, page.TotalCount);

        // Newest first.
        Assert.True(page.Items[0].CreatedAt > page.Items[1].CreatedAt);
    }

    [Fact]
    public async Task Get_AsACustomer_PassesPublicOnlyVisibility()
    {
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        comments.Seed(Comment(), Comment(isInternal: true, minutesLater: 5));
        var controller = CreateController(tickets, comments, Customer());

        var result = await controller.List(TicketId, CancellationToken.None);

        var page = Assert.IsType<PagedResponse<TicketCommentResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        // The translation happens at the boundary, in CallerContext.
        Assert.All(comments.Visibilities, v => Assert.False(v.IncludesInternal));

        // And both the page and the count are constrained by it.
        Assert.DoesNotContain(page.Items, c => c.IsInternal);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Get_AsStaff_PassesAllVisibility()
    {
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        comments.Seed(Comment(), Comment(isInternal: true, minutesLater: 5));
        var controller = CreateController(tickets, comments, Agent());

        var result = await controller.List(TicketId, CancellationToken.None);

        var page = Assert.IsType<PagedResponse<TicketCommentResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.All(comments.Visibilities, v => Assert.True(v.IncludesInternal));
        Assert.Contains(page.Items, c => c.IsInternal);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task Get_ForATicketTheCallerMayNotSee_Returns404NotAnEmptyPage()
    {
        // An empty thread for a ticket that exists is still a confirmation that it
        // exists.
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket(requesterId: OtherCustomerId));
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Customer());

        var result = await controller.List(TicketId, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.IsNotType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Get_ClampsPagingRatherThanRejectingIt()
    {
        var tickets = new FakeTicketRepository();
        tickets.Seed(SeededTicket());
        var comments = new FakeTicketCommentRepository();
        var controller = CreateController(tickets, comments, Agent());

        var result = await controller.List(TicketId, CancellationToken.None, page: 0, pageSize: 10_000);

        var page = Assert.IsType<PagedResponse<TicketCommentResponse>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, page.Page);
        Assert.Equal(TicketCommentQuery.MaxPageSize, page.PageSize);
    }

    // ---- Authorisation surface ----

    [Fact]
    public void Endpoints_RequireAuthentication()
    {
        // The class-level [Authorize] is what refuses an anonymous caller on both
        // routes. Neither action may carry [AllowAnonymous].
        Assert.NotNull(typeof(TicketCommentsController).GetCustomAttribute<AuthorizeAttribute>());

        var anonymous = typeof(TicketCommentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .ToList();

        Assert.Empty(anonymous);
    }

    [Fact]
    public void Endpoints_AreNotStaffOnly()
    {
        // A customer may read and write public comments on their own ticket, so
        // neither action carries the staff policy.
        var staffOnly = typeof(TicketCommentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>())
            .Where(a => a.Policy == AuthorizationPolicies.StaffOnly)
            .ToList();

        Assert.Empty(staffOnly);
    }
}
