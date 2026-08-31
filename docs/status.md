# Project status — CRM Ticketing System

Prepared 31 August 2026. Companion to [README.md](../README.md), which covers
how to run the system. This document covers **what was built, what was not, and
why** — the questions a reviewer is most likely to ask.

---

## Summary

A working three-tier ticketing system: Blazor WebAssembly client, ASP.NET Core
Web API, PostgreSQL. The ticket domain model, its persistence, its complete HTTP
surface, and the ticket list screen are implemented, tested, and merged.
Authentication is specified and planned but not implemented.

| | |
|---|---|
| Stories merged | 5 |
| GitHub issues closed | #3, #8, #9, #10, #12 |
| Tests | 171, across four projects, passing with no API and no database |
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

**Client** (`CrmTicketing.Client`)

A ticket list screen: paged, filterable by status and priority, with filter
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
| 06 | A foreign key that would turn a working request into a 500; JWT role claims that would silently fail every role check |

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
| **Identity, roles, authorisation** | #5, #6 | **Next.** Intake and a 489-line reviewed plan exist. Not started. |
| Ticket detail view and write actions | #13 | Blocked on the above — it needs an acting user. |
| Permission-gated UI | #16 | Cosmetic until endpoints refuse the call. |
| Kanban board | #14 | Consumes the transition map the API already publishes. |
| Comments and activity timeline | #11 | Needs an aggregate-boundary decision of its own. |
| Seed data | #4 | Small; deferred behind features. |
| Repository integration tests | #29 | Query logic has unit coverage; nothing exercises it against a real database. |
| SLA policies | #21 | Needs business-hours arithmetic. |
| Accounts, contacts, reporting | — | Two feature areas with no intake written; unspecified, not merely unbuilt. |

### On authentication specifically

Authentication was originally scheduled *after* the remaining UI. It was moved
ahead of it during planning, for a reason worth stating plainly: **every mutation
in the system is currently anonymous.** A ticket records when it was created and
last changed, but not by whom. That gap cannot be backfilled honestly, and it
widens with every ticket created — so it is the one remaining item that gets
strictly more expensive the longer it waits. Everything else on the list costs
the same whenever it is done.

The second reason is that a customer-facing portal makes `GET /api/tickets` a
security boundary: a customer must see only their own tickets. That belongs in
the repository, not in a screen. Building the detail view and the board on top of
an unfiltered query and retrofitting the filter underneath three screens is how
a disclosure bug happens.

---

## Known defects

- **#31 — `GET /health` reports `Healthy` when the database is unreachable.** The
  probe does not check the database. Found by inspection, not by an incident.
- **#29 — no integration test behind the repository's filtering, ordering, and
  paging.** These are exercised through a fake, so a mistake in the translation
  to SQL would not be caught. This matters more once authorisation adds a
  security-critical filter to the same code.
- Row ordering in the list is unverified. It appears newest-first and is
  consistent with the query, but no test asserts it, so no UI copy claims it.

---

## Anticipated questions

**Why is there no login screen?**
Deliberate sequencing, not an omission. Authorising a request requires knowing
what is being authorised — the aggregate, the endpoints, and the query surface
all had to exist first. It is the next story, with a reviewed plan.

**Can a user create a ticket?**
Over the API, yes — verified live, returning 201 with a correct `Location`
header. Not from the UI: the list screen is read-only, and write actions are
issue #13, which follows authentication because it needs an acting user.

**How do you know it works?**
Two independent ways. 171 automated tests that run with no API and no database.
And a live end-to-end pass against real PostgreSQL confirming things tests
cannot: that `Location` resolves, that a value object materialises on read, that
`pageSize=500` is clamped to 100 by the server, and that an illegal transition
returns 409 with the attempted values — including after a process restart, which
proved the refusal came from stored state rather than a cached object.

**Five stories seems slow.**
The specification work is front-loaded, and it substitutes for rework. The table
above lists defects caught before code existed, including two in the auth plan
that would each have cost a day of debugging. The remaining work is also
lumpier than an issue count suggests: the architectural decisions that are
expensive to reverse — the aggregate boundary, the layer graph, the error
contract, the persistence convention — are settled and verified.

**Is it production-ready?**
No, and it is not claimed to be. It needs authentication, seed data, the health
check fixed, integration tests behind the repository, and a deployment story.

**What would you do differently?**
Two things. The README drifted badly — it described the project as an empty
scaffold long after five stories had merged, which is the kind of error that
misrepresents finished work. It is now accurate and is treated as a deliverable.
And the same defect class recurred across plans — a miscount of test projects
appeared in three separate plans even after being corrected twice. That says the
fix belongs in the planning template, not in each plan.
