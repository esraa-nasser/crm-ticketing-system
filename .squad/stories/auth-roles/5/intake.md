# Story intake

- Folder: `.squad/stories/auth-roles/5/intake.md`

---

## Feature

- **Feature name (display):** Auth and roles — identity, sign-in, and endpoint authorisation
- **Feature slug (folder under `plans/`):** `auth-roles`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `5` *(this story also closes #6)*
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:api`, `area:infrastructure`, `sdd:needs-plan`

---

## Title

```
Identity, roles, sign-in, and endpoint authorisation
```

---

## Description

```
Give the system a notion of who is acting. Today every endpoint is open and every
mutation is anonymous: Ticket records CreatedAt and UpdatedAt but no actor, so
nothing knows who opened a ticket, who transitioned it, or who assigned it. That
gap grows with every ticket created, and it cannot be backfilled honestly.

This story introduces ASP.NET Core Identity over the existing PostgreSQL database,
three roles, token-based sign-in for the Blazor WebAssembly client, and role-based
authorisation on the ticket endpoints. It also threads an acting user through the
domain mutators so the audit hole closes at the same moment it stops growing.

Customers authenticate. That decision is what makes GET /api/tickets a security
boundary rather than a convenience: a Customer must see only their own tickets, and
that constraint belongs in the repository, not in a Razor page. This story enforces
it at the data-access layer so no future screen can forget it.

Closes #5 (identity and roles) and #6 (sign-in). Permission-gated UI is #16 and
follows this story, not the reverse - it gates what already exists rather than
inventing the model.
```

---

## Acceptance criteria

```
- [ ] ASP.NET Core Identity is configured over the existing CrmDbContext database
      with Guid keys. Identity types live in CrmTicketing.Infrastructure only.
      CrmTicketing.Domain still declares zero package references.
      Verify: grep -cE "(Project|Package)Reference"
              src/CrmTicketing.Domain/CrmTicketing.Domain.csproj -> 0
      Verify: grep -rn "Identity" src/CrmTicketing.Domain/ -> no output
- [ ] Three roles exist and are seeded idempotently on startup: Admin, Agent,
      Customer. Re-running startup does not duplicate them.
- [ ] Sign-in issues a bearer token the Blazor WASM client can carry. Sign-in with
      a bad password returns 401 and does not disclose whether the account exists.
- [ ] No self-registration. Accounts are created by an Admin. If the chosen
      Identity endpoint set exposes a public register route, it is explicitly
      disabled and a test asserts it returns 404 or 405.
- [ ] Ticket endpoints require an authenticated caller. An anonymous request
      returns 401, not 200 and not 500.
      Verify: every action on TicketsController is covered by [Authorize] at the
      controller or action level.
- [ ] Role rules enforced and tested:
        Admin     - full access
        Agent     - read all tickets, create, update, transition, assign
        Customer  - create; read and update ONLY tickets they raised; may not
                    assign, and may not transition beyond the moves a requester
                    is allowed
- [ ] Row-level filtering lives in TicketRepository, not in a controller and not
      in the client. A Customer calling GET /api/tickets receives only tickets
      whose RequesterId is their own user id, and GET /api/tickets/{id} for
      someone else's ticket returns 404 - not 403, which would confirm existence.
      Verify: a test calls the repository directly as a Customer and asserts
      another user's ticket is absent.
- [ ] Every domain mutator that changes a Ticket records the acting user. Ticket
      exposes CreatedBy and UpdatedBy, both non-empty Guids, set from the
      authenticated principal at the API boundary and passed into the domain.
      The domain still reads no clock and no HttpContext.
- [ ] A migration adds the Identity tables, the two Ticket columns, and a foreign
      key from ticket.requester_id to the user table. Existing rows are handled
      explicitly - state in the plan what happens to the two tickets already in
      the database, rather than letting the migration fail on a null column.
- [ ] The JWT signing key, and any other secret, is read from configuration and is
      absent from the repository. dotnet user-secrets locally; the CI workflow
      supplies a throwaway value. Constitution section VI.
      Verify: git grep -i "signingkey\|jwt.*secret" -- ':!*.md' -> no literal key
- [ ] Existing tests still pass. Where a test now needs an authenticated caller,
      it uses a test authentication handler - not a real password hash, which is
      deliberately slow and would show up in test duration.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no database running.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #3, #8, #9, #10 (all merged). Closes #5 and #6. Unblocks #16 (permission-gated UI). **Changes** #10's `GET /api/tickets` contract and story 05's repository call.
- **Depends on code areas or other stories:** `CrmDbContext`, `TicketRepository`, `TicketsController`, `Ticket` mutators, `Program.cs`, `AddPersistence`.

## Extra notes

- **This story reopens a merged contract.** `GET /api/tickets` and `GET /api/tickets/{id}` change behaviour for Customer callers. Story 04's tests assert the unfiltered behaviour and will need updating - that is expected, not a regression, and the plan must say so explicitly so the executor does not treat a failing story-04 test as a blocker.
- **Story 05's page is unaffected.** Its plan forbids the client from filtering, so row-level rules landing in the repository require no UI change. Confirm this when story 05 merges; if the page turned out to filter anything itself, that is a defect in story 05, not a change for this one.
- `docs/architecture.md` gains an entry under "Decisions taken by the scaffold" recording where Identity sits in the layer graph.

## Technical hints

```
DECISIONS MADE

Provider: ASP.NET Core Identity with EF Core over the existing PostgreSQL
database. Not Entra ID. Customers authenticate, and external customers have no
tenant accounts, so organisational SSO cannot serve the whole audience. Identity
also keeps the whole system testable offline, which every story so far has relied
on. If Entra is wanted for staff later it is added as a second authentication
scheme; authorisation rules are written against roles and claims, not against how
the principal was authenticated, so that addition should not reopen this work.

Keys: Guid, matching Ticket.RequesterId and Ticket.AssigneeId, which are already
Guids pointing at nothing. This story is what gives them something to point at.

The domain stays pure. Identity types are framework types and cannot enter
CrmTicketing.Domain (constitution section II). The domain keeps opaque Guids;
Infrastructure owns ApplicationUser and configures the foreign key in
TicketConfiguration. Ticket must not gain a navigation property to a user type.

Roles, exactly three:
  Admin     manages users, full ticket access
  Agent     works the queue: read all, create, update, transition, assign
  Customer  raises and follows their own tickets only

Roles are seeded, not database-editable. A role name appears in an authorisation
policy in code, so adding one is a code change, not a data change - the same
reasoning that made TicketPriority a fixed enum in story 03.

Row-level filtering belongs in TicketRepository. TicketQuery gains a constraint
carrying the caller's identity and role, applied inside ListAsync and CountAsync.
A controller-level filter would be bypassed by the next caller of the repository;
a client-side filter would be a disclosure bug. Constitution section VII permits
this because the consumer exists now.

Unauthorised reads return 404, not 403. Telling a Customer that ticket X exists
but is not theirs leaks the existence of other customers' tickets. 404 is the
correct answer to "a ticket you may see with this id does not exist".

Actor threading: mutators take Guid actorId alongside the DateTimeOffset they
already take. The API resolves it from the authenticated principal. This changes
every mutator signature and every existing domain test - that cost is the reason
to do it now, at two tickets, rather than later.

No self-registration in this story. Accounts are created by an Admin. A public
register endpoint on a system where a Customer role can read tickets is an
account-creation hole with no approval step behind it.

No refresh tokens in this story. A short-lived bearer token and re-authentication
on expiry is sufficient for a first pass; refresh-token rotation carries its own
revocation and storage design and deserves its own story rather than a hurried
implementation inside this one.
```

## Out of scope

```
- No permission-gated UI. Hiding buttons a role may not use is issue #16, and it
  is cosmetic until the endpoints refuse the call - which is what this story does.
- No customer portal UI, no separate shell, no self-service registration screen.
- No password reset, email confirmation, or account lockout tuning beyond
  Identity's defaults. Each needs an email transport decision that does not exist.
- No refresh tokens, no token revocation list, no "sign out everywhere".
- No external identity provider, no social sign-in, no Entra ID.
- No two-factor authentication.
- No user management screens. Admin account creation is an API call in this story.
- No Contact or Account aggregate. A Customer user is an identity; the CRM-side
  customer record is the customers-crm feature and is not this.
- No per-field permissions, no ticket sharing, no delegation, no teams or queues.
- No audit log or activity timeline. CreatedBy and UpdatedBy are two columns, not
  a history table - the timeline is issue #11.
- No change to the transition table. Which moves a Customer may make is an
  authorisation rule evaluated at the API boundary, not a second transition map.
```
