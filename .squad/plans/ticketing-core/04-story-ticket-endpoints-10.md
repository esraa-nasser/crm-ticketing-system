# Story 04 — Ticket CRUD endpoints with Shared contracts (Story: 10)

## Prerequisites

- Story 03 completed: [`03-story-ticket-aggregate-8.md`](03-story-ticket-aggregate-8.md) — `Ticket`, `TicketStatus`, `TicketPriority`, `TicketStatusTransitions`, `TicketTitle`, `InvalidTicketTransitionException`, `TicketConfiguration`, and the `AddTicket` migration are merged on `main`.
- Story 02 completed: [`../persistence/02-story-add-crmdbcontext-3.md`](../persistence/02-story-add-crmdbcontext-3.md) — `AddPersistence` is the only seam the API uses to reach persistence.
- No running PostgreSQL is required. Every verification step here works without a database; the repository is exercised through a hand-rolled fake, never through EF.

---

## Story Goal

Make the `Ticket` aggregate reachable over HTTP, and settle the persistence abstraction that story 02 deferred because it had no caller.

1. Seven endpoints under `/api/tickets` exchange `sealed record` contracts from `CrmTicketing.Shared`, which still references no other project.
2. Status and priority cross the wire as **strings**. An unknown value is a `400`, never a `500`.
3. `ITicketRepository` is declared in `CrmTicketing.Domain` and implemented in `CrmTicketing.Infrastructure`. No controller names `CrmDbContext`, EF Core, or Npgsql.
4. An illegal status move returns **409 Conflict** carrying the attempted `from` and `to`, mapped from `InvalidTicketTransitionException` by an `IExceptionHandler` — the API never re-encodes the transition table.
5. `GET /api/tickets/metadata` publishes the statuses, priorities, and the transition map, all read from `TicketStatusTransitions`, so the future UI has no reason to hardcode them.

The story stops at the HTTP boundary. No comments, no timeline, no UI, no auth, no delete.

---

## Context — Read These Files First

1. `src/CrmTicketing.Domain/Tickets/Ticket.cs` — all 191 lines. The factory `Ticket.Open` (~lines 72–99), `TransitionTo` (~lines 102–113), `Assign` (~lines 116–133), `Unassign` (~lines 136–146), `ChangePriority` (~lines 148–152). Note the properties at ~lines 50–66: `Title`, `Description`, and `Category` have **private setters and no public mutator**. This story adds one — see task 3.
2. `src/CrmTicketing.Domain/Tickets/TicketStatusTransitions.cs` — all 40 lines. `IsAllowed` (~line 31) and `AllowedFrom` (~line 38). `AllowedFrom` is what the metadata endpoint serialises. **Do not re-declare the table anywhere.**
3. `src/CrmTicketing.Domain/Tickets/InvalidTicketTransitionException.cs` — all 34 lines. The `From` and `To` properties are what the 409 problem details carry; the message is never parsed.
4. `src/CrmTicketing.Api/Program.cs` — all 33 lines. `AddSingleton(TimeProvider.System)` on line 6, `AddProblemDetails()` on line 8, `AddPersistence` on line 12, `UseExceptionHandler()` on line 25. This story inserts one service registration and changes nothing else.
5. `src/CrmTicketing.Api/Controllers/SystemController.cs` — all 29 lines. The controller style to match: `[ApiController]`, primary constructor injection, `[ProducesResponseType<T>]` on every action, expression-bodied where it reads cleanly.
6. `src/CrmTicketing.Shared/Contracts/ApiInfoResponse.cs` — all 15 lines. The contract style: `sealed record`, positional parameters, `<param>` XML docs on each.
7. `src/CrmTicketing.Infrastructure/DependencyInjection.cs` — all 41 lines. `AddPersistence` (~lines 20–41) registers `CrmDbContext` on line 38. Task 6 adds one line after it.
8. `tests/CrmTicketing.Api.Tests/SystemControllerTests.cs` — all 39 lines. The API test style: **no `WebApplicationFactory`, no mocking library**. `FixedTimeProvider` (~lines 18–21) is the clock fake to reuse; `FakeHostEnvironment` (~lines 10–16) is the pattern for the repository fake.
9. `tests/CrmTicketing.Api.Tests/CrmTicketing.Api.Tests.csproj` — all 24 lines. It references only `CrmTicketing.Api`; Domain and Shared arrive transitively. **No `.csproj` change is needed** and none may be added — `Microsoft.AspNetCore.Mvc.Testing` is not a dependency of this solution.
10. `docs/constitution.md` — §II (line 23) the layer graph: `Shared` holds only DTOs and depends on nothing. §IV (line 55) contracts are `sealed record`s and RFC 9457. §VII (line 86) three strikes before abstraction — the reason there is one repository interface and no `IUnitOfWork`.
11. `docs/architecture.md` — `## Decisions taken by the scaffold` (line 73). Task 10 appends one bullet recording the `ITicketRepository` decision.

