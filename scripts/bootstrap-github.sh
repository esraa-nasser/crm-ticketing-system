#!/usr/bin/env bash
#
# bootstrap-github.sh — one-shot GitHub setup for the CRM Ticketing System.
#
# Creates the repository, pushes main, then builds a Projects v2 board with
# sprint milestones, labels, and an issue backlog derived from the SquadKit
# plan index (.squad/plans/00-index.md).
#
# Usage:
#   gh auth login                      # once, interactively
#   ./scripts/bootstrap-github.sh      # from the repository root
#
# Everything is idempotent: re-running skips what already exists.
#
set -euo pipefail

# ─── Configuration ────────────────────────────────────────────────────────────
OWNER="${OWNER:-esraa-nasser}"
REPO="${REPO:-crm-ticketing-system}"
VISIBILITY="${VISIBILITY:-private}"          # private | public
PROJECT_TITLE="${PROJECT_TITLE:-CRM Ticketing System}"

SPRINT_WEEKS="${SPRINT_WEEKS:-2}"            # length of each sprint
SPRINT_COUNT="${SPRINT_COUNT:-5}"
SPRINT_START="${SPRINT_START:-}"             # YYYY-MM-DD; default = next Monday

DRY_RUN="${DRY_RUN:-0}"                      # 1 = print actions, change nothing

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
run()  { if [[ "$DRY_RUN" == "1" ]]; then printf '  %s[dry-run]%s %s\n' "$D" "$N" "$*"; else eval "$*"; fi; }

# ─── Preflight ────────────────────────────────────────────────────────────────
step "Preflight"

command -v gh  >/dev/null || die "GitHub CLI not found. Install: https://cli.github.com"
command -v git >/dev/null || die "git not found."
command -v jq  >/dev/null || die "jq not found. Install: https://jqlang.github.io/jq/download/"

gh auth status >/dev/null 2>&1 || die "Not authenticated. Run: gh auth login"
ok "gh authenticated as $(gh api user --jq .login)"

# Projects v2 needs the 'project' scope, which is not granted by default.
if ! gh auth status 2>&1 | grep -q 'project'; then
  warn "The 'project' scope is missing — the board step will fail."
  warn "Fix with:  gh auth refresh -h github.com -s project -s repo -s workflow"
  read -r -p "  Continue anyway? [y/N] " reply
  [[ "$reply" == "y" || "$reply" == "Y" ]] || exit 1
else
  ok "'project' scope present"
fi

[[ -f "CrmTicketing.slnx" ]] || die "Run this from the repository root (CrmTicketing.slnx not found)."
[[ -d ".git" ]] || die "No .git directory. This should be a git repository with a commit already made."
git rev-parse HEAD >/dev/null 2>&1 || die "No commits found. Commit before running this."
ok "repository root, $(git rev-list --count HEAD) commit(s) on $(git branch --show-current)"

# Date arithmetic differs between GNU and BSD userland.
if date -d "2026-01-01" +%F >/dev/null 2>&1; then
  DATE_KIND="gnu"
elif date -j -f "%Y-%m-%d" "2026-01-01" +%F >/dev/null 2>&1; then
  DATE_KIND="bsd"
else
  die "Neither GNU nor BSD 'date' semantics detected."
fi

