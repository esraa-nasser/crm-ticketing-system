# Story 07 — Seed a demonstrable database from an empty one (Story: 4)

## Prerequisites

- Story 06 completed: [`../auth-roles/06-story-identity-and-authorisation-5.md`](../auth-roles/06-story-identity-and-authorisation-5.md) — `IdentitySeeder`, `SeedIdentityAsync`, `RoleNames`, the `requester_id` foreign key, and the actor parameter on every mutator are merged on `main`.
- Story 03 completed: [`../ticketing-core/03-story-ticket-aggregate-8.md`](../ticketing-core/03-story-ticket-aggregate-8.md) — `Ticket.Open`, `TransitionTo`, and `TicketStatusTransitions` are the only way a ticket reaches a status.
- **This story extends story 06's seeding seam rather than adding a second one.** `Program.cs` keeps its single `await app.Services.SeedIdentityAsync(...)` call and still names no Identity type.
- A running PostgreSQL is required only for the manual verification step. Build and tests need neither a database nor a migration — this story adds none.
- **This is the first story in a new feature folder.** `.squad/plans/demo-data/00-overview.md` and a row in `.squad/plans/00-index.md` are created alongside the plan.

---

## Story Goal

Make the system demonstrable from an empty database, without hand-crafting tickets through curl.

1. Two demo users — one Agent, one Customer. **This story seeds no Admin**; story 06's bootstrap Admin is the single path by which a privileged account comes into existence, and demo seeding *requires* one to already exist.
2. Roughly a dozen tickets spread across every status, every priority, both assigned and unassigned, and a fortnight of ages.
3. **Off unless explicitly switched on.** Absent configuration seeds nothing.
4. **Refuses rather than merges.** A database that already holds tickets is left untouched.
5. Every seeded ticket is built by `Ticket.Open` and moved by `TransitionTo`, so a seeded row is indistinguishable from one a real user produced.

The walkthrough is the consumer. If an Agent and a Customer see the same number of tickets, the seed has failed its purpose.

---

## Context — Read These Files First

1. `src/CrmTicketing.Infrastructure/Identity/IdentitySeeder.cs` — all 124 lines. `SeedAsync` (~lines 31–35) is the sequence point this story appends to. `SeedBootstrapAdminAsync` (~lines 70–118) is the pattern to copy exactly: read configuration, return silently when absent, check existence before creating, throw with `Describe(result)` when Identity refuses, and delete the user if the role assignment fails. `Describe` (~line 123) exists so a failure message carries Identity's reasons and never the password.
2. `src/CrmTicketing.Infrastructure/DependencyInjection.cs` — all 77 lines. `SeedIdentityAsync` (~lines 65–76) opens the scope through `IServiceScopeFactory` and delegates. **This story changes its body by one call and its signature not at all.**
3. `src/CrmTicketing.Api/Program.cs` — all 44 lines. `AddSingleton(TimeProvider.System)` on line 7; `AddPersistence` on line 14; the single seeding call on line 24. **No change to this file.** If it appears in the diff, something is wrong.
4. `src/CrmTicketing.Domain/Tickets/Ticket.cs` — all 258 lines. `Open` (~lines 84–92) takes `(id, title, description, requesterId, createdAt, actorId, priority = Normal, category = null)`. `TransitionTo` (line 122), `Assign` (line 138), `ChangePriority` (line 172) each take `(…, DateTimeOffset at, Guid actorId)`. **Every seeded ticket goes through these.**
5. `src/CrmTicketing.Domain/Tickets/TicketStatusTransitions.cs` — the transition table. A seeded ticket cannot jump to `Pending`; it must walk `New → Open → Pending`. Read the table before choosing the target statuses in task 3.
6. `src/CrmTicketing.Domain/Tickets/ITicketRepository.cs` — all 30 lines. `AddAsync` (line 23), `CountAsync` (line 27), `SaveChangesAsync` (line 29). `CountAsync` takes a `TicketQuery`, which requires a `TicketAccess` — use `TicketAccess.All()` for the emptiness check.
7. `src/CrmTicketing.Infrastructure/Identity/RoleNames.cs` — `Agent` and `Customer` constants. **No role name appears as a literal in this story.**
8. `src/CrmTicketing.Api/appsettings.json` — all 20 lines. It carries `Jwt` with no `SigningKey`. This story adds no `Seed` section at all — see task 1.
9. `README.md` — `### Create the first account` (~line 146) documents the bootstrap Admin secrets; `### Run it` (~line 183) and the curl block (~lines 204–212) are what task 6 amends.
10. `docs/status.md` — `## What was built` (line 26) and `## What was not built, and why` (line 125). Task 6 moves demo data from the second to the first.
11. `docs/constitution.md` — §VI (line 75) configuration and secrets; §VII (line 86) three strikes before abstraction.

