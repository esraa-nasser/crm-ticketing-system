# Story intake

- Folder: `.squad/stories/demo-data/4/intake.md`

---

## Feature

- **Feature name (display):** Demo data — a reproducible database from zero
- **Feature slug (folder under `plans/`):** `demo-data`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `4`
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:infrastructure`, `sdd:needs-plan`

---

## Title

```
Seed a demonstrable database from an empty one
```

---

## Description

```
Make the system demonstrable from nothing. Today a fresh clone produces an empty
database: story 06's migration deletes any pre-existing tickets, the bootstrap
Admin is the only account, and every ticket after that has to be created by hand
with a bearer token. Showing the product to anyone means six curl calls and a
Guid copied between them.

This story adds demo seeding: two further users, and a set of tickets spread
across statuses, priorities, assignment, and age. It is the last part of issue
#4 - the migration tooling half landed with the EF Core Design package and
InitialCreate.

Seeding runs through the domain, not through SQL. Ticket.Open and TransitionTo
enforce the invariants and set CreatedBy, so seeded rows are indistinguishable
from rows a real user produced. A seeder writing INSERT statements could
manufacture a ticket in a state the aggregate forbids, and the first person to
trust the demo would be trusting a lie.

It is off unless switched on. A demo dataset appearing in a real deployment is
worse than no seeding at all.
```

---

## Acceptance criteria

```
- [ ] Seeding is disabled by default. It runs only when a configuration flag is
      explicitly true. Absent configuration seeds nothing and logs nothing
      alarming.
      Verify: starting the API with no seed configuration leaves the database
      exactly as it was.
- [ ] Idempotent. Running startup twice does not duplicate users or tickets.
      A database that already holds tickets is left untouched entirely - the
      seeder never merges into existing data.
- [ ] Users seeded: one Agent and one Customer. **No Admin is seeded here.**
      Story 06's bootstrap Admin is the single path by which a privileged account
      comes into existence, and a second one would mean two configuration shapes
      creating privileged accounts with no rule about which wins.
- [ ] Demo seeding **requires** an Admin to already exist. If the flag is on and
      the users table holds no user in the Admin role, seed nothing and throw,
      naming `Identity:BootstrapAdmin:Email` and `:Password`. A demo with two of
      the three roles cannot show account creation or the Admin side of any
      permission rule, and silently producing one would look like a defect in
      story 06 rather than missing configuration here.
      Verify: enabling the flag with no bootstrap Admin configured fails at
      startup with a message naming both keys.
- [ ] Emails and the shared password for the two demo users come from
      configuration; no password literal exists in the repository.
      Verify: git grep for the seeded password returns nothing.
- [ ] Tickets are created through Ticket.Open and moved with TransitionTo. No
      raw SQL, no direct DbContext.Add of a Ticket built by object initialiser.
      Verify: grep -rn "INSERT INTO ticket" src/ returns nothing.
- [ ] The seeded set exercises the surface a demo needs: every status including
      Closed, every priority, at least one assigned and one unassigned ticket,
      at least one with a null category, and tickets raised by both the Customer
      and the Agent so row-level filtering is visibly doing something.
- [ ] Ticket ages are spread across the past two weeks, taken from the injected
      TimeProvider rather than a literal date. Every ticket sharing one timestamp
      makes the list view look broken and makes ordering untestable by eye.
- [ ] Ordering is respected: users exist before tickets. The requester_id foreign
      key from story 06 rejects a ticket whose requester has not been created.
- [ ] Seeding failures are loud. A partial seed must not leave a half-populated
      database with the API reporting a clean start.
- [ ] docs/status.md and README.md describe how to switch seeding on, and the
      README's "create a ticket by curl" section notes seeding as the easier path.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no database running.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #3, #8, #9, #10 (merged). #5 and #6 (merged) — seeded tickets need real users because of the `requester_id` foreign key, and seeded users need Identity. Closes #4.
