using CrmTicketing.Shared.Contracts.Tickets;

namespace CrmTicketing.Client.Services;

/// <summary>
/// Read access to the ticket endpoints.
/// </summary>
/// <remarks>
/// This interface exists because components are tested with a stub and the repo's
/// convention is sealed-by-default, so a subclassed double is not available. It has
/// two implementations on day one — <see cref="TicketsApiClient"/> and the test stub
/// — which is not the speculative abstraction docs/constitution.md §VII bans.
/// <see cref="SystemApiClient"/> is deliberately left without one.
/// </remarks>
public interface ITicketsApiClient
{
    Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(
        string? status,
        string? priority,
        int page,
        CancellationToken cancellationToken);

    Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken);

    Task<TicketResponse> GetTicketAsync(Guid id, CancellationToken cancellationToken);

    Task<TicketResponse> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken);

    Task<TicketResponse> UpdateAsync(
        Guid id,
        UpdateTicketRequest request,
        CancellationToken cancellationToken);

    Task<TicketResponse> TransitionAsync(
        Guid id,
        TransitionTicketRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Assigns the ticket, or unassigns it when
    /// <see cref="AssignTicketRequest.AssigneeId"/> is null. One route serves both.
    /// </summary>
    Task<TicketResponse> AssignAsync(
        Guid id,
        AssignTicketRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// One page of a ticket's comments, newest first. What the caller receives is
    /// already visibility-filtered by the API; the client does no filtering of its own.
    /// </summary>
    Task<PagedResponse<TicketCommentResponse>> GetCommentsAsync(
        Guid ticketId,
        int page,
        CancellationToken cancellationToken);

    Task<TicketCommentResponse> AddCommentAsync(
        Guid ticketId,
        CreateCommentRequest request,
        CancellationToken cancellationToken);
}
