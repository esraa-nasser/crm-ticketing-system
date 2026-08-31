# CRM Ticketing System

A CRM ticketing platform built with **Blazor WebAssembly** and **ASP.NET Core** on
.NET 10, backed by PostgreSQL and developed with **spec-driven development** using
[SquadKit](https://github.com/AzmSquad/squad-kit).

> **Status: working vertical slice.** The ticket aggregate, its status workflow, its
> persistence mapping, the full HTTP surface, and the ticket list UI are implemented,
> tested, and merged. Every endpoint has been exercised live against a real database.
> Authentication is **not** implemented — see [What is not built yet](#what-is-not-built-yet),
> which explains why and what comes next.

**171 tests** across four projects, all passing with no API and no database running.
Every merge to `main` went through a pull request gated on build, test, and an
SDD-compliance check.

---

## What works today

**In the browser** — browse to `/tickets`:

- Every ticket from the database, in a paged table
- Filter by status and priority, using options served by the API rather than hardcoded
- Filter and page state in the query string, so a filtered view is linkable and the
  back button behaves
- Four distinct states: loading, rows, empty (a filter matched nothing), and failed
  (the API is unreachable or rejected the request), with the API's validation message
  surfaced rather than a generic one

**Over HTTP** — the full write surface exists and is verified, though no UI drives it yet:

| Method | Route | Behaviour |
|---|---|---|
| `POST` | `/api/tickets` | 201 with a `Location` header |
| `GET` | `/api/tickets/{id}` | 200 or 404 |
| `GET` | `/api/tickets` | 200, paged and filterable; `pageSize` clamped at 100 |
| `PATCH` | `/api/tickets/{id}` | 200, 404, or 400 |
| `POST` | `/api/tickets/{id}/status` | 200, 404, or **409** on an illegal transition |
| `POST` | `/api/tickets/{id}/assignee` | 200, 404, 400, or 409 |
| `GET` | `/api/tickets/metadata` | 200 — statuses, priorities, and the legal transition map |
| `GET` | `/api/system/info` | Build, environment, server clock |
| `GET` | `/health` | Liveness probe |
| `GET` | `/openapi/v1.json` | OpenAPI document (Development only) |

Every failure returns [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem
details. An illegal status transition returns **409 Conflict** carrying the attempted
`from` and `to` values — not 400, and not 500.

### The domain rules

A ticket has five statuses and ten legal moves between them. `Closed` is terminal:

| From | May move to |
|---|---|
| `New` | `Open`, `Closed` |
| `Open` | `Pending`, `Resolved`, `Closed` |
| `Pending` | `Open`, `Resolved`, `Closed` |
| `Resolved` | `Open`, `Closed` |
| `Closed` | *(nothing)* |

That table is declared once, in `CrmTicketing.Domain`. The API publishes it at
`GET /api/tickets/metadata` so the client renders legal actions from it rather than
re-encoding the rules. A test enumerates all 25 `(from, to)` pairs against an
independently hand-written copy — importing the production table would only prove the
code equals itself.

---

## Getting started

**Prerequisites**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **PostgreSQL 14+**, running locally. The API fails fast with an actionable message
  if it cannot find a connection string, so this is not optional.
- Node 18+ — only if you intend to run the SquadKit CLI

```bash
git clone https://github.com/esraa-nasser/crm-ticketing-system.git
cd crm-ticketing-system

dotnet restore CrmTicketing.slnx
dotnet build   CrmTicketing.slnx
dotnet test    CrmTicketing.slnx     # 171 tests, no database required
```

### Point it at a database

Create a database, then store the connection string with **user secrets** — never in
a file inside the repository (`docs/constitution.md` §VI):

```bash
createdb crm_ticketing

dotnet user-secrets init --project src/CrmTicketing.Api
dotnet user-secrets set "ConnectionStrings:CrmDatabase" \
  "Host=localhost;Port=5432;Database=crm_ticketing;Username=postgres;Password=YOUR_PASSWORD" \
  --project src/CrmTicketing.Api
```

Apply the migrations:

```bash
dotnet tool install --global dotnet-ef      # once per machine
dotnet ef database update \
  --project src/CrmTicketing.Infrastructure \
  --startup-project src/CrmTicketing.Api
```

That creates the `ticket` table. Column and table names are snake_case, applied by a
convention in `SnakeCaseNaming` rather than by attributes on every property.

### Run it

Two terminals — the client and the API are separate origins, and CORS is configured
for exactly these.

```bash
# terminal 1 — API on https://localhost:7043
dotnet run --project src/CrmTicketing.Api --launch-profile https
```

```bash
# terminal 2 — client on http://localhost:5098
dotnet run --project src/CrmTicketing.Client
```

Then open **<http://localhost:5098/tickets>**.

The database starts empty, so create a ticket first:

```bash
curl -sk -X POST -H "Content-Type: application/json" --data-binary @- \
  https://localhost:7043/api/tickets <<< '{
    "title": "Printer offline in Meeting Room 3",
    "description": "Reported by reception, started this morning.",
    "requesterId": "11111111-1111-1111-1111-111111111111",
    "priority": "High",
    "category": "Hardware"
  }'
```

Refresh `/tickets` and it appears.

> **If the page shows a connection error**, the browser is probably refusing the API's
> self-signed development certificate. `curl -k` skips that check; a browser does not.
> Run `dotnet dev-certs https --trust`, or open <https://localhost:7043/api/system/info>
> directly once and accept the warning.

`http://localhost:5098/diagnostics` calls `GET /api/system/info` and reports which
setting to fix if the API is unreachable.

---

## Why spec-driven

Every feature travels the same path:

```
intake.md  ──►  NN-story-*.md  ──►  code + tests
(what & why)    (how, concretely)    (execution)
 human, cheap    expensive model      cheap model
                 one pass             many passes
```

The expensive step happens once and produces a reviewable artifact. Everything
downstream reads that artifact instead of re-deriving the context.

This is not ceremony. Reviewing plans before implementation has caught, in this
project alone: a plan specifying an exception class that could not compile, a test
section that argued against its own acceptance criteria, a verification step whose
`grep` would have reported generated code as a violation, and an error-contract
design that would have shown users a generic message instead of the real validation
error. All were found by reading the plan, none by running the code.

Stories, plans, and their execution order live under [`.squad/`](.squad/):

| Story | Delivers | Issues |
|---|---|---|
| 01 | Solution scaffold, layer graph, CI, constitution | — |
| 02 | `CrmDbContext`, snake_case convention, EF Core Design | #3 |
| 03 | `Ticket` aggregate, status workflow, mapping, migration | #8, #9 |
| 04 | Ticket endpoints and `Shared` contracts | #10 |
| 05 | Ticket list view with filtering and paging | #12 |

See [docs/sdd-workflow.md](docs/sdd-workflow.md) for how to run the loop.

---

## What is not built yet

Deliberately, and in this order:

| Area | Issues | Why it is next, or why it waits |
|---|---|---|
| **Identity, roles, authorisation** | #5, #6 | **Next.** Every endpoint is currently open and every mutation is anonymous — `Ticket` records `CreatedAt` but no actor. That gap widens with every ticket created and cannot be backfilled honestly, which is why this was moved ahead of the remaining UI. An intake is written at `.squad/stories/auth-roles/5/intake.md`. |
| Ticket detail view, write actions | #13 | Needs an acting user, so it follows authorisation rather than preceding it. |
| Permission-gated UI | #16 | Cosmetic until the endpoints refuse the call, which #5 handles. |
| Kanban board | #14 | Consumes the transition map already published at `/api/tickets/metadata`. |
| Comments and activity timeline | #11 | Needs an owned-entity vs. separate-aggregate decision of its own. |
| Seed data | #4 | — |
| Repository integration tests | #29 | Filtering, ordering, and paging have unit coverage through a fake, but no test runs them against a real database. |
| SLA policies | #21 | Needs business-hours arithmetic. |

Known defect: `GET /health` reports `Healthy` when the database is unreachable (#31).

---

## Layout

```
CrmTicketing.slnx
├── Directory.Build.props        # shared compiler settings, warnings-as-errors
├── Directory.Packages.props     # every NuGet version, centrally managed
├── src/
│   ├── CrmTicketing.Domain          # aggregate & business rules — zero dependencies
│   ├── CrmTicketing.Shared          # contracts shared over the wire — zero references
│   ├── CrmTicketing.Infrastructure  # EF Core, repository, migrations
│   ├── CrmTicketing.Api             # ASP.NET Core Web API, composition root
│   └── CrmTicketing.Client          # Blazor WebAssembly client
├── tests/
│   ├── CrmTicketing.Domain.Tests          # 104 tests
│   ├── CrmTicketing.Api.Tests             #  33 tests
│   ├── CrmTicketing.Infrastructure.Tests  #  11 tests
│   └── CrmTicketing.Client.Tests          #  23 tests (bUnit)
├── docs/
│   ├── constitution.md          # the rules every plan is written against
│   ├── architecture.md          # layer graph, decisions made and deferred
│   └── sdd-workflow.md          # how to run the SquadKit loop
├── .squad/                      # SquadKit workspace: stories & plans
└── .claude/commands/            # /squad-plan, /squad-new-story
```

The dependency graph is one-directional and enforced by review:

```
Domain  ←  Infrastructure  ←  Api  →  Shared  ←  Client
```

`Domain` references nothing. `Shared` references nothing. `Client` references only
`Shared`, which is why contracts carry status and priority as strings rather than as
domain enums.

---

## Adding a feature

```bash
# 1. capture the requirement
squad new-story tickets --id 42 --title "Agent can triage an incoming ticket"
$EDITOR .squad/stories/tickets/42/intake.md

# 2. plan it (in Claude Code)
#    /squad-plan .squad/stories/tickets/42/intake.md

# 3. implement in a fresh session with only the plan file attached
dotnet test CrmTicketing.slnx
```

## Contributing

Read [docs/constitution.md](docs/constitution.md) first — it is short and it is
enforced. In particular:

- No production code without a story intake and a plan.
- The layer dependency graph is one-directional.
- NuGet versions go in `Directory.Packages.props`, nowhere else.
- No secrets in the repository, ever. CI fails the build if `.squad/secrets.yaml`
  becomes tracked.

Branches are `feature/<story-slug>`; commits use Conventional Commit prefixes and
reference the plan they implement. `main` is protected — changes arrive by pull
request with CI green.

## Project setup tooling

`scripts/bootstrap-github.sh` created this repository's planning surface: sprint
milestones, labels, the issue backlog derived from `.squad/plans/00-index.md`, and a
Projects v2 board with `Sprint`, `Priority`, and `Estimate` fields. It needs only
curl and git, and every step is idempotent, so a partial failure is safe to resume.

It is a one-time setup script and has already been run; it is documented here only
so the board's provenance is clear.

## License

MIT — see [LICENSE](LICENSE).
