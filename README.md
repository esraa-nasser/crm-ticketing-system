# CRM Ticketing System

A CRM ticketing platform built with **Blazor WebAssembly** and **ASP.NET Core** on
.NET 10, backed by PostgreSQL and developed with **spec-driven development** using
[SquadKit](https://github.com/AzmSquad/squad-kit).

> **Status: working vertical slice, with authentication.** The ticket aggregate, its
> status workflow, its persistence mapping, the full HTTP surface, the ticket list UI,
> and identity with role-based authorisation are implemented, tested, and merged.
> Every endpoint has been exercised live against a real database. See
> [What is not built yet](#what-is-not-built-yet) for what remains and why.

**242 tests** across four projects, all passing with no API and no database running.
Every merge to `main` went through a pull request gated on build, test, and an
SDD-compliance check.

---

## What works today

**In the browser** — sign in at `/signin`, then browse to `/tickets`:

- Every ticket from the database, in a paged table
- Filter by status and priority, using options served by the API rather than hardcoded
- Filter and page state in the query string, so a filtered view is linkable and the
  back button behaves
- Four distinct states: loading, rows, empty (a filter matched nothing), and failed
  (the API is unreachable or rejected the request), with the API's validation message
  surfaced rather than a generic one

**Over HTTP** — the full write surface exists and is verified. Only sign-in and the ticket list are driven by the UI so far; the rest is reachable by API:

| Method | Route | Behaviour |
|---|---|---|
| `POST` | `/api/tickets` | 201 with a `Location` header |
| `GET` | `/api/tickets/{id}` | 200 or 404 |
| `GET` | `/api/tickets` | 200, paged and filterable; `pageSize` clamped at 100 |
| `PATCH` | `/api/tickets/{id}` | 200, 404, or 400 |
| `POST` | `/api/tickets/{id}/status` | 200, 404, or **409** on an illegal transition |
| `POST` | `/api/tickets/{id}/assignee` | 200, 404, 400, or 409 |
| `GET` | `/api/tickets/metadata` | 200 — statuses, priorities, and the legal transition map |
| `POST` | `/api/auth/signin` | 200 with a bearer token, or 401 |
| `POST` | `/api/auth/users` | 201; Admin only — the sole route that creates an account |
| `GET` | `/api/system/info` | Build, environment, server clock |
| `GET` | `/health` | Liveness probe |
| `GET` | `/openapi/v1.json` | OpenAPI document (Development only) |

Every ticket endpoint requires an authenticated caller. Every failure returns
[RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details. An illegal status
transition returns **409 Conflict** carrying the attempted `from` and `to` values — not
400, and not 500.

### Roles

| Role | May |
|---|---|
| `Admin` | Everything, including creating accounts |
| `Agent` | Read every ticket; create, update, transition, and assign |
| `Customer` | Create; read and act on **only** the tickets they raised; never assign |

A Customer sees only their own tickets, and that constraint lives in
`TicketRepository` rather than in a controller or a screen — so no future caller can
forget it. Requesting someone else's ticket returns **404**, not 403: a 403 would
confirm the ticket exists.

A requester may withdraw their own ticket to `Closed` from any live status, and may
reopen a `Resolved` one to `Open`. Every other move is staff-only and returns **403** —
the move is legal in the workflow, the caller is simply not permitted to make it.

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
dotnet test    CrmTicketing.slnx     # 242 tests, no database required
```

### Point it at a database

The connection string is stored with **user secrets** — never in a file inside the
repository (`docs/constitution.md` §VI).

First create an empty database named `crm_ticketing`, however you normally would
— pgAdmin is fine. From a shell, `createdb` does it, though on a standard Windows
install it is not on `PATH`:

```bash
# macOS / Linux, or Windows with PostgreSQL's bin directory on PATH
createdb crm_ticketing

# Windows, standard installer layout - adjust the version number
"/c/Program Files/PostgreSQL/18/bin/createdb.exe" -U postgres -h localhost crm_ticketing
```

Then store the connection string. **Replace the password before running this** —
pasting the line unchanged stores the literal text `<your-password>` and the API
will fail to connect with an error that does not obviously point back here:

```bash
dotnet user-secrets init --project src/CrmTicketing.Api
dotnet user-secrets set "ConnectionStrings:CrmDatabase" \
  "Host=localhost;Port=5432;Database=crm_ticketing;Username=postgres;Password=<your-password>" \
  --project src/CrmTicketing.Api
```

`init` is safe to run more than once — it adds a `UserSecretsId` to the project
only if one is missing. `set` **overwrites** any existing value for that key, so
if you already have a working connection string, skip it rather than re-running
it with a placeholder.

> Prefer not to touch an existing secret? An environment variable outranks user
> secrets in the configuration chain, so
> `export ConnectionStrings__CrmDatabase="..."` works for a single shell without
> changing anything stored. Note the double underscore.

### Create the first account

`POST /api/auth/users` requires an Admin, and a fresh database has none — so the first
Admin is seeded from configuration at startup. Set both keys, or nobody can sign in:

```bash
dotnet user-secrets set "Identity:BootstrapAdmin:Email" "admin@example.com" --project src/CrmTicketing.Api
dotnet user-secrets set "Identity:BootstrapAdmin:Password" "<a real password>" --project src/CrmTicketing.Api
```

The account is created once, on the first startup after migration, and never touched
again — re-running startup neither duplicates it nor resets its password. Leaving these
keys unset is allowed and simply skips it. A JWT signing key is also required:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 characters>" --project src/CrmTicketing.Api
```

The API refuses to start without it rather than falling back to a default.

### Seed demo data (optional)

A dozen tickets and two extra users — an Agent and a Customer — so the app has something
to show without creating tickets by hand. Off unless you switch it on:

```bash
dotnet user-secrets set "Seed:Demo:Enabled" "true" --project src/CrmTicketing.Api
dotnet user-secrets set "Seed:Demo:AgentEmail" "agent@example.com" --project src/CrmTicketing.Api
dotnet user-secrets set "Seed:Demo:CustomerEmail" "customer@example.com" --project src/CrmTicketing.Api
dotnet user-secrets set "Seed:Demo:Password" "<a real password>" --project src/CrmTicketing.Api
```

Seeding runs at startup, after the bootstrap Admin. It **requires** that Admin to exist —
with the flag on and no Admin configured, startup fails rather than producing a demo
missing a third of its roles. It also **refuses if the ticket table is not empty**: it
never merges demo rows into a database you are already using. To reseed, drop and
recreate the database and run the migrations again.

Sign in as the Customer to see nine tickets, or as the Agent to see all twelve — the
difference is row-level filtering doing its job.

### Apply the migrations

```bash
dotnet tool install --global dotnet-ef      # once per machine
dotnet ef database update \
  --project src/CrmTicketing.Infrastructure \
  --startup-project src/CrmTicketing.Api
```

That creates the `ticket` table and the Identity tables. Column and table names are
snake_case, applied by a convention in `SnakeCaseNaming` rather than by attributes on
every property — which is why Identity's tables read `asp_net_users` rather than
`AspNetUsers`.

**Run this before starting the API.** Role seeding queries the roles table at startup,
so an unmigrated database fails fast with a message naming this command.

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

Sign in with the bootstrap Admin. The database starts empty, so create a ticket — note
that every call now needs a token. (If you switched on demo seeding above, skip this:
you already have twelve tickets and two more users.)

```bash
TOKEN=$(curl -sk -X POST -H "Content-Type: application/json" --data-binary @- \
  https://localhost:7043/api/auth/signin \
  <<< '{"email":"admin@example.com","password":"<your-password>"}' \
  | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)

curl -sk -X POST -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
  --data-binary @- https://localhost:7043/api/tickets <<< '{
    "title": "Printer offline in Meeting Room 3",
    "description": "Reported by reception, started this morning.",
    "requesterId": "<the admin user id from the sign-in response>",
    "priority": "High",
    "category": "Hardware"
  }'
```

`requesterId` must be a real user id — a foreign key enforces it, and an unknown id
returns 400 rather than failing at the database. Refresh `/tickets` and it appears.

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
| 06 | Identity, roles, sign-in, and endpoint authorisation | #5, #6 |

See [docs/sdd-workflow.md](docs/sdd-workflow.md) for how to run the loop.

---

## What is not built yet

Deliberately, and in this order:

| Area | Issues | Why it is next, or why it waits |
|---|---|---|
| Ticket detail view, write actions | #13 | **Next.** Authorisation is in place, so an acting user now exists to attribute writes to. |
| Permission-gated UI | #16 | The endpoints already refuse the call; this hides controls a role cannot use. |
| Kanban board | #14 | Consumes the transition map already published at `/api/tickets/metadata`. |
| Comments and activity timeline | #11 | Needs an owned-entity vs. separate-aggregate decision of its own. |
| Seed data | #4 | — |
| Repository integration tests | #29 | Filtering, ordering, and paging have unit coverage through a fake, but no test runs them against a real database. |
| SLA policies | #21 | Needs business-hours arithmetic. |

Known defect: `GET /health` reports `Healthy` when the database is unreachable (#31).
It is deliberately anonymous — a liveness probe that needs a token is useless to a load
balancer.

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
