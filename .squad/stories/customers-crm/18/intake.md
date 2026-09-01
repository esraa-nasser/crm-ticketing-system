# Story intake

- Folder: `.squad/stories/customers-crm/18/intake.md`

---

## Feature

- **Feature name (display):** Customers — accounts and contacts
- **Feature slug (folder under `plans/`):** `customers-crm`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `18`
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:domain`, `area:infrastructure`, `sdd:needs-plan`

---

## Title

```
Model Account and Contact, and map them
```

---

## Description

```
Put the CRM into the CRM ticketing system. The product has been called one since
the first commit and has never had a customer in it: RequesterId points at a
login, and there is no company, no person record, and no way to ask "show me
everything for this client" - the question a CRM exists to answer.

This story models the two aggregates and stops at the database boundary, exactly
as story 03 did for Ticket. An Account is a company. A Contact is a person at
one. Nothing above the domain and infrastructure layers is touched: no
endpoints, no contracts, no UI, and - deliberately - no link from Ticket.

Linking tickets to contacts is the next story, and it is the one that matters:
today's row-level filter keys on the login that raised a ticket, and once an
agent can raise one on someone's behalf that key is wrong. This story
deliberately changes nothing about tickets so that change lands on its own,
against an aggregate that already exists and is already tested.
```

---

## Acceptance criteria

```
- [ ] Account and Contact live in src/CrmTicketing.Domain/Customers/, derive from
      Entity, and expose no public setter that bypasses an invariant.
- [ ] CrmTicketing.Domain still declares zero package references and names no
      Identity type.
      Verify: grep -cE "(Project|Package)Reference"
              src/CrmTicketing.Domain/CrmTicketing.Domain.csproj -> 0
      Verify: grep -rnE "Microsoft\.AspNetCore\.Identity|ApplicationUser"
              --include='*.cs' --exclude-dir=bin --exclude-dir=obj
              src/CrmTicketing.Domain/ -> no output
- [ ] Account construction rejects, with ArgumentException naming the parameter:
      a null/empty/whitespace name, and a name longer than 200 characters after
      trimming.
- [ ] Contact construction rejects: a null/empty/whitespace display name, a name
      longer than 200 characters, an empty AccountId, and an email that is
      null/empty or longer than 256 characters after trimming.
- [ ] A Contact belongs to exactly one Account, by id. There is no navigation
      property in either direction and no collection of contacts on Account -
      the same rule story 09 applied to comments, for the same reason.
- [ ] A Contact may optionally carry a UserId: the login that person signs in
      with, or none. Most contacts never sign in. It is an opaque Guid, exactly
      as RequesterId is, so the domain still knows nothing about Identity.
- [ ] Contact email is unique across the whole system, enforced by a unique index
      and normalised to lower case before storage. A duplicate insert fails at
      the database rather than producing two records for one person.
- [ ] Both aggregates record CreatedAt, CreatedBy, UpdatedAt, and UpdatedBy, with
      the instant and the actor supplied by the caller. The domain reads no clock.
      Verify: grep -rnE "DateTime\.UtcNow|DateTime\.Now" --include='*.cs'
              --exclude-dir=bin --exclude-dir=obj src/ -> no output
- [ ] An Account can be deactivated and reactivated rather than deleted. A
      deactivated Account keeps its contacts and its history.
- [ ] Configurations live under Persistence/Configurations/, are found by
      ApplyConfigurationsFromAssembly, and produce snake_case names: tables
      "account" and "contact", columns including "account_id" and "user_id".
- [ ] A migration named AddAccountAndContact creates both tables, the foreign key
      from contact to account, the unique index on contact email, and an index on
      contact.account_id. It is additive: no DELETE, no DropColumn, no
      AlterColumn in Up().
- [ ] The ticket table is untouched. No column added, no foreign key, no data
      migrated.
      Verify: the migration names neither "ticket" nor "ticket_comment".
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no API and no database.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** none — this story stands alone. Closes #18. Unblocks the accounts and contacts HTTP surface, the ticket-to-contact link (which closes #43), and the customer 360 view. SLA policies (#21) should wait for the link story, since a policy usually keys on an account tier. #43 (an agent raising a ticket on a customer's behalf) closes in the link story, not here.
- **Depends on code areas or other stories:** `CrmTicketing.Domain.Common.Entity`, `CrmDbContext`, `SnakeCaseNaming`, the `IEntityTypeConfiguration` convention from story 02.

## Extra notes

- **Deliberately no repository.** No caller exists yet; the HTTP surface story adds one, shaped by a real consumer. Constitution §VII — the same reason story 02 shipped `CrmDbContext` with no repository and story 04 added `ITicketRepository` when an endpoint needed it.
- **Deliberately no demo data.** The seeder gains accounts and contacts in the link story, where a seeded ticket can point at a seeded contact. Seeding them here would produce customers no ticket refers to, which demonstrates nothing.
- The `Customers` folder is a new top-level area in the domain beside `Tickets` and `Common`. Say so in `docs/architecture.md`.

## Technical hints

```
DECISIONS MADE

Account is a company; Contact is a person at exactly one company. This is the
standard business-to-business support shape, and it is what makes "show me
everything for this client" answerable and what a per-account SLA tier can hang
from. A person who genuinely works for two companies gets two contact records -
rare enough that a many-to-many relationship is not worth its cost, and the
constraint is recorded here rather than discovered.

Account fields, kept minimal: Name (required, trimmed, 1-200), and IsActive.
No address, no industry, no size, no owner, no billing details. Every one of
those is a real CRM field and none has a consumer yet; they arrive when a screen
or a report asks for them.

Contact fields: DisplayName (required, 1-200), Email (required, unique,
lower-cased), AccountId (required), UserId (optional), IsActive.
No phone, no job title, no preferred contact method - same reasoning.

Email unique across the system, not per account. Two records with one email is
how a person ends up with a split history, and the ambiguity is worse than the
rare case of the same address at two companies. Lower-case before storing so
uniqueness is not defeated by capitalisation, and record that the normalisation
happens in the domain, not in the database.

UserId is an opaque Guid, exactly like RequesterId. A contact may have a login or
may not - most customers of most support desks never sign in. The domain must not
reference ApplicationUser; the foreign key, if any, is declared in the
configuration in Infrastructure, the way story 06 declared requester_id's.

Deactivate, never delete. A deleted account orphans its tickets and its history,
and "what happened with this client" stops being answerable. IsActive on both
aggregates; nothing in this story acts on it beyond storing it.

No navigation properties, in either direction. Account holds no collection of
contacts and Contact holds no Account reference - only an AccountId. Loading an
account should never drag its contacts, and story 09 made exactly this call for
comments. The join is a query, not a graph.
```

## Out of scope

```
- No endpoints, no contracts, no UI. That is the next story.
- No link from Ticket to Contact or Account, no column on ticket, no change to
  row-level filtering. That is the story after, and it is the one that changes
  what existing data means.
- No repository or query abstraction - no consumer exists yet (§VII).
- No demo seeding.
- No merge, no deduplication, no import.
- No account hierarchy: no parent companies, no subsidiaries, no groups.
- No custom fields, no tags, no notes on either aggregate.
- No SLA tier on Account (issue #21) - it belongs with the policy that reads it.
- No search, no filtering, no paging: those are query concerns and arrive with
  the endpoints that need them.
- No change to Identity, to ApplicationUser, or to how anyone signs in. A contact
  is a customer record; a user is a login; this story does not make one create
  the other.
```
