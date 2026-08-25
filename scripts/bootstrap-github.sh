#!/usr/bin/env bash
#
# bootstrap-github.sh — one-shot GitHub setup for the CRM Ticketing System.
#
# Creates the repository, pushes main, then builds a Projects v2 board with
# sprint milestones, labels, and an issue backlog derived from the SquadKit
# plan index (.squad/plans/00-index.md).
#
# Needs curl and git — nothing else. The GitHub CLI is used automatically if it
# happens to be installed and logged in, but is never required.
#
# Usage:
#
#   A) With a token (no extra tooling — curl ships with Git Bash and macOS):
#
#        export GITHUB_TOKEN=ghp_xxxxxxxx      # classic PAT: repo, project, workflow
#        ./scripts/bootstrap-github.sh
#
#   B) With the GitHub CLI, if you already have it:
#
#        gh auth login
#        gh auth refresh -h github.com -s project -s repo -s workflow
#        ./scripts/bootstrap-github.sh
#
# Every step is idempotent: re-running skips whatever already exists.
#
set -euo pipefail

# ─── Configuration ────────────────────────────────────────────────────────────
OWNER="${OWNER:-esraa-nasser}"
REPO="${REPO:-crm-ticketing-system}"
VISIBILITY="${VISIBILITY:-private}"          # private | public
PROJECT_TITLE="${PROJECT_TITLE:-CRM Ticketing System}"

SPRINT_WEEKS="${SPRINT_WEEKS:-2}"
SPRINT_COUNT="${SPRINT_COUNT:-5}"
SPRINT_START="${SPRINT_START:-}"             # YYYY-MM-DD; default = next Monday

DRY_RUN="${DRY_RUN:-0}"                      # 1 = print actions, change nothing
SKIP_PUSH="${SKIP_PUSH:-0}"                  # 1 = repo already pushed by hand

API_BASE="${API_BASE:-https://api.github.com}"
GRAPHQL_URL="${GRAPHQL_URL:-https://api.github.com/graphql}"

# ─── Output helpers ───────────────────────────────────────────────────────────
if [[ -t 1 ]]; then
  B=$'\033[1m'; G=$'\033[32m'; Y=$'\033[33m'; R=$'\033[31m'; D=$'\033[2m'; N=$'\033[0m'
else
  B=""; G=""; Y=""; R=""; D=""; N=""
fi
step() { printf '\n%s▸ %s%s\n' "$B" "$*" "$N"; }
ok()   { printf '  %s✓%s %s\n' "$G" "$N" "$*"; }
warn() { printf '  %s!%s %s\n' "$Y" "$N" "$*"; }
die()  { printf '\n  %s✗ %s%s\n\n' "$R" "$*" "$N" >&2; exit 1; }
skip() { printf '  %s·%s %s\n' "$D" "$N" "$*"; }

# ─── Tiny JSON helpers ────────────────────────────────────────────────────────
# Deliberately not using jq: it is not present in Git Bash and asking for another
# install is what this script exists to avoid. Every field read below comes from
# a single-object API response, where a first-match grep is unambiguous.