---

## Implementation tasks

### 1 — The query object

**Create file: `src/CrmTicketing.Domain/Tickets/TicketQuery.cs`**

```csharp
public sealed record TicketQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;

    public static TicketQuery Create(
        TicketStatus? status = null,
        TicketPriority? priority = null,
        Guid? assigneeId = null,
        Guid? requesterId = null,
        int page = 1,
        int pageSize = DefaultPageSize);

    public TicketStatus? Status { get; }
    public TicketPriority? Priority { get; }
    public Guid? AssigneeId { get; }
    public Guid? RequesterId { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int Skip => (Page - 1) * PageSize;
}
```

`Create` **clamps rather than rejects**: `page` below `1` becomes `1`; `pageSize` below `1` becomes `DefaultPageSize`; `pageSize` above `MaxPageSize` becomes `MaxPageSize`. A private constructor keeps `Create` the only way in, matching `TicketTitle` in `src/CrmTicketing.Domain/Tickets/TicketTitle.cs`.

Clamping lives here, not in the controller, so it is unit-testable without ASP.NET Core and cannot drift between callers.

### 2 — The repository interface

**Create file: `src/CrmTicketing.Domain/Tickets/ITicketRepository.cs`**

```csharp
public interface ITicketRepository
{
    Task<Ticket?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Ticket ticket, CancellationToken cancellationToken);
    Task<IReadOnlyList<Ticket>> ListAsync(TicketQuery query, CancellationToken cancellationToken);
    Task<int> CountAsync(TicketQuery query, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

**Framework-free.** No EF types, no `IQueryable`, no `DbSet` in any signature — `CrmTicketing.Domain.csproj` still declares zero references and must stay that way.

`SaveChangesAsync` sits on the repository, not on a separate `IUnitOfWork`. Constitution §VII: one aggregate, one caller. Split it out when a transaction must span two aggregates, and justify it in **that** story.

### 3 — The missing domain mutator

**File: `src/CrmTicketing.Domain/Tickets/Ticket.cs`**

`PATCH /api/tickets/{id}` updates title, description, and category, and `Ticket` currently exposes no way to do it — the setters at ~lines 50–58 are private and no public method touches them. Add one mutator after `ChangePriority` (~line 152):

```csharp
public void UpdateDetails(TicketTitle title, string description, string? category, DateTimeOffset at);
```

- `ArgumentNullException.ThrowIfNull(title)`.
- `description` runs through the existing `NormaliseDescription` (~line 154); `category` through the existing `NormaliseCategory` (~line 173). **Reuse them — do not write a second copy of those rules.**
- Sets `Title`, `Description`, `Category`, and `UpdatedAt`. Takes the instant from the caller like every other mutator.
- A closed ticket may still have its details corrected; no status guard here.

### 4 — The closed-ticket exception

**Create file: `src/CrmTicketing.Domain/Tickets/TicketClosedException.cs`**

```csharp
public sealed class TicketClosedException : InvalidOperationException
{
    public TicketClosedException() { }
    public TicketClosedException(string message) : base(message) { }
    public TicketClosedException(string message, Exception innerException)
        : base(message, innerException) { }
    public TicketClosedException(TicketStatus status, string operation)
        : base($"A ticket with status {status} cannot be {operation}.")
    public string Operation { get; }
}
```

**Message: `$"A ticket with status {status} cannot be {operation}."`** — byte-identical to what `Assign` and `Unassign` already throw, and to what `TicketTests.cs:180` asserts on. Do not change the wording.

The two-argument constructor takes `(TicketStatus, string)`, so it does not clash with the three CA1032 constructors above it. **CA1032 is genuinely satisfied — do not suppress it.** Mirror `InvalidTicketTransitionException.cs`, which has the same shape.

**File: `src/CrmTicketing.Domain/Tickets/Ticket.cs`** — replace the bare `InvalidOperationException` thrown by `Assign` (~lines 125–129) and `Unassign` (~lines 138–142) with `TicketClosedException`.

**Why this exists:** the intake requires `409` from `POST /api/tickets/{id}/assignee` when the ticket is closed. The handler in task 8 cannot distinguish a bare `InvalidOperationException` from a genuine fault, and the alternative — testing `Status == Closed` in the controller — would put a domain rule in the API. `TicketClosedException` derives from `InvalidOperationException`, so the story-03 tests asserting `Assert.Throws<InvalidOperationException>` still pass unchanged.

### 5 — The contracts

**Create files under `src/CrmTicketing.Shared/Contracts/Tickets/`** — one `sealed record` per file, positional parameters, `<param>` docs, matching `ApiInfoResponse.cs`:

```csharp
public sealed record CreateTicketRequest(
    string Title, string Description, Guid RequesterId, string? Priority, string? Category);

