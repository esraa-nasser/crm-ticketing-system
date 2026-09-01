# Story 09 — Comments on a ticket, authored and visibility-scoped (Story: 11)

## Prerequisites

- Story 03 completed: [`03-story-ticket-aggregate-8.md`](03-story-ticket-aggregate-8.md) — `Ticket`, `TicketStatus`, `TicketStatusTransitions`, and `TicketClosedException`. This story adds one method to `Ticket` and changes nothing else about it.
- Story 04 completed: [`04-story-ticket-endpoints-10.md`](04-story-ticket-endpoints-10.md) — `TicketsController`, `TicketMapper`, `DomainExceptionHandler`, and the `Shared.Contracts.Tickets` namespace. **No existing ticket endpoint changes.**
- Story 06 completed: [`../auth-roles/06-story-identity-and-authorisation-5.md`](../auth-roles/06-story-identity-and-authorisation-5.md) — `CallerContext`, `TicketAccess`, `AuthorizationPolicies.StaffOnly`, and `ApplicationUser`. **This is the first story that uses the actor rather than merely recording it:** a comment's whole meaning is who wrote it.
- Story 07 completed: [`../demo-data/07-story-demo-data-4.md`](../demo-data/07-story-demo-data-4.md) — `DemoDataSeeder` and `DemoTicketSpecification`. Task 12 extends the demo set; without it the feature is invisible from a clean database.
- Story 08 completed: [`../ticketing-ui/08-story-ticket-detail-13.md`](../ticketing-ui/08-story-ticket-detail-13.md) — `TicketDetail.razor`, `DisplayTime`, `TokenStore.UserId`, and the generalised `TicketsApiClient.SendAsync`. The thread lands inside that page.
- PostgreSQL is needed for the migration and for manual verification. Building and testing need neither.

---

## Story Goal

Give a ticket a conversation. A ticket can currently be raised, worked, and closed without anyone writing a word on it, which makes this a workflow tracker rather than a support system.

1. `TicketComment` is **its own aggregate**, not an owned collection. Reading a ticket loads no comments and returns no comment count.
2. Every comment carries `IsInternal` **from the first migration**. Internal is staff-only; public is visible to the requester too.
3. The rule that a `Closed` ticket accepts no comment lives in the **domain**, and returns **409** — the ticket's state forbids it, not the caller's role.
4. A Customer never receives an internal comment, and the filter lives in the **repository**, beside the row-level ticket rule.
5. The detail view shows the thread and a box to add to it, with the internal/public choice visible only to staff.

**Not in this story:** the activity timeline. Status changes, assignments, and priority changes are a derived event history, not authored text; storing them is a different design. Task 13 opens its own issue.

---

## Context — Read These Files First

1. `src/CrmTicketing.Domain/Tickets/Ticket.cs` — all 258 lines. `Assign` (~lines 138–154) is the model for the new guard: the closed check at ~lines 147–150 throws `TicketClosedException(Status, "assigned")`. Note `Touch` (~lines 207–211) and `RequireActor` (~lines 213–219). **Read `NormaliseDescription` (~lines 221–238)** — the trim-then-validate shape the comment body copies.
2. `src/CrmTicketing.Domain/Tickets/TicketClosedException.cs` — all 33 lines. The `(TicketStatus, string operation)` constructor (~lines 25–29) produces `"A ticket with status {status} cannot be {operation}."`, and `Operation` (~line 32) reaches the wire as a problem-details extension.
3. `src/CrmTicketing.Domain/Tickets/TicketAccess.cs` — all 39 lines. **This is the type `CommentVisibility` is modelled on**: a sealed record, private constructor, two named factories, and a comment explaining why it is not an enum of role names. Note `All()` at ~line 25 and the `(Guid?)null` cast disambiguating the record's copy constructor.
4. `src/CrmTicketing.Domain/Tickets/TicketQuery.cs` — all 85 lines. `Create` (~lines 57–85) clamps paging rather than rejecting it (~lines 68–75), and `Access` is required and defaultless so a caller that forgets it fails to compile. `TicketCommentQuery` follows all of it.
5. `src/CrmTicketing.Infrastructure/Persistence/TicketRepository.cs` — all 100 lines. `ApplyAccess` (~lines 63–66) and `Filter` (~lines 73–100) are `internal static` **so they can be tested over an in-memory `IQueryable` with no database**, and `ListAsync`/`CountAsync` (~lines 28–50) both route through `Filter` so a rule cannot constrain the page but not the total. The comment repository repeats this structure exactly.
6. `src/CrmTicketing.Infrastructure/Persistence/Configurations/TicketConfiguration.cs` — all 62 lines. Names are written PascalCase and rewritten centrally (~lines 9–11). `HasOne<ApplicationUser>().WithMany().HasForeignKey(...)` at ~lines 55–58 declares a foreign key **without a navigation property**, which is how the comment table references both `ticket` and `asp_net_users`.
7. `src/CrmTicketing.Api/Controllers/TicketsController.cs` — all 353 lines. `GetById` (~lines 89–101) shows why a ticket the caller may not see returns **404, never 403**. `RequesterAllowedTransitions` (~lines 334–341) and its `<remarks>` are the precedent for putting an **authorisation** rule at the boundary while the **workflow** rule stays in the domain. `TicketNotFound` (~lines 343–345) and `UnknownEnumValue` (~lines 347–352) are the helpers the new controller mirrors.
8. `src/CrmTicketing.Api/Infrastructure/CallerContext.cs` — all 57 lines. `Access()` (~lines 39–49) is "the single place a role becomes data visibility"; `IsStaff()` (~lines 52–57) is the same translation for capability. **Both are extended in task 4 and task 8b, not duplicated.**
9. `src/CrmTicketing.Api/Infrastructure/DomainExceptionHandler.cs` — `MapStatusCode` (~lines 27–33). `TicketClosedException` already maps to 409 and already emits `operation`. **The 409 requirement needs no handler change** — verify that rather than assuming it.
10. `src/CrmTicketing.Client/Pages/TicketDetail.razor` — all 390 lines. `DisplayTime.Local` at ~lines 56–59, `WriteThenRefreshAsync` (~lines 298–320) with its "the write's own response is discarded deliberately" remark, and `HandleFailure` (~lines 345–371) branching on `ApiRequestException.StatusCode` only. **The page is already 390 lines; task 10 extracts the thread rather than growing it.**
11. `src/CrmTicketing.Client/Services/TicketsApiClient.cs` — all 160 lines. The private `SendAsync<T>` (~lines 105–124) and `ReadFailureMessageAsync` (~lines 126–160) are the one copy of the problem-details parsing. **Route the two new calls through them; do not write a second copy.**
12. `src/CrmTicketing.Infrastructure/Identity/DemoDataSeeder.cs` — all 269 lines. The four guards (~lines 58–128), `Specifications` (~lines 145–159), and `SeedTicketsAsync` (~lines 218–269) building every row **through the aggregate, never an object initialiser**.
13. `tests/CrmTicketing.Infrastructure.Tests/Persistence/TicketRepositoryAccessTests.cs` — the in-memory-`IQueryable` pattern and its `<remarks>` explaining why the EF in-memory provider is deliberately not used. The visibility tests are written the same way.
14. `tests/CrmTicketing.Client.Tests/Pages/TicketDetailTests.cs` — all 410 lines. `StubTicketsApiClient` (~lines 22–90): hand-rolled, recording, settable responses and exceptions. No mocking library.
15. `docs/constitution.md` — §II (line 23) the layer graph, §III (line 46) domain invariants, §IV (line 55) contracts, §VII (line 86) simplicity.
16. Grep for `TicketAccess` across `src/` before starting. Every hit is a place the new visibility rule must **sit beside**, not replace.