date_add() {  # date_add <YYYY-MM-DD> <days> -> YYYY-MM-DD
  if [[ "$DATE_KIND" == "gnu" ]]; then
    date -u -d "$1 +$2 days" +%F
  else
    date -u -j -v"+$2"d -f "%Y-%m-%d" "$1" +%F
  fi
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

if gh repo view "${OWNER}/${REPO}" >/dev/null 2>&1; then
  skip "already exists"
  if ! git remote get-url origin >/dev/null 2>&1; then
    run "git remote add origin 'https://github.com/${OWNER}/${REPO}.git'"
    ok "added 'origin' remote"
  fi
  run "git push -u origin '$(git branch --show-current)'"
else
  run "gh repo create '${OWNER}/${REPO}' --${VISIBILITY} --source=. --remote=origin --push \
        --description 'CRM ticketing system — Blazor WebAssembly + ASP.NET Core, built with spec-driven development (SquadKit)'"
  ok "created and pushed"
fi

# ─── 2. Labels ────────────────────────────────────────────────────────────────
step "2/6  Labels"

# name|color|description
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
  if gh label list --repo "${OWNER}/${REPO}" --json name --jq '.[].name' 2>/dev/null | grep -qxF "$name"; then
    skip "$name"
  else
    run "gh label create '$name' --repo '${OWNER}/${REPO}' --color '$color' --description '$desc'" && ok "$name"
  fi
done

# ─── 3. Sprint milestones ─────────────────────────────────────────────────────
step "3/6  Sprint milestones"

declare -a SPRINT_TITLES=() SPRINT_STARTS=() SPRINT_ENDS=()
cursor="$SPRINT_START"
for ((i = 1; i <= SPRINT_COUNT; i++)); do
  span=$(( SPRINT_WEEKS * 7 ))
  end=$(date_add "$cursor" $(( span - 1 )))
  SPRINT_TITLES+=("Sprint ${i}")
  SPRINT_STARTS+=("$cursor")
  SPRINT_ENDS+=("$end")
  cursor=$(date_add "$cursor" "$span")
done

existing_ms=$(gh api "repos/${OWNER}/${REPO}/milestones?state=all&per_page=100" --jq '.[].title' 2>/dev/null || echo "")
for ((i = 0; i < SPRINT_COUNT; i++)); do
  title="${SPRINT_TITLES[$i]}"
  if grep -qxF "$title" <<< "$existing_ms"; then
    skip "$title"
  else
    run "gh api 'repos/${OWNER}/${REPO}/milestones' -X POST \
          -f title='$title' \
          -f state='open' \
          -f description='${SPRINT_STARTS[$i]} → ${SPRINT_ENDS[$i]}' \
          -f due_on='${SPRINT_ENDS[$i]}T23:59:59Z' >/dev/null" \
      && ok "$title  (${SPRINT_STARTS[$i]} → ${SPRINT_ENDS[$i]})"
  fi
done

ms_number() { gh api "repos/${OWNER}/${REPO}/milestones?state=all&per_page=100" --jq ".[] | select(.title==\"$1\") | .number"; }

# ─── 4. Issue backlog ─────────────────────────────────────────────────────────
step "4/6  Issue backlog"

# sprint#|labels|feature-slug|title|body
ISSUES=(
# ── Sprint 1 — persistence + auth foundations ──
"1|epic,area:infra,sdd:needs-intake|persistence|EPIC: Persistence layer — data store, EF Core, migrations|Choose and wire the data store. \`src/CrmTicketing.Infrastructure/Persistence/\` is currently a stub README by design; this epic replaces it.\n\nConstitution rules that bind here (docs/constitution.md §II):\n- \`CrmTicketing.Domain\` must not reference EF Core. Mapping lives in Infrastructure.\n- No \`DbContext\` reaches an Api controller directly.\n\nDecide: SQLite vs SQL Server vs PostgreSQL. Record the decision in docs/architecture.md."
"1|area:infra,sdd:needs-intake|persistence|Add CrmDbContext and IEntityTypeConfiguration convention|Create \`CrmDbContext\` plus a \`Configurations/\` folder with one \`IEntityTypeConfiguration<T>\` per aggregate. Register in the Api composition root behind an abstraction, not as a concrete type.\n\nAdd the EF Core package versions to \`Directory.Packages.props\` — never inline in a csproj."
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

existing_titles=$(gh issue list --repo "${OWNER}/${REPO}" --state all --limit 400 --json title --jq '.[].title' 2>/dev/null || echo "")
declare -a CREATED_URLS=()

for spec in "${ISSUES[@]}"; do
  IFS='|' read -r sprint labels slug title body <<< "$spec"

  if grep -qxF "$title" <<< "$existing_titles"; then
    skip "$title"
    continue
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

  if [[ "$DRY_RUN" == "1" ]]; then
    printf '  %s[dry-run]%s issue: %s  (S%s, %s)\n' "$D" "$N" "$title" "$sprint" "$labels"
    continue
  fi

  url=$(gh issue create --repo "${OWNER}/${REPO}" \
          --title "$title" \
          --body "$full_body" \
          --label "$labels" \
          --milestone "Sprint ${sprint}")
  CREATED_URLS+=("$url")
  ok "S${sprint}  ${title}"
done

# ─── 5. Projects v2 board ─────────────────────────────────────────────────────
step "5/6  Projects v2 board"

if [[ "$DRY_RUN" == "1" ]]; then
  warn "skipped under DRY_RUN"
else
  OWNER_ID=$(gh api graphql -f query='query($login:String!){ user(login:$login){ id } }' \
               -f login="$OWNER" --jq '.data.user.id')

  PROJECT_ID=$(gh project list --owner "$OWNER" --format json --limit 100 2>/dev/null \
                | jq -r --arg t "$PROJECT_TITLE" '.projects[]? | select(.title==$t) | .id' | head -1)

  if [[ -n "$PROJECT_ID" ]]; then
    skip "project '${PROJECT_TITLE}' already exists"
  else
    PROJECT_ID=$(gh api graphql -f query='
      mutation($ownerId:ID!,$title:String!){
        createProjectV2(input:{ownerId:$ownerId,title:$title}){ projectV2{ id number url } }
      }' -f ownerId="$OWNER_ID" -f title="$PROJECT_TITLE" \
      --jq '.data.createProjectV2.projectV2.id')
    ok "created project '${PROJECT_TITLE}'"
  fi

  PROJECT_URL=$(gh api graphql -f query='query($id:ID!){ node(id:$id){ ... on ProjectV2 { url } } }' \
                  -f id="$PROJECT_ID" --jq '.data.node.url')

  # --- Sprint field -----------------------------------------------------------
  # GitHub's native Iteration field is the ideal shape here, but it is a distinct
  # field type and createProjectV2Field only accepts TEXT / NUMBER / DATE /
  # SINGLE_SELECT. We attempt ITERATION anyway so the outcome is recorded rather
  # than assumed, then fall back to a single-select.
  step "      sprint field"
  iter_err=$(gh api graphql -f query='
    mutation($p:ID!){
      createProjectV2Field(input:{projectId:$p,dataType:ITERATION,name:"Sprint"}){
        projectV2Field{ ... on ProjectV2IterationField { id } }
      }
    }' -f p="$PROJECT_ID" 2>&1 >/dev/null || true)

  if [[ -z "$iter_err" ]]; then
    ok "native ITERATION field created — sprints are real iterations"
  else
    warn "ITERATION not creatable via API (as expected); using a single-select"
    printf '    %s%s%s\n' "$D" "$(head -c 160 <<< "$iter_err")" "$N"

    opts="["
    for ((i = 0; i < SPRINT_COUNT; i++)); do
      colors=(BLUE GREEN YELLOW ORANGE PURPLE PINK RED GRAY)
      c="${colors[$(( i % 8 ))]}"
      [[ $i -gt 0 ]] && opts+=","
      opts+="{\"name\":\"${SPRINT_TITLES[$i]}\",\"color\":\"${c}\",\"description\":\"${SPRINT_STARTS[$i]} to ${SPRINT_ENDS[$i]}\"}"
    done
    opts+="]"

    gh api graphql -f query="
      mutation(\$p:ID!){
        createProjectV2Field(input:{
          projectId:\$p, dataType:SINGLE_SELECT, name:\"Sprint\",
          singleSelectOptions:${opts}
        }){ projectV2Field{ ... on ProjectV2SingleSelectField { id name } } }
      }" -f p="$PROJECT_ID" --jq '.data.createProjectV2Field.projectV2Field.name' >/dev/null 2>&1 \
      && ok "single-select 'Sprint' with ${SPRINT_COUNT} options" \
      || warn "'Sprint' field already exists or could not be created"
  fi

  # --- Priority and Estimate --------------------------------------------------
  gh api graphql -f query='
    mutation($p:ID!){
      createProjectV2Field(input:{
        projectId:$p, dataType:SINGLE_SELECT, name:"Priority",
        singleSelectOptions:[
          {name:"P0 — blocker",color:RED,description:"Stops the sprint"},
          {name:"P1 — high",color:ORANGE,description:"Sprint commitment"},
          {name:"P2 — normal",color:YELLOW,description:"Planned"},
          {name:"P3 — low",color:GRAY,description:"Nice to have"}
        ]
      }){ projectV2Field{ ... on ProjectV2SingleSelectField { name } } }
    }' -f p="$PROJECT_ID" >/dev/null 2>&1 \
    && ok "'Priority' field" || skip "'Priority' already exists"

  gh api graphql -f query='
    mutation($p:ID!){
      createProjectV2Field(input:{projectId:$p,dataType:NUMBER,name:"Estimate"}){
        projectV2Field{ ... on ProjectV2Field { name } }
      }
    }' -f p="$PROJECT_ID" >/dev/null 2>&1 \
    && ok "'Estimate' field" || skip "'Estimate' already exists"

  # --- Add every issue to the board -------------------------------------------
  step "      adding issues to the board"
  added=0
  while read -r node_id; do
    [[ -z "$node_id" ]] && continue
    gh api graphql -f query='
      mutation($p:ID!,$c:ID!){ addProjectV2ItemById(input:{projectId:$p,contentId:$c}){ item{ id } } }' \
      -f p="$PROJECT_ID" -f c="$node_id" >/dev/null 2>&1 && added=$(( added + 1 )) || true
  done < <(gh issue list --repo "${OWNER}/${REPO}" --state all --limit 400 --json id --jq '.[].id')
  ok "${added} issue(s) on the board"
fi

# ─── 6. Repository settings ───────────────────────────────────────────────────
step "6/6  Repository settings"

run "gh api 'repos/${OWNER}/${REPO}' -X PATCH \
      -F has_issues=true -F has_projects=true -F has_wiki=false \
      -F delete_branch_on_merge=true \
      -F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false >/dev/null" \
  && ok "issues on, wiki off, squash-only, auto-delete merged branches"

run "gh api 'repos/${OWNER}/${REPO}/topics' -X PUT \
      -f 'names[]=blazor' -f 'names[]=csharp' -f 'names[]=dotnet' \
      -f 'names[]=crm' -f 'names[]=ticketing' -f 'names[]=spec-driven-development' >/dev/null" \
  && ok "topics set"

# ─── Summary ──────────────────────────────────────────────────────────────────
printf '\n%s─── Done ───%s\n\n' "$B" "$N"
printf '  Repository   https://github.com/%s/%s\n' "$OWNER" "$REPO"
[[ "${PROJECT_URL:-}" != "" ]] && printf '  Project      %s\n' "$PROJECT_URL"
printf '  Issues       https://github.com/%s/%s/issues\n' "$OWNER" "$REPO"
printf '  Milestones   https://github.com/%s/%s/milestones\n' "$OWNER" "$REPO"
printf '  Actions      https://github.com/%s/%s/actions\n\n' "$OWNER" "$REPO"

cat <<'NEXT'
  Next:

    1. Watch CI go green:            gh run watch
    2. Point SquadKit at the repo:   export GITHUB_TOKEN=<your token>
                                     squad doctor          # should be fully green
    3. Start the first real story:   squad new-story persistence --id <issue-number>
                                     # fill the intake, then /squad-plan it

  Board columns default to Todo / In Progress / Done. If the Sprint field came out
  as a single-select, converting it to a native Iteration field takes about a
  minute in the project's field settings — the API cannot create that type.

NEXT