---

## Implementation tasks

### 1 — Configuration

Four keys, all under one section, none with a default:

```
Seed:Demo:Enabled          bool, must be exactly true
Seed:Demo:AgentEmail       string
Seed:Demo:CustomerEmail    string
Seed:Demo:Password         string
```

**Add nothing to `appsettings.json`.** Not the flag, not an empty placeholder. A `"Seed": { "Demo": { "Enabled": false } }` block invites someone to flip it in a file that ships, and an empty `Password` key invites someone to fill it in and commit it. The absence of the section is the off switch; story 06 made the same choice for `Jwt:SigningKey`.

Local use:

```bash
dotnet user-secrets set "Seed:Demo:Enabled" "true" --project src/CrmTicketing.Api
dotnet user-secrets set "Seed:Demo:AgentEmail" "agent@example.com" --project src/CrmTicketing.Api
dotnet user-secrets set "Seed:Demo:CustomerEmail" "customer@example.com" --project src/CrmTicketing.Api
dotnet user-secrets set "Seed:Demo:Password" "<a real password>" --project src/CrmTicketing.Api
```

**Not keyed off `IsDevelopment()`.** A shared development environment is still someone's environment, and an environment name is an implicit decision made in a launch profile. A flag is an explicit decision visible in configuration.

### 2 — The demo users

**Create file: `src/CrmTicketing.Infrastructure/Identity/DemoDataSeeder.cs`**

```csharp
internal static class DemoDataSeeder
{
    internal static Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}
```

`internal`, like `IdentitySeeder`, so `CrmTicketing.Api` still names no Identity type.

Fixed, obviously-fake identifiers, declared as constants:

```csharp
private static readonly Guid AgentId    = Guid.Parse("dddddddd-0000-0000-0000-00000000a6e7");
private static readonly Guid CustomerId = Guid.Parse("dddddddd-0000-0000-0000-0000000c0570");
```

Deterministic so a demo URL captured today still resolves after a reseed. Assign `user.Id` **before** `CreateAsync` — Identity generates one otherwise. Both are far from `Guid.Empty`, which `Ticket.Open` rejects.

Guard order, all five checks before anything is written. **The order matters, and only one of them throws:**

1. `Seed:Demo:Enabled` is not exactly `true` → **return silently.** Not a warning; the default state is not a problem.
2. `Seed:Demo:Enabled` is `true` but any of the three other keys is absent or whitespace → **throw**, naming the missing keys. **Not a silent return.** The operator has asked for a demo and would get none; the reasoning is identical to guard 3's, and treating the two differently would mean a missing password fails silently while a missing Admin fails loudly. Note this differs from story 06's bootstrap Admin, where absent configuration is the *only* signal — here the flag has already expressed intent.
3. **No user holds `RoleNames.Admin`** → **throw**, naming both `Identity:BootstrapAdmin:Email` and `Identity:BootstrapAdmin:Password`. Use `UserManager.GetUsersInRoleAsync(RoleNames.Admin)` and check for empty.
4. `CountAsync(TicketQuery.Create(TicketAccess.All()), ct) > 0` → **return, logging that seeding was skipped because the database is not empty.** This one *is* worth a log line: someone who switched the flag on and saw nothing happen needs to know why.
5. Otherwise seed.

