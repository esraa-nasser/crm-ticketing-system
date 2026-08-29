# ticketing-core — plan overview

Entry point for the **ticketing-core** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 03 | [03-story-ticket-aggregate-8.md](03-story-ticket-aggregate-8.md) | Ticket aggregate, status workflow, and mapping | 8 (closes 9) | Story 02 (persistence) |

## Dependency notes

- **Story 03 covers two issues.** #8 (the aggregate) and #9 (status, priority, transitions) are planned as one story because a `Ticket` without a status is meaningless and a status enum with no consumer cannot be verified. Both close together.
- **The transition table is the contract.** `TicketStatusTransitions` is the single declaration of legal status moves. The API, the client, and any future workflow engine consult it — none of them re-encode it. A verification step greps for duplication.
- **`Closed` is terminal.** Reopening happens from `Resolved` only. Changing this later is one row in the table plus a test, but it is a product decision, not an implementation detail.
- **The domain never reads a clock.** Every mutator takes `DateTimeOffset at`. `TimeProvider` is registered in the API's composition root and supplies the instant at the call site, keeping the domain deterministic.
- **First consumer of the mapping convention.** `TicketConfiguration` is the first `IEntityTypeConfiguration<T>` in the solution, so story 03 is where story 02's `ApplyConfigurationsFromAssembly` and `ApplySnakeCaseNames` are proved on a real entity rather than an empty model.
- **`Model_HasNoEntityTypes` is deleted here.** That test existed to pin the "no aggregates yet" boundary; its removal is the deliberate signal the boundary moved.
- **Second migration, not the first.** `InitialCreate` (empty) already exists from the work done ahead of issue #4. `AddTicket` is the first migration whose `Up()` does anything, and it needs no `--output-dir` because EF follows the existing folder.

## Unblocks

| Issue | Why it was blocked |
|---|---|
| #10 — Ticket CRUD endpoints | Needs an aggregate to expose and a status machine to validate against |
| #4 — seed data | Needs a table with columns to seed |
| #21 — SLA policies | Needs `CreatedAt`, `Status`, and `Priority` to compute against |
| #11–#14 — ticket UI | Needs contracts from #10, which need this |
