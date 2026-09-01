# Project status — CRM Ticketing System

Prepared 31 August 2026. Companion to [README.md](../README.md), which covers
how to run the system. This document covers **what was built, what was not, and
why** — the questions a reviewer is most likely to ask.

---

## Summary

A working three-tier ticketing system: Blazor WebAssembly client, ASP.NET Core
Web API, PostgreSQL. The ticket domain model, its persistence, its complete HTTP
surface, the ticket list screen, identity with role-based authorisation, and a
seeded demo dataset are implemented, tested, and merged.

| | |
|---|---|
| Stories merged | 7 |
| GitHub issues closed | #3, #4, #5, #6, #8, #9, #10, #12 |
| Tests | 254, across four projects, passing with no API and no database |
| Merges to `main` | All via pull request, gated on build, test, and an SDD check |
| Live verification | Every endpoint exercised against a real PostgreSQL database |

---

## What was built

**Domain** (`CrmTicketing.Domain`, zero dependencies)

The `Ticket` aggregate with enforced invariants: a required trimmed title of
1–200 characters as a `TicketTitle` value object, a required description up to
10,000 characters, a non-empty requester, an optional assignee, and an optional
category. A ticket always starts at `New` regardless of what the caller asks
for. Five statuses, four priorities, and a transition table declaring ten legal
moves with `Closed` terminal.

The domain reads no clock. Every mutator takes the instant as a parameter, so
its behaviour is deterministic and testable without freezing time.

**Infrastructure** (`CrmTicketing.Infrastructure`)

EF Core 10 over PostgreSQL. Entity configuration is discovered by convention
rather than registered by hand, and a naming convention rewrites every table and
column to snake_case, so the database is readable without knowing C# naming
rules. Enums persist as strings, so reordering an enum cannot corrupt stored
data. Two migrations.

**API** (`CrmTicketing.Api`)

Seven ticket endpoints plus system info, health, and OpenAPI. Every failure
returns RFC 9457 problem details. An illegal status transition returns **409
Conflict** carrying the attempted `from` and `to` values — distinct from a 400
for malformed input and a 500 for a genuine fault. A metadata endpoint publishes
the statuses, priorities, and transition map so clients render legal actions
from the server rather than re-encoding the rules.

**Identity and authorisation** (`CrmTicketing.Infrastructure`, `CrmTicketing.Api`)

ASP.NET Core Identity over the same PostgreSQL database, with `Guid` keys so the
requester and assignee fields that were previously opaque now reference real
users. Three seeded roles — Admin, Agent, Customer — and bearer-token sign-in.
Every ticket endpoint requires an authenticated caller.

A Customer sees only the tickets they raised, and that constraint lives in
`TicketRepository` rather than in a controller or a screen, so no future caller
can bypass it. Requesting another customer's ticket returns 404 rather than 403,
because a 403 confirms the ticket exists.

Every mutation now records who made it. A requester may withdraw their own ticket
or reject a resolution; every other transition is staff-only and returns 403 —
distinct from the 409 that means the workflow itself forbids the move.

Identity types live only in Infrastructure. The domain still has zero package
references and knows nothing about users.

**Demo data** (`CrmTicketing.Infrastructure`)

Twelve tickets and two further users — an Agent and a Customer — seeded at startup
so the system is demonstrable from an empty database. Off unless
`Seed:Demo:Enabled` is explicitly true, and not keyed off the environment name: a
shared development environment is still someone's environment.

Every seeded ticket is built by `Ticket.Open` and moved by `TransitionTo`, never by
an `INSERT`, so a seeded row cannot exist in a state the aggregate forbids — and
each seeding startup walks the transition table, which is a free smoke test of the
story 03 rules against a real database.

Nine of the twelve are raised by the Customer and three by the Agent, so signing in
as each shows **9 tickets versus 12**. That split is the point: it is the visible
evidence that row-level filtering is doing something.

It refuses rather than merges. A ticket table that already holds rows is left
entirely alone, because adding demo data to a database someone is using cannot be
undone by re-running anything. It also requires the bootstrap Admin to exist and
fails loudly if it does not, rather than producing a demo missing a third of its
roles.