**Which guards throw, and why they split that way.** Guards 1 and 4 describe states where doing nothing is the right answer: nobody asked for a demo, or the database already has data. Guards 2 and 3 do not — the flag is on, so the operator *did* ask, and silently producing nothing (guard 2) or a demo missing a third of its roles (guard 3) would send them debugging the wrong thing. Guard 3's message must name `Identity:BootstrapAdmin:Email` and `:Password` specifically, because the mistake lives in story 06's configuration and nothing about a demo-seeding failure would otherwise point there.

The dividing line is intent: **once `Enabled` is `true`, every subsequent problem is loud.**

It must come **before** guard 4. A database with tickets but no Admin is still misconfigured, and reporting "skipped, not empty" would hide that.

**This story seeds no Admin.** Two mechanisms creating privileged accounts is the duplication that ends with a production Admin nobody remembers configuring. The dependency runs one way only: demo seeding requires the bootstrap Admin, and story 06 must keep working with no demo data at all.

**Refuse rather than merge.** A non-empty ticket table means someone is using this database. Merging demo rows into it is the failure mode worth designing against, because unlike a duplicate it cannot be undone by re-running anything.

Create each user with `FindByEmailAsync` first, exactly as `SeedBootstrapAdminAsync` does. Agent goes in `RoleNames.Agent`, Customer in `RoleNames.Customer`. On failure, throw with the Identity errors — reuse `Describe`, which means promoting it out of `IdentitySeeder` or duplicating three lines. **Promote it**: two callers is the threshold, and the alternative is two copies of the one line that must never print a password.

### 3 — The tickets

All twelve built in one place, in a private static method returning a specification the seeder then executes. Each entry declares: title, description, category, priority, requester, target status, assignee, and age in days.

**Ages come from `TimeProvider`.** Resolve it from the scope and compute every instant as an offset:

```csharp
var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
var createdAt = now.AddDays(-12);
```

**Never a literal date.** A hardcoded 2026 date ages badly and drifts out of whatever window the list view or a future SLA cares about. Note `TimeProvider` is registered in `Program.cs` line 7 rather than in `AddPersistence`, so it resolves from the same container — but a host that calls `AddPersistence` without registering it will throw here. That is correct behaviour and worth knowing when reading the exception.

The set, chosen so the demo shows something:

| # | Requester | Target status | Priority | Assigned | Category | Age (days) |
|---|---|---|---|---|---|---|
| 1 | Customer | `New` | `Normal` | no | Hardware | 0 |
| 2 | Customer | `New` | `High` | no | *(null)* | 1 |
| 3 | Customer | `Open` | `Urgent` | Agent | Hardware | 2 |
| 4 | Customer | `Open` | `Normal` | Agent | Billing | 3 |
| 5 | Customer | `Open` | `Low` | no | Access | 4 |
| 6 | Customer | `Pending` | `Normal` | Agent | Billing | 5 |
| 7 | Customer | `Pending` | `High` | no | *(null)* | 6 |
| 8 | Customer | `Resolved` | `Normal` | Agent | Access | 8 |
| 9 | Customer | `Closed` | `Low` | no | Hardware | 10 |
| 10 | Agent | `Open` | `High` | Agent | Internal | 7 |
| 11 | Agent | `Resolved` | `Normal` | no | Internal | 9 |
| 12 | Agent | `Closed` | `Urgent` | no | Internal | 12 |

That set satisfies every clause of the acceptance criteria: all five statuses, all four priorities, **five assigned** (rows 3, 4, 6, 8, 10) and seven unassigned, two with a null category, and — the point of the whole story — **nine raised by the Customer and three by the Agent, so a Customer sees 9 where an Agent sees 12.** Twelve is under the page size of 25, so paging does not obscure the filtering.

Reaching each status through legal moves only:

- `New` — `Open` alone.
- `Open` — then `TransitionTo(Open)`.
- `Pending` — `Open`, then `Pending`.
- `Resolved` — `Open`, then `Resolved`.
- `Closed` — `Open`, then `Closed`. **Not `New → Closed`**, which is legal but reads as a withdrawn ticket rather than a completed one.

Each transition takes its own instant, later than the ticket's creation and earlier than `now`, so `UpdatedAt` differs from `CreatedAt` and the list has something to order by. The actor is the Agent for staff moves and the requester for their own — seeded rows should look like the workflow they represent.

