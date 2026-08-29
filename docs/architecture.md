# Architecture

## Shape

A standalone Blazor WebAssembly client and an ASP.NET Core Web API, deployed
separately, sharing only serialisable contracts.

```
┌──────────────────────────────┐        HTTPS / JSON        ┌───────────────────────────────┐
│  CrmTicketing.Client         │ ────────────────────────►  │  CrmTicketing.Api             │
│  Blazor WebAssembly (net10)  │                            │  Controllers, composition     │
│  Pages, components,          │  ◄──────────────────────── │  root, CORS, problem details  │
│  typed API clients           │       ApiInfoResponse …    └───────────┬───────────────────┘
└──────────────┬───────────────┘                                        │
               │                                              ┌─────────┴─────────┐
               │                                              ▼                   ▼
               │                                  ┌───────────────────┐  ┌──────────────────┐
               └────────────────────────────────► │ CrmTicketing      │  │ CrmTicketing     │
                        project reference         │ .Shared           │  │ .Infrastructure  │
                                                  │ DTOs / contracts  │  │ persistence,     │
                                                  └───────────────────┘  │ integrations     │
                                                                         └────────┬─────────┘
                                                                                  ▼
                                                                       ┌────────────────────┐
                                                                       │ CrmTicketing       │
                                                                       │ .Domain            │
                                                                       │ entities, rules,   │
                                                                       │ zero dependencies  │
                                                                       └────────────────────┘
```

## Projects

| Project | Type | Responsibility | May reference |
|---|---|---|---|
| `src/CrmTicketing.Domain` | classlib | Entities, value objects, business invariants, workflow rules | *nothing* |
| `src/CrmTicketing.Shared` | classlib | Request/response contracts crossing the wire | BCL only |
| `src/CrmTicketing.Infrastructure` | classlib | Persistence, external services, mapping to/from domain | `Domain` |
| `src/CrmTicketing.Api` | web | HTTP surface, DI composition, CORS, auth, OpenAPI | `Domain`, `Shared`, `Infrastructure` |
| `src/CrmTicketing.Client` | blazorwasm | UI, routing, typed API clients | `Shared` |
| `tests/CrmTicketing.Domain.Tests` | xunit | Unit tests for domain rules | `Domain` |
| `tests/CrmTicketing.Api.Tests` | xunit | Controller and endpoint tests | `Api` |
| `tests/CrmTicketing.Infrastructure.Tests` | xunit | Persistence wiring and naming-convention tests | `Infrastructure` |

The dependency rules are binding — see Section II of
[the constitution](constitution.md).

## Cross-origin setup

Because the client is a *standalone* WebAssembly app rather than a hosted one, it
is served from a different origin than the API. Two settings must agree:

| Side | File | Setting |
|---|---|---|
| Client | `src/CrmTicketing.Client/wwwroot/appsettings.json` | `Api:BaseAddress` → the API's URL |
| API | `src/CrmTicketing.Api/appsettings.Development.json` | `Cors:AllowedOrigins` → the client's URL(s) |

Defaults for local development:

| Project | HTTP | HTTPS |
|---|---|---|
| `CrmTicketing.Api` | `http://localhost:5280` | `https://localhost:7043` |
| `CrmTicketing.Client` | `http://localhost:5098` | `https://localhost:7129` |

`Cors:AllowedOrigins` is empty in the base `appsettings.json` on purpose: a
deployed environment that forgets to configure it gets *no* cross-origin access
rather than a permissive fallback.

The `/diagnostics` page in the client exists to make a misconfiguration obvious —
it calls `GET /api/system/info` and reports exactly which setting to check when
the call fails.

## Decisions taken by the scaffold

These are settled; a plan that wants to change one must amend the constitution.

- **.NET 10**, C# `latest`, nullable reference types on, warnings as errors.
- **Central package management** — every NuGet version lives in
  `Directory.Packages.props`; `.csproj` files carry no `Version` attributes.
- **`.slnx` solution format** rather than the legacy `.sln`.
- **RFC 9457 problem details** for all error responses.
- **`TimeProvider` injected** rather than `DateTime.UtcNow`, so time is testable.
- **Typed HTTP clients** in `Client/Services/`; components never hold an
  `HttpClient` directly.
- **File-scoped namespaces**, `sealed` by default, primary constructors where
  they read cleanly.
- **PostgreSQL via Npgsql/EF Core**, mapped with `IEntityTypeConfiguration<T>`
  classes and snake_case names. Chosen for relational integrity across tickets,
  contacts, and accounts, and for first-class JSONB should custom fields be
  needed later.
- **Migrations live under `Persistence/Migrations/`**, generated with:
  `dotnet ef migrations add <Name> --project src/CrmTicketing.Infrastructure --startup-project src/CrmTicketing.Api --output-dir Persistence/Migrations`
  The `--output-dir` flag is required only for the first migration; later ones follow it.
- **Persistence is reached through `ITicketRepository`**, declared in
  `CrmTicketing.Domain/Tickets/` and implemented in
  `CrmTicketing.Infrastructure/Persistence/`. The interface is framework-free —
  no EF type and no `IQueryable` in any signature. `SaveChangesAsync` sits on the
  repository; there is no separate unit of work until a transaction must span two
  aggregates.
- **Generated code is exempt from style rules.** `.editorconfig` marks
  `[**/Migrations/**.cs]` as `generated_code = true`, because EF emits
  block-scoped namespaces that IDE0161 would otherwise turn into build errors
  under TreatWarningsAsErrors. Hand-written code is unaffected — verified.

## Decisions deliberately deferred

Left to the first planned stories, because they depend on scope that has not been
agreed yet:

- Authentication and authorisation scheme, and the role model.
- The ticket aggregate: status machine, SLA model, assignment rules.
- Whether customers/accounts are a separate aggregate or part of ticketing.
- Real-time updates (SignalR) versus polling.
- Hosting target and deployment pipeline beyond CI.
