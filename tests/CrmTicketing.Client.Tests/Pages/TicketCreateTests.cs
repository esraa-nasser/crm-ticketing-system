using Bunit;
using Bunit.TestDoubles;
using CrmTicketing.Client.Pages;
using CrmTicketing.Client.Services;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.Extensions.DependencyInjection;

namespace CrmTicketing.Client.Tests.Pages;

public sealed class TicketCreateTests : BunitContext
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CreatedId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class StubTicketsApiClient : ITicketsApiClient
    {
        public List<CreateTicketRequest> Creates { get; } = [];

        public TicketMetadataResponse Metadata { get; set; } = new(
            Statuses: ["New", "Open"],
            Priorities: ["Low", "Normal", "High"],
            Transitions: new Dictionary<string, IReadOnlyList<string>>());

        public Exception? MetadataException { get; set; }

        public Exception? CreateException { get; set; }

        public Task<TicketResponse> CreateAsync(
            CreateTicketRequest request,
            CancellationToken cancellationToken)
        {
            Creates.Add(request);

            return CreateException is not null
                ? Task.FromException<TicketResponse>(CreateException)
                : Task.FromResult(new TicketResponse(
                    Id: CreatedId,
                    Title: request.Title,
                    Description: request.Description,
                    Status: "New",
                    Priority: request.Priority ?? "Normal",
                    Category: request.Category,
                    RequesterId: request.RequesterId,
                    AssigneeId: null,
                    CreatedAt: DateTimeOffset.UnixEpoch,
                    UpdatedAt: DateTimeOffset.UnixEpoch));
        }

        public Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken) =>
            MetadataException is not null
                ? Task.FromException<TicketMetadataResponse>(MetadataException)
                : Task.FromResult(Metadata);

        public Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(
            string? status,
            string? priority,
            int page,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("The create form never lists.");

        public Task<TicketResponse> GetTicketAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The create form never reads one ticket.");

        public Task<TicketResponse> UpdateAsync(
            Guid id,
            UpdateTicketRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> TransitionAsync(
            Guid id,
            TransitionTicketRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> AssignAsync(
            Guid id,
            AssignTicketRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private StubTicketsApiClient Arrange(bool signedIn = true)
    {
        var stub = new StubTicketsApiClient();
        var tokens = new TokenStore();

        if (signedIn)
        {
            tokens.Set("a-token", "agent@example.com", UserId, ["Agent"]);
        }

        Services.AddSingleton<ITicketsApiClient>(stub);
        Services.AddSingleton(tokens);
        Services.AddScoped<TicketMetadataProvider>();
        Services.GetRequiredService<BunitNavigationManager>().NavigateTo("http://localhost/tickets/new");

        return stub;
    }

    private static void Fill(IRenderedComponent<TicketCreate> page)
    {
        page.Find("#new-title").Change("A new ticket");
        page.Find("#new-description").Change("Something is broken.");
    }

    [Fact]
    public void Create_PostsWithoutARequesterField()
    {
        var stub = Arrange();
        var page = Render<TicketCreate>();

        // No input anywhere is bound to a requester: the id comes from the token store.
        Assert.DoesNotContain("requester", page.Markup, StringComparison.OrdinalIgnoreCase);

        Fill(page);
        page.Find("form").Submit();

        Assert.Equal(UserId, Assert.Single(stub.Creates).RequesterId);
    }

    [Fact]
    public void Create_RefusesWhenTheUserIdIsUnknown()
    {
        var stub = Arrange(signedIn: false);

        var page = Render<TicketCreate>();

        // The form is not even rendered; there is nothing to submit.
        Assert.Empty(page.FindAll("form"));
        Assert.Empty(stub.Creates);
        Assert.Contains("could not be identified", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PriorityOptionsComeFromMetadata()
    {
        var stub = Arrange();

        var options = Render<TicketCreate>().FindAll("#new-priority option");

        // Asserted against the stub's metadata, never against literal names.
        Assert.Equal(stub.Metadata.Priorities.Count, options.Count);
        foreach (var priority in stub.Metadata.Priorities)
        {
            Assert.Contains(options, o => o.TextContent.Trim() == priority);
        }
    }

    [Fact]
    public void Create_NavigatesToTheNewTicket()
    {
        Arrange();
        var page = Render<TicketCreate>();

        Fill(page);
        page.Find("form").Submit();

        var navigation = Services.GetRequiredService<BunitNavigationManager>();
        Assert.EndsWith($"tickets/{CreatedId}", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RendersTheValidationMessageOnA400()
    {
        var stub = Arrange();
        stub.CreateException = new ApiRequestException("Ticket title must not be empty.", 400);

        var page = Render<TicketCreate>();
        Fill(page);
        page.Find("form").Submit();

        Assert.Contains("Ticket title must not be empty.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_MetadataFailure_DisablesSubmit()
    {
        var stub = Arrange();
        stub.MetadataException = new ApiRequestException("ignored", 500);

        var page = Render<TicketCreate>();

        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Contains("priority list could not be loaded", page.Markup, StringComparison.Ordinal);
    }
}
