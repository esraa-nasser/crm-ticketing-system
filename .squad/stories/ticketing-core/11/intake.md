# Story intake

- Folder: `.squad/stories/ticketing-core/11/intake.md`

---

## Feature

- **Feature name (display):** Ticketing core — comments
- **Feature slug (folder under `plans/`):** `ticketing-core`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `11`
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:domain`, `area:api`, `area:client`, `sdd:needs-plan`

---

## Title

```
Comments on a ticket, authored and visibility-scoped
```

---

## Description

```
Give a ticket a conversation. Today a ticket can be raised, worked, and closed
without anyone writing a word on it - which makes this a workflow tracker rather
than a support system. The core loop of every support desk is that a requester
writes, an agent replies, the requester replies, and someone resolves it.

This is the first feature that uses the actor rather than merely recording it.
Story 06 gave the system a notion of who is acting; a comment is the first thing
whose entire meaning is who wrote it.

Two decisions shape the story and are settled below rather than left to the
implementation: comments are their own aggregate, and every comment carries a
visibility flag from the first migration.

The activity timeline - status changed, assigned, priority raised - is NOT in
this story. It needs a stored event history, which is a different design from a
comment thread. Split it into its own issue rather than shipping half of each.
```

---

## Acceptance criteria

```
- [ ] TicketComment is its own aggregate under CrmTicketing.Domain/Tickets/,
      referencing a ticket by id. It is not an owned collection on Ticket, and
      reading a ticket never loads its comments.
      Verify: grep -rn "ICollection<TicketComment>\|List<TicketComment>"
              src/CrmTicketing.Domain/Tickets/Ticket.cs -> no output
- [ ] A comment requires a non-empty author, a non-empty ticket id, and a body of
      1-5000 characters after trimming. Construction rejects each with
      ArgumentException naming the parameter.
- [ ] Comments cannot be added to a Closed ticket. The rule lives in the domain -
      a method on Ticket that the API calls - not as an if-statement in a
      controller. Attempting it returns 409, not 400 or 403: the ticket's state
      forbids it, not the caller's role.
- [ ] Every comment carries IsInternal from the first migration. An internal
      comment is staff-only; a public one is visible to the requester too.
- [ ] A Customer never receives an internal comment, and the filtering lives in
      the repository alongside the row-level ticket rule - not in a controller and
      not in the client.
      Verify: a test calls the repository directly as a Customer and asserts an
      internal comment is absent from both the page and the count.
- [ ] A Customer may not create an internal comment. Sending IsInternal true as a
      Customer returns 403, and the stored comment is never internal by accident.
- [ ] Endpoints:
        POST /api/tickets/{id}/comments   201 | 400 | 403 | 404 | 409
        GET  /api/tickets/{id}/comments   200, paged, newest first
      Both refuse an unauthenticated caller, and both return 404 for a ticket the
      caller may not see - never 403, matching story 06.
- [ ] Contracts are sealed records in Shared/Contracts/Tickets/. Comments carry
      AuthorId as an opaque Guid; there is no author name, because no endpoint
      lists users. The client renders it the way it already renders an assignee.
- [ ] The ticket detail view shows the comment thread and a box to add one, with
      an internal/public choice visible only to staff. Every post re-fetches
      rather than appending locally.
- [ ] Timestamps render in the browser's zone through the DisplayTime formatter
      added in story 08. No raw UTC reaches a screen.
- [ ] A migration adds the comment table with a foreign key to ticket and to the
      user table, and an index supporting newest-first paging by ticket.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no API and no database.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #8, #9, #10, #12, #13 (merged), #5 and #6 (merged — a comment needs an author). Closes the comments half of #11. The activity timeline was split into **#47** and is not in this story.
- **Depends on code areas or other stories:** `Ticket`, `TicketAccess`, `ITicketRepository`, `CallerContext`, `TicketDetail.razor`, `DisplayTime`, `ITicketsApiClient`.

## Extra notes

- Story 07's demo seeder should gain a few comments so the thread is visible in a demo. If seeding is left untouched, the feature is invisible from a clean database — the same failure #4 existed to prevent.
- The detail view is getting long. If it needs splitting into components, do that here rather than after #14 adds more.

## Technical hints

```
DECISIONS MADE

Separate aggregate, not an owned collection. Comments grow without bound, and an
owned collection means every ticket read drags the whole history - or means
configuring EF not to, which is the same decision made less visibly. TicketComment
has its own id, its own repository methods, and its own paging.

But the closed-ticket rule stays in the domain. Ticket gains a method - the plan
picks the name - that throws when the ticket may not be commented on. The API
already loads the ticket before commenting, because it must check the caller may
see it at all, so the check costs no extra query. This is the compromise the split
requires: comments live outside the aggregate, and the rule about them lives
inside it.

409, not 400 or 403, for a closed ticket. The caller is permitted and the request
is well-formed; the ticket's state forbids it. Same distinction story 06 drew
between a workflow refusal and an authorisation refusal.

IsInternal from the first migration, not later. Adding a visibility flag to a
table that already holds comments means deciding what every historical comment
was, and there is no honest answer. One boolean now costs nothing; retrofitting it
costs a judgement call about other people's words. This is the same reasoning that
moved authentication ahead of the UI.

Visibility filtering belongs in the repository. A Customer must not receive an
internal comment, and that is the same class of rule as row-level ticket access -
which story 06 put in TicketRepository precisely so no future caller could forget
it. Put this beside it, in the shared filter helper, for the same reason.

No author names. AuthorId is an opaque Guid, as RequesterId and AssigneeId still
are, because no endpoint lists users (#43). The client renders it as it already
renders an assignee. When a users endpoint lands, all three improve together.

No editing and no deleting. An edited comment raises what the audit trail should
show, and a deleted one raises whether the thread should say something was
removed. Both are product questions; neither has been asked.
```

## Out of scope

```
- No activity timeline. Status changes, assignments, and priority changes are a
  derived event history, not authored text, and storing them is a different
  design. Its own issue.
- No editing or deleting a comment.
- No attachments, no rich text, no markdown, no mentions, no notifications.
- No email ingestion or reply-by-email.
- No author display names - no endpoint lists users (#43).
- No reactions, no threading, no replies-to-a-comment. One flat list per ticket.
- No draft saving and no optimistic append: a post re-fetches, matching story 08.
- No change to the ticket endpoints or to TicketResponse. Comments are their own
  routes; a ticket read returns no comment data and no comment count.
- No real-time updates. Someone else's comment appears on the next fetch.
```
