using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using CrmTicketing.Client.Pages;
using CrmTicketing.Client.Services;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Client.Tests.Pages;

/// <summary>
/// Component tests for the ticket list. The API is stubbed at
/// <see cref="ITicketsApiClient"/>, so these need no HTTP, no API, and no database.
/// </summary>
public sealed class TicketsTests : BunitContext
{
    private sealed record Call(string? Status, string? Priority, int Page);

    private sealed class StubTicketsApiClient : ITicketsApiClient
    {
        public List<Call> Calls { get; } = [];

        public PagedResponse<TicketSummaryResponse> Result { get; set; } =
            new([], Page: 1, PageSize: 25, TotalCount: 0);

        public TicketMetadataResponse Metadata { get; set; } = new(
            Statuses: ["New", "Open", "Pending"],
            Priorities: ["Low", "Normal"],
            Transitions: new Dictionary<string, IReadOnlyList<string>>());

        public Exception? ListException { get; set; }

        public Exception? MetadataException { get; set; }

        public Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(
            string? status,
            string? priority,
            int page,
            CancellationToken cancellationToken)
        {
            Calls.Add(new Call(status, priority, page));

            return ListException is not null
                ? Task.FromException<PagedResponse<TicketSummaryResponse>>(ListException)
                : Task.FromResult(Result);
        }

        public Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken) =>
            MetadataException is not null
                ? Task.FromException<TicketMetadataResponse>(MetadataException)
                : Task.FromResult(Metadata);

        // This class tests the list page, which performs no writes. Throwing rather
        // than returning a default makes an accidental call visible.
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

