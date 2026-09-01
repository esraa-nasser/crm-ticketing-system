using CrmTicketing.Client.Services;
using CrmTicketing.Shared.Contracts.Tickets;

namespace CrmTicketing.Client.Tests.Services;

public sealed class TicketMetadataProviderTests
{
    private sealed class CountingClient(TicketMetadataResponse? metadata) : ITicketsApiClient
    {
        public int MetadataCalls { get; private set; }

        public bool FailNextCall { get; set; }

        public Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(
            string? status,
            string? priority,
            int page,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test never lists tickets.");

        public Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken)
        {
            MetadataCalls++;

            if (FailNextCall)
            {
                FailNextCall = false;
                return Task.FromException<TicketMetadataResponse>(
                    new ApiRequestException("The API could not complete the request.", 500));
            }

            return Task.FromResult(metadata!);
        }

        // This class tests the metadata provider, which performs no writes. Throwing
        // rather than returning a default makes an accidental call visible.
        public Task<TicketResponse> GetTicketAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketResponse> CreateAsync(
            CreateTicketRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

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

        public Task<PagedResponse<TicketCommentResponse>> GetCommentsAsync(
            Guid ticketId,
            int page,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TicketCommentResponse> AddCommentAsync(
            Guid ticketId,
            CreateCommentRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static TicketMetadataResponse Metadata() => new(
        Statuses: ["New", "Open"],
        Priorities: ["Low", "Normal"],
        Transitions: new Dictionary<string, IReadOnlyList<string>> { ["New"] = ["Open"], ["Open"] = [] });

    [Fact]
    public async Task GetAsync_FetchesOnceAcrossMultipleCalls()
    {
        var client = new CountingClient(Metadata());
        var provider = new TicketMetadataProvider(client);

        await provider.GetAsync();
        await provider.GetAsync();
        await provider.GetAsync();

        Assert.Equal(1, client.MetadataCalls);
    }

    [Fact]
    public async Task GetAsync_RetriesAfterAFailure()
    {
        var client = new CountingClient(Metadata()) { FailNextCall = true };
        var provider = new TicketMetadataProvider(client);

        await Assert.ThrowsAsync<ApiRequestException>(() => provider.GetAsync());

        // A cached faulted task would replay the failure forever.
        var metadata = await provider.GetAsync();

        Assert.Equal(2, client.MetadataCalls);
        Assert.Equal(2, metadata.Statuses.Count);
    }
}