public sealed record UpdateTicketRequest(
    string Title, string Description, string? Category, string? Priority);

public sealed record TransitionTicketRequest(string Status);

public sealed record AssignTicketRequest(Guid? AssigneeId);

public sealed record TicketResponse(
    Guid Id, string Title, string Description, string Status, string Priority,
    string? Category, Guid RequesterId, Guid? AssigneeId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record TicketSummaryResponse(
    Guid Id, string Title, string Status, string Priority, string? Category,
    Guid RequesterId, Guid? AssigneeId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record TicketMetadataResponse(
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> Priorities,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Transitions);
```

- **`CrmTicketing.Shared` must not reference `CrmTicketing.Domain`.** That is why `Status` and `Priority` are `string`, and why no contract names `TicketStatus`. Do not add a project reference and do not copy the enums into `Shared` — a duplicated enum drifts.
- `TicketSummaryResponse` deliberately omits `Description`. The list endpoint must not ship 100 full descriptions.
- `AssignTicketRequest.AssigneeId` is nullable: `null` means **unassign**. There is no separate `DELETE` route.
- `UpdateTicketRequest.Priority` is nullable meaning "leave unchanged"; `Title` and `Description` are required by the aggregate and are always sent.

### 6 — The repository implementation

**Create file: `src/CrmTicketing.Infrastructure/Persistence/TicketRepository.cs`**

```csharp
internal sealed class TicketRepository(CrmDbContext context) : ITicketRepository
```

- `GetAsync` — `context.Set<Ticket>().FirstOrDefaultAsync(t => t.Id == id, cancellationToken)`. `CrmDbContext` declares no `DbSet` property (see its `<remarks>`, ~lines 8–12); reach the set through `Set<Ticket>()`.
- `AddAsync` — `context.Set<Ticket>().AddAsync(...)`.
- `ListAsync` — apply the filters, then `OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)`, then `Skip(query.Skip).Take(query.PageSize)`, then `ToListAsync`. The `ThenBy` is not decoration: without a tiebreaker, two tickets sharing a `CreatedAt` can repeat or vanish across pages.
- `CountAsync` — the same filters, no ordering, no paging.
- Share the filter predicate between `ListAsync` and `CountAsync` through one private `IQueryable<Ticket> Filter(IQueryable<Ticket>, TicketQuery)` helper, so a filter can never apply to the page but not the count.
- `SaveChangesAsync` — delegates to the context.

`internal sealed` for the same reason `TicketConfiguration` is: nothing outside Infrastructure may name it.

**File: `src/CrmTicketing.Infrastructure/DependencyInjection.cs`** — after `services.AddDbContext<CrmDbContext>(...)` on line 38, add:

```csharp
services.AddScoped<ITicketRepository, TicketRepository>();
```

The API keeps calling only `AddPersistence` (`Program.cs` line 12) and still names no persistence type.

### 7 — Mapping and parsing

**Create file: `src/CrmTicketing.Api/Mapping/TicketMapper.cs`** — `internal static class`:

- `ToResponse(Ticket)` returns `TicketResponse`; `ToSummary(Ticket)` returns `TicketSummaryResponse`. Enums cross the boundary as `ticket.Status.ToString()` and `ticket.Priority.ToString()`, and the title as `ticket.Title.Value`.
- `static bool TryParseStatus(string? value, out TicketStatus status)` and the equivalent for priority.

**Parsing rule — do not use `Enum.TryParse` at all.** It accepts numeric text, so `"3"` would silently mean the third member and `"99"` would produce an undeclared value. Guarding its output leaves the numeric path in place and invites the next edge case. Remove the path instead: match against the declared names.

```csharp
private static bool TryParseName<TEnum>(string? value, out TEnum parsed)
    where TEnum : struct, Enum
{
    parsed = default;
    var trimmed = value?.Trim();

    if (string.IsNullOrEmpty(trimmed))
    {
        return false;
    }

    foreach (var name in Enum.GetNames<TEnum>())
    {
        if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            parsed = Enum.Parse<TEnum>(name);
            return true;
        }
    }

    return false;
}
```

`"3"`, `" 3 "`, and `"99"` then fail identically, through the same path as `"Frozen"`. **Statuses and priorities cross the wire as names, never as numbers.**

Mapping lives in the API, not in `Shared` (which must hold no behaviour, constitution §II) and not in `Domain` (which must not know about contracts).

### 8 — The exception handler

**Create file: `src/CrmTicketing.Api/Infrastructure/DomainExceptionHandler.cs`**

```csharp
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    internal static int? MapStatusCode(Exception exception);
    public ValueTask<bool> TryHandleAsync(HttpContext, Exception, CancellationToken);
}
```

`MapStatusCode` is a pure static so it can be unit-tested without an `HttpContext`:

| Exception | Status | Problem details extensions |
|---|---|---|
| `InvalidTicketTransitionException` | `409` | `from` = `From.ToString()`, `to` = `To.ToString()` |
| `TicketClosedException` | `409` | `operation` = `Operation` |
| `ArgumentException` (and `ArgumentNullException`) | `400` | `parameter` = `ParamName` |
| anything else | `null` | — handler returns `false`, so genuine faults stay `500` |

**Order matters:** test `InvalidTicketTransitionException` and `TicketClosedException` before `InvalidOperationException`, and `ArgumentException` last among the 400s — `ArgumentNullException` derives from it.

Never put exception text in the response body; §IV bans it. `Title` is a fixed phrase per status and `Detail` is omitted.

**File: `src/CrmTicketing.Api/Program.cs`** — one line only, between line 8 (`AddProblemDetails`) and line 9 (`AddOpenApi`):

```csharp
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
```

`UseExceptionHandler()` on line 25 already runs the pipeline. **Change nothing else in `Program.cs`.**

### 9 — The controller

**Create file: `src/CrmTicketing.Api/Controllers/TicketsController.cs`**

```csharp
[ApiController]
[Route("api/tickets")]
public sealed class TicketsController(ITicketRepository repository, TimeProvider timeProvider)
    : ControllerBase