**Client** (`CrmTicketing.Client`)

A sign-in screen, a bearer token held in memory rather than `localStorage`, and a
ticket list screen: paged, filterable by status and priority, with filter
options fetched from the API rather than hardcoded. Filter and page state live in
the URL, so a filtered view is linkable and the browser back button works. Four
visually distinct states — loading, rows, empty, failed — so a connection failure
never reads as "your data is gone."

---

## How it was built

Every feature travelled the same path: a written intake stating what and why, a
plan stating how in concrete terms, then implementation against that plan. Plans
are reviewed before code is written.

That review is not ceremony, and the evidence is specific. Defects caught in
plans, before any code existed:

| Story | Defect found in review |
|---|---|
| 02 | Required a test to access an `internal` member without granting access |
| 03 | Transition counts wrong in six places; the code was right, the prose was not |
| 04 | Mandated an exception message that contradicted an existing assertion |
| 05 | Specified an exception class that could not compile; a section arguing against its own acceptance criteria; a verification step whose `grep` would report generated code as a violation |
| 06 | A foreign key that would turn a working request into a 500; JWT role claims that would silently fail every role check; a seeder with no call site; a bootstrap gap making the whole story unusable; a transition rule expressed as prose that permitted a customer to self-triage |
| 07 | A guard that returned silently where its neighbour threw, for the same failure; a miscounted row in the plan's own data table; two edge cases citing the wrong guard after a renumber; a verification `grep` matching every type whose name merely starts with `Ticket` |

**And one the review missed.** Story 08's implementation found an open redirect
that had been live on `main` since story 06: the sign-in page read a `returnUrl`
query parameter and navigated to it without checking it was same-site, so a link
to `/signin?returnUrl=https://evil.example` would have sent a user to another
host immediately after they typed their password — and the application would have
appeared to send them. It is fixed in story 08, which accepts only a path
beginning with a single `/`, rejecting the protocol-relative `//host` form that a
naive relative-URL check misses.

Worth stating plainly rather than burying: story 06's plan never mentioned
`returnUrl`. The parameter was added during implementation as a reasonable piece
of user experience, and the review that followed read the plan rather than the
code. **Reviewing specifications cannot catch a defect in code the specification
never described.** Six stories of specification review caught a great deal; the
one security defect that reached `main` came through the gap beside it. The
remedy is a code-reading pass on diffs that add behaviour no plan asked for, not
more plan review.

Additionally, an error-contract design was changed after capturing a real API
response: the planned three-field problem-details record would have shown users
"One or more validation errors occurred." instead of the actual validation
message, on the most likely error path in the application. That was found by
capturing the payload rather than assuming its shape — and captured payloads are
now used as test fixtures rather than hand-written ones.

**Engineering constraints, enforced rather than aspirational**

- Warnings are errors. No suppressions outside `.editorconfig`, each with a
  written reason.
- The layer dependency graph is one-directional: `Domain` and `Shared` reference
  nothing; `Client` references only `Shared`. Verified by a grep in every story's
  checklist.
- Every NuGet version lives in one file. An inline version is a defect.
- No secrets in the repository. CI fails if the secrets file becomes tracked.
- `main` is branch-protected; changes arrive by pull request with CI green.

---

## What was not built, and why

| Area | Issues | Status |
|---|---|---|
| Ticket detail view and write actions | #13 | **Next.** Authorisation is in place, so writes can now be attributed. |
| Permission-gated UI | #16 | Endpoints already refuse the call; this hides controls a role cannot use. |
| Kanban board | #14 | Consumes the transition map the API already publishes. |
| Comments and activity timeline | #11 | Needs an aggregate-boundary decision of its own. |
| Repository integration tests | #29 | Query logic has unit coverage; nothing exercises it against a real database. |
| SLA policies | #21 | Needs business-hours arithmetic. |
| Accounts, contacts, reporting | — | Two feature areas with no intake written; unspecified, not merely unbuilt. |

### On authentication specifically

