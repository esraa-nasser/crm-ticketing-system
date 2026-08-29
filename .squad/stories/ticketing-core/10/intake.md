# Story intake

- Folder: `.squad/stories/ticketing-core/10/intake.md`

---

## Feature

- **Feature name (display):** Ticketing core — HTTP surface
- **Feature slug (folder under `plans/`):** `ticketing-core`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `10`
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:api`, `sdd:needs-plan`

---

## Title

```
Ticket CRUD endpoints with Shared contracts
```

---

## Description

```
Make the Ticket aggregate reachable over HTTP. This is the first story that
crosses from the domain into the API, and the first place TicketStatusTransitions
is consulted from outside CrmTicketing.Domain.

It also answers the question story 02 deliberately deferred: how the API reaches
persistence. Story 02 registered CrmDbContext behind the AddPersistence seam
because no caller existed and an interface with no consumer would have violated
constitution section VII. A caller exists now, so the abstraction gets decided
here - with a real consumer to shape it.

The story delivers the ticket endpoints and the contracts they exchange. Comments
and the activity timeline are issue #11. No UI is touched.
```

---

## Acceptance criteria

```
- [ ] Contracts live in src/CrmTicketing.Shared/Contracts/Tickets/ as sealed
      records. CrmTicketing.Shared still declares zero project references - it
      must NOT reference CrmTicketing.Domain.
      Verify: grep -c ProjectReference src/CrmTicketing.Shared/CrmTicketing.Shared.csproj -> 0
- [ ] Status and priority cross the wire as strings, never as domain enums.
      Unknown or misspelled values produce 400, not 500.
- [ ] ITicketRepository is declared in CrmTicketing.Domain and implemented in
      CrmTicketing.Infrastructure. No controller names CrmDbContext.
      Verify: grep -rn "CrmDbContext\|EntityFrameworkCore\|Npgsql" src/CrmTicketing.Api/ -> no output
- [ ] Endpoints implemented, all returning RFC 9457 problem details on failure:
        POST   /api/tickets                  201 + Location header
        GET    /api/tickets/{id}             200 | 404
        GET    /api/tickets                  200, paged and filterable
        PATCH  /api/tickets/{id}             200 | 404 | 400
        POST   /api/tickets/{id}/status      200 | 404 | 409
        POST   /api/tickets/{id}/assignee    200 | 404 | 400 | 409
        GET    /api/tickets/metadata         200
- [ ] An illegal status transition returns 409 Conflict carrying the attempted
      from and to values - not 400, not 500.
- [ ] GET /api/tickets supports filtering by status, priority, assignee, and
      requester, plus page and pageSize. pageSize is capped at 100; a larger
      request is clamped, not rejected. The response reports total count.
- [ ] GET /api/tickets/metadata returns the allowed statuses, allowed priorities,
      and the legal transition map, sourced from TicketStatusTransitions. The
      client must never hardcode these.
- [ ] Timestamps come from the injected TimeProvider at the API boundary and are
      passed into domain mutators. grep -rn "DateTime.UtcNow\|DateTime.Now" src/
      still returns nothing.
- [ ] Controller tests cover: create returns 201 with a Location header, get by
      unknown id returns 404, an illegal transition returns 409, an unknown
      status string returns 400, and pageSize=500 is clamped to 100.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no database running.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #8 and #9 (merged). Closes #10. Unblocks #11 (comments), #12–#14 (UI), #16 (permissions).
- **Depends on code areas or other stories:** `CrmTicketing.Domain.Tickets.*`, `AddPersistence` in `CrmTicketing.Infrastructure.DependencyInjection`, `TimeProvider` registered in `Program.cs`.

## Extra notes

- This story resolves the open question carried in `.squad/plans/persistence/00-overview.md` about a domain-declared abstraction. Update that note when it lands.

## Technical hints

```
DECISIONS MADE

Enums on the wire: strings, not enum types. CrmTicketing.Shared must not
reference CrmTicketing.Domain (constitution section II), so a contract cannot
name TicketStatus. Duplicating the enum into Shared invites drift. Strings are
already the persistence representation, and they are tolerant for API consumers.
Parse at the API boundary; an unparseable value is a 400.

Metadata endpoint: GET /api/tickets/metadata returns the statuses, the
priorities, and the transition map, all derived from TicketStatusTransitions.
This is what keeps the transition table single-sourced once a UI exists - the
kanban board (#14) renders legal moves from this, never from a hardcoded list.

Persistence abstraction: ONE interface, ITicketRepository, declared in
src/CrmTicketing.Domain/Tickets/. Framework-free - no EF types, no
IQueryable in the signature.

  Task<Ticket?> GetAsync(Guid id, CancellationToken ct);
  Task AddAsync(Ticket ticket, CancellationToken ct);
  Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken ct);
  Task<int> CountAsync(TicketQuery query, CancellationToken ct);
  Task SaveChangesAsync(CancellationToken ct);

SaveChangesAsync sits on the repository rather than a separate IUnitOfWork.
Section VII: one aggregate, one caller, so a second abstraction is not yet
earned. Split it out when a transaction must span two aggregates - and say so
in that story, not this one.

TicketQuery is a domain record of nullable filters plus paging:
  TicketQuery(TicketStatus? Status, TicketPriority? Priority, Guid? AssigneeId,
              Guid? RequesterId, int Page, int PageSize)

Registration: extend AddPersistence in
src/CrmTicketing.Infrastructure/DependencyInjection.cs to register
ITicketRepository -> TicketRepository. The API still calls only AddPersistence
and still names no persistence type.

Error mapping: an IExceptionHandler registered in Program.cs maps
  InvalidTicketTransitionException -> 409 Conflict, with the from/to values in
                                      the problem details extensions
  ArgumentException                -> 400 Bad Request, naming the parameter
  everything else                  -> unchanged, so genuine faults stay 500
AddProblemDetails() and UseExceptionHandler() are already wired in Program.cs.

Contracts, all sealed records in Shared/Contracts/Tickets/:
  CreateTicketRequest, UpdateTicketRequest, TransitionTicketRequest,
  AssignTicketRequest, TicketResponse, TicketSummaryResponse,
  PagedResponse<T>, TicketMetadataResponse

TicketSummaryResponse is deliberately smaller than TicketResponse - the list
endpoint must not ship full descriptions for 100 rows.

Time: controllers resolve TimeProvider and pass GetUtcNow() into domain
mutators. The domain still never reads a clock.
```

## Out of scope

```
- No comments and no activity timeline. That is issue #11, and it needs its own
  aggregate decision (owned entity vs separate aggregate).
- No UI. The list, detail, and kanban views are issues #12-#14.
- No authentication or authorisation. Endpoints are open in this story;
  permission-gating is issue #16 and depends on #5 and #6.
- No SLA fields, due dates, or breach logic (issue #21).
- No Contact or Account lookup. RequesterId and AssigneeId stay opaque Guids
  with no validation that they refer to anything.
- No delete endpoint. Whether tickets are ever deleted, and whether deletion is
  soft, is an unanswered product question - do not invent one.
- No bulk operations, no export, no search across free text. Filtering is by the
  four listed fields only.
- No caching, no rate limiting, no pagination cursors. Offset paging is enough
  for a first pass.
- No OpenAPI examples or client generation beyond what MapOpenApi already emits.
- No change to the layer graph in docs/architecture.md, other than recording the
  ITicketRepository decision under Decisions taken by the scaffold.
```
