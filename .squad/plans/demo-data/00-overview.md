# demo-data — plan overview

Entry point for the **demo-data** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 07 | [07-story-demo-data-4.md](07-story-demo-data-4.md) | Seed a demonstrable database from an empty one | 4 | Story 06 (auth-roles), Story 03 (ticket aggregate) |

## Dependency notes

- **Closes the second half of issue #4.** The migration tooling half — the `Microsoft.EntityFrameworkCore.Design` package and `InitialCreate` — landed ahead of the persistence stories. This is the seed-data half, and it could not have been written earlier: seeded tickets need real users because of story 06's `requester_id` foreign key, and seeded users need Identity.
- **Extends story 06's seam, does not add a second one.** `DemoDataSeeder` is invoked from inside `SeedIdentityAsync`, so `Program.cs` keeps its single call and still names no Identity type. A second extension method would make the composition root's seeding surface grow with every story that seeds anything.
- **Through the domain, always.** Every seeded ticket comes from `Ticket.Open` and `TransitionTo`. That costs more code than an `INSERT` and buys the guarantee that no seeded row can exist in a state the aggregate forbids — the first person to trust a demo built on raw SQL would be trusting a lie. It also walks the transition table on every seeding startup, which is a free smoke test of story 03's rules against a real database.
- **One Admin path, and it is not this one.** Story 06's bootstrap Admin is the single way a privileged account comes into existence; this story seeds an Agent and a Customer and *requires* that Admin to already exist, throwing at startup when it does not. Two mechanisms creating privileged accounts is the duplication that ends with a production Admin nobody remembers configuring. The dependency runs one way: demo seeding needs story 06, and story 06 must keep working with no demo data at all.
- **Off unless switched on, and not keyed off `IsDevelopment()`.** A shared development environment is still someone's environment. A flag is an explicit decision visible in configuration; an environment name is an implicit one buried in a launch profile.
- **Refuses rather than merges.** A non-empty ticket table means someone is using this database. Merging demo rows into it is the failure mode worth designing against, because unlike a duplicate row it cannot be undone by re-running anything.
- **The split of requesters is the point.** Nine tickets raised by the Customer and three by the Agent, so a Customer sees 9 where an Agent sees 12. A seed where every ticket shares one requester would satisfy every other requirement and still leave story 06's row-level filtering invisible. Test 7 exists to protect exactly that.
- **No migration, no schema change.** If EF regenerates the model snapshot during this story, something touched the model that should not have.
- **The suite gains no coverage of the seeder itself.** The guards and the Identity calls need a database; what the tests cover is the twelve-row specification, extracted as a pure function. Issue #29 still owns the missing integration host, and the manual verification steps are not a substitute for it.

## Deferred

| Issue | What it adds | Why not story 07 |
|---|---|---|
| #11 | Seeded comments and activity entries | The aggregate does not exist yet |
| #21 | SLA fields and due dates on seeded tickets | Nothing to seed them into |
| #29 | A CI database, and integration tests over the seeder | The suite runs with no database by design; giving CI one is that issue's decision |

## Unblocks

| Issue | Why it was blocked |
|---|---|
| #16 — permission-gated UI | Needs a populated database to show a role actually seeing less |
| #12–#14 — ticket UI work | Needs more than one hand-made ticket to look like anything |