---

## Product rules (from story)

| | Current behaviour | New behaviour |
|---|---|---|
| A ticket's conversation | None. A ticket carries a description and nothing else. | A flat, paged, newest-first thread. |
| Reading a ticket | Returns `TicketResponse`. | **Unchanged.** No comment data, no comment count. |
| A `Closed` ticket | Refuses assignment (409) and transitions (409); accepts edits. | Also refuses comments, **409**. |
| A Customer's visibility | Confined to tickets they raised. | Also confined to **public** comments on those tickets. |
| Writing an internal comment | Does not exist. | Staff only. A Customer sending `IsInternal: true` gets **403**, and nothing is stored. |

---

## Implementation tasks

### 1 — The `TicketComment` aggregate

**Create file: `src/CrmTicketing.Domain/Tickets/TicketComment.cs`**

```csharp
public sealed class TicketComment : Entity
{
    public const int MaxBodyLength = 5000;

    public Guid TicketId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; }

    /// <summary>Staff-only when true. Set once, at construction; there is no mutator.</summary>
    public bool IsInternal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TicketComment Write(
        Guid id,
        Guid ticketId,
        Guid authorId,
        string body,
        bool isInternal,
        DateTimeOffset at);
}
```

Named `Write` for the business event, matching `Ticket.Open`. It validates:

- `ticketId` empty → `ArgumentException(..., nameof(ticketId))`
- `authorId` empty → `ArgumentException(..., nameof(authorId))`
- `body` null, empty, or whitespace **after trimming** → `ArgumentException(..., nameof(body))`
- trimmed `body` longer than `MaxBodyLength` → `ArgumentException(..., nameof(body))`

Trim first, then validate, then store the trimmed value — the shape `Ticket.NormaliseDescription` uses. `ArgumentException` already maps to 400 in `DomainExceptionHandler.MapStatusCode`, and the `ParamName` already reaches the response as the `parameter` extension. **No handler change is needed for any of these.**

Add the EF materialisation constructor with the same comment `Ticket.cs` carries at ~lines 38–48: private, parameterless, `: base(Guid.NewGuid())` because the base rejects `Guid.Empty`, and `Body = null!`.

**Properties are `private set`, not get-only.** `Ticket.CreatedBy` at ~lines 68–78 explains why: a static factory assigns after the private constructor has run. It also keeps them discoverable by EF convention, which get-only properties are not — the trap that cost story 03 a regenerated migration.

**No editing and no deleting.** No `Edit`, no `Delete`, no `IsDeleted`. An edited comment raises what the audit trail should show and a deleted one raises whether the thread should say something was removed; both are product questions and neither has been asked. Do not add a soft-delete column "for later".

### 2 — The closed-ticket rule, in the domain

**File: `src/CrmTicketing.Domain/Tickets/Ticket.cs`**

Add one method, beside `Unassign`:

```csharp
/// <summary>
/// Throws when the ticket's state forbids a comment. Called by whatever is about
/// to write one; <see cref="TicketComment"/> is a separate aggregate and cannot
/// ask the ticket itself.
/// </summary>
/// <exception cref="TicketClosedException">The ticket is closed.</exception>
public void EnsureCanBeCommentedOn()
{
    if (Status == TicketStatus.Closed)
    {
        throw new TicketClosedException(Status, "commented on");
    }
}
```

Message: `"A ticket with status Closed cannot be commented on."`

**This is the compromise the aggregate split requires**, and it is worth stating plainly in the code comment: comments live outside the aggregate, and the rule about them lives inside it. A controller writing `if (ticket.Status == TicketStatus.Closed) return Conflict();` would be a second declaration of a domain rule, which §III forbids and which drifts the first time a fifth status appears.

**It costs no extra query.** The API already loads the ticket before commenting, because it must check the caller may see it at all.

`Ticket` gains **no** collection, **no** navigation property, and **no** comment count. The acceptance criteria greps for this.

### 3 — `CommentVisibility`

**Create file: `src/CrmTicketing.Domain/Tickets/CommentVisibility.cs`**

```csharp
public sealed record CommentVisibility
{
    private CommentVisibility(bool includesInternal) => IncludesInternal = includesInternal;

    public bool IncludesInternal { get; }

    /// <summary>Staff. Internal and public alike.</summary>
    public static CommentVisibility All() => new(true);

    /// <summary>Everyone else. Public comments only.</summary>
    public static CommentVisibility PublicOnly() => new(false);
}
```

Deliberately **not a bare `bool` parameter** and deliberately not an enum of role names, for the reason `TicketAccess` states at its ~lines 6–11: the repository must not know what a role is. A `bool includeInternal` argument would be silently invertible at a call site; two named factories are not.

Roles map to a `CommentVisibility` at the API boundary — task 4 — which is the single place that translation happens, exactly as `CallerContext.Access()` already does for tickets.

