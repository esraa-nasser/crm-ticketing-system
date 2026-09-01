# Story intake

- Folder: `.squad/stories/ticketing-ui/13/intake.md`

---

## Feature

- **Feature name (display):** Ticketing UI — detail view and write actions
- **Feature slug (folder under `plans/`):** `ticketing-ui`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `13`
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:client`, `sdd:needs-plan`

---

## Title

```
Ticket detail view, and the first writes from the UI
```

---

## Description

```
Make the product usable without curl. Every write endpoint has existed and been
verified since story 04, and every one of them is still reachable only from a
terminal. This story gives them a screen.

Three things: a detail view at /tickets/{id} showing everything the list
deliberately omits, a create form, and the write actions - edit, transition,
assign - driven from that detail view.

It is the first story where the UI causes state to change, which makes it the
first place a user can be misled about whether something happened. A failed
write that looks like a success is worse than a write that plainly refuses.

Story 06 is what makes this possible now rather than earlier: writes need an
actor, and there finally is one. The acting user comes from the token, never
from a form field.
```

---

## Acceptance criteria

```
- [ ] A route at /tickets/{id} renders the full ticket, including the description
      the list omits, and every field TicketResponse carries.
- [ ] A row in the list links to its detail view, and the detail view links back
      to the list preserving the filters and page the user came from. Story 05
      put that state in the query string precisely so this is possible.
- [ ] A route at /tickets/new creates a ticket. The requester is always the
      signed-in user - there is no requester field on the form.
- [ ] Status transitions are rendered from the transition map returned by
      GET /api/tickets/metadata, for the ticket's current status only. No status
      name and no transition rule is written as a literal in the Client.
      Verify: grep for status names in src/CrmTicketing.Client/ -> no output
- [ ] A terminal ticket offers no transitions at all, because the map returns an
      empty list for Closed - not because the UI special-cases it.
- [ ] Editing title, description, category, and priority through PATCH. The form
      is populated from the current values and submits only what the endpoint
      accepts.
- [ ] Assignment is self-service only: an "Assign to me" action and an "Unassign"
      action. There is no user picker, because no endpoint lists users.
- [ ] Every write refreshes from the server rather than patching local state. A
      screen that shows what the client hoped happened is how a silently failed
      write becomes a lie.
- [ ] Failures are visible and specific. A 400 shows the validation message from
      the problem details, a 409 shows what the conflict was, a 403 says the
      action is not permitted for this role, and a 404 says the ticket is gone.
      None of them shows a stack trace or a traceId.
- [ ] A 401 on any call sends the user to /signin rather than rendering an error.
- [ ] Component tests cover: the detail view renders a stubbed ticket; only the
      legal transitions for the current status appear; a Closed ticket offers
      none; a successful transition triggers a re-fetch; a 409 renders the
      conflict message and leaves the displayed status unchanged; the create form
      posts without a requester field; and "Assign to me" sends the signed-in
      user's id.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no API and no database.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #10 (endpoints), #12 (list view), #5 and #6 (identity) — all merged. Closes #13. Unblocks #16 (permission-gated UI) and #11 (comments, which need a detail view to live on).
- **Depends on code areas or other stories:** `TicketsApiClient`, `TicketMetadataProvider`, `TokenStore`, `ApiProblem`, `Tickets.razor`, and the `PATCH`, `status`, and `assignee` endpoints from story 04.

## Extra notes

- **This story does not hide controls by role.** That is #16. A Customer will see an "Assign to me" button that the API refuses with 403, and this story's job is to make that refusal legible rather than to prevent the click. Hiding it first would mean building the gating before there is anything to gate.
- The typed-client convention from story 05 holds: no component injects `HttpClient`, and every call goes through `ITicketsApiClient`.

## Technical hints

```
DECISIONS MADE

No user picker, anywhere. Assignment is "Assign to me" and "Unassign"; creation
always sets the requester to the signed-in user. Both would need a list of users,
and no endpoint returns one - story 06 added POST /api/auth/users but no GET.
Adding one is an API change with its own authorisation question (who may
enumerate users?) and belongs in its own story. An Agent claiming a ticket is the
common case and needs no picker.

Re-fetch after every write, never patch local state. The endpoints already return
the updated TicketResponse, but re-fetching also catches a concurrent change by
someone else. Optimistic updates would need conflict handling this story has no
mechanism for.

Transitions come from the metadata map, filtered to the current status. This is
what the metadata endpoint was built for in story 04 and what the client has been
fetching since story 05 without using. A hardcoded list of buttons would be a
second transition table, which the constitution's single-source rule forbids.

Create is a separate route, /tickets/new, not a modal. It is linkable, it has its
own validation states, and a modal over a list that may be filtered is a
navigation trap.

Errors reuse the existing seam. TicketsApiClient already parses ApiProblem and
prefers the validation message over the generic title; ApiRequestException
already carries StatusCode. The page branches on that status code - it does not
re-parse messages or match on strings.

Last write wins, and say so. No ETag, no version column, no optimistic
concurrency. Two agents editing the same ticket will overwrite each other
silently. That is acceptable for now and it must be written down rather than
discovered - a concurrency story needs a version field on the aggregate, which is
a domain and migration change.

The acting user comes from the token. No form field, no query parameter, no
client-supplied actor anywhere. Story 06 forces the requester for a Customer at
the API boundary; the client must not send one at all.
```

## Out of scope

```
- No permission-gated UI. Controls are visible to everyone and the API refuses
  what it should - that is issue #16 and it follows this story.
- No user picker, no user list endpoint, no assigning to anyone but yourself, and
  no raising a ticket on someone else's behalf.
- No comments and no activity timeline (issue #11). The detail view leaves room
  for them; it does not build them.
- No optimistic concurrency, no ETags, no version field. Last write wins.
- No delete. There is no delete endpoint and whether tickets are ever deleted is
  an unanswered product question.
- No file attachments, no rich text, no markdown in descriptions.
- No SLA or due-date display (issue #21).
- No bulk actions, no keyboard shortcuts, no undo.
- No kanban board (issue #14) - though this story's transition handling is what
  it will reuse.
- No design system or component library beyond the Bootstrap already present.
- No change to any endpoint. If the UI wants something the API does not offer,
  that is a finding to report, not a contract to reopen.
```
