# ticketing-core — plan overview

Entry point for the **ticketing-core** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 03 | [03-story-ticket-aggregate-8.md](03-story-ticket-aggregate-8.md) | Ticket aggregate, status workflow, and mapping | 8 (closes 9) | Story 02 (persistence) |
| 04 | [04-story-ticket-endpoints-10.md](04-story-ticket-endpoints-10.md) | Ticket CRUD endpoints with Shared contracts | 10 | Story 03 |

## Dependency notes

- **Story 03 covers two issues.** #8 (the aggregate) and #9 (status, priority, transitions) are planned as one story because a `Ticket` without a status is meaningless and a status enum with no consumer cannot be verified. Both close together.
- **The transition table is the contract.** `TicketStatusTransitions` is the single declaration of legal status moves. The API, the client, and any future workflow engine consult it — none of them re-encode it. A verification step greps for duplication.
- **`Closed` is terminal.** Reopening happens from `Resolved` only. Changing this later is one row in the table plus a test, but it is a product decision, not an implementation detail.
- **The domain never reads a clock.** Every mutator takes `DateTimeOffset at`. `TimeProvider` is registered in the API's composition root and supplies the instant at the call site, keeping the domain deterministic.
- **First consumer of the mapping convention.** `TicketConfiguration` is the first `IEntityTypeConfiguration<T>` in the solution, so story 03 is where story 02's `ApplyConfigurationsFromAssembly` and `ApplySnakeCaseNames` are proved on a real entity rather than an empty model.
- **`Model_HasNoEntityTypes` is deleted here.** That test existed to pin the "no aggregates yet" boundary; its removal is the deliberate signal the boundary moved.
- **Second migration, not the first.** `InitialCreate` (empty) already exists from the work done ahead of issue #4. `AddTicket` is the first migration whose `Up()` does anything, and it needs no `--output-dir` because EF follows the existing folder.
- **Story 04 answers persistence's open question.** Story 02 registered `CrmDbContext` behind `AddPersistence` because no caller existed. Story 04 is that caller, and it settles the shape: one domain-declared `ITicketRepository`, `SaveChangesAsync` on the repository, no `IUnitOfWork` until a transaction spans two aggregates. The note in [`../persistence/00-overview.md`](../persistence/00-overview.md) is closed by that story.
- **Story 04 adds no migration.** It maps existing columns onto contracts; `TicketConfiguration` and `AddTicket` are untouched, and a changed model snapshot is a signal something went wrong.
- **Enums stop at the boundary.** `CrmTicketing.Shared` cannot reference `CrmTicketing.Domain` (constitution §II), so status and priority cross the wire as strings and are parsed in the API. `GET /api/tickets/metadata` is what keeps the client from hardcoding either list.
- **Story 04 adds `Ticket.UpdateDetails`.** Story 03 shipped no mutator for title, description, or category because nothing called for one; `PATCH` is that caller. The aggregate grows in the story that needs it, not ahead of it.

## Unblocks

| Issue | Why it was blocked |
|---|---|
| #4 — seed data | Needs a table with columns to seed (unblocked by story 03) |
| #21 — SLA policies | Needs `CreatedAt`, `Status`, and `Priority` to compute against (unblocked by story 03) |
| #11 — comments and activity timeline | Needs the ticket HTTP surface from story 04 |
| #12–#14 — ticket UI | Needs the contracts and the metadata endpoint from story 04 |
| #16 — permission-gated endpoints | Needs routes to gate, from story 04 |