### 4 — `CallerContext.CommentVisibility()`

**File: `src/CrmTicketing.Api/Infrastructure/CallerContext.cs`**

```csharp
/// <summary>
/// Which comments this caller may see. The counterpart to <see cref="Access"/>:
/// that one confines which tickets, this one confines which comments on them.
/// </summary>
public static CommentVisibility CommentVisibility(this ClaimsPrincipal principal) =>
    principal.IsStaff()
        ? Domain.Tickets.CommentVisibility.All()
        : Domain.Tickets.CommentVisibility.PublicOnly();
```

Built on the existing `IsStaff()` (~lines 52–57), never on a fresh `IsInRole` call. Anyone holding no known role is treated as a Customer and sees public comments only — the same "less visibility, not more" default `Access()` documents at ~lines 43–45.

### 5 — `TicketCommentQuery` and `ITicketCommentRepository`

**Create file: `src/CrmTicketing.Domain/Tickets/TicketCommentQuery.cs`** — modelled on `TicketQuery`:

```csharp
public sealed record TicketCommentQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;

    public Guid TicketId { get; }
    public CommentVisibility Visibility { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int Skip => (Page - 1) * PageSize;

    public static TicketCommentQuery Create(
        Guid ticketId,
        CommentVisibility visibility,
        int page = 1,
        int pageSize = DefaultPageSize);
}
```

`ticketId` and `visibility` are **required and defaultless**, for the reason `TicketQuery.Create` gives about `access` at ~lines 53–56: a caller that forgets either must fail to compile rather than silently receive every row. `Create` throws `ArgumentException` on an empty `ticketId`, and clamps paging rather than rejecting it.

The page sizes deliberately match `TicketQuery`'s. A thread could justify a larger default, but a second paging convention is a thing to keep in sync forever in exchange for fewer round-trips on a screen nobody has complained about.

**Create file: `src/CrmTicketing.Domain/Tickets/ITicketCommentRepository.cs`**

```csharp
public interface ITicketCommentRepository
{
    Task AddAsync(TicketComment comment, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketComment>> ListAsync(TicketCommentQuery query, CancellationToken cancellationToken);

    Task<int> CountAsync(TicketCommentQuery query, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

**A separate interface, not new methods on `ITicketRepository`.** Comments are their own aggregate with their own paging; hanging them off the ticket repository would make "separate aggregate" true in the domain and false in the storage contract. There is no `GetAsync` for a single comment because nothing reads one — no edit, no delete, no permalink. Framework-free, like `ITicketRepository` (~lines 7–13).

### 6 — `TicketCommentRepository`

**Create file: `src/CrmTicketing.Infrastructure/Persistence/TicketCommentRepository.cs`**

`internal sealed class TicketCommentRepository(CrmDbContext context) : ITicketCommentRepository`, structured exactly like `TicketRepository`:

```csharp
/// <summary>
/// Confines the query to what <paramref name="visibility"/> permits. The one place
/// the internal-comment rule is expressed.
/// </summary>
/// <remarks>Internal so it can be tested over an in-memory queryable, with no database.</remarks>
internal static IQueryable<TicketComment> ApplyVisibility(
    IQueryable<TicketComment> comments,
    CommentVisibility visibility) =>
    visibility.IncludesInternal ? comments : comments.Where(c => !c.IsInternal);

internal static IQueryable<TicketComment> Filter(
    IQueryable<TicketComment> comments,
    TicketCommentQuery query)
{
    // Visibility first, then the ticket. A total that counts comments the caller
    // cannot read discloses that a staff conversation is happening.
    comments = ApplyVisibility(comments, query.Visibility);

    return comments.Where(c => c.TicketId == query.TicketId);
}
```

`ListAsync` and `CountAsync` both route through `Filter`, so **the internal filter can never apply to the page but not the total**. That is the failure the acceptance criteria names explicitly, and it is why both are asserted separately in test 9.

Ordering is **newest first**, with a tiebreaker:

```csharp
.OrderByDescending(c => c.CreatedAt)
.ThenByDescending(c => c.Id)
```

Not decoration. Two comments sharing a `CreatedAt` can repeat or vanish across pages without it — the same reasoning as `TicketRepository.ListAsync` at ~lines 34–35. `Id` is version 7, so descending id is descending creation order and the tiebreaker never contradicts the sort. `AsNoTracking()` on both reads, matching the ticket repository.

**File: `src/CrmTicketing.Infrastructure/DependencyInjection.cs`** — one line beside ~line 41:

```csharp
services.AddScoped<ITicketCommentRepository, TicketCommentRepository>();
```

### 7 — Mapping and migration

**Create file: `src/CrmTicketing.Infrastructure/Persistence/Configurations/TicketCommentConfiguration.cs`**

```csharp
builder.ToTable("TicketComment");
builder.HasKey(c => c.Id);

builder.Property(c => c.TicketId).IsRequired();
builder.Property(c => c.AuthorId).IsRequired();
builder.Property(c => c.Body).HasMaxLength(TicketComment.MaxBodyLength).IsRequired();
builder.Property(c => c.IsInternal).IsRequired();
builder.Property(c => c.CreatedAt).IsRequired();

// No navigation properties in either direction: TicketComment never learns that
// ApplicationUser exists, and Ticket never learns that TicketComment does.
builder.HasOne<Ticket>().WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);

