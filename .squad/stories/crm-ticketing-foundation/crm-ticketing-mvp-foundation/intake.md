> **Source:** manual entry (tracker skipped via `--no-tracker`).
> Active tracker for this workspace: `github` — this story is not linked.
> Run `squad tracker link <story-path> <tracker-id>` later if you want to attach one.

# Story intake

Fill this template for each story you want planned. Keep it copy-paste-friendly: the planner reads **this file and the files in `attachments/`**, nothing else.

- Folder: `.squad/stories/crm-ticketing-foundation/crm-ticketing-mvp-foundation/intake.md`
- Binaries (screenshots, PDFs, exports): put them in `attachments/` next to this file and list them below.
- Do **not** rely on external links (tracker URLs, wiki, chat) — the planner cannot open them. Paste the content you want considered.

This is **not** an implementation prompt. It is the input to the plan-generation meta-prompt bundled with squad-kit (`generate-plan.md` in the installed package).

---

## Feature

- **Feature name (display):** CRM Ticketing — Foundation
- **Feature slug (folder under `plans/`):** `crm-ticketing-foundation`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `` *(unlinked; run `squad tracker link` once the GitHub issue exists)*
- **Work item type:** `Task`
- **Status:** `Done` *(delivered by the repository scaffold — see Description)*
- **Assignee:** `esraa-nasser`
- **Labels:** `foundation`, `architecture`, `sdd`

External tracker links are **not** followed by the planner. Keep the id for naming and traceability only.

---

## Title

*(Paste the work item title verbatim. Prefilled when `squad new-story` fetched from a tracker.)*

```
CRM ticketing MVP foundation
```

---

## Description

*(Paste the full work item description. Prefilled when fetched from a tracker.)*

```
Stand up the repository and engineering baseline for a CRM ticketing system so
that all subsequent feature work can be specified, planned, and implemented
through the SquadKit loop rather than improvised.

This story covers the skeleton only. It deliberately implements no ticketing or
CRM behaviour — the product scope is captured in follow-up stories once it has
been agreed.

Delivered:

- .NET 10 solution (`CrmTicketing.slnx`) with a one-directional layer graph:
  Domain (no dependencies) <- Infrastructure <- Api -> Shared <- Client.
- Standalone Blazor WebAssembly client and a separate ASP.NET Core Web API,
  wired across origins via a configuration-driven CORS allow-list plus an
  `Api:BaseAddress` setting on the client.
- A `/diagnostics` page and `GET /api/system/info` endpoint whose only job is to
  prove the client-to-API wiring works and to name the setting to fix when it
  does not.
- Central package management (`Directory.Packages.props`), shared compiler
  settings with warnings-as-errors, `.editorconfig`, `.gitattributes`.
- Two xUnit projects with real (if small) tests, so the verification command in
  every future plan has something to run against.
- Engineering constitution (`docs/constitution.md`) as the standing contract that
  all plans are written against; architecture and workflow docs alongside it.
- SquadKit workspace initialised: `github` tracker, `claude-code` + `copilot`
  agents, global story sequence.
- CI with two jobs: build/test/publish, and an SDD guard that runs `squad doctor`
  and fails if `.squad/secrets.yaml` is ever tracked.
```

---

## Acceptance criteria

*(Checklist, bullets, Gherkin, etc. Prefilled for Azure DevOps when the work item has acceptance criteria.)*

```
- [x] `dotnet restore && dotnet build CrmTicketing.slnx` succeeds with zero
      warnings under TreatWarningsAsErrors.
- [x] `dotnet test CrmTicketing.slnx` runs and passes in both test projects.
- [x] `CrmTicketing.Domain.csproj` declares no PackageReference and no
      ProjectReference.
- [x] `CrmTicketing.Client` references `CrmTicketing.Shared` only — not Domain,
      not Infrastructure, not Api.
- [x] No `.csproj` in the solution carries a `Version` attribute on a
      PackageReference; all versions resolve from `Directory.Packages.props`.
- [x] Running the API and the client together, `/diagnostics` renders the API
      name, version, environment, and server clock.
- [x] With `Cors:AllowedOrigins` empty, a cross-origin browser call is refused —
      there is no permissive fallback.
- [x] `squad doctor` reports a healthy workspace.
- [x] `.squad/secrets.yaml` is git-ignored and untracked; CI fails if that changes.
- [x] `docs/constitution.md` exists and states the layer rules, contract rules,
      test rules, and secret-handling rules.
```

---

## Attachments

Place files in `attachments/` next to this `intake.md`, then list them here so the planner knows what to open.

| File (relative to this folder) | What it is |
| ------------------------------ | ---------- |
| *(none)* | — |

None.

---

## Dependencies

- **Blocked by / related ids:** none — this is the first story in the repository.
- **Depends on code areas or other stories:** none. Every subsequent story depends
  on this one for its layer boundaries, contract conventions, and verification
  commands.

## Extra notes (optional)

- Product scope (which of core ticketing, customer/contact CRM, auth & roles, and
  dashboards land in the MVP) is **still open** and is the subject of the next
  intake. Do not infer it from this story.
- Hosting model was chosen as *standalone* Blazor WebAssembly plus a separate Web
  API — not Blazor Server and not an ASP.NET-hosted WASM app. The cross-origin
  cost of that choice is paid explicitly in `CorsPolicies` and the client's
  `Api:BaseAddress`.

## Technical hints (optional)

- APIs, screens, services already discussed. Repos/roots: `.`. Primary language: `C#`.
- Target framework `net10.0` across all projects, set once in
  `Directory.Build.props`.
- `TimeProvider` is injected rather than calling `DateTime.UtcNow`, so any story
  involving SLA clocks or timestamps has a testable seam from day one.
- Error responses use RFC 9457 problem details via `AddProblemDetails()` /
  `UseExceptionHandler()`; new endpoints should not invent an error envelope.
- Typed HTTP clients live in `src/CrmTicketing.Client/Services/`;
  `SystemApiClient` is the reference implementation to copy.

## Out of scope

- Any ticket, customer, contact, comment, or SLA entity or endpoint.
- Authentication, authorisation, identity, and the role model.
- Choice of database, ORM, and migration strategy —
  `Infrastructure/Persistence/` is intentionally a stub with a README.
- UI design system beyond the default Bootstrap the template ships.
- Real-time updates, notifications, email ingestion, file attachments.
- Deployment, hosting, and infrastructure-as-code beyond the CI workflow.
- Localisation and right-to-left layout.
