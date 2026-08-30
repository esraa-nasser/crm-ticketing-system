# Story intake

- Folder: `.squad/stories/ticketing-ui/12/intake.md`

---

## Feature

- **Feature name (display):** Ticketing UI — list view
- **Feature slug (folder under `plans/`):** `ticketing-ui`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `12`
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:client`, `sdd:needs-plan`

---

## Title

```
Ticket list view with filtering, paging, and metadata-driven controls
```

---

## Description

```
The first screen in the product. Story 04 put the ticket endpoints on the wire
and they have been exercised live - 201, 200, 400, 404, and 409 on both domain
exception types. This story gives them a consumer.

It is also the first story that writes a Razor page. The scaffold left routing,
MainLayout, NavMenu, and the typed-client convention in place (SystemApiClient
is the worked example) but deliberately shipped no feature pages, so this story
establishes the pattern the detail view (#13) and the kanban board (#14) will
follow. Getting the shape right matters more here than getting the screen
pretty.

The story ends at read-only display. Creating, editing, and transitioning a
ticket from the UI belong to #13, which owns the detail view and the forms.
```

---

## Acceptance criteria

```
- [ ] A route at /tickets renders a table of tickets from GET /api/tickets, and
      NavMenu gains a Tickets entry replacing the placeholder comment.
- [ ] All API access goes through a new TicketsApiClient in
      src/CrmTicketing.Client/Services/, registered with AddHttpClient in
      Program.cs alongside SystemApiClient. No component injects HttpClient.
      Verify: grep -rn "HttpClient" src/CrmTicketing.Client/Pages/ -> no output
- [ ] The status and priority filter controls are populated from
      GET /api/tickets/metadata. No status or priority name is written as a
      literal anywhere in the Client project.
      Verify: grep -rn "\"Resolved\"\|\"Pending\"\|\"Urgent\"" \
              --include='*.razor' --include='*.cs' src/CrmTicketing.Client/
              -> no output
- [ ] Filter and page state lives in the query string (/tickets?status=Open&page=2),
      so a filtered list is linkable and the browser back button behaves. State
      is read from the URI, never held only in component fields.
- [ ] Four display states are distinct and each is reachable in a test: loading,
      loaded-with-rows, loaded-but-empty (a filter matched nothing), and failed
      (the API is unreachable or returned a problem-details error). "Empty" and
      "failed" must not render the same thing.
- [ ] A failed request surfaces the problem-details title to the user. The raw
      exception, the stack trace, and the traceId are not shown in the UI.
- [ ] The page requests only TicketSummaryResponse and renders only its fields.
      No call to GET /api/tickets/{id} is made to populate a list row.
- [ ] Paging: next/previous controls driven by the total count in
      PagedResponse<T>. Requesting a page past the end shows the empty state,
      not an error.
- [ ] A new test project tests/CrmTicketing.Client.Tests using bUnit, added to
      CrmTicketing.slnx and to the CI workflow's test run.
- [ ] Component tests cover: rows render from a stubbed client, the empty state
      renders when the page has zero items, the error state renders when the
      client throws, filter controls render the options returned by metadata,
      and changing a filter issues a request carrying that filter.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no API and no database running.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #10 (merged - contracts and endpoints). Closes #12. Unblocks #13 (detail view) and #14 (kanban).
- **Depends on code areas or other stories:** `CrmTicketing.Shared.Contracts.Tickets.*`, `GET /api/tickets`, `GET /api/tickets/metadata`, the `SystemApiClient` typed-client convention, `NavMenu.razor`.

## Extra notes

- **Ordering is unverified.** Issue #29 records that `TicketRepository.ListAsync`
  filtering, ordering, and paging have no integration test behind them. This
  story must not claim a sort order in its UI copy, and must not add a sort
  control - see Out of scope. If the list appears unordered during manual
  testing, that is #29, not a defect in this story.
- The NavMenu placeholder comment ("Feature navigation ... is added by the
  stories that implement those features") is consumed here. Remove it.

## Technical hints

```
DECISIONS MADE

Filters in v1: status and priority ONLY.

The API also accepts assigneeId and requesterId, but both are opaque Guids with
no User or Contact aggregate behind them (#5, #6, and the customers-crm feature
are all unbuilt). A filter control that demands a hand-typed Guid is not a
usable feature, and a picker cannot be built without an entity to pick from.
Status and priority are closed sets served by /api/tickets/metadata, so they
become dropdowns with no lookup and no invented data. Assignee filtering
arrives with the story that introduces users.

No polling, no SignalR, no auto-refresh.

The list loads on navigation and on an explicit Refresh action. Nothing in the
requirements asks for live updates, and a timer costs a disposal path, a
cancellation path, and a request every N seconds per open tab. If live updates
are wanted later, the answer is SignalR and its own story - not a polling loop
retrofitted here.

No sort control.

GET /api/tickets exposes no sort parameter. A sortable column header would
either lie (sorting one page client-side) or require changing the endpoint,
which is a change to a merged, verified contract. If sorting is wanted, it is a
story against #10, and it should land before the kanban board.

URL as the state store.

Filters and page number are query-string parameters bound with
[SupplyParameterFromQuery]. Component fields hold no filter state of their own.
This is decided now rather than later because retrofitting linkable state means
rewriting every handler on the page - and #13 will link back into a filtered
list from a ticket detail view.

Metadata fetched once.

GET /api/tickets/metadata is fetched on first render and held in a scoped
service so #13 and #14 reuse it rather than each refetching. The transition map
is not needed by this story, but the same response carries it, so the service
returns the whole TicketMetadataResponse rather than projecting statuses out.

Testing: bUnit.

tests/CrmTicketing.Client.Tests with the bunit package, pinned in
Directory.Packages.props like every other dependency (central package
management - no inline Version attributes). TicketsApiClient is stubbed at the
seam, so tests need no HTTP, no API, and no database. The .editorconfig
[tests/**.cs] block already suppresses CA1707 and CA1822, so the new project
inherits the same rules.

Contracts are consumed, not redefined. The Client references
CrmTicketing.Shared already. It must not declare its own view models that
mirror TicketSummaryResponse.
```

## Out of scope

```
- No create, edit, transition, or assign from this screen. Every write action is
  the detail view, issue #13.
- No kanban or board layout. That is issue #14, and it consumes the transition
  map this story fetches but does not use.
- No sort control and no change to GET /api/tickets. See Technical hints.
- No assignee or requester filter, and no user picker of any kind.
- No free-text search. The endpoint does not support it and adding it is a
  change to #10 plus a database index decision.
- No authentication, no login, no permission-gated columns or actions. That is
  issues #5, #6, and #16.
- No comments or activity timeline (issue #11).
- No SLA or due-date columns (issue #21).
- No virtualisation, no infinite scroll, no client-side caching of pages.
  Offset paging with next/previous is enough at pageSize 25.
- No design system, theme, or component library beyond the Bootstrap already in
  wwwroot. Do not add MudBlazor, Radzen, or Fluent UI - that is a decision with
  consequences for every later screen and it needs its own story.
- No localisation or RTL work, notwithstanding the organisation's locale. It is
  a cross-cutting concern and doing it per-screen guarantees inconsistency.
- No integration test that starts the API. Issue #29 owns the database-backed
  repository tests.
```