// Supports the only query there is: newest-first by ticket.
builder.HasIndex(c => new { c.TicketId, c.CreatedAt }).IsDescending(false, true);
```

**`Cascade` on `ticket_id`, `Restrict` on `author_id`**, and the asymmetry is deliberate. A comment has no meaning without its ticket, so were a ticket ever deleted its thread should go with it. A user is not owned by their comments, and deleting an account must not silently erase what they wrote — the same `Restrict` `TicketConfiguration` uses for `requester_id` at ~line 58. Nothing deletes a ticket today; this states the intent for whoever adds it.

Names are written PascalCase here. `ApplySnakeCaseNames` rewrites the table to `ticket_comment`, the columns to `ticket_id`/`author_id`/`is_internal`/`created_at`, the key to `pk_ticket_comment`, the constraints to `fk_ticket_comment_*`, and the index to `ix_ticket_comment_ticket_id_created_at`. **Assert those names against the emitted migration; do not assume them.**

**Generate the migration:**

```bash
dotnet ef migrations add AddTicketComment --project src/CrmTicketing.Infrastructure --startup-project src/CrmTicketing.Api
```

**Read the emitted file before applying it.** `Up()` must contain exactly one `CreateTable` and one `CreateIndex`, and **must not** contain `DELETE FROM`, `DropColumn`, `AlterColumn`, or any change to the `ticket` table. This migration is purely additive; anything else in it means the model drifted somewhere it should not have.

**`is_internal` is `NOT NULL` from this migration**, not added later. Adding a visibility flag to a table that already holds comments means deciding what every historical comment was, and there is no honest answer. One boolean now costs nothing; retrofitting it costs a judgement call about other people's words.

### 8 — Contracts

**Create file: `src/CrmTicketing.Shared/Contracts/Tickets/CreateCommentRequest.cs`**

```csharp
/// <param name="Body">The comment text. Trimmed, 1-5000 characters.</param>
/// <param name="IsInternal">
/// True for a staff-only comment. A Customer sending true is refused with 403;
/// the value is never silently downgraded to false.
/// </param>
public sealed record CreateCommentRequest(string Body, bool IsInternal);
```

**Create file: `src/CrmTicketing.Shared/Contracts/Tickets/TicketCommentResponse.cs`**

```csharp
public sealed record TicketCommentResponse(
    Guid Id,
    Guid TicketId,
    Guid AuthorId,
    string Body,
    bool IsInternal,
    DateTimeOffset CreatedAt);
```

**`AuthorId` is an opaque `Guid` and there is no author name**, because no endpoint lists users (#43). `RequesterId` and `AssigneeId` are still rendered the same way; when a users endpoint lands, all three improve together. Do not add a `DisplayName` the API would have to invent.

**File: `src/CrmTicketing.Api/Mapping/TicketMapper.cs`** — add `ToResponse(TicketComment)`. Same file: it is the one place a ticket-shaped domain type becomes a contract, and a second mapper class for one method is the speculative split §VII bans.

**`TicketResponse` and `TicketSummaryResponse` do not change.** No comment count, no latest-comment preview. A count would need a second query on every ticket read, and the intake rules it out.

### 8b — `SignInResponse.IsStaff`

**The internal/public toggle must be staff-only, and the Client cannot currently tell.** `RoleNames` lives in `CrmTicketing.Infrastructure`, and §II's layer graph forbids the Client referencing it. `TokenStore.Roles` carries the role *names*, but the grouping — which names count as staff — is a policy, and `CallerContext.IsStaff()` (~lines 52–57) is already the single declaration of it.

**Resolution: add `bool IsStaff` to `SignInResponse`**, populated at sign-in.

**File: `src/CrmTicketing.Shared/Contracts/Auth/SignInResponse.cs`** — one more parameter, documented as "whether the account may act as staff; the server's own answer, not a role name the client interprets."

**File: `src/CrmTicketing.Api/Controllers/AuthController.cs`** — populate it at ~line 72 from the roles already fetched at ~line 69, through `RoleNames.Admin`/`RoleNames.Agent`. **Not a literal.**

**File: `src/CrmTicketing.Client/Services/TokenStore.cs`** — `public bool IsStaff { get; private set; }`, set in `Set`, reset to `false` in `Clear`.

**Why the alternative loses.** Having the Client hold `"Admin"`/`"Agent"` literals and test `Roles.Contains(...)` would be a second declaration of what "staff" means, in a project that cannot reference the first. It would drift the day a fourth role appears, and it would drift silently — the toggle would simply stop appearing for a role that should have it. This is the same additive-contract move story 08 made for `UserId`, for the same reason: a contract missing something a consumer genuinely needs.

**The client's copy is a hint, not enforcement.** Task 9's 403 is the enforcement, and it stands whatever the client believes. A stale token whose holder has since lost the Agent role still renders the toggle and is still refused by the API. Say so in the code comment — a UI flag that looks like a permission check is how a permission check ends up not existing.

### 9 — The endpoints

**Create file: `src/CrmTicketing.Api/Controllers/TicketCommentsController.cs`**

`[ApiController]`, `[Authorize]`, `[Route("api/tickets/{ticketId:guid}/comments")]`. **A separate controller**, not more actions on `TicketsController`: that file is already 353 lines, comments are a separate aggregate with a separate repository, and the route is a genuine sub-resource. It carries its own private `TicketNotFound()` returning the same 404 problem with the same title, so the two controllers cannot disagree about what a missing ticket looks like.

Constructor: `(ITicketRepository tickets, ITicketCommentRepository comments, TimeProvider timeProvider)`.

**`POST /api/tickets/{ticketId}/comments` → 201 | 400 | 403 | 404 | 409**

In this order, and the order is the specification:

1. Load the ticket with `tickets.GetAsync(ticketId, User.Access(), ct)`. Null → **404**. A ticket the caller may not see is indistinguishable from one that does not exist; a 403 here would confirm it exists, which is exactly what story 06's `GetById` avoids.
2. `if (request.IsInternal && !User.IsStaff()) return Forbid();` → **403**. Before any construction and before any write, so **the stored comment is never internal by accident**. Do not silently coerce `IsInternal` to `false` — a Customer who ticked a box and got a public comment has been lied to.
3. `ticket.EnsureCanBeCommentedOn();` → **409** via `DomainExceptionHandler`. The caller is permitted and the request is well-formed; the ticket's state forbids it. Never `Conflict()` written by hand — the exception carries `operation` into the problem details for free.
4. `TicketComment.Write(Guid.CreateVersion7(), ticketId, User.UserId(), request.Body, request.IsInternal, timeProvider.GetUtcNow())` → an invalid body throws `ArgumentException` → **400**.
5. `AddAsync` + `SaveChangesAsync`, return **201**.

**The 403 precedes the 409 deliberately.** A Customer commenting internally on a closed ticket is refused for the reason that is about them, not the one that is about the ticket, and telling them the ticket is closed would confirm a state they should learn about only if they were allowed to act at all.

**201 with no `Location`.** `CreatedAtAction` needs a route that reads one comment, and there is none. Return `StatusCode(StatusCodes.Status201Created, response)` and say in a comment that the absent `Location` is because no single-comment route exists, not an oversight. Do not invent `GET /comments/{id}` to satisfy the convention.

**`GET /api/tickets/{ticketId}/comments` → 200 | 404**

1. Load the ticket the same way. Null → **404**. **Not an empty page** — an empty thread on a ticket the caller cannot see leaks that the ticket exists.
2. `TicketCommentQuery.Create(ticketId, User.CommentVisibility(), page, pageSize)`.
3. `ListAsync` + `CountAsync`, return `PagedResponse<TicketCommentResponse>` with the served page and page size, exactly as `TicketsController.List` does at ~lines 153–157.

Both routes **refuse an unauthenticated caller** through the class-level `[Authorize]`; neither carries `[Authorize(Policy = StaffOnly)]`, because a Customer may read and write public comments on their own ticket.

**`TicketsController` is not edited.** Not one line.

### 10 — The thread in the UI

**File: `src/CrmTicketing.Client/Services/ITicketsApiClient.cs`** — two methods:

```csharp
Task<PagedResponse<TicketCommentResponse>> GetCommentsAsync(Guid ticketId, int page, CancellationToken cancellationToken);

