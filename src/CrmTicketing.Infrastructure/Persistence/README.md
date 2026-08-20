# Persistence

Deliberately empty. The data store, ORM, and migration strategy for the CRM
ticketing domain are decided by the **first planned story**, not by the scaffold.

When that story lands, this folder holds:

- `CrmDbContext.cs` — the EF Core context
- `Configurations/` — one `IEntityTypeConfiguration<T>` per aggregate
- `Migrations/` — generated, never hand-edited

Rules from `docs/constitution.md` that apply here:

- `CrmTicketing.Domain` must not reference EF Core. Mapping lives in this project.
- No `DbContext` type ever crosses into `CrmTicketing.Api` controllers directly;
  it is reached through an abstraction declared in the domain or application layer.