```

`[Route("api/tickets")]` is written out rather than `[controller]` so the route does not change if the class is ever renamed.

| Verb and route | Request | Success | Failure |
|---|---|---|---|
| `POST /api/tickets` | `CreateTicketRequest` | `201` pointing at `GetById`, body `TicketResponse` | `400` |
| `GET /api/tickets/{id:guid}` | — | `200` `TicketResponse` | `404` |
| `GET /api/tickets` | query string | `200` `PagedResponse<TicketSummaryResponse>` | `400` |
| `PATCH /api/tickets/{id:guid}` | `UpdateTicketRequest` | `200` `TicketResponse` | `404`, `400` |
| `POST /api/tickets/{id:guid}/status` | `TransitionTicketRequest` | `200` `TicketResponse` | `404`, `409`, `400` |
| `POST /api/tickets/{id:guid}/assignee` | `AssignTicketRequest` | `200` `TicketResponse` | `404`, `400`, `409` |
| `GET /api/tickets/metadata` | — | `200` `TicketMetadataResponse` | — |

Rules for every action:

- **Time.** `var now = timeProvider.GetUtcNow();` at the top of each mutating action, passed into the domain call. The domain still never reads a clock.
- **Ids.** `Guid.CreateVersion7()` for a new ticket id — sequential, so it does not fragment the primary-key index.
- **`201`.** `CreatedAtAction(nameof(GetById), new { id = ticket.Id }, TicketMapper.ToResponse(ticket))`.
- **`404`.** A missing ticket returns `Problem(statusCode: StatusCodes.Status404NotFound)` — problem details, not a bare `NotFound()`.
- **`400` for a bad enum string.** Parse before touching the domain and return `ValidationProblem` naming the field. Do **not** let it reach the aggregate.
- **Persist.** Every mutating action ends with `await repository.SaveChangesAsync(cancellationToken)` before mapping the response.
- **Cancellation.** Every action takes `CancellationToken cancellationToken` and passes it down.
- **Attributes.** `[ProducesResponseType<T>(StatusCodes.Status200OK)]` and one per documented failure status, matching `SystemController`.

Route specifics:

- `GET /api/tickets/metadata` is declared with `[HttpGet("metadata")]`. It cannot collide with `[HttpGet("{id:guid}")]` because of the `:guid` constraint — keep that constraint on every id route.
- `GET /api/tickets` binds `[FromQuery] string? status, string? priority, Guid? assigneeId, Guid? requesterId, int page = 1, int pageSize = 25`, parses the two strings, and builds the query with `TicketQuery.Create(...)`. **Clamping is `TicketQuery`'s job — do not clamp here as well.**
- `POST /api/tickets/{id}/assignee`: a null `AssigneeId` calls `ticket.Unassign(now)`, otherwise `ticket.Assign(request.AssigneeId.Value, now)`.
- `GET /api/tickets/metadata` builds `Transitions` as `Enum.GetValues<TicketStatus>().ToDictionary(s => s.ToString(), s => TicketStatusTransitions.AllowedFrom(s).Select(t => t.ToString()).ToList())`. **Source it from `AllowedFrom` — never from a literal.**

### 10 — Documentation

**File: `docs/architecture.md`** — append one bullet to `## Decisions taken by the scaffold` (line 73), after the migrations bullet:

