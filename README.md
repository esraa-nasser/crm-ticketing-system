# CRM Ticketing System

A CRM ticketing platform built with **Blazor WebAssembly** and **ASP.NET Core**
on .NET 10, developed with **spec-driven development** using
[SquadKit](https://github.com/AzmSquad/squad-kit).

> **Status: scaffold.** The solution structure, engineering constitution, CI, and
> SquadKit workspace are in place. No domain features are implemented yet — the
> ticketing scope is being defined as story intakes under `.squad/stories/` and
> will be planned before it is written. That ordering is the point of the method,
> not an oversight.

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
downstream reads that artifact instead of re-deriving the context. See
[docs/sdd-workflow.md](docs/sdd-workflow.md).

## One-time GitHub setup

`scripts/bootstrap-github.sh` creates the repository, pushes `main`, and builds
the whole planning surface: sprint milestones, labels, a 24-issue backlog derived
from `.squad/plans/00-index.md`, and a Projects v2 board with `Sprint`,
`Priority`, and `Estimate` fields.

It needs only **curl and git** — both already on your machine (Git Bash ships
curl). The GitHub CLI is used automatically if it happens to be installed and
logged in, but is never required.

```bash
# classic token with repo + project + workflow scopes:
#   https://github.com/settings/tokens
export GITHUB_TOKEN=ghp_xxxxxxxx

DRY_RUN=1 ./scripts/bootstrap-github.sh    # preview, change nothing
./scripts/bootstrap-github.sh              # 5 × 2-week sprints from next Monday
```

Options:

```bash
SPRINT_WEEKS=1 SPRINT_COUNT=8 ./scripts/bootstrap-github.sh
SPRINT_START=2026-09-07 ./scripts/bootstrap-github.sh
SKIP_PUSH=1 ./scripts/bootstrap-github.sh          # already pushed by hand
VISIBILITY=public ./scripts/bootstrap-github.sh
```

Every step is idempotent — a re-run skips whatever already exists, so a partial
failure is safe to resume.

The token must be a **classic** PAT: fine-grained tokens cannot access Projects
owned by a personal account. The script detects that and warns rather than
failing silently.

Pushing only the code needs no token at all — create the empty repo on
github.com, then `git remote add origin …` and `git push -u origin main`.
Milestones, issues, and the board are GitHub API objects rather than git objects,
which is why they need the script.

## Getting started

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download), Node 18+
(for SquadKit).

```bash
git clone https://github.com/esraa-nasser/crm-ticketing-system.git
cd crm-ticketing-system

dotnet restore CrmTicketing.slnx
dotnet build   CrmTicketing.slnx
dotnet test    CrmTicketing.slnx

npm install -g squad-kit
squad doctor
```

### Run it

Two terminals — the client and API are separate origins.

```bash
# terminal 1 — API on https://localhost:7043
dotnet run --project src/CrmTicketing.Api --launch-profile https

# terminal 2 — client on https://localhost:7129
dotnet run --project src/CrmTicketing.Client --launch-profile https
```

Then open `https://localhost:7129/diagnostics`. It calls
`GET /api/system/info` and tells you which setting to fix if the call fails.

| Endpoint | Purpose |
|---|---|
| `GET /health` | Liveness probe |
| `GET /api/system/info` | Build, environment, server clock |
| `GET /openapi/v1.json` | OpenAPI document (Development only) |

## Layout

```
CrmTicketing.slnx
├── Directory.Build.props        # shared compiler settings, warnings-as-errors
├── Directory.Packages.props     # every NuGet version, centrally managed
├── src/
│   ├── CrmTicketing.Domain          # entities & business rules — zero dependencies
│   ├── CrmTicketing.Shared          # DTOs / contracts shared over the wire
│   ├── CrmTicketing.Infrastructure  # persistence & integrations (stub)
│   ├── CrmTicketing.Api             # ASP.NET Core Web API, composition root
│   └── CrmTicketing.Client          # Blazor WebAssembly client
├── tests/
│   ├── CrmTicketing.Domain.Tests
│   └── CrmTicketing.Api.Tests
├── docs/
│   ├── constitution.md          # the rules every plan is written against
│   ├── architecture.md          # layer graph, decisions made and deferred
│   └── sdd-workflow.md          # how to run the SquadKit loop
├── .squad/                      # SquadKit workspace: stories & plans
└── .claude/commands/            # /squad-plan, /squad-new-story
```

## Adding a feature

```bash
# 1. capture the requirement
squad new-story tickets --id CRM-101 --title "Agent can triage an incoming ticket"
$EDITOR .squad/stories/tickets/CRM-101/intake.md

# 2. plan it (in Claude Code)
#    /squad-plan .squad/stories/tickets/CRM-101/intake.md

# 3. implement in a fresh session with only the plan file attached
dotnet test CrmTicketing.slnx
```

Full detail, including the optional direct-planner API and Jira tracker setup, is
in [docs/sdd-workflow.md](docs/sdd-workflow.md).

## Contributing

Read [docs/constitution.md](docs/constitution.md) first — it is short and it is
enforced. In particular:

- No production code without a story intake and a plan.
- The layer dependency graph in `docs/architecture.md` is one-directional.
- NuGet versions go in `Directory.Packages.props`, nowhere else.
- No secrets in the repository, ever. CI fails the build if `.squad/secrets.yaml`
  becomes tracked.

Branches are `feature/<story-slug>`; commits use Conventional Commit prefixes and
reference the plan they implement.

## License

MIT — see [LICENSE](LICENSE).