- **Depends on code areas or other stories:** `IdentitySeeder` and `SeedIdentityAsync` from story 06, `Ticket.Open`, `TicketStatusTransitions`, `TicketRepository`, `TimeProvider`.

## Extra notes

- Story 06 established the seeding seam: `IdentitySeeder` runs behind `SeedIdentityAsync`, called once from `Program.cs`. This story extends that seam rather than adding a second one — the composition root must still name one Infrastructure extension and no Identity type.
- The walkthrough for this project is the immediate consumer. If the seeded set does not make row-level filtering visible — an Agent seeing more than a Customer — it has not done its job.

## Technical hints

```
DECISIONS MADE

Off by default, on by explicit configuration. A single flag,
Seed:Demo:Enabled, false unless set. Not keyed off IsDevelopment(): a shared
development environment is still someone's environment, and the flag makes the
decision visible in configuration rather than implicit in a launch profile.

Refuse rather than merge. If the ticket table is non-empty, seed nothing and
return. Merging demo rows into a database someone is using is the failure mode
worth designing against - it is not recoverable by re-running anything.

Through the domain, always. Every seeded ticket comes from Ticket.Open followed
by TransitionTo calls, saved through ITicketRepository. This costs more code
than an INSERT and buys the guarantee that no seeded row can exist in a state the
aggregate forbids. It also exercises the transition table on every startup that
seeds, which is a free smoke test of story 03's rules.

Passwords from configuration, like story 06's bootstrap Admin:
  Seed:Demo:AgentEmail, Seed:Demo:CustomerEmail, Seed:Demo:Password
Absent means no seeding, same rule as the flag. Constitution section VI.

One Admin path, and it is not this one. Story 06 seeds the bootstrap Admin from
Identity:BootstrapAdmin; this story seeds an Agent and a Customer and depends on
that Admin already existing. Two mechanisms creating privileged accounts is the
kind of duplication that produces a production Admin nobody remembers
configuring. The dependency runs one way: demo seeding requires the bootstrap
Admin, never the reverse - story 06 must keep working with no demo data at all.

Ordering inside SeedIdentityAsync: roles, then the bootstrap Admin, then - only
when the flag is on - the demo users, then the tickets. Each stage depends on the
one before it.

Deterministic identifiers. Seeded users get fixed, obviously-fake Guids so a
demo URL captured today still resolves after a reseed. Tickets keep their
Version 7 Guids - they carry a timestamp and nothing links to them externally.

Ages from TimeProvider. The seeder resolves TimeProvider and computes instants as
offsets from GetUtcNow() - two hours ago, yesterday, twelve days ago. Never a
hardcoded date, which would age badly and would drift out of any window the list
view or a future SLA cares about.

Suggested shape, roughly a dozen tickets: several New and Open raised by the
Customer, a couple Pending, one or two Resolved, one Closed, with priorities
spread and two or three assigned to the Agent. Enough that pagination at 25 does
not trigger, and enough that filtering by status visibly changes the table.
```

## Out of scope

```
- No fake company names, invented customer identities, or realistic-looking
  personal data. Seeded content is obviously synthetic - "Printer offline in
  Meeting Room 3", not a plausible person with a plausible complaint. Demo data
  that reads as real gets screenshotted and mistaken for real.
- No seeding of comments or activity entries. That aggregate does not exist yet
  (#11).
- No SLA fields or due dates (#21).
- No CI database. The test suite runs with no database by design, and giving CI
  one is issue #29's decision, not this story's.
- No reset or teardown command. "Drop the database and re-run" is the documented
  path; a destructive command in the application is a liability that has to be
  guarded forever.
- No configurable dataset size, no randomisation, no faker library. A fixed,
  readable set that a human can reason about beats a generated one nobody can
  predict.
- No UI for triggering a seed. It is a startup concern.
- No change to the bootstrap Admin behaviour from story 06, and no second way to
  create an Admin. If demo seeding needs one, it requires the existing path
  rather than adding another.
```