Authentication was originally scheduled *after* the remaining UI, and was moved
ahead of it during planning. The reason is worth stating plainly, because it is
the clearest example of sequencing by cost rather than by visibility: **every
mutation was anonymous.** A ticket recorded when it changed but not who changed
it, and that gap cannot be backfilled honestly — it widens with every ticket
created. It was the one remaining item that got strictly more expensive the
longer it waited. Everything else costs the same whenever it is done.

The second reason was that a customer-facing portal makes `GET /api/tickets` a
security boundary. Building the detail view and the board on an unfiltered query
and retrofitting the filter underneath three screens is how a disclosure bug
happens. Doing it first meant the filter went into `TicketRepository` with one
caller instead of three.

The story was implemented against a plan that went through five rounds of
review. Two of the defects found would each have cost a day: a foreign key that
turned a working request into a 500, and JWT role claims that silently fail every
role check while looking like a policy bug. A third — the plan specified an
account-creation endpoint that requires an Admin, on a system with no way to
create the first one — would have shipped an authentication system nobody could
log into.

---

## Known defects

- **#31 — `GET /health` reports `Healthy` when the database is unreachable.** The
  probe does not check the database. Found by inspection, not by an incident.
- **#29 — no integration test behind the repository's filtering, ordering, and
  paging.** These are exercised through a fake, so a mistake in the translation
  to SQL would not be caught. This matters more once authorisation adds a
  security-critical filter to the same code.
- No automated test exercises a real HTTP 401 or 404. Authorisation is asserted at
  the attribute level by reflection and verified by hand against a running API;
  there is no integration-test host. Same root cause as #29.
- Row ordering in the list is unverified. It appears newest-first and is
  consistent with the query, but no test asserts it, so no UI copy claims it.
- **Last write wins on a ticket, silently.** There is no ETag, no version column,
  and no optimistic concurrency. Two agents editing the same ticket overwrite each
  other, and because every write re-reads from the server, the loser sees the
  winner's values without being told a collision happened. Accepted deliberately
  rather than half-built: a fix needs a version field on the aggregate, which is a
  domain change and a migration, and belongs in its own story.

---

## Anticipated questions

**How do you know the authorisation actually works?**
It was exercised live against real PostgreSQL with real tokens, not only by
tests. Signed in as a seeded Admin, created an Agent and a Customer, then
confirmed: the Agent's list returns two tickets where the Customer's returns one,
with the total count constrained to match; a Customer creating a ticket in
someone else's name has the requester forced to their own id; reading or
transitioning another customer's ticket returns 404; assignment is refused for a
Customer and succeeds for an Agent.

The unit tests were also checked for whether they *bite*: the access rule was
temporarily removed and the suite re-run, and exactly the expected tests failed.
A test that passes whether or not the rule exists is worse than no test.

**Can a user create a ticket?**
Over the API, yes — verified live. Not from the UI: the list screen is read-only,
and write actions are issue #13, which now has an acting user to attribute them
to.

**How do you know it works?**
Two independent ways. 171 automated tests that run with no API and no database.
And a live end-to-end pass against real PostgreSQL confirming things tests
cannot: that `Location` resolves, that a value object materialises on read, that
`pageSize=500` is clamped to 100 by the server, and that an illegal transition
returns 409 with the attempted values — including after a process restart, which
proved the refusal came from stored state rather than a cached object.

**Six stories seems slow.**
The specification work is front-loaded, and it substitutes for rework. The table
above lists defects caught before code existed, including two in the auth plan
that would each have cost a day of debugging. The remaining work is also
lumpier than an issue count suggests: the architectural decisions that are
expensive to reverse — the aggregate boundary, the layer graph, the error
contract, the persistence convention — are settled and verified.

**Is it production-ready?**
No, and it is not claimed to be. It needs the health
check fixed, integration tests behind the repository, and a deployment story.

**What would you do differently?**
Two things. The README drifted badly — it described the project as an empty
scaffold long after five stories had merged, which is the kind of error that
misrepresents finished work. It is now accurate and is treated as a deliverable.
And the same defect class recurred across plans — a miscount of test projects
appeared in three separate plans even after being corrected twice. That says the
fix belongs in the planning template, not in each plan.