> **Persistence is reached through `ITicketRepository`**, declared in `CrmTicketing.Domain/Tickets/` and implemented in `CrmTicketing.Infrastructure/Persistence/`. `SaveChangesAsync` sits on the repository; there is no separate unit of work until a transaction must span two aggregates.

**File: `.squad/plans/persistence/00-overview.md`** — the last bullet, *"Open question carried forward"*, is answered by this story. Replace it with a line recording that story 04 chose one domain-declared repository interface and no `IUnitOfWork`, linking `../ticketing-core/04-story-ticket-endpoints-10.md`. This is the one plan file this story is authorised to edit.

**No change** to `docs/constitution.md`. This story adds no project and no edge to the layer graph.

---

## Edge Cases & Failure Modes

- **Unknown status string.** `POST /api/tickets/{id}/status` with `"Frozen"` returns `400` from `TicketMapper.TryParseStatus` before the aggregate is touched. Enforced in the controller action, not in `Ticket.TransitionTo`.
- **Numeric status string.** `"3"` parses cleanly through a naive `Enum.TryParse` and would silently mean `Resolved`. Name matching in task 7 removes that path entirely, so `"3"`, `" 3 "`, and `"99"` all fail exactly as `"Frozen"` does. Test them explicitly — it is the failure a reviewer will not think of.
- **Legal-but-same status.** `TicketStatusTransitions.IsAllowed(x, x)` is `false` by story-03 design, so transitioning a ticket to the status it already holds returns `409`, not `200`. Deliberate.
- **Assigning a closed ticket.** `TicketClosedException` maps to `409`. Distinct from an illegal transition: no status change was attempted, and the extensions carry `operation`, not `from`/`to`.
- **`pageSize=500`.** Clamped to `100` by `TicketQuery.Create`, and the response reports `PageSize = 100`. **Never a `400`** — the intake is explicit that oversized pages are clamped, not rejected.
- **`page=0` or negative.** Clamped to `1`. `Skip` is therefore never negative, which would throw inside EF at query translation.
- **Empty page beyond the end.** `page=99` on three tickets returns `200` with an empty `Items` array and the true `TotalCount`. Not a `404` — the collection exists, the slice is empty.
- **`PATCH` with a title of 201 characters.** `TicketTitle.Create` throws `ArgumentException`; the handler maps it to `400` with `parameter = "value"`. The ticket is not mutated, because the title is constructed before `UpdateDetails` is called.
- **Partial `PATCH` failure.** Build every value object **before** calling any mutator, so a bad description cannot leave a ticket with a new title and an old description. There is no rollback below this point — `SaveChangesAsync` is only reached on success.
- **Unknown ticket id on any mutating route.** `404` before the request body is validated, so a bad body on a missing ticket is a `404`, not a `400`. Pick this order and keep it consistent across all four mutating routes.
- **`Guid.Empty` as a requester.** `Ticket.Open` throws `ArgumentException` (`src/CrmTicketing.Domain/Tickets/Ticket.cs` ~lines 82–85), which maps to `400`. The API adds no second check.
- **`RequesterId` or `AssigneeId` referring to nobody.** Not validated. There is no Contact aggregate yet; both stay opaque `Guid`s. Explicitly out of scope — do not add a lookup.
- **Concurrent transitions.** Two callers moving the same ticket `Open` → `Resolved` simultaneously both succeed; the second write wins. No optimistic concurrency token exists on `Ticket`, and adding one is not this story. Record it, do not fix it.
- **`TicketTitle` conversion on read.** A row holding an invalid title throws inside `TicketTitle.Create` during materialisation, surfacing as a `500`. Inherited from story 03's converter and unchanged here.

