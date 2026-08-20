# CRM Ticketing System — Engineering Constitution

The non-negotiable rules for this repository. Every SquadKit plan is written
against this document, and every pull request is reviewed against it. When a
rule here conflicts with a plan, **this document wins** — fix the plan.

Amendments happen by pull request that edits this file, with a one-line entry
added to [Amendment log](#amendment-log). Nothing is amended mid-implementation.

---

## I. Specs precede code

No production code is written before a story intake exists under
`.squad/stories/` and a plan exists under `.squad/plans/`.

- Ambiguity is resolved **in the intake**, not in a code review comment.
- If an implementation session discovers the plan is wrong, it stops and the
  plan is revised. Silently diverging from the plan is a defect.
- Trivial changes (typo, comment, version bump) are exempt. Anything that adds
  or changes behaviour is not.

## II. The layer graph is acyclic and one-directional

```
Client ─────────► Shared ◄───────── Api
                                     │
                                     ├──► Domain
                                     └──► Infrastructure ──► Domain
```

- `CrmTicketing.Domain` references **nothing** — no EF Core, no ASP.NET Core,
  no `Shared`. It is plain C# expressing the business.
- `CrmTicketing.Shared` holds only DTOs and contracts. No behaviour, no
  dependencies beyond the base class library.
- `CrmTicketing.Infrastructure` owns persistence and outbound integrations. It
  maps to and from domain types; the mapping never leaks upward.
- `CrmTicketing.Api` is the only composition root. It is the only project that
  knows how the others are wired together.
- `CrmTicketing.Client` talks to the API over HTTP and to `Shared` for contract
  types. It never references `Domain` or `Infrastructure`.

A pull request that adds an edge not on this diagram must first amend this
document.

## III. Domain types protect their own invariants

- No public parameterless constructors and no public setters on entities where
  a caller could produce an invalid instance.
- Invalid state throws at construction, not at save time.
- Enumerations that represent workflow (ticket status, priority) are modelled
  explicitly, and illegal transitions are rejected by the domain — not by the
  UI and not by a database constraint alone.

## IV. Contracts are versioned and explicit

- Every endpoint takes and returns a type from `CrmTicketing.Shared.Contracts`.
  Domain entities are never serialised onto the wire.
- Request and response types are `sealed record`s.
- Errors use RFC 9457 problem details (`AddProblemDetails`). No bespoke error
  envelopes and no exception text returned to callers.
- A breaking change to a contract is a new route or a new type, never a silent
  reshape of an existing one.

## V. Tests are part of the deliverable, not a follow-up

- Every plan states its verification commands. A story is done when those pass.
- Domain rules get unit tests. Endpoints get tests at the controller or
  `WebApplicationFactory` level.
- `dotnet build` runs with `TreatWarningsAsErrors`. A green build with
  suppressed warnings is not green — suppressions need a comment saying why.
- No test asserts on wall-clock time or ambient state. `TimeProvider` is
  injected; `DateTime.Now` and `DateTime.UtcNow` are banned in production code.

## VI. Configuration and secrets

- No secret, connection string, token, or key is committed. `.squad/secrets.yaml`
  and `.env` are git-ignored and CI fails if they are tracked.
- Local development uses `dotnet user-secrets`; deployed environments use their
  platform's secret store.
- The client's API base address comes from `wwwroot/appsettings.json`. Note that
  **everything shipped to a WebAssembly client is public** — no secret ever goes
  in the `Client` project.
- CORS origins are an explicit allow-list. `AllowAnyOrigin` is never used.

## VII. Simplicity is the default

- Three strikes before abstraction: write it concretely until the third caller
  appears.
- No new NuGet dependency without a line in the plan justifying it. Versions
  live only in `Directory.Packages.props`.
- No new project in the solution without an amendment to Section II.
- Prefer deleting code to configuring it.

## VIII. Every change is traceable

- Commits reference the story: `feat(tickets): add SLA due date [NN-story-slug]`.
- Conventional Commit prefixes: `feat`, `fix`, `refactor`, `test`, `docs`,
  `chore`, `ci`.
- Pull requests link the plan file they implement. A PR with no plan link needs
  a one-line explanation of which exemption in Section I applies.
- `main` is protected. Work happens on `feature/<story-slug>` branches.

---

## Amendment log

| Date | Change | Rationale |
|------|--------|-----------|
| 2026-08-20 | Initial constitution ratified alongside the repository scaffold. | Establishes the rules the first planned story is written against. |
