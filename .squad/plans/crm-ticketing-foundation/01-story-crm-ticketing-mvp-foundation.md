# Story 01 — Repository, layer graph, and SDD baseline

## Prerequisites

- None. This is the first story in the repository.

---

## Story Goal

Stand up the repository so that every later feature can be specified, planned, and
implemented through the SquadKit loop instead of improvised.

1. A .NET 10 solution whose project boundaries make the intended architecture
   physically enforceable — the compiler refuses a dependency the design forbids.
2. A standalone Blazor WebAssembly client and a separate ASP.NET Core Web API that
   demonstrably talk to each other, with the cross-origin configuration made
   explicit rather than implicit.
3. A written constitution, an architecture map, and a workflow guide, so a plan
   can cite a rule instead of re-arguing it.
4. CI that runs the verification commands every future plan will name, plus a
   guard that keeps SquadKit secrets out of the repository.

No ticketing or CRM behaviour is implemented. That is the explicit boundary of
this story.

---

## Context — Read These Files First

1. `docs/constitution.md` — §II is the layer graph this story physically enforces;
   §IV is the contract shape; §VI is the secret-handling rule the CI guard backs.
2. `docs/architecture.md` — the "Decisions taken by the scaffold" and "Decisions
   deliberately deferred" tables. Anything in the second table is *not* this
   story's job.
3. `.squad/stories/crm-ticketing-foundation/crm-ticketing-mvp-foundation/intake.md`
   — the acceptance criteria this plan is graded against, and the **Out of scope**
   list at the bottom.
4. `.squad/config.yaml` — `tracker.type: github`, `naming.globalSequence: true`
   (this is why the plan is numbered `01` globally, not per feature).

---

## Implementation tasks

### 1 — Solution and project skeleton

**Create: `CrmTicketing.slnx`** plus seven projects.

```
src/CrmTicketing.Domain           classlib   (net10.0)
src/CrmTicketing.Shared           classlib   (net10.0)
src/CrmTicketing.Infrastructure   classlib   (net10.0)
src/CrmTicketing.Api              webapi     (net10.0, controllers)
src/CrmTicketing.Client           blazorwasm (net10.0, standalone)
tests/CrmTicketing.Domain.Tests   xunit      (net10.0)
tests/CrmTicketing.Api.Tests      xunit      (net10.0)
```

Project references — exactly these edges, no others:

| From | To |
|---|---|
| `Infrastructure` | `Domain` |
| `Api` | `Domain`, `Infrastructure`, `Shared` |
| `Client` | `Shared` |
| `Domain.Tests` | `Domain` |
| `Api.Tests` | `Api` |
| `Domain` | *(none)* |
| `Shared` | *(none)* |

Delete every template sample: `WeatherForecast.cs`,
`Controllers/WeatherForecastController.cs`, `Pages/Counter.razor`,
`Pages/Weather.razor`, `wwwroot/sample-data/`, and each `Class1.cs` /
`UnitTest1.cs`.

### 2 — Build settings and central package management

**Create file: `Directory.Build.props`**

One `PropertyGroup` applying to all projects: `net10.0`, `LangVersion=latest`,
`Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`,
`EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest-recommended`,
`InvariantGlobalization=true`, `ManagePackageVersionsCentrally=true`.

**Create file: `Directory.Packages.props`**

Every version in the solution, grouped by label. Then strip the `Version`
attribute from every `PackageReference` in every `.csproj`, and remove the
now-redundant `TargetFramework`, `Nullable`, and `ImplicitUsings` properties that
`dotnet new` wrote into them.

**Create files:** `.editorconfig` (file-scoped namespaces as a warning,
`_camelCase` private fields, `CA1848`/`CA2007` off), `.gitattributes` (`eol=lf`,
binary classifications).

### 3 — Domain seed: identity base type

**Create file: `src/CrmTicketing.Domain/Common/Entity.cs`**

```csharp
public abstract class Entity
{
    protected Entity(Guid id);        // throws ArgumentException on Guid.Empty
    public Guid Id { get; }           // get-only
    public override bool Equals(object? obj);   // type-and-id equality
    public override int GetHashCode();          // HashCode.Combine(GetType(), Id)
}
```

Type-sensitive equality matters: a `Ticket` and a `Customer` sharing a `Guid` are
not the same thing. Nothing else goes in `Domain` — the aggregates belong to
`ticketing-core`.