---

## Test Plan

### 11 — Domain tests

**Create file: `tests/CrmTicketing.Domain.Tests/Tickets/TicketQueryTests.cs`**

1. `[Theory]` clamping `pageSize`: `500` to `100`, `101` to `100`, `100` to `100`, `0` to `25`, `-5` to `25`.
2. `[Theory]` clamping `page`: `0` to `1`, `-3` to `1`, `1` to `1`, `7` to `7`.
3. `[Fact]` `Skip` equals `(Page - 1) * PageSize` and is never negative for a clamped query.
4. `[Fact]` filters round-trip: a query built with all four filters exposes exactly those values.

**File: `tests/CrmTicketing.Domain.Tests/Tickets/TicketTests.cs`** (add to the existing class)

5. `UpdateDetails` sets title, description, and trimmed category, and advances `UpdatedAt`.
6. `UpdateDetails` rejects a null title, an empty description, a 10001-character description, and a 101-character category — each an `ArgumentException` with the right `ParamName`.
7. `UpdateDetails` succeeds on a closed ticket.
8. `Assign` and `Unassign` on a closed ticket throw `TicketClosedException`. The existing story-03 assertions on `InvalidOperationException` stay green because `TicketClosedException` derives from it — **do not weaken them**.

### 12 — API tests

**Create file: `tests/CrmTicketing.Api.Tests/Controllers/TicketsControllerTests.cs`**

Build a `FakeTicketRepository : ITicketRepository` backed by a `List<Ticket>`, in the hand-rolled style of `FakeHostEnvironment` in `SystemControllerTests.cs` (~lines 10–16). Reuse `FixedTimeProvider` (~lines 18–21). **No mocking library, no `WebApplicationFactory`, no database.**

9. `Create_ReturnsCreatedPointingAtGetById` — `201` with `CreatedAtActionResult.ActionName == nameof(GetById)` and `RouteValues["id"]` equal to the new ticket's id, plus `Status == "New"`, `Priority == "Normal"`, and the ticket is in the repository. **Assert the action reference, not a `Location` header** — a controller unit test has no routing, so no header is materialised.
10. `GetById_UnknownId_Returns404`.
11. `Transition_IllegalMove_Returns409` — the action lets `InvalidTicketTransitionException` escape; assert the exception carries the right `From` and `To`, then assert `DomainExceptionHandler.MapStatusCode` returns `409` for it.
12. `Transition_UnknownStatusString_Returns400` — both `"Frozen"` and `"3"`.
13. `List_PageSize500_IsClampedTo100` — the response reports `PageSize == 100`.
14. `List_FiltersByStatusAndAssignee` — only matching tickets come back, and `TotalCount` reflects the filter, not the page.
15. `Patch_UnknownId_Returns404`, and `Patch_UpdatesTitleAndDescription` returns `200` with the new values.
16. `Assign_NullAssigneeId_Unassigns`, and `Assign_ClosedTicket_ThrowsTicketClosed`.
17. `Metadata_ReturnsTransitionMapFromDomain` — `Transitions["Closed"]` is empty, `Transitions["New"]` has two entries containing `"Open"` and `"Closed"`, and the map has five entries. **Assert count plus membership, never sequence equality** — the domain's `FrozenSet` guarantees no order.