Task<TicketCommentResponse> AddCommentAsync(Guid ticketId, CreateCommentRequest request, CancellationToken cancellationToken);
```

**File: `src/CrmTicketing.Client/Services/TicketsApiClient.cs`** — implement both through the existing private `SendAsync<T>` (~lines 105–124). **`grep -c "ReadFromJsonAsync<ApiProblem>"` must still return 1** after this task.

**Create file: `src/CrmTicketing.Client/Components/TicketComments.razor`** — a new `Components/` folder; the Client has `Pages/`, `Layout/`, and `Services/` only.

`[Parameter] public Guid TicketId { get; set; }`, injecting `ITicketsApiClient` and `TokenStore`. It owns its own loading, error, and busy state, and renders:

- The thread, newest first, each entry showing the body, `@DisplayTime.Local(comment.CreatedAt)`, the `AuthorId` rendered as `TicketDetail.razor` renders `AssigneeId` at ~line 53, and a visible **Internal** marker on an internal comment. A staff user reading a thread must be able to tell at a glance what the requester can see; an unmarked internal comment is one screenshot away from being sent to them.
- An empty-thread message distinct from a failed load.
- A textarea and a Post button. Disabled while busy, and refused client-side when the trimmed body is empty — a request that can only 400 is not worth sending.
- **The internal/public choice only when `Tokens.IsStaff`.** A Customer sees no toggle and posts public comments.
- Paging when `TotalCount` exceeds the served `PageSize`, driven by the response's own `Page`/`PageSize` and never by a client-side constant — the rule story 05 set and `TicketsApiClient.PageSize`'s remark at ~lines 18–22 restates.

**Every post re-fetches.** Discard the 201 response and call `GetCommentsAsync` again, matching `WriteThenRefreshAsync`'s remark at ~lines 290–297. Appending locally would show a thread missing whatever someone else wrote in the meantime, and would show the comment as posted even if it silently was not.

Error handling branches on `ApiRequestException.StatusCode` only, never on message text:

| Status | Behaviour |
|---|---|
| 400 | render the exception message — already the validation text |
| 403 | "You do not have permission to post an internal comment." |
| 404 | "That ticket no longer exists, or is not yours to see." |
| 409 | render the exception message — already says the ticket is closed |
| 401 | navigate to `/signin?returnUrl=…`, render no error |
| other | a generic failure message |

**File: `src/CrmTicketing.Client/Pages/TicketDetail.razor`** — add `<TicketComments TicketId="Id" />` below the edit form, under an `<h2 class="h5">Comments</h2>`. **That is the only change to this file.** Its four display states, transition rendering, assignment conditionals at ~lines 179–191, and `WriteThenRefreshAsync` are untouched. The component does not re-render when the ticket is transitioned, and it does not need to: a comment thread does not change because a status did.

### 11 — Timestamps

Every rendered instant goes through `DisplayTime.Local` (`src/CrmTicketing.Client/Services/DisplayTime.cs`, all 24 lines). **No raw `DateTimeOffset` reaches a screen.** Story 08 fixed exactly this defect on the list and detail views; a new component rendering `@comment.CreatedAt` would reintroduce it in the first place anyone looks.

Verification step 6 greps for it.

### 12 — Demo comments

**File: `src/CrmTicketing.Infrastructure/Identity/DemoTicketSpecification.cs`** — a second record beside the first:

```csharp
/// <param name="TicketIndex">Index into DemoDataSeeder.Specifications.</param>
/// <param name="Author">Which seeded user wrote it.</param>
/// <param name="Body">Obviously synthetic, like every other seeded string.</param>
/// <param name="IsInternal">Whether it is staff-only.</param>
/// <param name="HoursAfterTicket">Offset from the ticket's creation instant.</param>
internal sealed record DemoCommentSpecification(
    int TicketIndex,
    DemoRequester Author,
    string Body,
    bool IsInternal,
    int HoursAfterTicket);