`Assign` after reaching the target status, and **never on a `Closed` ticket** — `Ticket.Assign` throws `TicketClosedException` for a closed ticket, so rows 9 and 12 have no assignee by necessity as well as by design.

Save once at the end through `SaveChangesAsync`, after all twelve are added.

### 4 — Wiring into the existing seam

**File: `src/CrmTicketing.Infrastructure/DependencyInjection.cs`** — inside `SeedIdentityAsync`, after the existing `IdentitySeeder.SeedAsync` call:

```csharp
await DemoDataSeeder
    .SeedAsync(scope.ServiceProvider, cancellationToken)
    .ConfigureAwait(false);
```

Same scope, same await, same failure path.

**The full ordering inside `SeedIdentityAsync`, and every stage depends on the one before it:**

```
roles → bootstrap Admin → (only if the flag is on) demo users → demo tickets
```

Roles must exist before any user can be placed in one. The bootstrap Admin must exist before guard 3 can pass. Users must be committed before tickets, because the `requester_id` foreign key added in story 06 rejects a ticket whose requester does not exist. **Appending `DemoDataSeeder.SeedAsync` after `IdentitySeeder.SeedAsync` is what makes that ordering true** — do not reorder the two calls or run them concurrently.

The method keeps its name. `SeedIdentityAsync` is now slightly narrow for what it does, but renaming it would touch `Program.cs`, and the plan for story 06 was explicit that the composition root takes exactly three changes. **A second extension method is the wrong fix** — the API would then name two seeding concerns and the count would grow with every story that seeds anything.

**Failures are loud.** Any exception propagates out of `SeedIdentityAsync` and out of `Program.cs` before `app.Run()`, so a partial seed stops the API rather than leaving a half-populated database behind a clean start. Do not wrap any of this in a `try`/`catch` that logs and continues.

### 5 — No migration, no schema change

This story adds no column, no table, and no index. `git status --short src/CrmTicketing.Infrastructure/Persistence/Migrations/` returns nothing at the end of it. If EF regenerates the snapshot, something touched the model that should not have.

### 6 — Documentation

**File: `README.md`** — after `### Create the first account` (~line 146), add a `### Seed demo data (optional)` section: the four `dotnet user-secrets` commands from task 1, a sentence that it runs at startup and refuses if the ticket table is not empty, and the sign-in emails for the two demo users. The password is `<a real password>` in angle brackets, matching the convention the rest of the file uses.

Amend the curl block (~lines 204–212) with one line noting that seeding is the easier path to a populated database than creating tickets by hand.

**File: `docs/status.md`** — move demo data from `## What was not built, and why` (line 125) into `## What was built` (line 26), and state that a Customer sees 9 of the 12 seeded tickets while an Agent sees all 12, since that is the observable evidence row-level filtering works.

---

## Edge Cases & Failure Modes

