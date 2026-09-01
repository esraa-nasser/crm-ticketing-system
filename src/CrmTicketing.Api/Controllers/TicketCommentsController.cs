using CrmTicketing.Api.Infrastructure;
using CrmTicketing.Api.Mapping;
using CrmTicketing.Domain.Tickets;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmTicketing.Api.Controllers;

/// <summary>
/// The comment HTTP surface for one ticket.
/// </summary>
/// <remarks>
/// A separate controller from <see cref="TicketsController"/>: comments are a separate
/// aggregate with a separate repository, and the route is a genuine sub-resource.
/// Neither action carries a staff-only policy - a customer may read and write public
/// comments on their own ticket.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/tickets/{ticketId:guid}/comments")]
public sealed class TicketCommentsController(
    ITicketRepository tickets,
    ITicketCommentRepository comments,
    TimeProvider timeProvider)
    : ControllerBase
{
    /// <summary>Adds a comment to a ticket.</summary>
    [HttpPost]
    [ProducesResponseType<TicketCommentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketCommentResponse>> Create(
        Guid ticketId,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The ticket is loaded first because the caller's right to see it decides
        // everything else. A ticket they may not see comes back null, so this answers
        // 404 rather than 403 - a 403 would confirm the ticket exists.
        var ticket = await tickets
            .GetAsync(ticketId, User.Access(), cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return TicketNotFound();
        }

        // Before construction and before any write, so the stored comment is never
        // internal by accident. Deliberately not a silent downgrade to public: a
        // customer who ticked the box and got a public comment has been lied to.
        //
        // This precedes the closed-ticket check on purpose. A customer commenting
        // internally on a closed ticket is refused for the reason that is about them,
        // not the one that is about the ticket.
        if (request.IsInternal && !User.IsStaff())
        {
            return Forbid();
        }

        // Throws TicketClosedException, which DomainExceptionHandler turns into a 409.
        // The rule is the domain's; this controller never tests for a status.
        ticket.EnsureCanBeCommentedOn();

        // An invalid body throws ArgumentException, which the same handler turns into
        // a 400 carrying the parameter name.
        var comment = TicketComment.Write(
            // Version 7 is sequential, so new rows land at the end of the primary key
            // index instead of fragmenting it - and so the id tiebreaker used for
            // newest-first paging agrees with creation order.
            Guid.CreateVersion7(),
            ticketId,
            User.UserId(),
            request.Body,
            request.IsInternal,
            timeProvider.GetUtcNow());

        await comments.AddAsync(comment, cancellationToken).ConfigureAwait(false);
        await comments.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // No Location header: CreatedAtAction needs a route that reads one comment, and
        // there is none. Inventing GET /comments/{id} to satisfy the convention would
        // add a route nothing calls.
        return StatusCode(StatusCodes.Status201Created, TicketMapper.ToResponse(comment));
    }

    /// <summary>Returns a ticket's comments, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<TicketCommentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<TicketCommentResponse>>> List(
        Guid ticketId,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TicketCommentQuery.DefaultPageSize)
    {
        // 404, not an empty page. An empty thread for a ticket the caller cannot see is
        // still a confirmation that the ticket exists.
        var ticket = await tickets
            .GetAsync(ticketId, User.Access(), cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return TicketNotFound();
        }

        // Paging is clamped inside TicketCommentQuery, never here. Visibility
        // constrains both the page and the count inside the repository.
        var query = TicketCommentQuery.Create(ticketId, User.CommentVisibility(), page, pageSize);

        var items = await comments.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var total = await comments.CountAsync(query, cancellationToken).ConfigureAwait(false);

        return Ok(new PagedResponse<TicketCommentResponse>(
            Items: items.Select(TicketMapper.ToResponse).ToList(),
            Page: query.Page,
            PageSize: query.PageSize,
            TotalCount: total));
    }

    // Worded identically to TicketsController's, so the two cannot disagree about what
    // a missing ticket looks like to a caller.
    private ObjectResult TicketNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The ticket was not found.");
}
