# Story 02 — Add CrmDbContext and the IEntityTypeConfiguration convention (Story: 3)

## Prerequisites

- Story 01 completed: [`../crm-ticketing-foundation/01-story-crm-ticketing-mvp-foundation.md`](../crm-ticketing-foundation/01-story-crm-ticketing-mvp-foundation.md) — established the layer graph, central package management, and the `AddBlazorClientCors` extension pattern this story mirrors.
- Provider decision made at epic level (issue #2): **PostgreSQL**. This plan assumes it and records it in `docs/architecture.md`.
- No running PostgreSQL instance is required to complete or verify this story. Nothing here opens a connection.

---

## Story Goal

Give `CrmTicketing.Infrastructure` a working EF Core context for PostgreSQL, wired into the API's composition root through a single extension method, with a mapping convention that later aggregates plug into without further ceremony.

1. `CrmDbContext` exists and its model builds.
2. The API can start with persistence registered, and does so without naming `CrmDbContext`, `DbContext`, or Npgsql anywhere in `CrmTicketing.Api`.
3. Table and column names come out `snake_case` by convention, decided once rather than per entity.
4. The connection string is supplied by configuration and never committed.

**Not in scope:** any entity, any `DbSet`, any migration. `CrmDbContext` ships with an empty model on purpose — see `## Out of scope` in the intake. The first aggregate arrives with its own story and brings its own configuration class.

---

## Context — Read These Files First

1. `src/CrmTicketing.Api/Configuration/CorsPolicies.cs` — read the whole file (37 lines). `AddBlazorClientCors` (~lines 15–36) is the **precedent for this story's registration seam**: a static class, one `IServiceCollection` extension, configuration read inside the method, and a deliberate fail-closed branch (~lines 25–30). `AddPersistence` mirrors this shape.
2. `src/CrmTicketing.Api/Program.cs` — the whole file (31 lines). Service registration is ~lines 5–10; `AddBlazorClientCors(builder.Configuration)` on line 10 is where the new `AddPersistence` call goes. Note the file never names a concrete implementation type.
3. `src/CrmTicketing.Infrastructure/Persistence/README.md` — all 16 lines. This stub states the two binding rules (~lines 14–16) and is **deleted** by this story.
4. `src/CrmTicketing.Infrastructure/CrmTicketing.Infrastructure.csproj` — 13 lines. The single `ProjectReference` to Domain is ~lines 3–5; the new `PackageReference` goes in its own `ItemGroup`.
5. `Directory.Packages.props` — read ~lines 8–25. Note the labelled `ItemGroup` pattern (`Label="ASP.NET Core / Blazor"` line 13, `Label="Testing"` line 20) and the comment at ~lines 8–12 forbidding inline versions. Add a third labelled group.
6. `docs/constitution.md` — §II *The layer graph is acyclic and one-directional* (line 23) and §VI *Configuration and secrets* (line 75). §VII (line 86) governs the abstraction decision discussed in Edge Cases.
7. `docs/architecture.md` — *Decisions taken by the scaffold* (~lines 72–86) and *Decisions deliberately deferred* (~lines 87–97). Line 92 currently reads `- Data store and ORM (\`Infrastructure/Persistence/\` is a stub).` and moves.
8. `tests/CrmTicketing.Api.Tests/SystemControllerTests.cs` — ~lines 10–21. Hand-rolled `private sealed class` fakes, **no mocking library**. Match this style; do not introduce Moq or NSubstitute.
9. `src/CrmTicketing.Domain/Common/Entity.cs` — ~lines 12–19. The `<remarks>` block states "no persistence attributes". Nothing in this story edits this file.
10. `.editorconfig` — ~lines 53–55. Test projects suppress `CA1707`/`CA1822`; production code does not. New analyzer suppressions are not permitted by this story.

---

## Implementation tasks

### 1 — Register the package version centrally

**File: `Directory.Packages.props`**

Add a third labelled `ItemGroup` after the Testing group (currently ending line 25):

```xml
  <ItemGroup Label="Persistence">
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>
```

`10.0.3` is the current release aligned with EF Core 10. **Do not** add `Microsoft.EntityFrameworkCore.Design` — that belongs to the migrations story (#4).

**File: `src/CrmTicketing.Infrastructure/CrmTicketing.Infrastructure.csproj`**

Add, as a new `ItemGroup`, with **no `Version` attribute**:

```xml
  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
```

**No change** to `src/CrmTicketing.Api/CrmTicketing.Api.csproj`. The API reaches EF Core transitively and must not reference the provider directly.

### 2 — Create the context

**Create file: `src/CrmTicketing.Infrastructure/Persistence/CrmDbContext.cs`**

```csharp
using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the CRM ticketing database.
/// </summary>
/// <remarks>
/// The model is intentionally empty. Aggregates register themselves through
/// <see cref="IEntityTypeConfiguration{TEntity}"/> classes in Configurations/,
/// discovered by ApplyConfigurationsFromAssembly. No DbSet is declared here.
/// </remarks>
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CrmDbContext).Assembly);
        modelBuilder.ApplySnakeCaseNames();
    }
}
```

**Create directory: `src/CrmTicketing.Infrastructure/Persistence/Configurations/`** containing only `.gitkeep`. An empty directory documents where configurations go; `ApplyConfigurationsFromAssembly` finds them wherever they live in the assembly, but the convention is that they live here.

### 3 — snake_case naming, decided once

**Create file: `src/CrmTicketing.Infrastructure/Persistence/SnakeCaseNaming.cs`**

```csharp
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Persistence;

/// <summary>
/// Rewrites table, column, key, and index names to snake_case, the PostgreSQL
/// convention. Applied centrally so no entity configuration repeats it.
/// </summary>
internal static class SnakeCaseNaming
{
    public static void ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        // iterate modelBuilder.Model.GetEntityTypes(); for each:
        //   SetTableName(ToSnakeCase(GetTableName()))
        //   each property: SetColumnName(ToSnakeCase(GetColumnName()))
        //   each key / foreign key / index: set its database name to snake_case
    }

    internal static string ToSnakeCase(string name);  // "TicketStatus" -> "ticket_status"
}
```

`ToSnakeCase` must handle consecutive capitals without inserting a separator between them (`SLAPolicy` → `sla_policy`, not `s_l_a_policy`) and must leave an already-lowercase name unchanged. It is `internal` and unit-tested directly — see the Test Plan.

With an empty model this method is a no-op at runtime. It is written now so the first aggregate inherits the convention rather than negotiating it.

**Do not** add the `EFCore.NamingConventions` package. Constitution §VII: a new dependency needs justification, and thirty lines of local code is cheaper than a dependency here.

### 4 — The registration seam

**Create file: `src/CrmTicketing.Infrastructure/DependencyInjection.cs`**

```csharp
namespace CrmTicketing.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the persistence layer. The caller supplies configuration and
    /// learns nothing about EF Core, Npgsql, or CrmDbContext.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration);
}
```

Behaviour:

- Read `configuration.GetConnectionString("CrmDatabase")`.
- If it is null or whitespace, **throw `InvalidOperationException`** naming the key and the `dotnet user-secrets` command. Fail fast at startup, mirroring the Client's `Api:BaseAddress` guard from Story 01. Do **not** fall back to a default connection string.
- `services.AddDbContext<CrmDbContext>(o => o.UseNpgsql(connectionString))`.
- Return `services`.

This requires `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Configuration.Abstractions`, both pulled in transitively by the Npgsql provider. If the build reports either as missing, add it to `Directory.Packages.props` under the Persistence group and note it in the PR — do not add it speculatively.

**File: `src/CrmTicketing.Api/Program.cs`**

Add one `using CrmTicketing.Infrastructure;` beside line 1, and one registration line immediately after `AddBlazorClientCors(...)` on line 10:

```csharp
builder.Services.AddPersistence(builder.Configuration);
```

That is the **only** edit to this file. No `using Microsoft.EntityFrameworkCore`, no `CrmDbContext`.

### 5 — Configuration

**File: `src/CrmTicketing.Api/appsettings.json`**

Add a `ConnectionStrings` section beside the existing `Cors` block (~lines 9–11), with an **empty value** — the key documents itself, the value comes from user-secrets:

```json
  "ConnectionStrings": {
    "CrmDatabase": ""
  }
```

**No change** to `appsettings.Development.json`. A real connection string in a committed file violates constitution §VI, and the `UserSecretsId` is already set (`CrmTicketing.Api.csproj` line 4).

### 6 — Documentation

**File: `docs/architecture.md`**

- Delete line 92 from *Decisions deliberately deferred*: `- Data store and ORM (\`Infrastructure/Persistence/\` is a stub).`
- Add to *Decisions taken by the scaffold* (after ~line 85):
  `- **PostgreSQL via Npgsql/EF Core**, mapped with \`IEntityTypeConfiguration<T>\` classes and snake_case names. Chosen for relational integrity across tickets, contacts, and accounts, and for first-class JSONB should custom fields be needed later.`
- In the projects table, add a row for `tests/CrmTicketing.Infrastructure.Tests`.

**Delete file: `src/CrmTicketing.Infrastructure/Persistence/README.md`** — its content is now realised in code.

**No change** to `docs/constitution.md`. The src layer graph is untouched.

---

## Edge Cases & Failure Modes

- **Missing connection string.** Trigger: `ConnectionStrings:CrmDatabase` unset. Expected: `InvalidOperationException` at startup naming the key and the user-secrets command. Enforced in `AddPersistence` (task 4). A silent default would let a developer run against the wrong database.
- **Empty-string connection string.** The `appsettings.json` key ships empty, so a null check alone is insufficient — check for whitespace too.
- **Consecutive capitals in `ToSnakeCase`.** `SLAPolicy`, `TicketID`, `HTTPStatus`. Expected: `sla_policy`, `ticket_id`, `http_status`. Unit-tested directly.
- **Already-lowercase names.** `id` must stay `id`, not become `_id` or `i_d`.
- **Reserved PostgreSQL identifiers.** `user`, `order`, and `group` are reserved. Not triggered by this story's empty model, but the first aggregate named `Order` will hit it. Noted here so the aggregate story plans for quoted identifiers.
- **The empty model makes a weak test.** `context.Model` on a zero-entity model cannot fail for mapping reasons. Verification proves wiring, not mapping correctness. The test gains teeth when the first aggregate lands; do not treat a green run here as proof the mapping convention works.
- **Uncertainty to surface — the "abstraction" wording.** The intake requires the context be registered "behind an abstraction declared outside Infrastructure". With zero aggregates there is nothing to abstract, and introducing an `IUnitOfWork` with no callers would violate constitution §VII (*three strikes before abstraction*). This plan interprets the criterion as satisfied by the `AddPersistence` seam: `CrmTicketing.Api` names no persistence type. **If the reviewer intended a domain-declared interface, stop and revise this plan** rather than improvising one during implementation.
- **New test project vs §II.** Task 7 adds `tests/CrmTicketing.Infrastructure.Tests`. This does not alter the src layer graph — no new edge among Domain/Shared/Infrastructure/Api/Client — so it needs a row in the architecture table, not a constitution amendment. If the reviewer disagrees, the fallback is to place the tests in `tests/CrmTicketing.Api.Tests`, which already reaches Infrastructure transitively.

---

## Test Plan

### 7 — Create the test project

**Create file: `tests/CrmTicketing.Infrastructure.Tests/CrmTicketing.Infrastructure.Tests.csproj`**

Copy `tests/CrmTicketing.Domain.Tests/CrmTicketing.Domain.Tests.csproj` verbatim, changing only the `ProjectReference` to `..\..\src\CrmTicketing.Infrastructure\CrmTicketing.Infrastructure.csproj`. It inherits `net10.0`, nullable, and warnings-as-errors from `Directory.Build.props`; no `Version` attributes.

Register it: `dotnet sln CrmTicketing.slnx add tests/CrmTicketing.Infrastructure.Tests`

**File: `src/CrmTicketing.Infrastructure/CrmTicketing.Infrastructure.csproj`** *(amendment — see log)*

`ToSnakeCase` is `internal`, so the test assembly needs access:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="CrmTicketing.Infrastructure.Tests" />
  </ItemGroup>
```

This is the correct trade: `ToSnakeCase` is an implementation detail with no callers outside the assembly, and widening it to `public` purely to satisfy a test would put test convenience ahead of API design. The grant is scoped to one named test assembly.

### 8 — Unit tests

**Create file: `tests/CrmTicketing.Infrastructure.Tests/Persistence/SnakeCaseNamingTests.cs`**

`[Theory]` over `ToSnakeCase`, one case per row:

| input | expected |
|---|---|
| `Ticket` | `ticket` |
| `TicketStatus` | `ticket_status` |
| `SLAPolicy` | `sla_policy` |
| `TicketID` | `ticket_id` |
| `id` | `id` |
| `Id` | `id` |

**Create file: `tests/CrmTicketing.Infrastructure.Tests/Persistence/CrmDbContextTests.cs`**

1. `Model_BuildsWithoutThrowing` — construct `DbContextOptionsBuilder<CrmDbContext>().UseNpgsql("Host=localhost;Database=placeholder;Username=u;Password=p")`, `new CrmDbContext(options)`, assert `context.Model` is not null. Reading the model does **not** open a connection; this test must pass with no PostgreSQL running and no Docker.
2. `Model_HasNoEntityTypes` — assert `context.Model.GetEntityTypes()` is empty. This pins the "no aggregates yet" boundary; it is expected to be **deleted** by the first aggregate story, and that deletion is the signal the boundary moved deliberately.

Match `SystemControllerTests.cs` (~lines 10–21): hand-rolled fakes, no mocking library.

### 9 — Regression

3. Existing `tests/CrmTicketing.Domain.Tests` and `tests/CrmTicketing.Api.Tests` must pass unchanged. If either needs editing, something in the layer graph moved — stop.

---

## Migration / Rollback

No database migration: this story creates no schema. `Migrations/` does not exist yet.

Rollback is a straight revert — delete the four new source files, the test project, and the two `Directory.Packages.props` / `.csproj` entries, then restore `Persistence/README.md` and the `docs/architecture.md` line. Nothing outside the repository is touched, and no database state exists to unwind.

Half-applied risk: if the package is added but `AddPersistence` is not, the solution still builds and the API still starts — persistence is simply absent. Task 4 is what makes the feature real, so review the diff for `Program.cs`.

---

## Verification Steps

1. **Backend builds:** `dotnet build CrmTicketing.slnx` from the repository root — zero warnings, zero errors. Warnings are errors here.
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — all **three** test projects green (Domain.Tests, Api.Tests, Infrastructure.Tests), with no PostgreSQL running.
3. **Domain stays clean:** `grep -cE '(Project|Package)Reference' src/CrmTicketing.Domain/CrmTicketing.Domain.csproj` → `0`.
4. **No inline versions:** `grep -rn 'PackageReference' --include=*.csproj . | grep -i version` → no output.
5. **API stays ignorant of EF:** `grep -rn 'CrmDbContext\|EntityFrameworkCore\|Npgsql' src/CrmTicketing.Api/` → matches only in `CrmTicketing.Api.csproj`? **No** — expect *no output at all*. Any hit is a defect.
6. **No committed secrets:** `grep -rn 'Password=' --include=*.json .` → no output.
7. **Regression:** `dotnet run --project src/CrmTicketing.Api --launch-profile https` after setting the user-secret, then `GET https://localhost:7043/health` → `Healthy`, and `/api/system/info` still returns JSON. Then unset the secret and confirm startup fails with the named `InvalidOperationException` rather than a null-reference.

---

## Done Criteria

- [ ] `src/CrmTicketing.Infrastructure/Persistence/CrmDbContext.cs` exists, derives from `DbContext`, declares no `DbSet`, and calls `ApplyConfigurationsFromAssembly`.
- [ ] `Persistence/README.md` is deleted; `Persistence/Configurations/` exists.
- [ ] `Npgsql.EntityFrameworkCore.PostgreSQL` version appears only in `Directory.Packages.props`; no `.csproj` carries a `Version` attribute.
- [ ] `CrmTicketing.Domain.csproj` still declares zero `PackageReference` and zero `ProjectReference`.
- [ ] No EF attribute (`[Table]`, `[Key]`, `[Column]`) appears under `src/CrmTicketing.Domain`.
- [ ] `src/CrmTicketing.Api` contains no reference to `CrmDbContext`, `EntityFrameworkCore`, or `Npgsql`; `Program.cs` gained exactly one registration line and one `using`.
- [ ] Connection string comes from configuration; a missing or blank value throws at startup with an actionable message; nothing secret is committed.
- [ ] `docs/architecture.md` records PostgreSQL under *Decisions taken*, removes it from *Decisions deferred*, and lists the new test project.
- [ ] `dotnet build CrmTicketing.slnx` is clean under `TreatWarningsAsErrors`.
- [ ] `dotnet test CrmTicketing.slnx` passes with no database running, including `ToSnakeCase` theory cases and the model-builds test.
- [x] Overview `00-overview.md` updated with this story. *(Satisfied during planning — not an implementation task. See amendment log.)*

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 03 (issue #4, migrations and seed data).**

---

## Amendment log

| Date | Change | Why |
|------|--------|-----|
| 2026-08-26 | Task 7 gains the `InternalsVisibleTo` item group. | The plan required `ToSnakeCase` (internal) be unit-tested directly but never granted the test assembly access, so the Test Plan could not compile as written. Raised by the executor rather than worked around — correct behaviour. |
| 2026-08-26 | Verification step 2: "four test projects" → three. | Miscount in the original plan. The solution has Domain.Tests, Api.Tests, and the new Infrastructure.Tests. |
| 2026-08-26 | Done Criteria: overview item marked satisfied at planning time. | `00-overview.md` is written by the planning session, not the executor. Listing it as an implementation criterion was a category error and put the executor in conflict with its read-only scope. |
| 2026-08-26 | Abstraction question resolved: the `AddPersistence` seam stands. | Confirmed as the intended reading. A domain-declared interface with zero callers would violate constitution §VII; it is revisited when the first aggregate gives it a consumer. |
