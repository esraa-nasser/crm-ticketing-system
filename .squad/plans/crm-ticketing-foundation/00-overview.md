# crm-ticketing-foundation — plan overview

Entry point for the **crm-ticketing-foundation** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 01 | [01-story-crm-ticketing-mvp-foundation.md](01-story-crm-ticketing-mvp-foundation.md) | Repository, layer graph, and SDD baseline | *(unlinked)* | — |

## Dependency notes

Story 01 establishes the contracts every later story is planned against:

- **Layer graph** — `Domain` depends on nothing; `Infrastructure → Domain`;
  `Api → {Domain, Infrastructure, Shared}`; `Client → Shared`. A plan that needs
  a new edge must first amend `docs/constitution.md` §II.
- **Wire contracts** — every endpoint takes and returns a `sealed record` from
  `CrmTicketing.Shared.Contracts`. `ApiInfoResponse` is the reference shape.
- **Verification commands** — `dotnet build CrmTicketing.slnx` and
  `dotnet test CrmTicketing.slnx`. Plans should name these, plus any narrower
  filter (e.g. `--filter FullyQualifiedName~Tickets`).
- **Injected time** — `TimeProvider` is registered in the API's composition root.
  Stories touching SLA clocks, due dates, or audit stamps must resolve it rather
  than calling `DateTime.UtcNow`.
- **Package versions** — new dependencies add a `PackageVersion` to
  `Directory.Packages.props`; the `.csproj` reference carries no version.

Deliberately left open by this feature, so downstream plans must decide it
explicitly rather than inheriting a default:

| Open decision | Owning candidate feature |
|---|---|
| Data store, ORM, migrations | `persistence` |
| Ticket status machine and SLA model | `ticketing-core` |
| Whether customers are a separate aggregate | `customers-crm` |
| Identity provider and role model | `auth-roles` |
| Polling vs SignalR for live updates | `reporting-dashboard` |
