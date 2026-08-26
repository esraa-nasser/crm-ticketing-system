# persistence — plan overview

Entry point for the **persistence** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 02 | [02-story-add-crmdbcontext-3.md](02-story-add-crmdbcontext-3.md) | Add CrmDbContext and the IEntityTypeConfiguration convention | 3 | Story 01 (scaffold) |

## Dependency notes

- **Provider decided at epic level.** Issue #2 settles PostgreSQL; story 02 assumes it and records the decision in `docs/architecture.md`. Changing provider later means revising that entry, not just swapping a package.
- **Story 02 creates no schema.** The model is deliberately empty — no `DbSet`, no aggregate, no migration. Issue #4 (migrations and seed data) is the next story in this feature and depends entirely on 02.
- **The mapping convention is the contract.** Every later aggregate registers itself through an `IEntityTypeConfiguration<T>` under `Persistence/Configurations/`, discovered by `ApplyConfigurationsFromAssembly`. Aggregate stories add a configuration class; they never edit `CrmDbContext.OnModelCreating`.
- **snake_case is applied centrally.** `SnakeCaseNaming.ApplySnakeCaseNames` runs after configurations, so an aggregate that sets an explicit table name still gets converted. An aggregate needing a verbatim name must say so in its own plan.
- **`CrmDbContextTests.Model_HasNoEntityTypes` is expected to be deleted** by the first aggregate story. Its removal is the deliberate signal that the "no aggregates yet" boundary has moved.
- **Open question carried forward.** Story 02 registers persistence through the `AddPersistence` extension rather than a domain-declared interface, because with zero aggregates an `IUnitOfWork` would have no callers (constitution §VII). The first aggregate story decides whether a domain-side abstraction is warranted.
