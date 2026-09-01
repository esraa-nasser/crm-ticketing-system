using Bunit;
using Bunit.TestDoubles;
using CrmTicketing.Client.Components;
using CrmTicketing.Client.Services;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Client.Tests.Components;

/// <summary>
/// Component tests for the comment thread. The API is stubbed at
/// <see cref="ITicketsApiClient"/>, so these need no HTTP, no API, and no database.
/// </summary>
public sealed class TicketCommentsTests : BunitContext
{
    private static readonly Guid TicketId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed class StubTicketsApiClient : ITicketsApiClient
    {
        public List<CreateCommentRequest> Posts { get; } = [];

        public int GetCommentsCalls { get; private set; }

        /// <summary>Returned by successive reads; the last repeats.</summary>
        public List<PagedResponse<TicketCommentResponse>> Reads { get; } = [];

        public Exception? ReadException { get; set; }

        public Exception? PostException { get; set; }

        public Task<PagedResponse<TicketCommentResponse>> GetCommentsAsync(
            Guid ticketId,
            int page,
            CancellationToken cancellationToken)
        {
            GetCommentsCalls++;

            if (ReadException is not null)
            {
                return Task.FromException<PagedResponse<TicketCommentResponse>>(ReadException);
            }

            // Successive reads return successive entries so a test can tell a re-fetch
            // apart from the write's own response.
            var index = Math.Min(GetCommentsCalls - 1, Reads.Count - 1);
            return Task.FromResult(Reads[index]);
        }

        public Task<TicketCommentResponse> AddCommentAsync(
            Guid ticketId,
            CreateCommentRequest request,
            CancellationToken cancellationToken)
        {
            Posts.Add(request);

            return PostException is not null
                ? Task.FromException<TicketCommentResponse>(PostException)
                : Task.FromResult(CommentOf("the write's own response", isInternal: request.IsInternal));
        }

        // The thread does none of these. Throwing rather than returning a default makes
        // an accidental call visible.
        public Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(string? status, string? priority, int page, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> GetTicketAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> TransitionAsync(Guid id, TransitionTicketRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static TicketCommentResponse CommentOf(
        string body,
        bool isInternal = false,
        int minutesLater = 0) =>
        new(
            Id: Guid.CreateVersion7(),
            TicketId: TicketId,
            AuthorId: UserId,
            Body: body,
            IsInternal: isInternal,
            CreatedAt: Now.AddMinutes(minutesLater));

    private static PagedResponse<TicketCommentResponse> Page(
        TicketCommentResponse[] items,
        int page = 1,
        int pageSize = 25,
        int? total = null) =>
        new(items, page, pageSize, total ?? items.Length);

    private StubTicketsApiClient Arrange(bool isStaff = true, params TicketCommentResponse[] comments)
    {
        var stub = new StubTicketsApiClient();
        stub.Reads.Add(Page(comments));

        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", UserId, isStaff ? ["Agent"] : ["Customer"], isStaff);

        Services.AddSingleton<ITicketsApiClient>(stub);
        Services.AddSingleton(tokens);
        Services.GetRequiredService<BunitNavigationManager>()
            .NavigateTo($"http://localhost/tickets/{TicketId}");

        return stub;
    }

    private IRenderedComponent<TicketComments> RenderThread() =>
        Render<TicketComments>(p => p.Add(c => c.TicketId, TicketId));

    [Fact]
    public void Thread_RendersNewestFirstWithLocalTimestamps()
    {
        var newest = CommentOf("Written second.", minutesLater: 30);
        var oldest = CommentOf("Written first.");
        var stub = Arrange(isStaff: true);
        stub.Reads[0] = Page([newest, oldest]);

        var thread = RenderThread();
        var rendered = thread.FindAll("[data-testid=comment]");

        Assert.Equal(2, rendered.Count);

        // Order comes from the stub's response; the component does not re-sort.
        Assert.Contains("Written second.", rendered[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Written first.", rendered[1].TextContent, StringComparison.Ordinal);

        // Rendered through DisplayTime, not as a raw DateTimeOffset. Story 08 fixed
        // this defect on the list and detail views; a new component is where it comes
        // back.
        Assert.Contains(DisplayTime.Local(newest.CreatedAt), thread.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(newest.CreatedAt.ToString("o"), thread.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Thread_MarksInternalCommentsVisibly()
    {
        var stub = Arrange();
        stub.Reads[0] = Page([CommentOf("Staff only.", isInternal: true), CommentOf("Public.")]);

        var thread = RenderThread();

        // Exactly one marker: a staff reader must be able to tell at a glance what the
        // requester can see.
        Assert.Single(thread.FindAll("[data-testid=internal-badge]"));
    }

    [Fact]
    public void Thread_EmptyRendersDistinctlyFromFailed()
    {
        Arrange();

        var thread = RenderThread();

        Assert.Single(thread.FindAll("[data-testid=comments-empty]"));
        Assert.Empty(thread.FindAll(".alert-danger"));
    }

    [Fact]
    public void Thread_LoadFailure_RendersAnErrorWithRetry()
    {
        var stub = Arrange();
        stub.ReadException = new ApiRequestException("The API could not complete the request.", 500);

        var thread = RenderThread();

        Assert.Single(thread.FindAll(".alert-danger"));
        Assert.Empty(thread.FindAll("[data-testid=comments-empty]"));
    }

    [Fact]
    public void Post_RefetchesRatherThanAppending()
    {
        var stub = Arrange();

        // The two responses differ, so the assertion can tell a re-fetch from the
        // write's own response.
        stub.Reads.Add(Page([CommentOf("from the re-fetch")]));

        var thread = RenderThread();
        Assert.Equal(1, stub.GetCommentsCalls);

        thread.Find("#comment-body").Change("A new comment.");
        thread.Find("button.btn-primary").Click();

        Assert.Equal(2, stub.GetCommentsCalls);
        Assert.Contains("from the re-fetch", thread.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("the write's own response", thread.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Post_SendsTheBodyAndTheVisibility()
    {
        var stub = Arrange(isStaff: true);
        stub.Reads.Add(Page([]));

        var thread = RenderThread();
        thread.Find("#comment-body").Change("Using the last spare.");
        thread.Find("#comment-internal").Change(true);
        thread.Find("button.btn-primary").Click();

        var posted = Assert.Single(stub.Posts);
        Assert.Equal("Using the last spare.", posted.Body);
        Assert.True(posted.IsInternal);
    }

    [Fact]
    public void Post_RefusesAnEmptyBodyWithoutCallingTheApi()
    {
        var stub = Arrange();

        var thread = RenderThread();
        thread.Find("#comment-body").Change("   ");

        // Disabled, and clicking it anyway records nothing: a request that can only
        // return 400 is not worth sending.
        Assert.True(thread.Find("button.btn-primary").HasAttribute("disabled"));
        Assert.Empty(stub.Posts);
        Assert.Equal(1, stub.GetCommentsCalls);
    }

    [Fact]
    public void Toggle_IsHiddenForANonStaffUser()
    {
        Arrange(isStaff: false);

        var thread = RenderThread();

        Assert.Empty(thread.FindAll("#comment-internal"));
    }

    [Fact]
    public void Toggle_IsShownForStaff()
    {
        Arrange(isStaff: true);

        var thread = RenderThread();

        Assert.Single(thread.FindAll("#comment-internal"));
    }

    [Fact]
    public void Post_Forbidden_RendersAPermissionMessage()
    {
        var stub = Arrange();
        stub.PostException = new ApiRequestException("ignored", 403);

        var thread = RenderThread();
        thread.Find("#comment-body").Change("Let me in.");
        thread.Find("button.btn-primary").Click();

        Assert.Contains("do not have permission", thread.Markup, StringComparison.Ordinal);

        // Never the exception's own text, a stack trace, or a traceId.
        Assert.DoesNotContain("ignored", thread.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", thread.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiRequestException", thread.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Post_Conflict_RendersTheClosedMessageAndLeavesTheThreadUnchanged()
    {
        var stub = Arrange();
        stub.Reads[0] = Page([CommentOf("Already here.")]);
        stub.PostException = new ApiRequestException(
            "The request conflicts with the current state of the ticket.",
            409);

        var thread = RenderThread();
        thread.Find("#comment-body").Change("Too late.");
        thread.Find("button.btn-primary").Click();

        // A 409 already carries a useful message, so it is rendered as-is.
        Assert.Contains("conflicts with the current state", thread.Markup, StringComparison.Ordinal);
        Assert.Contains("Already here.", thread.Markup, StringComparison.Ordinal);
        Assert.Empty(stub.Posts.Skip(1));
    }

    [Fact]
    public void Post_NotFound_RendersTheGoneMessage()
    {
        var stub = Arrange();
        stub.PostException = new ApiRequestException("ignored", 404);

        var thread = RenderThread();
        thread.Find("#comment-body").Change("Anyone there?");
        thread.Find("button.btn-primary").Click();

        Assert.Contains("no longer exists", thread.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Post_Unauthorised_NavigatesToSignIn()
    {
        var stub = Arrange();
        stub.PostException = new ApiRequestException("ignored", 401);

        var thread = RenderThread();
        thread.Find("#comment-body").Change("Still here?");
        thread.Find("button.btn-primary").Click();

        var navigation = Services.GetRequiredService<BunitNavigationManager>();

        Assert.Contains("signin", navigation.Uri, StringComparison.Ordinal);
        Assert.Contains("returnUrl", navigation.Uri, StringComparison.Ordinal);

        // A 401 is a redirect, not an error state.
        Assert.Empty(thread.FindAll(".alert-danger"));
    }

    [Fact]
    public void Post_BadRequest_RendersTheValidationMessage()
    {
        var stub = Arrange();
        stub.PostException = new ApiRequestException("Comment body must not be empty.", 400);

        var thread = RenderThread();
        thread.Find("#comment-body").Change("A body the server dislikes.");
        thread.Find("button.btn-primary").Click();

        Assert.Contains("Comment body must not be empty.", thread.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Post_Failure_KeepsTheDraft()
    {
        // Losing what someone typed to a failed request is worse than a stale box.
        var stub = Arrange();
        stub.PostException = new ApiRequestException("ignored", 500);

        var thread = RenderThread();
        thread.Find("#comment-body").Change("Worth keeping.");
        thread.Find("button.btn-primary").Click();

        Assert.Contains("Worth keeping.", thread.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Paging_IsDrivenByWhatTheServerServed()
    {
        var stub = Arrange();

        // Ten more than one page holds, as the server reported it.
        stub.Reads[0] = Page([CommentOf("One of many.")], page: 1, pageSize: 2, total: 10);

        var thread = RenderThread();

        Assert.Contains("Older", thread.Markup, StringComparison.Ordinal);

        // Five pages of two, from the served page size - never a client-side constant.
        Assert.Contains("Page 1 of 5", thread.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Paging_IsAbsentWhenEverythingFitsOnOnePage()
    {
        var stub = Arrange();
        stub.Reads[0] = Page([CommentOf("The only one.")], page: 1, pageSize: 25, total: 1);

        var thread = RenderThread();

        Assert.DoesNotContain("Older", thread.Markup, StringComparison.Ordinal);
    }
}
