# Story intake

- Folder: `.squad/stories/ticketing-core/8/intake.md`

---

## Feature

- **Feature name (display):** Ticketing core — Ticket aggregate
- **Feature slug (folder under `plans/`):** `ticketing-core`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `8` *(this story also closes #9)*
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:domain`, `sdd:needs-plan`

---

## Title

```
Model the Ticket aggregate, its status workflow, and its persistence mapping
```

---

## Description

```
Create the Ticket aggregate: the first real domain type in the product, and the
first consumer of the IEntityTypeConfiguration convention established by story 02.

Covers GitHub issues #8 (Model the Ticket aggregate on the Entity base type) and
#9 (Model TicketStatus, TicketPriority, and the transition table). These are one
cohesive unit - a Ticket without a status is meaningless, and a status enum with
no consumer cannot be verified. Splitting them produces a half-state.

The story ends at the database boundary. The aggregate exists, enforces its own
rules, maps to a table, and has a migration that creates it. Nothing above the
domain and infrastructure layers is touched: no endpoints, no contracts, no UI.

This is also the story that gives the empty InitialCreate migration something to
follow, and gives ApplyConfigurationsFromAssembly its first configuration to find.
```

---

## Acceptance criteria

```
- [ ] TicketStatus has exactly five members: New, Open, Pending, Resolved, Closed.
      TicketPriority has exactly four: Low, Normal, High, Urgent.
- [ ] Ticket derives from CrmTicketing.Domain.Common.Entity. No public setter
      allows an invariant to be bypassed.
- [ ] Construction rejects each of these with ArgumentException naming the
      parameter: null/empty/whitespace title, title of 201 chars, null/empty
      description, description of 10001 chars, Guid.Empty requester.
- [ ] A newly constructed Ticket has Status == New regardless of what the caller
      passes, and Priority == Normal when none is supplied.
- [ ] TransitionTo enforces the full matrix. The test enumerates all 25
      (from, to) pairs - not a sample - asserting the 11 legal ones succeed and
      the other 14 throw InvalidTicketTransitionException.
- [ ] Closed is terminal: every TransitionTo from Closed throws.
- [ ] Assign rejects Guid.Empty, and rejects assigning a ticket in Closed.
- [ ] The transition table appears exactly once in the solution.
      Verify: grep -rln "TicketStatus.Resolved" src/ shows only Domain files.
- [ ] The domain stays pure: grep -cE "(Project|Package)Reference"
      src/CrmTicketing.Domain/CrmTicketing.Domain.csproj is still 0, and
      grep -rn "DateTime.UtcNow\|DateTime.Now" src/ returns nothing.
- [ ] TicketConfiguration exists under Persistence/Configurations/, is discovered
      by ApplyConfigurationsFromAssembly, and stores both enums as strings.
- [ ] snake_case applied: a test asserts the entity maps to table "ticket" with
      columns including "requester_id" and "created_at".
- [ ] A migration named AddTicket is generated into Persistence/Migrations and
      its Up() creates the ticket table - the first non-empty migration.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes, all test projects.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #3 (CrmDbContext, merged), #4 (migration tooling, partially done). Closes #8 and #9. Unblocks #10 (endpoints) and #21 (SLA).
- **Depends on code areas or other stories:** `CrmTicketing.Domain.Common.Entity`, `CrmDbContext.OnModelCreating`, `SnakeCaseNaming`.

## Extra notes

- `CrmDbContextTests.Model_HasNoEntityTypes` must be **deleted** by this story. Its removal is the deliberate signal that the "no aggregates yet" boundary has moved.

## Technical hints

```
DECISIONS MADE

Status set - exactly five:
  New       created, not yet triaged
  Open      being worked
  Pending   waiting on the customer
  Resolved  fix delivered, awaiting confirmation
  Closed    terminal

Legal transitions (everything else is illegal):
  New      -> Open, Closed
  Open     -> Pending, Resolved, Closed
  Pending  -> Open, Resolved, Closed
  Resolved -> Open, Closed
  Closed   -> (nothing)

Closed is TERMINAL. Reopening happens from Resolved only. An unambiguous terminal
state keeps SLA maths and reporting honest; a customer returning after closure is
a new interaction, linked later by a related-ticket field.

Priority - a fixed domain enum: Low, Normal, High, Urgent. Default Normal.
Priority drives SLA branches in code, so it cannot be a database-editable list.

Category - an OPTIONAL free-text string, max 100 chars, for this story only.
A Category lookup entity is a taxonomy decision with its own story.

Invariants - Ticket refuses construction without:
  Title        required, trimmed, 1-200 chars, as a TicketTitle value object
  Description  required, trimmed, 1-10000 chars
  RequesterId  required, non-empty Guid (opaque - no Contact entity exists yet)
  AssigneeId   optional
  Status       always starts as New; callers cannot choose
  Priority     supplied, defaults to Normal

Time: the domain takes DateTimeOffset as a parameter. It must NOT depend on
TimeProvider or call DateTime.UtcNow - the caller supplies the instant.

Persistence: enums stored as strings via HasConversion<string>(), so the database
stays readable and reordering the enum cannot corrupt data.
```

## Out of scope

```
- No API endpoints, controllers, or Shared contracts. Ticket CRUD is issue #10.
- No UI of any kind. List, detail, and kanban are issues #11-#14.
- No comments or activity timeline (issue #10).
- No SLA policy, due dates, or business-hours maths (issue #21).
- No Category entity or lookup table. Category is a plain optional string here.
- No human-readable ticket number (TCK-1001). Sequence generation is an
  infrastructure concern with its own story.
- No Contact, Account, or User entity. RequesterId and AssigneeId are opaque
  Guids with no foreign key, because no such aggregate exists yet.
- No authorisation. Who may transition a ticket is issues #5, #6, and #16.
- No soft delete, no audit history beyond CreatedAt and UpdatedAt.
- No seed data (issue #4).
- No repository or query abstraction. Persistence access arrives with the
  endpoints that need it - constitution section VII.
- No change to the layer graph in docs/architecture.md.
```
