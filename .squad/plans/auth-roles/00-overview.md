# auth-roles — plan overview

Entry point for the **auth-roles** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 06 | [06-story-identity-and-authorisation-5.md](06-story-identity-and-authorisation-5.md) | Identity, roles, sign-in, and endpoint authorisation | 5 (closes 6) | Story 04 (endpoints), Story 05 (list view) |

## Dependency notes

- **The largest story so far, and the only one that reopens merged contracts.** It changes every `Ticket` mutator signature (44 call sites), changes what `GET /api/tickets` returns for a Customer, and ships a destructive migration. The plan carries a `## Sequencing` section because the middle group of tasks leaves the solution uncompilable until it is finished.
- **Story 04's controller tests will change, and that is expected.** They assert unfiltered reads and anonymous access. A failing story-04 test during this work is part of the job; a failing story-03 *domain* test is a real defect, because the transition table must not move.
- **Authorisation is not the transition table.** Which status moves are legal is `TicketStatusTransitions`, unchanged and still the single declaration. Which moves a *Customer* may make is an authorisation rule at the API boundary. Conflating them would give the system two workflow definitions that drift.
- **Row-level filtering lives in the repository, not a controller.** A controller check is bypassed by the next caller of `ITicketRepository`; a client-side filter is a disclosure bug. Constitution §VII permits the abstraction because the consumer exists now.
- **404, never 403, for a ticket you may not see.** Telling a Customer that ticket X exists but is not theirs discloses other customers' tickets to anyone who can guess a Guid. The repository returning null makes this the automatic answer rather than a rule someone must remember.
- **The audit hole closes at two rows.** `Ticket` records timestamps but no actor, so nothing knows who opened or transitioned a ticket. That gap cannot be backfilled honestly and grows with every ticket — which is the whole argument for paying the 44-call-site cost now.
- **The migration deletes data.** Both existing tickets carry a `requester_id` with no user behind it, so the foreign key cannot be added while they exist. They are hand-made throwaway rows from exercising the API on 29–30 August 2026, and the plan deletes them explicitly rather than inventing a fictitious user to satisfy the constraint.
- **This story edits `ci.yml`.** Story 05's plan asserted that file stays untouched; that assertion does not carry over. CI must supply a throwaway signing key or every API test fails at startup on the fail-closed guard.
- **The client is in scope, minimally.** Endpoints requiring auth plus a client sending no token is a 401 on the screen story 05 just shipped. Sign-in and a bearer-token handler are included so this story does not ship a regression. Hiding buttons by role stays issue #16.

## Deferred to later stories

| Issue | What it adds | Why not story 06 |
|---|---|---|
| #16 — permission-gated UI | Hiding actions a role cannot use | Cosmetic until the endpoints refuse the call, which is what this story does |
| — | Refresh tokens, revocation, sign-out-everywhere | Rotation carries its own revocation and storage design |
| — | Password reset, email confirmation | Each needs an email transport decision that does not exist |
| — | External identity, Entra ID, 2FA | Rules are written against roles and claims, so a second scheme can be added without reopening this |

## Unblocks

| Issue | Why it was blocked |
|---|---|
| #16 — permission-gated UI | Needs roles, a principal, and endpoints that already refuse |
| #11 — comments and activity timeline | Needs an actor to attribute an entry to |