- **The flag on with incomplete configuration.** Guard 2 throws naming the missing keys. A silent return here would be the same defect as guard 3's — the operator asked for a demo and got nothing, with no indication why.
- **The flag absent versus false.** Both seed nothing. Read it as `bool` and require exactly `true`; a string comparison against `"True"` breaks on casing, and treating "present" as "on" makes `Enabled=false` a trap.
- **Seeding an already-populated database.** **Guard 4** catches it — the `CountAsync` check, not the Admin check at guard 3. Without it the seeder adds twelve more tickets on every restart, and the twelfth restart leaves 144 — recoverable only by hand.
- **A partially seeded database from an earlier failure.** **Guard 4** sees a non-empty table and refuses, which is correct: the plan's answer to a bad seed is "drop the database and re-run", documented in task 6, not a repair path.
- **The flag on, but no bootstrap Admin configured.** Guard 3 throws and the API does not start. That is the designed outcome, not a defect — but the message must name `Identity:BootstrapAdmin:Email` and `:Password`, because the operator's mistake is in story 06's configuration and nothing about a demo-seeding failure would otherwise point there.
- **A bootstrap Admin that exists but is not in the Admin role.** `GetUsersInRoleAsync` returns empty and guard 3 throws, which is correct: the account cannot do an Admin's job. Do not soften this to "any user exists".
- **The demo users exist but the tickets do not.** Possible if a first run failed between the two. The users are found by `FindByEmailAsync` and skipped, the ticket table is empty, and the tickets seed normally. The two halves are independently idempotent for exactly this reason.
- **A weak seed password.** Identity rejects it, `CreateAsync` fails, and the seeder throws naming the reasons. **The password itself never enters the message** — that is what `Describe` is for.
- **Ordering: tickets before users.** The `requester_id` foreign key throws `DbUpdateException`, which nothing maps, so it surfaces as a startup crash. That is the right outcome, but the cause reads as a database error rather than an ordering mistake — which is why task 4 states the ordering rather than leaving it to inference.
- **`Assign` on a closed ticket.** `Ticket.Assign` throws `TicketClosedException` when `Status` is `Closed`. Rows 9 and 12 must not be assigned, and assignment must happen after the transitions rather than before, or a ticket assigned while `Open` and then closed keeps an assignee that the table above does not claim.
- **An illegal transition in the seed table.** `TransitionTo` throws `InvalidTicketTransitionException` and the API fails to start. This is a feature: the seeder walks the transition table on every seeding startup, which is a free smoke test of story 03's rules against a real database.
- **`TimeProvider` unregistered.** `GetRequiredService<TimeProvider>()` throws. It is registered in `Program.cs` line 7, not in `AddPersistence`, so a future host that calls `AddPersistence` alone loses it. Do not paper over this with `TimeProvider.System` as a fallback — the domain's whole discipline is that time comes from the caller.
- **Two tickets sharing an instant.** Every age in the table is distinct, so `OrderByDescending(CreatedAt)` is stable without relying on the `ThenBy(Id)` tiebreaker. Do not collapse two rows onto the same age to save a line.
- **Seeded content that reads as real.** Titles must be obviously synthetic — "Printer offline in Meeting Room 3", never a plausible person with a plausible grievance. Demo data that reads as real gets screenshotted and mistaken for real.
- **Uncertainty to surface — the emptiness check counts through `TicketAccess.All()`.** That is the correct reading for a seeder, which is not a user. But it means the check bypasses the row-level rule deliberately, and a reader who has just finished story 06 may see `TicketAccess.All()` and think it a leak. Comment it at the call site.

---

## Test Plan

### 7 — What is testable without a database

The seeder resolves `UserManager`, `RoleManager`, `ITicketRepository`, and `TimeProvider` from a scope and writes through EF. **None of that is unit-testable without either a database or a mock of Identity's manager types**, and story 06 established that this project does neither. What *is* testable is the part with branching logic and no I/O.

**Create file: `tests/CrmTicketing.Infrastructure.Tests/Identity/DemoTicketPlanTests.cs`**

Extract the twelve-row table from task 3 into an `internal static` method returning the specification — a record of `(Title, Description, Category, Priority, RequesterKind, TargetStatus, AssignToAgent, AgeInDays)` — separate from the code that executes it. That method is pure, and these tests exercise it:

1. `Specification_HasTwelveTickets`.
2. `Specification_CoversEveryStatus` — the distinct target statuses equal `Enum.GetValues<TicketStatus>()`, so a status added later fails this until the seed covers it.
3. `Specification_CoversEveryPriority` — likewise for `TicketPriority`.
4. `Specification_HasBothAssignedAndUnassigned`, and **no closed ticket is assigned** — the rule `Ticket.Assign` would otherwise enforce by throwing at startup.
5. `Specification_HasAtLeastOneNullCategory`.
6. `Specification_AgesAreDistinctAndWithinAFortnight` — every age unique, all between 0 and 14.
7. `Specification_SplitsRequestersSoFilteringIsVisible` — both requester kinds present, and the Customer's share is strictly less than the total. **This is the test that protects the story's purpose:** a seed where every ticket has the same requester would satisfy every other assertion here and still make the demo useless.
8. `Specification_ReachesEveryStatusThroughLegalTransitions` — for each row, walk `New` to the target through the path task 3 specifies and assert `TicketStatusTransitions.IsAllowed` at every step. This catches a seed row that would throw at startup, without starting anything.