json_escape() {
  local s="$1"
  s="${s//\\/\\\\}"; s="${s//\"/\\\"}"
  s="${s//$'\n'/\\n}"; s="${s//$'\r'/\\r}"; s="${s//$'\t'/\\t}"
  printf '"%s"' "$s"
}
jstr() {  # jstr <json> <key>  -> first string value for that key
  grep -o "\"$2\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" <<< "$1" \
    | head -1 | sed 's/.*:[[:space:]]*"//; s/"$//'
}
jnum() {  # jnum <json> <key>  -> first numeric value for that key
  grep -o "\"$2\"[[:space:]]*:[[:space:]]*[0-9][0-9]*" <<< "$1" \
    | head -1 | sed 's/.*:[[:space:]]*//'
}
# Existence checks match on the leading ASCII run of a title rather than the whole
# string. Titles here contain em-dashes, and APIs may return them raw or as \uXXXX
# escapes; an exact comparison would silently re-create issues that already exist.
ascii_key() {
  local k
  k=$(printf '%s' "$1" | LC_ALL=C sed 's/[^ -~].*$//')
  if [[ ${#k} -ge 10 ]]; then printf '%s' "$k"; else printf '%s' "$1"; fi
}

# ─── API layer — gh if available, curl otherwise ──────────────────────────────
USE_GH=0

api_rest() {  # api_rest <METHOD> <path-without-leading-slash> [json-body]
  local method="$1" path="$2" body="${3:-}"
  if [[ "$USE_GH" == "1" ]]; then
    if [[ -n "$body" ]]; then
      printf '%s' "$body" | gh api --method "$method" "$path" --input - 2>/dev/null || true
    else
      gh api --method "$method" "$path" 2>/dev/null || true
    fi
  else
    if [[ -n "$body" ]]; then
      curl -sS -X "$method" \
        -H "Authorization: Bearer ${GITHUB_TOKEN}" \
        -H "Accept: application/vnd.github+json" \
        -H "X-GitHub-Api-Version: 2022-11-28" \
        -d "$body" "${API_BASE}/${path}" 2>/dev/null || true
    else
      curl -sS -X "$method" \
        -H "Authorization: Bearer ${GITHUB_TOKEN}" \
        -H "Accept: application/vnd.github+json" \
        -H "X-GitHub-Api-Version: 2022-11-28" \
        "${API_BASE}/${path}" 2>/dev/null || true
    fi
  fi
}

api_graphql() {  # api_graphql <query-string>
  local payload
  payload="{\"query\":$(json_escape "$1")}"
  if [[ "$USE_GH" == "1" ]]; then
    printf '%s' "$payload" | gh api graphql --input - 2>/dev/null || true
  else
    curl -sS -X POST \
      -H "Authorization: Bearer ${GITHUB_TOKEN}" \
      -H "Content-Type: application/json" \
      -d "$payload" "$GRAPHQL_URL" 2>/dev/null || true
  fi
}

# ─── Preflight ────────────────────────────────────────────────────────────────
step "Preflight"

command -v git  >/dev/null || die "git not found."
command -v curl >/dev/null || die "curl not found."

if command -v gh >/dev/null && gh auth status >/dev/null 2>&1; then
  USE_GH=1
  ok "using GitHub CLI (authenticated)"
  if ! gh auth status 2>&1 | grep -q 'project'; then
    warn "'project' scope missing — the board step will fail"
    warn "fix: gh auth refresh -h github.com -s project -s repo -s workflow"
  fi
elif [[ -n "${GITHUB_TOKEN:-}" ]]; then
  ok "using curl with GITHUB_TOKEN"
else
  die "No credentials. Either:
       export GITHUB_TOKEN=ghp_xxx    (classic token: repo, project, workflow)
     or install the GitHub CLI and run: gh auth login"
fi

[[ -f "CrmTicketing.slnx" ]] || die "Run this from the repository root (CrmTicketing.slnx not found)."
[[ -d ".git" ]] || die "No .git directory here."
git rev-parse HEAD >/dev/null 2>&1 || die "No commits found."
BRANCH=$(git branch --show-current)
ok "repository root, $(git rev-list --count HEAD) commit(s) on ${BRANCH}"

# Verify the credential actually works before creating anything.
if [[ "$DRY_RUN" != "1" ]]; then
  whoami_json=$(api_rest GET "user")
  login=$(jstr "$whoami_json" "login")
  if [[ -z "$login" ]]; then
    die "Could not authenticate to ${API_BASE}.
     If using a token, check it is a CLASSIC token with 'repo' and 'project'
     scopes and has not expired: https://github.com/settings/tokens"
  fi
  ok "authenticated as ${login}"
fi

# Date arithmetic differs between GNU and BSD userland.
if date -d "2026-01-01" +%F >/dev/null 2>&1; then
  DATE_KIND="gnu"
elif date -j -f "%Y-%m-%d" "2026-01-01" +%F >/dev/null 2>&1; then
  DATE_KIND="bsd"
else
  die "Neither GNU nor BSD 'date' semantics detected."
fi
date_add() {
  if [[ "$DATE_KIND" == "gnu" ]]; then date -u -d "$1 +$2 days" +%F
  else date -u -j -v"+$2"d -f "%Y-%m-%d" "$1" +%F; fi
}

if [[ -z "$SPRINT_START" ]]; then
  today=$(date -u +%F)
  dow=$(if [[ "$DATE_KIND" == "gnu" ]]; then date -u -d "$today" +%u; else date -u -j -f "%Y-%m-%d" "$today" +%u; fi)
  SPRINT_START=$(date_add "$today" $(( (8 - dow) % 7 == 0 ? 7 : (8 - dow) % 7 )))
fi
ok "sprints: ${SPRINT_COUNT} × ${SPRINT_WEEKS}w, first starts ${SPRINT_START}"
[[ "$DRY_RUN" == "1" ]] && warn "DRY_RUN=1 — nothing will be created"

# ─── 1. Repository ────────────────────────────────────────────────────────────
step "1/6  Repository ${OWNER}/${REPO}"

if [[ "$DRY_RUN" == "1" ]]; then
  skip "would create and push"
else
  repo_json=$(api_rest GET "repos/${OWNER}/${REPO}")
  if [[ -n "$(jstr "$repo_json" "full_name")" ]]; then
    skip "already exists"
  else
    priv="true"; [[ "$VISIBILITY" == "public" ]] && priv="false"
    body="{\"name\":$(json_escape "$REPO"),\"private\":${priv},\"description\":$(json_escape "CRM ticketing system — Blazor WebAssembly + ASP.NET Core, built with spec-driven development (SquadKit)"),\"has_issues\":true,\"has_projects\":true,\"has_wiki\":false}"
    created=$(api_rest POST "user/repos" "$body")
    [[ -n "$(jstr "$created" "full_name")" ]] || die "Repository creation failed:
     $(head -c 300 <<< "$created")"
    ok "created"
  fi

  if [[ "$SKIP_PUSH" == "1" ]]; then
    skip "push skipped (SKIP_PUSH=1)"
  else
    git remote get-url origin >/dev/null 2>&1 \
      || git remote add origin "https://github.com/${OWNER}/${REPO}.git"
    git push -u origin "$BRANCH" && ok "pushed ${BRANCH}" \
      || warn "push failed — push manually, then re-run with SKIP_PUSH=1"
  fi
fi

# ─── 2. Labels ────────────────────────────────────────────────────────────────
step "2/6  Labels"

LABELS=(
  "epic|6f42c1|Feature-level umbrella issue spanning several stories"
  "sdd:needs-intake|d4c5f9|No SquadKit story intake written yet"
  "sdd:needs-plan|c5def5|Intake written, plan not generated yet"
  "sdd:ready|0e8a16|Plan generated and ready to implement"
  "area:domain|1d76db|CrmTicketing.Domain — entities and business rules"
  "area:api|0052cc|CrmTicketing.Api — HTTP surface"
  "area:client|5319e7|CrmTicketing.Client — Blazor WebAssembly UI"
  "area:infra|b60205|Persistence, integrations, deployment"
  "area:docs|fef2c0|Documentation and constitution changes"
  "blocked|e11d21|Waiting on another issue or an external decision"
)
for spec in "${LABELS[@]}"; do
  IFS='|' read -r name color desc <<< "$spec"
  if [[ "$DRY_RUN" == "1" ]]; then skip "would create '$name'"; continue; fi
  body="{\"name\":$(json_escape "$name"),\"color\":$(json_escape "$color"),\"description\":$(json_escape "$desc")}"
  resp=$(api_rest POST "repos/${OWNER}/${REPO}/labels" "$body")
  if [[ -n "$(jstr "$resp" "name")" ]]; then ok "$name"; else skip "$name (exists)"; fi
done

# ─── 3. Sprint milestones ─────────────────────────────────────────────────────
step "3/6  Sprint milestones"

declare -a SPRINT_TITLES=() SPRINT_STARTS=() SPRINT_ENDS=()
declare -a MS_NUMBERS=()
cursor="$SPRINT_START"
for ((i = 1; i <= SPRINT_COUNT; i++)); do
  span=$(( SPRINT_WEEKS * 7 ))
  end=$(date_add "$cursor" $(( span - 1 )))
  SPRINT_TITLES+=("Sprint ${i}")
  SPRINT_STARTS+=("$cursor")
  SPRINT_ENDS+=("$end")
  cursor=$(date_add "$cursor" "$span")
done

existing_ms=$(api_rest GET "repos/${OWNER}/${REPO}/milestones?state=all&per_page=100")

for ((i = 0; i < SPRINT_COUNT; i++)); do
  title="${SPRINT_TITLES[$i]}"
  if [[ "$DRY_RUN" == "1" ]]; then
    skip "would create '$title'  (${SPRINT_STARTS[$i]} → ${SPRINT_ENDS[$i]})"
    MS_NUMBERS+=("0"); continue
  fi

  # Existing milestone? Pull its number out of the block that contains the title.
  # Whitespace-tolerant: some hosts pretty-print, GitHub itself returns compact.
  num=$(tr '}' '}\n' <<< "$existing_ms" \
        | grep -E "\"title\"[[:space:]]*:[[:space:]]*\"${title}\"" | head -1 \
        | grep -oE '"number"[[:space:]]*:[[:space:]]*[0-9]+' | head -1 \
        | sed 's/.*[^0-9]//' || true)

  if [[ -n "$num" ]]; then
    MS_NUMBERS+=("$num"); skip "$title (#$num)"
  else
    body="{\"title\":$(json_escape "$title"),\"state\":\"open\",\"description\":$(json_escape "${SPRINT_STARTS[$i]} → ${SPRINT_ENDS[$i]}"),\"due_on\":\"${SPRINT_ENDS[$i]}T23:59:59Z\"}"
    resp=$(api_rest POST "repos/${OWNER}/${REPO}/milestones" "$body")
    num=$(jnum "$resp" "number")
    if [[ -n "$num" ]]; then
      MS_NUMBERS+=("$num"); ok "$title  (${SPRINT_STARTS[$i]} → ${SPRINT_ENDS[$i]})"
    else
      MS_NUMBERS+=("0"); warn "$title — could not create"
    fi
  fi
done

# ─── 4. Issue backlog ─────────────────────────────────────────────────────────
step "4/6  Issue backlog"

# sprint#|labels|feature-slug|title|body   (\n in body is expanded)
ISSUES=(
# ── Sprint 1 — persistence + auth foundations ──
"1|epic,area:infra,sdd:needs-intake|persistence|EPIC: Persistence layer — data store, EF Core, migrations|Choose and wire the data store. \`src/CrmTicketing.Infrastructure/Persistence/\` is currently a stub README by design; this epic replaces it.\n\nConstitution rules that bind here (docs/constitution.md §II):\n- \`CrmTicketing.Domain\` must not reference EF Core. Mapping lives in Infrastructure.\n- No \`DbContext\` reaches an Api controller directly.\n\nDecide: SQLite vs SQL Server vs PostgreSQL. Record the decision in docs/architecture.md."
"1|area:infra,sdd:needs-intake|persistence|Add CrmDbContext and IEntityTypeConfiguration convention|Create \`CrmDbContext\` plus a \`Configurations/\` folder with one \`IEntityTypeConfiguration<T>\` per aggregate. Register it in the Api composition root behind an abstraction, not as a concrete type.\n\nEF Core package versions go in \`Directory.Packages.props\` — never inline in a csproj."
"1|area:infra,sdd:needs-intake|persistence|Add initial migration and a seeded local database|Generate the first migration and a development seeder. Migrations are generated, never hand-edited.\n\nVerification: \`dotnet ef database update\` from a clean clone produces a working local database."
"1|epic,area:api,sdd:needs-intake|auth-roles|EPIC: Authentication and role model|Admin / Agent / Customer roles with permission-gated UI.\n\nOpen decision: ASP.NET Core Identity vs an external provider (Entra ID). Whichever is chosen, remember that everything shipped to a WebAssembly client is public — no secret goes in the Client project (constitution §VI)."
"1|area:api,sdd:needs-intake|auth-roles|Wire authentication on the API and token flow to the client|Add authentication to the Api composition root and a token acquisition path for the standalone WASM client. \`UseAuthorization()\` is already in the pipeline — it currently guards nothing."

# ── Sprint 2 — ticketing core domain + API ──
"2|epic,area:domain,sdd:needs-intake|ticketing-core|EPIC: Ticket aggregate and status workflow|The core of the product. Ticket with status, priority, category, assignment, and an activity timeline.\n\nConstitution §III applies: illegal status transitions are rejected by the domain — not by the UI, and not by a database constraint alone."
"2|area:domain,sdd:needs-intake|ticketing-core|Model the Ticket aggregate on the Entity base type|Build \`Ticket\` on \`CrmTicketing.Domain.Common.Entity\`. No public setters that let a caller bypass an invariant; invalid state throws at construction.\n\nUnit tests for every rejected transition, mirroring \`tests/CrmTicketing.Domain.Tests/Common/EntityTests.cs\`."
"2|area:domain,sdd:needs-intake|ticketing-core|Model TicketStatus, TicketPriority, and the transition table|Model workflow explicitly rather than as a loose enum. Document the legal transition matrix in the plan before writing it."
"2|area:api,sdd:needs-intake|ticketing-core|Ticket CRUD endpoints with Shared contracts|Endpoints take and return \`sealed record\` types from \`CrmTicketing.Shared.Contracts\` — domain entities are never serialised onto the wire (constitution §IV). Errors use RFC 9457 problem details; \`AddProblemDetails()\` is already registered.\n\n\`ApiInfoResponse\` is the reference contract shape."
"2|area:api,sdd:needs-intake|ticketing-core|Comments and activity timeline endpoints|Append-only activity log per ticket. Use the injected \`TimeProvider\` for timestamps — \`DateTime.UtcNow\` is banned in production code (constitution §V)."

# ── Sprint 3 — ticketing UI ──
"3|area:client,sdd:needs-intake|ticketing-core|Ticket list view with filtering and paging|Add a \`TicketsApiClient\` to \`src/CrmTicketing.Client/Services/\` following the \`SystemApiClient\` pattern. Components never hold an \`HttpClient\` directly."
"3|area:client,sdd:needs-intake|ticketing-core|Ticket detail view with comment timeline|Detail page, status transitions surfaced as actions the domain actually permits, comment composer."
"3|area:client,sdd:needs-intake|ticketing-core|Kanban board grouped by status|Drag-to-transition. Every move round-trips through the API so the domain validates it — the board must not become a second source of truth for legal transitions."
"3|area:client,sdd:needs-intake|ticketing-core|Replace the scaffold nav and home page with real feature navigation|\`NavMenu.razor\` currently carries Home + Diagnostics and a comment marking where feature links go. Remove the scaffold-state notice from \`Home.razor\`."
"3|area:api,sdd:needs-intake|auth-roles|Permission-gate ticket endpoints and UI by role|Customers see only their own tickets; Agents see their queue; Admins see everything. Enforce on the API first — client-side gating is presentation, never security."

# ── Sprint 4 — customers / CRM ──
"4|epic,area:domain,sdd:needs-intake|customers-crm|EPIC: Accounts, contacts, and customer 360|Open decision recorded in \`.squad/plans/crm-ticketing-foundation/00-overview.md\`: whether customers are a separate aggregate or part of ticketing. Settle it in the intake, not mid-implementation."
"4|area:domain,sdd:needs-intake|customers-crm|Model Account and Contact aggregates|Both on the \`Entity\` base type. Decide and document the identity rule for contacts (email uniqueness scope)."
"4|area:api,sdd:needs-intake|customers-crm|Contact-to-ticket linking endpoints|Associate tickets with contacts and accounts. Consider what happens to tickets when a contact is merged or removed."
"4|area:client,sdd:needs-intake|customers-crm|Customer 360 view with ticket history|Single page: account, contacts, open and historical tickets, aggregate SLA posture."

# ── Sprint 5 — SLA + reporting ──
"5|area:domain,sdd:needs-intake|ticketing-core|SLA policies and due-date calculation|Business-hours-aware due dates. This is why \`TimeProvider\` is registered in the composition root — resolve it, and make every SLA test deterministic."
"5|epic,area:client,sdd:needs-intake|reporting-dashboard|EPIC: Dashboard and reporting|KPI tiles, open-by-status and by-priority breakdowns, agent workload, SLA breach report."
"5|area:api,sdd:needs-intake|reporting-dashboard|Aggregate reporting endpoints|Server-side aggregation — do not ship raw ticket sets to the client and reduce them in the browser."
"5|area:client,sdd:needs-intake|reporting-dashboard|Dashboard page with KPI tiles and charts|Pick and justify a charting approach in the plan; add its package version to \`Directory.Packages.props\`."
"5|area:docs,sdd:needs-intake|reporting-dashboard|Decide polling vs SignalR for live board updates|Open decision from the foundation overview. Whichever wins, amend \`docs/architecture.md\`."
)

existing_issues_raw=$(api_rest GET "repos/${OWNER}/${REPO}/issues?state=all&per_page=100" || true)

declare -a ISSUE_NODE_IDS=()
created_count=0

for spec in "${ISSUES[@]}"; do
  IFS='|' read -r sprint labels slug title body <<< "$spec"

  if [[ "$DRY_RUN" == "1" ]]; then
    printf '  %s·%s would create S%s  %s\n' "$D" "$N" "$sprint" "$title"; continue
  fi

  if grep -qF "$(ascii_key "$title")" <<< "$existing_issues_raw"; then
    skip "$title"; continue
  fi

  full_body="$(printf '%b' "$body")

---

**Spec-driven workflow** — this issue is a requirement, not a licence to start coding.

\`\`\`bash
squad new-story ${slug} --id <this-issue-number>
# fill the intake, especially 'Acceptance criteria' and 'Out of scope'
# then in Claude Code:  /squad-plan .squad/stories/${slug}/<id>/intake.md
\`\`\`

Read \`docs/constitution.md\` before planning. Per §I, no production code lands
without an intake under \`.squad/stories/\` and a plan under \`.squad/plans/\`.

Feature slug: \`${slug}\` · Target: **Sprint ${sprint}**"

  # labels CSV -> JSON array
  labels_json="["; first=1
  IFS=',' read -ra larr <<< "$labels"
  for l in "${larr[@]}"; do
    [[ $first -eq 0 ]] && labels_json+=","
    labels_json+="$(json_escape "$l")"; first=0
  done
  labels_json+="]"

  ms_idx=$(( sprint - 1 ))
  ms_part=""
  if [[ $ms_idx -lt ${#MS_NUMBERS[@]} && "${MS_NUMBERS[$ms_idx]}" != "0" ]]; then
    ms_part=",\"milestone\":${MS_NUMBERS[$ms_idx]}"
  fi

  req="{\"title\":$(json_escape "$title"),\"body\":$(json_escape "$full_body"),\"labels\":${labels_json}${ms_part}}"
  resp=$(api_rest POST "repos/${OWNER}/${REPO}/issues" "$req")
  node_id=$(jstr "$resp" "node_id")

  if [[ -n "$node_id" ]]; then
    ISSUE_NODE_IDS+=("$node_id")
    created_count=$(( created_count + 1 ))
    ok "S${sprint}  ${title}"
  else
    warn "failed: ${title}  $(head -c 120 <<< "$resp")"
  fi
done
[[ "$DRY_RUN" != "1" ]] && ok "${created_count} issue(s) created"

# ─── 5. Projects v2 board ─────────────────────────────────────────────────────
step "5/6  Projects v2 board"

PROJECT_URL=""
if [[ "$DRY_RUN" == "1" ]]; then
  skip "skipped under DRY_RUN"
else
  owner_json=$(api_graphql "query { user(login: \\\"${OWNER}\\\") { id } }")
  OWNER_ID=$(jstr "$owner_json" "id")

  if [[ -z "$OWNER_ID" ]]; then
    warn "GraphQL unavailable — skipping the board."
    warn "A classic token with the 'project' scope is required; fine-grained"
    warn "tokens cannot access Projects owned by a personal account."
  else
    proj_json=$(api_graphql "mutation { createProjectV2(input: {ownerId: \\\"${OWNER_ID}\\\", title: \\\"${PROJECT_TITLE}\\\"}) { projectV2 { id url } } }")
    PROJECT_ID=$(jstr "$proj_json" "id")
    PROJECT_URL=$(jstr "$proj_json" "url")

    if [[ -z "$PROJECT_ID" ]]; then
      warn "could not create the project: $(head -c 200 <<< "$proj_json")"
    else
      ok "created project '${PROJECT_TITLE}'"

      # GitHub's native Iteration field is the ideal shape for sprints, but
      # createProjectV2Field accepts only TEXT / NUMBER / DATE / SINGLE_SELECT.
      # Attempt it anyway so the outcome is recorded rather than assumed.
      iter=$(api_graphql "mutation { createProjectV2Field(input: {projectId: \\\"${PROJECT_ID}\\\", dataType: ITERATION, name: \\\"Sprint\\\"}) { projectV2Field { ... on ProjectV2IterationField { id } } } }")
      if grep -q '"errors"' <<< "$iter"; then
        warn "ITERATION not creatable via API (expected) — using a single-select"
        opts="["
        colors=(BLUE GREEN YELLOW ORANGE PURPLE PINK RED GRAY)
        for ((i = 0; i < SPRINT_COUNT; i++)); do
          [[ $i -gt 0 ]] && opts+=", "
          opts+="{name: \\\"${SPRINT_TITLES[$i]}\\\", color: ${colors[$(( i % 8 ))]}, description: \\\"${SPRINT_STARTS[$i]} to ${SPRINT_ENDS[$i]}\\\"}"
        done
        opts+="]"
        r=$(api_graphql "mutation { createProjectV2Field(input: {projectId: \\\"${PROJECT_ID}\\\", dataType: SINGLE_SELECT, name: \\\"Sprint\\\", singleSelectOptions: ${opts}}) { projectV2Field { ... on ProjectV2SingleSelectField { id } } } }")
        grep -q '"errors"' <<< "$r" && warn "'Sprint' field failed" || ok "single-select 'Sprint' with ${SPRINT_COUNT} options"
      else
        ok "native ITERATION field created"
      fi

      r=$(api_graphql "mutation { createProjectV2Field(input: {projectId: \\\"${PROJECT_ID}\\\", dataType: SINGLE_SELECT, name: \\\"Priority\\\", singleSelectOptions: [{name: \\\"P0 — blocker\\\", color: RED, description: \\\"Stops the sprint\\\"}, {name: \\\"P1 — high\\\", color: ORANGE, description: \\\"Sprint commitment\\\"}, {name: \\\"P2 — normal\\\", color: YELLOW, description: \\\"Planned\\\"}, {name: \\\"P3 — low\\\", color: GRAY, description: \\\"Nice to have\\\"}]}) { projectV2Field { ... on ProjectV2SingleSelectField { id } } } }")
      grep -q '"errors"' <<< "$r" && skip "'Priority' skipped" || ok "'Priority' field"

      r=$(api_graphql "mutation { createProjectV2Field(input: {projectId: \\\"${PROJECT_ID}\\\", dataType: NUMBER, name: \\\"Estimate\\\"}) { projectV2Field { ... on ProjectV2Field { id } } } }")
      grep -q '"errors"' <<< "$r" && skip "'Estimate' skipped" || ok "'Estimate' field"

      added=0
      for nid in "${ISSUE_NODE_IDS[@]}"; do
        r=$(api_graphql "mutation { addProjectV2ItemById(input: {projectId: \\\"${PROJECT_ID}\\\", contentId: \\\"${nid}\\\"}) { item { id } } }")
        grep -q '"errors"' <<< "$r" || added=$(( added + 1 ))
      done
      ok "${added} issue(s) added to the board"
    fi
  fi
fi

# ─── 6. Repository settings ───────────────────────────────────────────────────
step "6/6  Repository settings"

if [[ "$DRY_RUN" == "1" ]]; then
  skip "skipped under DRY_RUN"
else
  api_rest PATCH "repos/${OWNER}/${REPO}" \
    '{"has_issues":true,"has_projects":true,"has_wiki":false,"delete_branch_on_merge":true,"allow_squash_merge":true,"allow_merge_commit":false,"allow_rebase_merge":false}' >/dev/null
  ok "issues on, wiki off, squash-only, auto-delete merged branches"

  api_rest PUT "repos/${OWNER}/${REPO}/topics" \
    '{"names":["blazor","csharp","dotnet","crm","ticketing","spec-driven-development"]}' >/dev/null
  ok "topics set"
fi

# ─── Summary ──────────────────────────────────────────────────────────────────
printf '\n%s─── Done ───%s\n\n' "$B" "$N"
printf '  Repository   https://github.com/%s/%s\n' "$OWNER" "$REPO"
[[ -n "$PROJECT_URL" ]] && printf '  Project      %s\n' "$PROJECT_URL"
printf '  Issues       https://github.com/%s/%s/issues\n' "$OWNER" "$REPO"
printf '  Milestones   https://github.com/%s/%s/milestones\n' "$OWNER" "$REPO"
printf '  Actions      https://github.com/%s/%s/actions\n\n' "$OWNER" "$REPO"

cat <<'NEXT'
  Next:

    1. Watch CI:                     https://github.com/esraa-nasser/crm-ticketing-system/actions
    2. Point SquadKit at the repo:   export GITHUB_TOKEN=<your token>
                                     squad doctor          # should be fully green
    3. Start the first real story:   squad new-story persistence --id <issue-number>
                                     # fill the intake, then /squad-plan it

  If the Sprint field came out as a single-select, converting it to a native
  Iteration field takes about a minute in the project's field settings — the
  API cannot create that type.

NEXT
