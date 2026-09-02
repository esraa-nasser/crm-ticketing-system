using Bunit;
using Bunit.TestDoubles;
using CrmTicketing.Client.Pages;
using CrmTicketing.Client.Services;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Client.Tests.Pages;

/// <summary>
/// Component tests for the ticket detail view. The API is stubbed at
/// <see cref="ITicketsApiClient"/>, so these need no HTTP, no API, and no database.
/// </summary>
public sealed class TicketDetailTests : BunitContext
{
    private static readonly Guid TicketId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private sealed record WriteCall(string Kind, string? Payload);

    private sealed class StubTicketsApiClient : ITicketsApiClient
    {
        public List<WriteCall> Writes { get; } = [];

        public int GetTicketCalls { get; private set; }

        /// <summary>Returned by successive <c>GetTicketAsync</c> calls; the last repeats.</summary>
        public List<TicketResponse> Reads { get; } = [];

        public TicketMetadataResponse Metadata { get; set; } = new(
            Statuses: ["New", "Open", "Closed"],
            Priorities: ["Low", "Normal", "High"],
            Transitions: new Dictionary<string, IReadOnlyList<string>>
            {
                ["New"] = ["Open", "Closed"],
                ["Open"] = ["Pending", "Resolved", "Closed"],
                ["Closed"] = [],
            });

        public Exception? ReadException { get; set; }

        public Exception? WriteException { get; set; }

        public Exception? MetadataException { get; set; }

        public Task<TicketResponse> GetTicketAsync(Guid id, CancellationToken cancellationToken)
        {
            GetTicketCalls++;

            if (ReadException is not null)
            {
                return Task.FromException<TicketResponse>(ReadException);
            }

            // Successive reads return successive entries so a test can tell a
            // re-fetch apart from the write's own response.
            var index = Math.Min(GetTicketCalls - 1, Reads.Count - 1);
            return Task.FromResult(Reads[index]);
        }

        public Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken) =>
            MetadataException is not null
                ? Task.FromException<TicketMetadataResponse>(MetadataException)
                : Task.FromResult(Metadata);

        public Task<TicketResponse> TransitionAsync(
            Guid id,
            TransitionTicketRequest request,
            CancellationToken cancellationToken) =>
            Record("transition", request.Status);

        public Task<TicketResponse> AssignAsync(
            Guid id,
            AssignTicketRequest request,
            CancellationToken cancellationToken) =>
            Record("assign", request.AssigneeId?.ToString());

        public Task<TicketResponse> UpdateAsync(
            Guid id,
            UpdateTicketRequest request,
            CancellationToken cancellationToken) =>
            Record("update", request.Title);