### 4 — Wire contract

**Create file: `src/CrmTicketing.Shared/Contracts/ApiInfoResponse.cs`**

```csharp
public sealed record ApiInfoResponse(
    string Name,
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc);
```

This is the reference shape for every future contract: `sealed record`, positional,
serialisable, no behaviour.

### 5 — API composition root

**Create file: `src/CrmTicketing.Api/Configuration/CorsPolicies.cs`**

```csharp
public static class CorsPolicies
{
    public const string BlazorClient = "BlazorClient";
    public static IServiceCollection AddBlazorClientCors(
        this IServiceCollection services, IConfiguration configuration);
}
```

Reads `Cors:AllowedOrigins` as `string[]`. When the array is **empty, register the
policy with no origins** — deny the cross-origin call. Do not fall back to
`AllowAnyOrigin()`; a misconfigured environment must fail closed.

**Edit file: `src/CrmTicketing.Api/Program.cs`**

Register in order: `TimeProvider.System` as a singleton, `AddControllers`,
`AddProblemDetails`, `AddOpenApi`, `AddHealthChecks`, `AddBlazorClientCors`.
Pipeline: `MapOpenApi` in Development only, `UseHttpsRedirection` in non-Development
only, then `UseExceptionHandler`, `UseStatusCodePages`,
`UseCors(CorsPolicies.BlazorClient)`, `UseAuthorization`, `MapControllers`,
`MapHealthChecks("/health")`.

`TimeProvider` is registered now, before anything needs it, so the SLA-clock story
has a testable seam and never reaches for `DateTime.UtcNow`.

**Create file: `src/CrmTicketing.Api/Controllers/SystemController.cs`**

`[ApiController]`, `[Route("api/[controller]")]`, primary constructor taking
`IHostEnvironment` and `TimeProvider`. One action:
`[HttpGet("info")] ActionResult<ApiInfoResponse> GetInfo()`. Version comes from a
static `AssemblyInformationalVersionAttribute` lookup, defaulting to `"0.0.0"`.

**Edit files:** `appsettings.json` — add `"Cors": { "AllowedOrigins": [] }`.
`appsettings.Development.json` — populate it with
`https://localhost:7129` and `http://localhost:5098`.
`CrmTicketing.Api.http` — replace the weather request with `/health`,
`/api/system/info`, and `/openapi/v1.json`.

### 6 — Client wiring

**Create file: `src/CrmTicketing.Client/wwwroot/appsettings.json`**

```json
{ "Api": { "BaseAddress": "https://localhost:7043/" } }
```

**Edit file: `src/CrmTicketing.Client/Program.cs`**

Replace the template's `AddScoped(sp => new HttpClient { BaseAddress =
builder.HostEnvironment.BaseAddress })` — wrong for a standalone client, which is
not served from the API's origin. Read `Api:BaseAddress` from configuration and
**throw `InvalidOperationException` when it is missing**; a null base address
would otherwise surface as an opaque runtime failure on first navigation. Register
`AddHttpClient<SystemApiClient>`.

**Create file: `src/CrmTicketing.Client/Services/SystemApiClient.cs`**

Typed client, primary constructor taking `HttpClient`, one method
`GetInfoAsync(CancellationToken)` calling `GetFromJsonAsync<ApiInfoResponse>`.
Components never hold an `HttpClient`; they inject a typed client from this folder.

**Create file: `src/CrmTicketing.Client/Pages/Diagnostics.razor`** (`@page "/diagnostics"`)

Three render states: error, loading, loaded table. Catch only
`HttpRequestException` and `TaskCanceledException`. The error branch must name
`Api:BaseAddress` and `Cors:AllowedOrigins` explicitly — this page exists to make
a misconfiguration self-diagnosing.

**Edit files:** `Pages/Home.razor` (project orientation and the "scaffold state"
notice), `Layout/NavMenu.razor` (Home + Diagnostics only), `Layout/MainLayout.razor`
and `wwwroot/index.html` (title and top-row link).

### 7 — Tests

**Create file: `tests/CrmTicketing.Domain.Tests/Common/EntityTests.cs`**

Two private nested `Entity` subclasses (`Ticket`, `Customer`). Four facts:
`Guid.Empty` rejected with `ParamName == "id"`; same type + same id equal (and
equal hash codes); different types + same id **not** equal; same type + different
ids not equal.

**Create file: `tests/CrmTicketing.Api.Tests/SystemControllerTests.cs`**

Hand-rolled `FakeHostEnvironment : IHostEnvironment` and
`FixedTimeProvider : TimeProvider` — no mocking library, per constitution §VII.
Assert the controller returns the injected environment name and the fixed clock,
proving nothing reads ambient state.

### 8 — Persistence stub

**Create file: `src/CrmTicketing.Infrastructure/Persistence/README.md`**

State what will live here (`CrmDbContext`, `Configurations/`, `Migrations/`) and
restate the two binding rules: `Domain` never references EF Core, and no
`DbContext` reaches an `Api` controller directly. An empty folder would read as an
oversight; a README makes the deferral deliberate.

**No changes required** in `Infrastructure` beyond this. Choosing the data store is
the `persistence` feature's job.

### 9 — SquadKit workspace

```bash
squad init -y --agents claude-code,copilot --tracker github \
  --name "CRM Ticketing System" --language "C#" --no-planner --skip-secrets-prompt
