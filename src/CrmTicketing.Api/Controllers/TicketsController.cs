using CrmTicketing.Api.Configuration;
using CrmTicketing.Api.Infrastructure;
using CrmTicketing.Api.Mapping;
using CrmTicketing.Domain.Tickets;
using CrmTicketing.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrmTicketing.Api.Controllers;

/// <summary>
/// The ticket HTTP surface. Every rule it enforces belongs to the domain; this
/// class parses, delegates, and maps.
/// </summary>
/// <remarks>
/// The route is written out rather than using <c>[controller]</c> so renaming the
/// class cannot move the endpoints.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/tickets")]
public sealed class TicketsController(
    ITicketRepository repository,
    IUserDirectory userDirectory,
    TimeProvider timeProvider)
    : ControllerBase
{
    /// <summary>Opens a new ticket.</summary>
    [HttpPost]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketResponse>> Create(
        CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var priority = TicketPriority.Normal;

        if (request.Priority is not null && !TicketMapper.TryParsePriority(request.Priority, out priority))
        {
            return UnknownEnumValue(nameof(request.Priority), request.Priority);
        }

        var actorId = User.UserId();

        // A Customer always raises tickets in their own name. Taking the id from the
        // body would let them create a ticket they immediately cannot see, which
        // reads as data loss.
        var requesterId = User.IsStaff() ? request.RequesterId : actorId;

        // requester_id carries a foreign key, so an id referring to nobody makes the
        // insert fail as a 500. Check first and answer 400. Only staff can reach this
        // with an arbitrary id.
        if (requesterId != actorId
            && !await userDirectory.ExistsAsync(requesterId, cancellationToken).ConfigureAwait(false))
        {
            ModelState.AddModelError(
                nameof(request.RequesterId),
                "No user exists with that id.");

            return ValidationProblem(ModelState);
        }

        var now = timeProvider.GetUtcNow();

        var ticket = Ticket.Open(
            // Version 7 is sequential, so new rows land at the end of the primary
            // key index instead of fragmenting it.
            Guid.CreateVersion7(),
            TicketTitle.Create(request.Title),
            request.Description,
            requesterId,
            now,
            actorId,
            priority,
            request.Category);

        await repository.AddAsync(ticket, cancellationToken).ConfigureAwait(false);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return CreatedAtAction(
            nameof(GetById),
            new { id = ticket.Id },
            TicketMapper.ToResponse(ticket));
    }

    /// <summary>Returns one ticket in full.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        // A ticket the caller may not see comes back null, so this answers 404 rather
        // than 403 — a 403 would confirm the ticket exists.
        var ticket = await repository
            .GetAsync(id, User.Access(), cancellationToken)
            .ConfigureAwait(false);

        return ticket is null ? TicketNotFound() : Ok(TicketMapper.ToResponse(ticket));
    }

    /// <summary>Returns a filtered, paged list of tickets.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<TicketSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<TicketSummaryResponse>>> List(
        CancellationToken cancellationToken,
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] Guid? assigneeId = null,
        [FromQuery] Guid? requesterId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TicketQuery.DefaultPageSize)
    {
        TicketStatus? parsedStatus = null;
        TicketPriority? parsedPriority = null;

        if (status is not null)
        {
            if (!TicketMapper.TryParseStatus(status, out var value))
            {
                return UnknownEnumValue(nameof(status), status);
            }

            parsedStatus = value;
        }

        if (priority is not null)
        {
            if (!TicketMapper.TryParsePriority(priority, out var value))
            {
                return UnknownEnumValue(nameof(priority), priority);
            }

            parsedPriority = value;
        }

        // Paging is clamped inside TicketQuery, never here. Access constrains both
        // the page and the count inside the repository.
        var query = TicketQuery.Create(
            User.Access(),
            parsedStatus,
            parsedPriority,
            assigneeId,
            requesterId,
            page,
            pageSize);

        var tickets = await repository.ListAsync(query, cancellationToken).ConfigureAwait(false);
        var total = await repository.CountAsync(query, cancellationToken).ConfigureAwait(false);

        return Ok(new PagedResponse<TicketSummaryResponse>(
            Items: tickets.Select(TicketMapper.ToSummary).ToList(),
            Page: query.Page,
            PageSize: query.PageSize,
            TotalCount: total));
    }

    /// <summary>Corrects a ticket's descriptive fields.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketResponse>> Update(
        Guid id,
        UpdateTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await repository
            .GetAsync(id, User.Access(), cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return TicketNotFound();
        }

        TicketPriority? priority = null;

        if (request.Priority is not null)
        {
            if (!TicketMapper.TryParsePriority(request.Priority, out var value))
            {
                return UnknownEnumValue(nameof(request.Priority), request.Priority);
            }

            priority = value;
        }

        var now = timeProvider.GetUtcNow();
        var actorId = User.UserId();

        // The title is built before any mutator runs, so an invalid one cannot
        // leave the ticket half-updated.
        ticket.UpdateDetails(
            TicketTitle.Create(request.Title),
            request.Description,
            request.Category,
            now,
            actorId);

        if (priority is { } newPriority)
        {
            ticket.ChangePriority(newPriority, now, actorId);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(TicketMapper.ToResponse(ticket));
    }

    /// <summary>Moves a ticket to another status.</summary>
    [HttpPost("{id:guid}/status")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> Transition(
        Guid id,
        TransitionTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await repository
            .GetAsync(id, User.Access(), cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return TicketNotFound();
        }

        if (!TicketMapper.TryParseStatus(request.Status, out var target))
        {
            return UnknownEnumValue(nameof(request.Status), request.Status);
        }

        // 403, not 409: the move is legal in the workflow, this caller is simply not
        // permitted to make it. An authorisation rule at the boundary — the
        // transition table stays the single declaration of what is legal for anyone.
        if (!User.IsStaff() && !RequesterAllowedTransitions.Contains((ticket.Status, target)))
        {
            return Forbid();
        }

        // An illegal move throws InvalidTicketTransitionException, which
        // DomainExceptionHandler turns into a 409. The transition table is never
        // consulted here.
        ticket.TransitionTo(target, timeProvider.GetUtcNow(), User.UserId());

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(TicketMapper.ToResponse(ticket));
    }

    /// <summary>Assigns a ticket, or unassigns it when the id is null.</summary>
    [HttpPost("{id:guid}/assignee")]
    [Authorize(Policy = AuthorizationPolicies.StaffOnly)]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> Assign(
        Guid id,
        AssignTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await repository
            .GetAsync(id, User.Access(), cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return TicketNotFound();
        }

        var now = timeProvider.GetUtcNow();
        var actorId = User.UserId();

        if (request.AssigneeId is { } assigneeId)
        {
            ticket.Assign(assigneeId, now, actorId);
        }
        else
        {
            ticket.Unassign(now, actorId);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(TicketMapper.ToResponse(ticket));
    }

    /// <summary>
    /// Publishes the workflow vocabulary so a client never hardcodes it.
    /// </summary>
    [HttpGet("metadata")]
    [ProducesResponseType<TicketMetadataResponse>(StatusCodes.Status200OK)]
    public ActionResult<TicketMetadataResponse> GetMetadata() => Ok(new TicketMetadataResponse(
        Statuses: Enum.GetNames<TicketStatus>(),
        Priorities: Enum.GetNames<TicketPriority>(),
        // Sourced from the domain table, never from a literal. The cast is
        // required because IReadOnlyDictionary<,> is not covariant in its value.
        Transitions: Enum.GetValues<TicketStatus>().ToDictionary(
            status => status.ToString(),
            status => (IReadOnlyList<string>)TicketStatusTransitions.AllowedFrom(status)
                .Select(target => target.ToString())
                .ToList())));

    /// <summary>
    /// The (from, to) moves a requester may make on their own ticket: withdraw it
    /// from any live status, or reject a resolution by reopening it.
    /// </summary>
    /// <remarks>
    /// Source-aware, and the distinction is load-bearing. A target-only set of
    /// { Closed, Open } would permit New to Open, letting a requester mark their own
    /// untriaged ticket as being worked. Open means an agent has picked it up; a
    /// customer setting it asserts staff activity that is not happening, and anything
    /// later keyed off the move into Open inherits that false signal. The only
    /// legitimate route to Open for a requester is rejecting a resolution.
    ///
    /// An authorisation rule at the API boundary, deliberately not a second
    /// transition table: TicketStatusTransitions stays the one declaration of what is
    /// legal for anyone.
    /// </remarks>
    private static readonly HashSet<(TicketStatus From, TicketStatus To)> RequesterAllowedTransitions =
    [
        (TicketStatus.New, TicketStatus.Closed),
        (TicketStatus.Open, TicketStatus.Closed),
        (TicketStatus.Pending, TicketStatus.Closed),
        (TicketStatus.Resolved, TicketStatus.Closed),
        (TicketStatus.Resolved, TicketStatus.Open),
    ];

    private ObjectResult TicketNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The ticket was not found.");

    private ActionResult UnknownEnumValue(string field, string value)
    {
        ModelState.AddModelError(field, $"'{value}' is not a recognised value.");

        return ValidationProblem(ModelState);
    }
}