### 8 — What is not tested

9. **No test executes the seeder.** The guards, the Identity calls, and the repository writes are covered by the manual step in Verification, not by the suite. Issue #29 owns the missing integration host; do not add an in-memory provider to fake one, and do not claim the manual run as automated coverage.

### 9 — Regression

10. All four test projects pass unchanged. This story touches no existing production file except `DependencyInjection.cs`, and adds no behaviour to any path that runs when the flag is absent — which is every existing test.

---

## Verification Steps

1. **Backend builds:** `dotnet build CrmTicketing.slnx` — zero warnings, zero errors.
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — four test projects, no database.
3. **No password in the repository:** `git grep -inE "Seed:Demo:Password *= *\"|seedpassword" -- ':!*.md'` returns no literal.
4. **No raw SQL against tickets:** `grep -rn "INSERT INTO ticket" --include='*.cs' --exclude-dir=bin --exclude-dir=obj src/` returns nothing.
5. **No object-initialiser tickets:**

    ```bash
    grep -rnE "new Ticket[[:space:]]*\(" --include='*.cs' --exclude-dir=bin --exclude-dir=obj src/
    ```

    Returns hits only inside `Ticket.cs`. **The bare string `"new Ticket"` does not work** — it is a prefix of `new TicketQuery(`, `new TicketTitle(`, `new TicketAccess(`, and every `Ticket*Response` record, all of which are legitimate. Requiring the opening parenthesis immediately after `Ticket` is what distinguishes constructing the aggregate from constructing something whose name starts with it.
6. **No `Seed` section shipped:** `grep -c "Seed" src/CrmTicketing.Api/appsettings.json` returns `0`.
7. **The composition root is unchanged:** `git status --short src/CrmTicketing.Api/Program.cs` returns nothing.
8. **No migration:** `git status --short src/CrmTicketing.Infrastructure/Persistence/Migrations/` returns nothing.
9. **Off by default, with PostgreSQL running:** with no `Seed:Demo:*` configuration, start the API and confirm the ticket count is unchanged.
10. **Enabled with no bootstrap Admin:** drop and recreate the database, apply migrations, set the four `Seed:Demo:*` keys but no `Identity:BootstrapAdmin:*` keys, and start the API. Confirm it fails at startup with a message naming both bootstrap keys, and that the ticket table is still empty.
11. **On, from empty:** drop and recreate the database, apply migrations, set the four keys, start the API. Confirm 12 tickets, 3 users, and that signing in as the Customer lists **9** while the Agent lists **12**.
12. **Idempotent:** restart the API. Confirm still 12 tickets and 3 users, and a log line saying seeding was skipped.

---

## Done Criteria

- [ ] Seeding runs only when `Seed:Demo:Enabled` is exactly `true`; absent configuration seeds nothing and logs nothing alarming.
- [ ] A non-empty ticket table is left entirely untouched, with a log line explaining why.
- [ ] One Agent and one Customer are seeded with fixed, obviously-fake `Guid`s, from configured emails and password. **No Admin is seeded by this story.**
- [ ] With the flag on and no user in the Admin role, startup throws naming `Identity:BootstrapAdmin:Email` and `:Password`, and nothing is written.
- [ ] No password literal exists anywhere in the repository, and no failure message contains one.
- [ ] Twelve tickets exist, built by `Ticket.Open` and moved by `TransitionTo`; no raw SQL and no object-initialiser `Ticket`.
- [ ] Every status and every priority appears; at least one assigned, one unassigned, one null category; no closed ticket is assigned.
- [ ] Ages are distinct, spread across a fortnight, and derived from `TimeProvider`.
- [ ] A Customer sees 9 tickets where an Agent sees 12.
- [ ] Users are committed before tickets; a seeding failure stops startup rather than leaving a half-populated database.
- [ ] `Program.cs` and `appsettings.json` are unchanged; no migration is added.
- [ ] `README.md` and `docs/status.md` document how to switch seeding on.
- [ ] `dotnet build` clean; `dotnet test` passes with no database.
- [ ] Overview `00-overview.md` created and `00-index.md` updated with the new feature.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 08 (issue #16, permission-gated UI).**