**Create file: `tests/CrmTicketing.Api.Tests/Infrastructure/DomainExceptionHandlerTests.cs`**

18. `[Theory]` over `MapStatusCode`: `InvalidTicketTransitionException` to `409`, `TicketClosedException` to `409`, `ArgumentException` to `400`, `ArgumentNullException` to `400`, `InvalidOperationException` to `null`, `Exception` to `null`.

### 13 — Infrastructure tests

19. **None.** `TicketRepository` translates LINQ to SQL and cannot be asserted without a database; CI has none. Do not add an in-memory provider to fake it — it would test a different query engine than production. Integration coverage belongs to the story that introduces a test database.

### 14 — Regression

20. `tests/CrmTicketing.Api.Tests/SystemControllerTests.cs`, `tests/CrmTicketing.Domain.Tests/Common/EntityTests.cs`, `tests/CrmTicketing.Domain.Tests/Tickets/TicketStatusTransitionsTests.cs`, `TicketTitleTests.cs`, and both files under `tests/CrmTicketing.Infrastructure.Tests/Persistence/` pass **unchanged**. `TicketTests.cs` gains tests but loses none.

---

## Migration / Rollback

**No database migration.** This story adds no column, no table, and no index; `TicketConfiguration` and the `AddTicket` migration are untouched. Confirm with `git status` that nothing under `src/CrmTicketing.Infrastructure/Persistence/Migrations/` changed — if EF regenerates the snapshot, something edited the model that should not have.

Rollback is reverting the commit. The only externally visible surface is new routes; nothing existing changes shape, so no client can break.

---

## Verification Steps

1. **Backend builds:** `dotnet build CrmTicketing.slnx` — zero warnings, zero errors under `TreatWarningsAsErrors`.
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — all four test projects, no database running.
3. **`Shared` stays dependency-free:** `grep -c ProjectReference src/CrmTicketing.Shared/CrmTicketing.Shared.csproj` returns `0`.
4. **`Domain` stays dependency-free:** `grep -cE "(Project|Package)Reference" src/CrmTicketing.Domain/CrmTicketing.Domain.csproj` returns `0`.
5. **The API names no persistence type:** `grep -rn --include='*.cs' "CrmDbContext\|EntityFrameworkCore\|Npgsql" src/CrmTicketing.Api/` returns no output. The `--include` is required: `CrmTicketing.Api.csproj` legitimately carries `Microsoft.EntityFrameworkCore.Design` as a `PrivateAssets="all"` reference so `dotnet ef` can use the API as its startup project, and an unscoped grep flags it.
6. **No ambient clock:** `grep -rn "DateTime.UtcNow\|DateTime.Now" src/` returns no output.
7. **The transition table is still single-sourced:** `grep -rln "TicketStatus.Resolved" src/` returns only `src/CrmTicketing.Domain/Tickets/TicketStatusTransitions.cs`.
8. **No migration churn:** `git status --short src/CrmTicketing.Infrastructure/Persistence/Migrations/` returns no output.
9. **No new package:** `git diff Directory.Packages.props` returns no output.
10. **Optional, with PostgreSQL running:** `dotnet run --project src/CrmTicketing.Api`, then `curl` `GET /api/tickets/metadata` and confirm the `Closed` entry in `Transitions` is empty. Not required to pass — CI has no database.

---

## Done Criteria