    private static TicketSummaryResponse Ticket(
        string title,
        string status = "Open",
        string priority = "High",
        string? category = "Billing",
        Guid? assigneeId = null) =>
        new(
            Id: Guid.NewGuid(),
            Title: title,
            Status: status,
            Priority: priority,
            Category: category,
            RequesterId: Guid.NewGuid(),
            AssigneeId: assigneeId,
            CreatedAt: new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt: new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    private StubTicketsApiClient Arrange(string uri = "http://localhost/tickets")
    {
        var stub = new StubTicketsApiClient();
        Services.AddSingleton<ITicketsApiClient>(stub);
        Services.AddScoped<TicketMetadataProvider>();
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo(uri);

        return stub;
    }

    [Fact]
    public void Rows_RenderFromTheClient()
    {
        var stub = Arrange();
        stub.Result = new(
            [Ticket("Printer is on fire"), Ticket("Second ticket"), Ticket("Third ticket")],
            Page: 1,
            PageSize: 25,
            TotalCount: 3);

        var page = Render<Tickets>();

        Assert.Equal(3, page.FindAll("tbody tr").Count);
        var markup = page.Markup;
        Assert.Contains("Printer is on fire", markup, StringComparison.Ordinal);
        Assert.Contains("Open", markup, StringComparison.Ordinal);
        Assert.Contains("High", markup, StringComparison.Ordinal);
        Assert.Contains("Billing", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_RenderUnassignedAndAnEmDashForNulls()
    {
        var stub = Arrange();
        var assignee = Guid.Parse("22222222-2222-2222-2222-222222222222");
        stub.Result = new(
            [Ticket("No category", category: null), Ticket("Assigned", assigneeId: assignee)],
            Page: 1,
            PageSize: 25,
            TotalCount: 2);

        var page = Render<Tickets>();

        Assert.Contains("Unassigned", page.Markup, StringComparison.Ordinal);
        Assert.Contains("—", page.Markup, StringComparison.Ordinal);
        Assert.Contains("22222222", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyState_RendersWhenThePageHasNoItems()
    {
        var stub = Arrange();
        stub.Result = new([], Page: 1, PageSize: 25, TotalCount: 0);

        var page = Render<Tickets>();

        Assert.Single(page.FindAll(".alert-secondary"));
        Assert.Empty(page.FindAll(".alert-danger"));
        Assert.Empty(page.FindAll("table"));
    }

    [Fact]
    public void ErrorState_RendersWhenTheClientThrows()
    {
        var stub = Arrange();
        stub.ListException = new ApiRequestException("The request was not valid.", 400);

        var page = Render<Tickets>();

        Assert.Single(page.FindAll(".alert-danger"));
        Assert.Empty(page.FindAll(".alert-secondary"));
        Assert.Empty(page.FindAll("table"));
        Assert.Contains("The request was not valid.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorState_DoesNotLeakDiagnostics()
    {
        var stub = Arrange();
        var thrown = new ApiRequestException("'Frozen' is not a recognised value.", 400);
        stub.ListException = thrown;

        var page = Render<Tickets>();
        var markup = page.Markup;

        Assert.Contains("'Frozen' is not a recognised value.", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("traceId", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(ApiRequestException), markup, StringComparison.Ordinal);
        Assert.DoesNotContain(thrown.ToString(), markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterControls_RenderTheOptionsFromMetadata()
    {
        var stub = Arrange();

        var page = Render<Tickets>();

        // Asserted against the stub's metadata, never against literal names.
        var statusOptions = page.FindAll("#status-filter option");
        Assert.Equal(stub.Metadata.Statuses.Count + 1, statusOptions.Count);
        foreach (var status in stub.Metadata.Statuses)
        {
            Assert.Contains(statusOptions, o => o.TextContent.Trim() == status);
        }

        var priorityOptions = page.FindAll("#priority-filter option");
        Assert.Equal(stub.Metadata.Priorities.Count + 1, priorityOptions.Count);
        foreach (var priority in stub.Metadata.Priorities)
        {
            Assert.Contains(priorityOptions, o => o.TextContent.Trim() == priority);
        }
    }

    [Fact]
    public void ChangingAFilter_IssuesARequestCarryingThatFilter()
    {
        var stub = Arrange("http://localhost/tickets?page=3");
        stub.Result = new([Ticket("A ticket")], Page: 3, PageSize: 25, TotalCount: 100);
        var page = Render<Tickets>();
        var chosen = stub.Metadata.Statuses[1];

        page.Find("#status-filter").Change(chosen);

        var last = stub.Calls[^1];
        Assert.Equal(chosen, last.Status);
        // Changing a filter resets paging; page 3 of a narrower filter may not exist.
        Assert.Equal(1, last.Page);
    }

    [Fact]
    public void Paging_NextRequestsTheFollowingPage()
    {
        var stub = Arrange();
        stub.Result = new([Ticket("A ticket")], Page: 1, PageSize: 25, TotalCount: 60);
        var page = Render<Tickets>();

        var buttons = page.FindAll("button");
        var previous = buttons.Single(b => b.TextContent.Contains("Previous", StringComparison.Ordinal));
        var next = buttons.Single(b => b.TextContent.Contains("Next", StringComparison.Ordinal));

        Assert.True(previous.HasAttribute("disabled"));
        Assert.False(next.HasAttribute("disabled"));

        next.Click();

        Assert.Equal(2, stub.Calls[^1].Page);
    }

    [Fact]
    public void Paging_NextIsDisabledOnTheLastPage()
    {
        var stub = Arrange();
        stub.Result = new([Ticket("A ticket")], Page: 1, PageSize: 25, TotalCount: 10);

        var page = Render<Tickets>();

        var next = page.FindAll("button")
            .Single(b => b.TextContent.Contains("Next", StringComparison.Ordinal));
        Assert.True(next.HasAttribute("disabled"));
    }

    [Fact]
    public void Paging_ShowsTheServedPageNotTheRequestedOne()
    {
        var stub = Arrange("http://localhost/tickets?page=0");
        // The API clamps page 0 to 1 and says so in the response.
        stub.Result = new([Ticket("A ticket")], Page: 1, PageSize: 25, TotalCount: 1);

        var page = Render<Tickets>();

        Assert.Contains("Showing 1–1 of 1", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PagePastTheEnd_RendersEmptyNotError()
    {
        var stub = Arrange("http://localhost/tickets?page=99");
        stub.Result = new([], Page: 99, PageSize: 25, TotalCount: 60);

        var page = Render<Tickets>();

        Assert.Single(page.FindAll(".alert-secondary"));
        Assert.Empty(page.FindAll(".alert-danger"));
    }

    [Fact]
    public void MetadataFailure_StillRendersRows()
    {
        var stub = Arrange();
        stub.MetadataException = new ApiRequestException("Metadata is unavailable.", 500);
        stub.Result = new([Ticket("A ticket")], Page: 1, PageSize: 25, TotalCount: 1);

        var page = Render<Tickets>();

        Assert.Single(page.FindAll("tbody tr"));
        Assert.Empty(page.FindAll(".alert-danger"));
        Assert.True(page.Find("#status-filter").HasAttribute("disabled"));
        Assert.True(page.Find("#priority-filter").HasAttribute("disabled"));
    }

    [Fact]
    public void QueryStringFilters_AreSentOnTheFirstLoad()
    {
        var stub = Arrange("http://localhost/tickets?status=Pending&priority=Low&page=2");
        stub.Result = new([Ticket("A ticket")], Page: 2, PageSize: 25, TotalCount: 60);

        Render<Tickets>();

        var call = stub.Calls[0];
        Assert.Equal("Pending", call.Status);
        Assert.Equal("Low", call.Priority);
        Assert.Equal(2, call.Page);
    }

    [Fact]
    public void Load_IsNotRepeatedWhenParametersAreUnchanged()
    {
        var stub = Arrange();
        stub.Result = new([Ticket("A ticket")], Page: 1, PageSize: 25, TotalCount: 1);
        var page = Render<Tickets>();

        page.Render();
        page.Render();

        Assert.Single(stub.Calls);
    }
}