```

**File: `src/CrmTicketing.Infrastructure/Identity/DemoDataSeeder.cs`** — a `CommentSpecifications` list beside `Specifications` (~lines 145–159), `internal static` and pure so its shape is assertable without a database. Cover, at minimum:

- a ticket with a **customer comment and an agent reply**, so the core loop is visible;
- at least one **internal** comment on a ticket the Customer can see, so signing in as each user shows a **different thread** — this is the only way the visibility rule is demonstrable from the demo data;
- at least one ticket with **no comments**, so the empty state is reachable;
- **no comment on a `Closed` ticket**. `EnsureCanBeCommentedOn` is not consulted by the seeder — comments are written directly through `TicketComment.Write` — so a closed-ticket comment would seed a state the API refuses to create. A test asserts the demo set contains none.

Seed them after the tickets are saved: `ticket_comment.ticket_id` carries a foreign key, so the tickets must be committed first. The seeder resolves `ITicketCommentRepository` from the same scope. The four existing guards (~lines 58–128) are unchanged — comments seed only when tickets do.

`LogSeeded` gains a comment count; update the `[LoggerMessage]` template and its call site together.

### 13 — The activity timeline is not this story

Open a GitHub issue: **"Activity timeline: a stored event history for status, assignment, and priority changes"**. The body should say it was split out of #11 deliberately, that it needs a stored event history rather than authored text, and that shipping half of each is what the split avoided. Add the number to the intake's Dependencies line and to this feature's `00-overview.md`.

Do this **before** closing #11, so the split is recorded rather than remembered.

---

## Edge Cases & Failure Modes

- **A comment on a `Closed` ticket.** `Ticket.EnsureCanBeCommentedOn` (task 2) throws `TicketClosedException`, which `DomainExceptionHandler.MapStatusCode` already maps to **409** with `operation: "commented on"`. **Not 400** (the request is well-formed) and **not 403** (the caller is permitted). Test 5 pins the status code, not the message.
- **A Customer sending `IsInternal: true`.** **403**, before construction and before any write. Test 14 asserts the repository received nothing — a 403 that still stored a row is the defect this ordering exists to prevent.
- **A Customer reading a thread containing internal comments.** They are absent from **both the page and the `TotalCount`**. A filtered page with an unfiltered total tells them how many comments they are not being shown, which discloses that a staff conversation is happening. Test 9 asserts both.
- **A comment on a ticket the caller may not see.** **404** from `tickets.GetAsync` returning null, on both routes. Never 403, and never an empty 200 on the read — an empty thread for a ticket that exists is still a confirmation that it exists.
- **A comment on a ticket that does not exist.** Identical 404, by construction. The two cases are deliberately indistinguishable, as `ITicketRepository.GetAsync` documents at ~lines 16–20.
- **An empty or whitespace-only body.** Trimmed first, then rejected with `ArgumentException(nameof(body))` → **400** with `parameter: "body"`. `"   "` and `""` behave identically.
- **A body of exactly 5000 characters after trimming.** Accepted. 5001 rejected. Test 3 pins both sides of the boundary — an off-by-one here is a rule nobody can see.
- **Unicode in a body.** `MaxBodyLength` counts UTF-16 code units, as `Ticket.MaxDescriptionLength` already does. An emoji-heavy comment is refused sooner than its visible length suggests. Consistent with the existing rule and **not** changed here; if that is wrong it is wrong for descriptions too, and it is one story for both.
- **`page=0` or `pageSize=1000`.** Clamped inside `TicketCommentQuery.Create`, never rejected and never clamped in the controller. A page past the end returns an empty `Items` with the true `TotalCount`.
- **Two comments sharing a `CreatedAt`.** The `ThenByDescending(c => c.Id)` tiebreaker keeps paging stable. Without it a comment can appear on two pages or on none. The repository tests seed identical instants.
- **A comment whose `AuthorId` refers to a deleted user.** Cannot occur: `author_id` carries a `Restrict` foreign key, so the delete fails rather than the comment orphaning. There is no user-delete route today; task 7 states the intent for when there is.
- **A stale `IsStaff` on the client.** The token still says Agent after the role was revoked, so the toggle renders and the API answers 403. Correct, and the reason task 8b's flag is documented as a hint. The reverse — newly granted, no toggle until re-sign-in — is the same 60-minute token lifetime story 08 already lives with.
- **A 401 mid-thread.** Any call may 401 at any time; the component navigates to `/signin?returnUrl=…` rather than rendering an error, matching `TicketDetail.HandleFailure` at ~lines 353–357.
- **The thread failing to load while the ticket loads.** The ticket renders and the comments section shows its own error with a retry. Blanking a working page because a sub-resource failed is the failure story 05 rejected for metadata.
- **A post succeeding and the re-fetch failing.** Leave the last known thread on screen and say the comment was posted but the thread could not be reloaded. Do not roll back the display — the same rule `RefreshQuietlyAsync` follows at ~lines 322–341.
- **Concurrent posts.** Two people posting at once both succeed and both appear on the next fetch. There is no conflict to detect — comments are append-only, which is one reason the aggregate is separate. **No real-time updates:** someone else's comment appears on the next fetch, not before.
- **A half-applied migration.** `AddTicketComment` is a single additive `CreateTable` plus one `CreateIndex`. If it fails, `Down()` drops the table and nothing else is touched — no existing table is altered, unlike `AddIdentityAndTicketActor`. **This migration must contain no `DELETE`, no `DropColumn`, and no `AlterColumn`;** verification step 8 greps for them.

---

## Test Plan

### Domain — `tests/CrmTicketing.Domain.Tests/Tickets/`

**Create file: `TicketCommentTests.cs`**

1. `Write_StoresTheTrimmedBody` — leading and trailing whitespace removed, `IsInternal`, `AuthorId`, `TicketId`, and `CreatedAt` all as supplied.
2. `Write_RejectsAnEmptyBody` — `[Theory]` over `null`, `""`, `"   "`, `"\t\n"`. `Assert.Throws<ArgumentException>` with `ParamName == "body"`.
3. `Write_EnforcesTheBodyBoundary` — 5000 characters accepted, 5001 rejected. Both sides, one test.
4. `Write_RejectsAnEmptyTicketIdAndAnEmptyAuthorId` — two cases, each asserting the right `ParamName`. An empty author is the failure this whole story is about; it must not construct.

**File: `TicketTests.cs`** — extend:

5. `EnsureCanBeCommentedOn_ThrowsOnAClosedTicket` — `Assert.Throws<TicketClosedException>`, **exact type, not `ThrowsAny`**. `ThrowsAny` would let a revert to a plain `InvalidOperationException` pass while the endpoint silently regressed from 409 to 500 — the correction made to this file's line 177 in story 04, for the same reason.
6. `EnsureCanBeCommentedOn_AllowsEveryOtherStatus` — `[Theory]` over `New`, `Open`, `Pending`, `Resolved`. Does not throw.
7. `Ticket_HasNoCommentCollection` — reflection over `typeof(Ticket).GetProperties()`, asserting none is assignable to `IEnumerable<TicketComment>`. Pins the "not an owned collection" criterion in code rather than only in a grep, so it survives a refactor a grep would not catch.

**Create file: `TicketCommentQueryTests.cs`**

8. `Create_ClampsPaging` — `page=0` → 1, `pageSize=0` → default, `pageSize=1000` → `MaxPageSize`. Mirrors `TicketQueryTests`.

### Infrastructure — `tests/CrmTicketing.Infrastructure.Tests/Persistence/`

**Create file: `TicketCommentRepositoryVisibilityTests.cs`** — the in-memory-`IQueryable` pattern from `TicketRepositoryAccessTests.cs`, no database.

9. `Filter_HidesInternalCommentsFromAPublicOnlyCaller` **and** `Filter_CountsOnlyWhatAPublicOnlyCallerMaySee` — two tests, page and count. **This is the acceptance criterion's named verification**: it calls the repository directly as a Customer and asserts an internal comment is absent from both.
10. `Filter_ConfinesToTheRequestedTicket` — comments on another ticket are excluded, including from the count.
11. `ApplyVisibility_ReturnsEverythingForStaff` — an internal comment survives `CommentVisibility.All()`. The mutation guard: without it, a `Filter` that dropped internal comments unconditionally would still pass test 9.

Ordering is asserted where it is implemented — over the queryable, with two comments sharing a `CreatedAt`, proving the `Id` tiebreaker makes the order total.

**File: `tests/CrmTicketing.Infrastructure.Tests/Identity/DemoTicketPlanTests.cs`** — extend:

12. `DemoComments_ReferenceRealTicketsAndNeverAClosedOne` — every `TicketIndex` is in range, and no comment targets a specification whose `TargetStatus` is `Closed`. A closed-ticket comment would seed a state the API refuses to create.
13. `DemoComments_IncludeAnInternalCommentOnACustomerVisibleTicket` — otherwise the visibility rule is invisible in the demo, which is the whole reason task 12 exists.

### API — `tests/CrmTicketing.Api.Tests/Controllers/`

**Create file: `TicketCommentsControllerTests.cs`** — a `FakeTicketCommentRepository` beside the existing `FakeTicketRepository` pattern (`TicketsControllerTests.cs`, ~lines 29–78), plus `FixedTimeProvider` and `TestProblemDetailsFactory` from the same file.

14. `Post_AsACustomer_WithIsInternalTrue_ReturnsForbidAndStoresNothing` — **403 and the fake repository is empty.** Both halves; a 403 that still wrote a row is the exact defect the ordering in task 9 prevents.
15. `Post_ToAClosedTicket_Throws_TicketClosedException` — asserts the exception propagates to `DomainExceptionHandler` rather than the controller returning `Conflict()` itself. Paired with an existing `DomainExceptionHandlerTests` case proving that type maps to 409.
16. `Post_ToATicketTheCallerMayNotSee_Returns404` — a Customer posting to another customer's ticket. **404, never 403.**
17. `Post_UsesTheCallerAsAuthor_NotTheBody` — the stored `AuthorId` is `User.UserId()`. There is no author field on the request; this pins that no future one is honoured.
18. `Post_AsStaff_WithIsInternalTrue_StoresAnInternalComment` — the positive case, without which test 14 passes for a controller that refuses everything.
19. `Get_ReturnsNewestFirst_Paged` — the served `Page`, `PageSize`, and `TotalCount` come from the query and the repository, not from constants.
20. `Get_ForATicketTheCallerMayNotSee_Returns404` — **not an empty 200.**
21. `Get_AsACustomer_PassesPublicOnlyVisibility` and `Get_AsStaff_PassesAll` — the fake records the `CommentVisibility` it was handed. Pins `CallerContext.CommentVisibility` at the boundary where the translation happens.
22. `Endpoints_RequireAuthentication` — reflection over the controller asserting the class carries `[Authorize]`, matching the existing convention in `TicketsControllerTests`.

**File: `tests/CrmTicketing.Api.Tests/Controllers/AuthControllerTests.cs`** — extend:

23. `SignIn_ReportsIsStaffForAnAgent_AndNotForACustomer` — task 8b's flag comes from the server's own role check.

### Client — `tests/CrmTicketing.Client.Tests/`

**Create file: `Components/TicketCommentsTests.cs`** — `BunitContext`, extending `StubTicketsApiClient` with the two new methods.

24. `Thread_RendersNewestFirstWithLocalTimestamps` — order comes from the stub's response, and each timestamp equals `DisplayTime.Local(...)`. **Not a raw `DateTimeOffset`** — story 08 fixed this defect once already.
25. `Thread_MarksInternalCommentsVisibly` — an internal comment renders a marker a public one does not.
26. `Post_RefetchesRatherThanAppending` — after a successful post the stub records a second `GetCommentsAsync`, and the rendered thread comes from it. Make the two responses differ so the assertion can tell them apart.
27. `Toggle_IsHiddenForANonStaffUser_AndShownForStaff` — driven by `TokenStore.IsStaff`, both directions.
28. `Post_Forbidden_RendersAPermissionMessage` — a 403 renders the permission text and no stack trace, `traceId`, or exception type name.
29. `Post_Conflict_RendersTheClosedMessage` — a 409 renders the message and the thread is unchanged.
30. `Post_Unauthorised_NavigatesToSignIn` — a 401 leaves the URI at `/signin` with a `returnUrl`, and renders no error alert.
31. `Post_RefusesAnEmptyBody_WithoutCallingTheApi` — a request that can only 400 is not sent.
32. `Thread_EmptyRendersDistinctlyFromFailed` — the empty state and the error state are different elements.

**File: `tests/CrmTicketing.Client.Tests/Services/TokenStoreTests.cs`** — extend:

33. `Set_StoresIsStaffFromTheResponse` and `Clear_ResetsIsStaff`.

**File: `tests/CrmTicketing.Client.Tests/Pages/TicketDetailTests.cs`** — extend:

34. `Detail_RendersTheCommentsSection` — the component is present. The existing 410 lines of assertions must keep passing; if task 10's single insertion breaks one, it changed the page structure those tests depend on.

### What is not tested

35. **No automated test exercises a real write against the API or the database.** The repository tests run over an in-memory `IQueryable`, which is a different query engine than PostgreSQL — the `!c.IsInternal` predicate is proved in LINQ-to-Objects, not in SQL. Issue #29 owns that gap and this story does not close it. Verification step 12 is the manual compensation, and it is not automated coverage.

---

## Migration / Rollback

`AddTicketComment` is **purely additive**: one `CreateTable`, one `CreateIndex`, two foreign keys. No existing table is altered and no row is deleted, unlike `AddIdentityAndTicketActor`.

- **Rollback:** `dotnet ef database update AddIdentityAndTicketActor --project src/CrmTicketing.Infrastructure --startup-project src/CrmTicketing.Api` drops `ticket_comment` and everything in it. Comments are the only data lost; tickets and users are untouched.
- **Half-applied state:** if the table is created but a foreign key fails, EF's transaction rolls the whole migration back on PostgreSQL — DDL is transactional there. A partially created table is not a state to plan for.
- **Deploying the API before the migration** returns 500 on both comment routes and leaves every other route working, because nothing else queries the table. Migrate first regardless.

---

## Verification Steps

1. **Backend builds:** `dotnet build CrmTicketing.slnx` — zero warnings, zero errors under `TreatWarningsAsErrors`.
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — four test projects, **no API and no database**.
3. **`Ticket` holds no comment collection:**

    ```bash
    grep -rn "ICollection<TicketComment>\|List<TicketComment>\|IEnumerable<TicketComment>" src/CrmTicketing.Domain/Tickets/Ticket.cs
    ```

    Returns no output. This is the acceptance criterion's own command.
4. **The closed-ticket rule is declared once:**

    ```bash
    grep -rn "TicketStatus.Closed" --include='*.cs' src/CrmTicketing.Api/
    ```

    Returns only the `RequesterAllowedTransitions` entries in `TicketsController.cs`. **No comment controller may name `Closed`** — the rule is `EnsureCanBeCommentedOn`.
5. **The visibility filter is declared once:**

    ```bash
    grep -rn "IsInternal" --include='*.cs' src/ | grep -v "src/CrmTicketing.Shared\|Configurations\|Migrations"
    ```

    The only filtering hit is `TicketCommentRepository.ApplyVisibility`. **No `Where(c => !c.IsInternal)` in a controller and none in the Client.**
6. **No raw timestamp in the new component:**

    ```bash
    grep -n "CreatedAt" src/CrmTicketing.Client/Components/TicketComments.razor
    ```

    Every rendering hit is wrapped in `DisplayTime.Local`.
7. **One copy of the problem-details parsing:** `grep -c "ReadFromJsonAsync<ApiProblem>" src/CrmTicketing.Client/Services/TicketsApiClient.cs` returns `1`.
8. **The migration is additive only:**

    ```bash
    grep -nE "DELETE|DropColumn|AlterColumn|DropTable" src/CrmTicketing.Infrastructure/Persistence/Migrations/*AddTicketComment.cs
    ```

    Returns only the `DropTable` inside `Down()`. Anything in `Up()` means the model drifted.
9. **The emitted names match the convention:**

    ```bash
    grep -nE "ticket_comment|is_internal|ix_ticket_comment" src/CrmTicketing.Infrastructure/Persistence/Migrations/*AddTicketComment.cs
    ```

    Confirms `ApplySnakeCaseNames` reached the new table, its columns, and its index. `is_internal` must be `nullable: false`.
10. **The ticket endpoints are untouched:**

    ```bash
    git diff --stat main -- src/CrmTicketing.Api/Controllers/TicketsController.cs src/CrmTicketing.Shared/Contracts/Tickets/TicketResponse.cs src/CrmTicketing.Shared/Contracts/Tickets/TicketSummaryResponse.cs
    ```

    Returns no output. The only contract change outside the new files is `SignInResponse` — task 8b.
11. **Apply the migration:**

    ```bash
    dotnet ef database update --project src/CrmTicketing.Infrastructure --startup-project src/CrmTicketing.Api
    ```

    Then confirm the table and index exist: `\d ticket_comment` in `psql`.
12. **Manual, with the API, client, and PostgreSQL running against a freshly reseeded demo database.** Sign in as the **Agent**: open a ticket with seeded comments, confirm the thread renders newest-first with local timestamps and that internal comments are marked. Post a public comment, then an internal one, and confirm both appear after the re-fetch. Open a **`Closed`** ticket and confirm posting is refused with the **conflict message**, not a stack trace. Then sign in as the **Customer**: open the same ticket and confirm **the internal comments are absent, the count is lower, and there is no internal/public toggle**. Post a comment as the Customer and confirm the Agent sees it. **Report the two comment counts, not that it passed.**

---

## Done Criteria

- [ ] `TicketComment` is its own aggregate under `Domain/Tickets/`, referencing a ticket by id; `Ticket` has no comment collection and reading a ticket loads none.
- [ ] Construction rejects an empty author, an empty ticket id, and a body outside 1–5000 characters after trimming, each with `ArgumentException` naming the parameter.
- [ ] The closed-ticket rule is `Ticket.EnsureCanBeCommentedOn`, in the domain, and returns **409** — not 400 and not 403.
- [ ] `IsInternal` is `NOT NULL` from the first migration.
- [ ] A Customer never receives an internal comment, and the filter is in `TicketCommentRepository`, applied to **both the page and the count**.
- [ ] A Customer sending `IsInternal: true` gets **403** and nothing is stored.
- [ ] `POST` and `GET /api/tickets/{id}/comments` exist with the documented statuses; both refuse an unauthenticated caller and both return **404** for a ticket the caller may not see.
- [ ] Contracts are sealed records in `Shared/Contracts/Tickets/`; `AuthorId` is an opaque `Guid` and no author name is invented.
- [ ] The detail view shows the thread and a post box, with the internal/public choice visible only to staff; every post re-fetches.
- [ ] Every rendered timestamp goes through `DisplayTime.Local`.
- [ ] The migration adds `ticket_comment` with foreign keys to `ticket` and `asp_net_users` and a `(ticket_id, created_at DESC)` index.
- [ ] The demo seeder produces comments including an internal one on a customer-visible ticket, and none on a closed ticket.
- [ ] `TicketsController`, `TicketResponse`, and `TicketSummaryResponse` are unchanged.
- [ ] The activity-timeline issue is opened and its number recorded in the intake and the overview.
- [ ] `dotnet build CrmTicketing.slnx` clean; `dotnet test CrmTicketing.slnx` passes with no API and no database.
- [ ] `00-overview.md` updated with this story.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 10 (issue #16, permission-gated endpoints and UI).**