- [ ] All seven routes exist under `/api/tickets` with the documented status codes.
- [ ] Contracts are `sealed record`s under `src/CrmTicketing.Shared/Contracts/Tickets/`; `CrmTicketing.Shared` has zero project references.
- [ ] Status and priority cross the wire as strings; an unknown or numeric string is `400`, never `500`.
- [ ] `ITicketRepository` is declared in `CrmTicketing.Domain` and framework-free; `TicketRepository` is `internal sealed` in Infrastructure and registered inside `AddPersistence`.
- [ ] No controller names `CrmDbContext`, EF Core, or Npgsql.
- [ ] `DomainExceptionHandler` maps illegal transitions to `409` with `from` and `to` in the extensions, and leaves unrecognised exceptions as `500`.
- [ ] `GET /api/tickets/metadata` sources statuses, priorities, and the transition map from `TicketStatusTransitions`.
- [ ] `pageSize=500` is clamped to `100` by `TicketQuery`, not rejected, and the response reports the clamped value.
- [ ] Timestamps come from the injected `TimeProvider`; the domain reads no clock.
- [ ] `Ticket.UpdateDetails` exists and reuses the existing description and category normalisers.
- [ ] `dotnet build` clean; `dotnet test` passes with no database.
- [ ] `docs/architecture.md` records the `ITicketRepository` decision, and the open question in `../persistence/00-overview.md` is closed.
- [ ] Overview `00-overview.md` updated with this story.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 05 (issue #11, comments and activity timeline).**

---

## Amendment log

Decisions taken after the plan was first written, during implementation. Each one
is already reflected in the task it names.

1. **Task 4 — exception message.** The plan proposed `"A closed ticket cannot be assigned."` That string was arbitrary; `Assign` and `Unassign` already threw `$"A ticket with status {status} cannot be {operation}."`, and `TicketTests.cs:180` asserts on it. `TicketClosedException` now carries the existing message verbatim, so no behaviour and no assertion changes.
2. **Task 4 — CA1032 is satisfied, not suppressed.** The domain-specific constructor takes `(TicketStatus status, string operation)`, which does not collide with the three constructors CA1032 requires. All four coexist; there is no suppression anywhere in this story.
3. **Task 7 — the numeric parse path is removed, not guarded.** The plan patched `Enum.TryParse` with a leading-character check. The parser now matches `Enum.GetNames<T>()` case-insensitively against the trimmed input, so `"3"`, `" 3 "`, and `"99"` fail through exactly the same path as `"Frozen"`. Statuses cross the wire as names, never as numbers.
4. **Task 9 — the metadata cast is explicit.** `IReadOnlyDictionary<,>` is not covariant in its value, so the `ToDictionary` value selector casts to `(IReadOnlyList<string>)`. Without it the dictionary does not convert to the contract's type.
5. **Test 9 — assert the action reference, not a `Location` header.** A controller unit test has no routing, so no header is materialised. The test asserts `CreatedAtActionResult.ActionName == nameof(GetById)` and `RouteValues["id"]`. The done criterion now reads "returns 201 pointing at `GetById`".
6. **Test 17 — count plus membership, never sequence equality.** The domain's `FrozenSet` guarantees no ordering, so asserting on element order would be asserting on an implementation detail.
7. **Verification step 5 — scoped to sources.** `grep -rn` without `--include='*.cs'` flags the legitimate `Microsoft.EntityFrameworkCore.Design` reference in `CrmTicketing.Api.csproj`, which exists so `dotnet ef` can use the API as its startup project. The step now scopes to `*.cs`.

Two further deviations found while implementing, recorded here rather than left silent:

8. **`TicketTests.cs:177` changed from `Assert.Throws<InvalidOperationException>` to `Assert.Throws<TicketClosedException>`.** xUnit's `Assert.Throws<T>` demands an exact type match, so narrowing `Assign` to `TicketClosedException` broke the original assertion. `Assert.ThrowsAny<InvalidOperationException>` was rejected as the fix: it would keep passing if someone reverted `Assign` to throwing a plain `InvalidOperationException`, while `DomainExceptionHandler` silently regressed the endpoint from **409 to 500**. The exception type is load-bearing for the HTTP contract, so the test pins it exactly. The message assertion on the following line — the line the message decision in entry 1 was made to protect — is untouched.
9. **`CrmTicketing.Api.csproj` gained `<InternalsVisibleTo Include="CrmTicketing.Api.Tests" />`.** `DomainExceptionHandler` is `internal sealed`, and tests 11, 16, and 18 call `MapStatusCode`. This mirrors the identical line `CrmTicketing.Infrastructure.csproj` already carries for its own test project; the alternative was making the handler public, which would widen the API surface for no runtime reason.