        public Task<TicketResponse> CreateAsync(
            CreateTicketRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The detail view never creates.");

        public Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(
            string? status,
            string? priority,
            int page,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The detail view never lists.");

        /// <summary>
        /// The detail view embeds the comment thread, so every render of this page calls
        /// here. An empty page keeps these tests about the ticket; the thread has its own
        /// file.
        /// </summary>
        public Task<PagedResponse<TicketCommentResponse>> GetCommentsAsync(
            Guid ticketId,
            int page,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResponse<TicketCommentResponse>([], Page: 1, PageSize: 25, TotalCount: 0));

        public Task<TicketCommentResponse> AddCommentAsync(
            Guid ticketId,
            CreateCommentRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Posting is exercised in TicketCommentsTests.");

        private Task<TicketResponse> Record(string kind, string? payload)
        {
            Writes.Add(new WriteCall(kind, payload));

            return WriteException is not null
                ? Task.FromException<TicketResponse>(WriteException)
                : Task.FromResult(Reads[^1]);
        }
    }

    private static TicketResponse Ticket(
        string status = "Open",
        string priority = "Normal",
        string? category = "Billing",
        Guid? assigneeId = null,
        string title = "Printer offline in Meeting Room 3",
        string description = "Reported by reception, started this morning.") =>
        new(
            Id: TicketId,
            Title: title,
            Description: description,
            Status: status,
            Priority: priority,
            Category: category,
            RequesterId: UserId,
            AssigneeId: assigneeId,
            CreatedAt: Now,
            UpdatedAt: Now);

    private StubTicketsApiClient Arrange(
        string uri = "http://localhost/tickets/11111111-1111-1111-1111-111111111111",
        bool signedIn = true,
        bool isStaff = true)
    {
        var stub = new StubTicketsApiClient();
        stub.Reads.Add(Ticket());

        var tokens = new TokenStore();

        if (signedIn)
        {
            tokens.Set(
                "a-token",
                isStaff ? "agent@example.com" : "customer@example.com",
                UserId,
                isStaff ? ["Agent"] : ["Customer"],
                isStaff);
        }

        Services.AddSingleton<ITicketsApiClient>(stub);
        Services.AddSingleton(tokens);

        // Story 10. The default stays staff, so every test written before this story
        // keeps the behaviour it was written against.
        Services.AddSingleton(new Capabilities(tokens));

        Services.AddScoped<TicketMetadataProvider>();
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo(uri);

        return stub;
    }

    private IRenderedComponent<TicketDetail> RenderDetail() =>
        Render<TicketDetail>(p => p.Add(c => c.Id, TicketId));

    [Fact]
    public void Detail_RendersTheFullTicket()
    {
        var stub = Arrange();
        stub.Reads[0] = Ticket(description: "A description the list deliberately omits.");

        var markup = RenderDetail().Markup;

        Assert.Contains("Printer offline in Meeting Room 3", markup, StringComparison.Ordinal);
        // The description is the field the list omits; its presence is the point.
        Assert.Contains("A description the list deliberately omits.", markup, StringComparison.Ordinal);
        Assert.Contains("Billing", markup, StringComparison.Ordinal);
        Assert.Contains("Unassigned", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_RendersOnlyTheLegalTransitionsForTheCurrentStatus()
    {
        var stub = Arrange();
        stub.Reads[0] = Ticket(status: "Open");

        var page = RenderDetail();

        // Asserted against the stub's map, never against literal status names.
        var expected = stub.Metadata.Transitions["Open"];
        foreach (var target in expected)
        {
            Assert.Contains(page.FindAll("button"), b => b.TextContent.Trim() == target);
        }

        // Nothing the map does not list for this status.
        var notOffered = stub.Metadata.Transitions["New"].Except(expected, StringComparer.Ordinal);
        foreach (var absent in notOffered)
        {
            Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Trim() == absent);
        }
    }

    [Fact]
    public void Detail_ClosedTicketOffersNoTransitions()
    {
        var stub = Arrange();
        stub.Reads[0] = Ticket(status: "Closed");

        var page = RenderDetail();

        // Empty because the map says so, not because the page tests for a status.
        Assert.Empty(stub.Metadata.Transitions["Closed"]);
        foreach (var anyStatus in stub.Metadata.Statuses)
        {
            Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Trim() == anyStatus);
        }

        Assert.Contains("No further moves", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Transition_RefetchesRatherThanPatchingLocalState()
    {
        var stub = Arrange();
        stub.Reads.Add(Ticket(status: "Closed", title: "Refetched title"));

        var page = RenderDetail();
        Assert.Equal(1, stub.GetTicketCalls);

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Closed").Click();

        // A second read happened, and what is displayed came from it.
        Assert.Equal(2, stub.GetTicketCalls);
        Assert.Equal("transition", Assert.Single(stub.Writes).Kind);
        Assert.Contains("Refetched title", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Transition_Conflict_RendersTheMessageAndLeavesTheStatusUnchanged()
    {
        var stub = Arrange();
        stub.Reads[0] = Ticket(status: "Open");
        stub.WriteException = new ApiRequestException("A ticket cannot move from Open to Open.", 409);

        var page = RenderDetail();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Closed").Click();

        Assert.Contains("A ticket cannot move from Open to Open.", page.Markup, StringComparison.Ordinal);
        // The re-read returns the same ticket, so the displayed status is unchanged.
        Assert.Contains("Open", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Forbidden_RendersAPermissionMessage()
    {
        var stub = Arrange();
        var thrown = new ApiRequestException("ignored", 403);
        stub.WriteException = thrown;

        var page = RenderDetail();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Closed").Click();

        Assert.Contains("You do not have permission", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(ApiRequestException), page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(thrown.ToString(), page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Unauthorised_NavigatesToSignIn()
    {
        var stub = Arrange();
        stub.ReadException = new ApiRequestException("ignored", 401);

        RenderDetail();

        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        Assert.Contains("signin", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void NotFound_RendersDistinctlyFromFailed()
    {
        var stub = Arrange();
        stub.ReadException = new ApiRequestException("ignored", 404);

        var page = RenderDetail();

        Assert.Contains("no longer exists", page.Markup, StringComparison.Ordinal);
        // Distinct from the failed state: retry is not the remedy for a 404.
        Assert.Empty(page.FindAll(".alert-danger"));
        Assert.Single(page.FindAll(".alert-warning"));
    }

    [Fact]
    public void BackLink_PreservesFilterAndPage()
    {
        Arrange("http://localhost/tickets/11111111-1111-1111-1111-111111111111?status=Open&page=3");

        var href = RenderDetail().FindAll("a").First(a => a.TextContent.Contains("Back", StringComparison.Ordinal))
            .GetAttribute("href");

        Assert.NotNull(href);
        Assert.Contains("status=Open", href, StringComparison.Ordinal);
        Assert.Contains("page=3", href, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignToMe_SendsTheSignedInUsersId()
    {
        var stub = Arrange();

        var page = RenderDetail();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Assign to me").Click();

        var write = Assert.Single(stub.Writes);
        Assert.Equal("assign", write.Kind);
        Assert.Equal(UserId.ToString(), write.Payload);
    }

    [Fact]
    public void Unassign_SendsNull()
    {
        var stub = Arrange();
        // Unassign is only offered for a ticket assigned to the caller.
        stub.Reads[0] = Ticket(assigneeId: UserId);

        var page = RenderDetail();
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Unassign").Click();

        var write = Assert.Single(stub.Writes);
        Assert.Equal("assign", write.Kind);
        Assert.Null(write.Payload);
    }

    [Fact]
    public void NeitherAssignmentActionIsOfferedWhenTheUserIdIsUnknown()
    {
        Arrange(signedIn: false);

        var labels = RenderDetail().FindAll("button").Select(b => b.TextContent.Trim()).ToList();

        // Without a known user id, neither action can be built: "Assign to me" has no
        // id to send, and "Unassign" cannot know whether the ticket is the caller's.
        Assert.DoesNotContain("Assign to me", labels);
        Assert.DoesNotContain("Unassign", labels);
    }

    [Fact]
    public void MetadataFailure_StillRendersTheTicket()
    {
        var stub = Arrange();
        stub.MetadataException = new ApiRequestException("ignored", 500);

        var page = RenderDetail();

        // The ticket renders; only the transitions are unavailable.
        Assert.Contains("Printer offline in Meeting Room 3", page.Markup, StringComparison.Ordinal);
        Assert.Contains("No further moves", page.Markup, StringComparison.Ordinal);
    }

    // ---- Defects found in manual verification ----

    private static IReadOnlyList<string> ButtonLabels(IRenderedComponent<TicketDetail> page) =>
        [.. page.FindAll("button").Select(b => b.TextContent.Trim())];

    [Fact]
    public void AssignToMe_IsNotOfferedWhenTheTicketIsAlreadyMine()
    {
        var stub = Arrange();
        stub.Reads[0] = Ticket(assigneeId: UserId);

        var page = RenderDetail();

        // Offering it would let a click write the same assignee back, advancing
        // UpdatedAt and UpdatedBy for no change.
        Assert.DoesNotContain("Assign to me", ButtonLabels(page));
        Assert.Contains("Unassign", ButtonLabels(page));
    }

    [Fact]
    public void Unassign_IsNotOfferedWhenTheTicketIsUnassigned()
    {
        var stub = Arrange();
        stub.Reads[0] = Ticket(assigneeId: null);

        var page = RenderDetail();

        Assert.Contains("Assign to me", ButtonLabels(page));
        Assert.DoesNotContain("Unassign", ButtonLabels(page));
    }

    [Fact]
    public void AssignedToSomeoneElse_OffersOnlyTakingIt()
    {
        var stub = Arrange();
        stub.Reads[0] = Ticket(assigneeId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));

        var page = RenderDetail();

        // Assignment is self-service: an agent may take it, but clearing someone
        // else's assignment is not an action this screen offers.
        Assert.Contains("Assign to me", ButtonLabels(page));
        Assert.DoesNotContain("Unassign", ButtonLabels(page));
    }

    [Fact]
    public void Timestamps_RenderInLocalTimeNotRawUtc()
    {
        var stub = Arrange();
        // Deliberately not midnight UTC: a value whose local rendering differs from
        // its UTC one wherever the test happens to run.
        var created = new DateTimeOffset(2026, 9, 1, 13, 45, 0, TimeSpan.Zero);
        stub.Reads[0] = Ticket() with { CreatedAt = created, UpdatedAt = created };

        var markup = RenderDetail().Markup;

        // The raw round-trip form carries a "Z"; the rendered form must not be it.
        // This holds even on a UTC machine, where the two instants coincide but the
        // formats still differ.
        Assert.DoesNotContain(created.ToString("u"), markup, StringComparison.Ordinal);
        Assert.Contains(DisplayTime.Local(created), markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_RendersTheCommentsSection()
    {
        // The thread is a child component with its own tests; this asserts only that
        // the page hosts it. The stub returns an empty page, so the empty state is what
        // renders.
        Arrange();

        var page = RenderDetail();

        Assert.Contains("Comments", page.Markup, StringComparison.Ordinal);
        Assert.Single(page.FindAll("[data-testid=comments-empty]"));
    }

    // ---- Story 10: the role matrix. Same ticket, same stub, differing only by role ----

    [Fact]
    public void Detail_AsACustomer_ShowsNoAssignmentSection()
    {
        Arrange(isStaff: false);

        var page = RenderDetail();

        Assert.DoesNotContain("Assign to me", ButtonLabels(page));
        Assert.DoesNotContain("Unassign", ButtonLabels(page));

        // The fallback span renders whenever both buttons are hidden, so gating only
        // the buttons would leave a Customer reading about an assignment workflow they
        // have no part in - and which is false whenever the ticket is unassigned.
        Assert.DoesNotContain("Assigned to someone else.", page.Markup, StringComparison.Ordinal);

        // The heading goes with it. A heading over an empty div is not an explanation.
        // Asserted on the test id rather than the word "Assignment": a substring match
        // on a common noun would fail for the wrong reason the day some other section
        // uses it.
        Assert.Empty(page.FindAll("[data-testid=assignment-section]"));
    }

    [Fact]
    public void Detail_AsStaff_ShowsTheAssignmentSection()
    {
        // The mirror of the test above. Without it, that one passes for a page that
        // renders no assignment section to anybody.
        Arrange(isStaff: true);

        var page = RenderDetail();

        Assert.Single(page.FindAll("[data-testid=assignment-section]"));
        Assert.Contains("Assign to me", ButtonLabels(page));
    }

    [Fact]
    public void Detail_AsACustomer_StillShowsTransitions()
    {
        // Finding 1 made visible. RequesterAllowedTransitions has no endpoint, so the
        // client cannot learn it, and gating these buttons would mean a second copy of
        // an authorisation rule. A Customer still sees moves the API may refuse with
        // 403, and story 08's message rendering is what makes that legible.
        //
        // If #51 lands and a later story gates these, this is the test that should
        // fail and be deliberately changed.
        var stub = Arrange(isStaff: false);
        stub.Reads[0] = Ticket(status: "Open");

        var page = RenderDetail();
        var expected = stub.Metadata.Transitions["Open"];

        // Without this the foreach below iterates zero times over an empty map and the
        // test passes having asserted nothing - confirmed by mutation: emptying
        // Transitions["Open"] left this test green.
        Assert.NotEmpty(expected);

        foreach (var target in expected)
        {
            Assert.Contains(ButtonLabels(page), label => label == target);
        }
    }

    [Fact]
    public void Detail_AsACustomer_StillShowsTheEditForm()
    {
        // PATCH /api/tickets/{id} carries no staff policy, so a requester editing
        // their own ticket is a rule the API permits. Hiding the form would be a
        // client-only gate with no API counterpart.
        Arrange(isStaff: false);

        var page = RenderDetail();

        Assert.Single(page.FindAll("form"));
        Assert.NotNull(page.Find("#edit-title"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClosedTicket_OffersNoTransitions_ForEitherRole(bool isStaff)
    {
        // Role decides whether a control exists; state decides whether it is offered.
        // A Closed ticket offers nothing because Transitions["Closed"] is empty, and
        // story 10 did not convert that into a role check.
        var stub = Arrange(isStaff: isStaff);
        stub.Reads[0] = Ticket(status: "Closed");

        var page = RenderDetail();

        Assert.Empty(stub.Metadata.Transitions["Closed"]);
        Assert.Contains("No further moves", page.Markup, StringComparison.Ordinal);
    }
}
