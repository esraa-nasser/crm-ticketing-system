using CrmTicketing.Api.Mapping;
using CrmTicketing.Domain.Tickets;
using CrmTicketing.Shared.Contracts.Tickets;
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
[Route("api/tickets")]
public sealed class TicketsController(ITicketRepository repository, TimeProvider timeProvider)
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

        var now = timeProvider.GetUtcNow();

        var ticket = Ticket.Open(
            // Version 7 is sequential, so new rows land at the end of the primary
            // key index instead of fragmenting it.
            Guid.CreateVersion7(),
            TicketTitle.Create(request.Title),
            request.Description,
            request.RequesterId,
            now,
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
        var ticket = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);

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

        // Paging is clamped inside TicketQuery, never here.
        var query = TicketQuery.Create(
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

        var ticket = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);

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

        // The title is built before any mutator runs, so an invalid one cannot
        // leave the ticket half-updated.
        ticket.UpdateDetails(
            TicketTitle.Create(request.Title),
            request.Description,
            request.Category,
            now);

        if (priority is { } newPriority)
        {
            ticket.ChangePriority(newPriority, now);
        }

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(TicketMapper.ToResponse(ticket));
    }

    /// <summary>Moves a ticket to another status.</summary>
    [HttpPost("{id:guid}/status")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> Transition(
        Guid id,
        TransitionTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (ticket is null)
        {
            return TicketNotFound();
        }

        if (!TicketMapper.TryParseStatus(request.Status, out var target))
        {
            return UnknownEnumValue(nameof(request.Status), request.Status);
        }

        // An illegal move throws InvalidTicketTransitionException, which
        // DomainExceptionHandler turns into a 409. The transition table is never
        // consulted here.
        ticket.TransitionTo(target, timeProvider.GetUtcNow());

        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(TicketMapper.ToResponse(ticket));
    }

    /// <summary>Assigns a ticket, or unassigns it when the id is null.</summary>
    [HttpPost("{id:guid}/assignee")]
    [ProducesResponseType<TicketResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketResponse>> Assign(
        Guid id,
        AssignTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ticket = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (ticket is null)
        {
            return TicketNotFound();
        }

        var now = timeProvider.GetUtcNow();

        if (request.AssigneeId is { } assigneeId)
        {
            ticket.Assign(assigneeId, now);
        }
        else
        {
            ticket.Unassign(now);
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

    private ObjectResult TicketNotFound() => Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "The ticket was not found.");

    private ActionResult UnknownEnumValue(string field, string value)
    {
        ModelState.AddModelError(field, $"'{value}' is not a recognised value.");

        return ValidationProblem(ModelState);
    }
}