squad new-story crm-ticketing-foundation --title "CRM ticketing MVP foundation" --no-tracker -y
```

Then fill the generated `intake.md` completely — every section, including
**Out of scope**, which is what stops the next plan from sprawling. Add the feature
row to `.squad/plans/00-index.md` and the story row to
`crm-ticketing-foundation/00-overview.md`.

`--no-planner` leaves direct-API planning off; `docs/sdd-workflow.md` documents
how to enable it. Merge, do not overwrite, the squad-managed block in `.gitignore`.

### 10 — Documentation and CI

**Create files:** `README.md`, `CONTRIBUTING.md`, `docs/constitution.md`,
`docs/architecture.md`, `docs/sdd-workflow.md`, `LICENSE` (MIT),
`.github/pull_request_template.md`.

**Create file: `.github/workflows/ci.yml`** — two jobs:

- `build-and-test`: setup .NET `10.0.x`, restore, build Release, test with TRX +
  XPlat coverage, publish the client, upload results with `if: always()`.
- `sdd-guard`: setup Node 22, `npm install -g squad-kit`, `squad doctor`, then
  fail via `git ls-files --error-unmatch .squad/secrets.yaml` if that file is ever
  tracked.

---

## Verification Steps

1. **Restore and build clean:** `dotnet build CrmTicketing.slnx` — zero warnings
   (they are errors here).
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — both projects green.
3. **Layer graph holds:** `grep -rE '(Project|Package)Reference' src/CrmTicketing.Domain/*.csproj`
   returns nothing, and no `.csproj` in the tree contains
   `PackageReference` with a `Version=` attribute.
4. **Wiring works end to end:** run `dotnet run --project src/CrmTicketing.Api
   --launch-profile https` and `dotnet run --project src/CrmTicketing.Client
   --launch-profile https`, then open `https://localhost:7129/diagnostics` and
   confirm the table shows name, version, environment, and server time.
5. **Fails closed:** clear `Cors:AllowedOrigins` in
   `appsettings.Development.json`, reload `/diagnostics`, and confirm the error
   branch renders instead of the table.
6. **Workspace healthy:** `squad doctor` reports no problems, and
   `git check-ignore .squad/secrets.yaml` confirms the ignore rule.

---

## Done Criteria

- [x] Solution builds and both test projects pass under `TreatWarningsAsErrors`.
- [x] `Domain` has zero references; `Client` references only `Shared`.
- [x] All NuGet versions resolve from `Directory.Packages.props`.
- [x] `/api/system/info` and `/health` respond; `/diagnostics` renders the table.
- [x] Empty `Cors:AllowedOrigins` denies cross-origin calls with no permissive
      fallback.
- [x] `TimeProvider` is injected; no `DateTime.UtcNow` in production code.
- [x] `docs/constitution.md`, `docs/architecture.md`, `docs/sdd-workflow.md`
      written and cross-linked from `README.md`.
- [x] SquadKit workspace initialised and `squad doctor` is clean.
- [x] CI enforces build, test, `squad doctor`, and the untracked-secrets guard.
- [x] Overview `00-overview.md` updated with this story.
