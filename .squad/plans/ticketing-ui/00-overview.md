# ticketing-ui — plan overview

Entry point for the **ticketing-ui** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 05 | [05-story-ticket-list-view-12.md](05-story-ticket-list-view-12.md) | Ticket list view with filtering, paging, and metadata-driven controls | 12 | Story 04 (ticketing-core), Story 01 (scaffold) |

## Dependency notes

- **The first feature screen in the product.** Story 01 shipped the Blazor scaffold — routing, `MainLayout`, `NavMenu`, and the typed-client convention worked through `SystemApiClient` — but deliberately no feature pages. Story 05 establishes the page pattern that #13 and #14 copy, so the shape matters more here than the styling.
- **Consumes story 04, changes nothing in it.** `GET /api/tickets` and `GET /api/tickets/metadata` are merged and verified. This feature adds no endpoint, no contract, and no server-side file; a diff touching `src/CrmTicketing.Api` or `src/CrmTicketing.Shared` means something went wrong.
- **The metadata endpoint earns its keep here.** Status and priority names are fetched, never hardcoded, and a verification grep enforces it. That endpoint was built in story 04 with no consumer; story 05 is why it exists, and #14 will reuse the transition map it also carries.
- **The URL is the state store.** Filters and page number are query-string parameters, decided now rather than retrofitted, because #13 links back into a filtered list from a ticket detail view.
- **`ITicketsApiClient` is the first interface in the Client.** `SystemApiClient` has none and is deliberately not retrofitted: the new interface exists because a component test needs a second implementation, and the repo's `sealed`-by-default convention rules out a subclassed double. Constitution §VII bans speculative abstraction, not an abstraction with two implementations on day one.
- **First new test project since the scaffold.** `tests/CrmTicketing.Client.Tests` adds bUnit, the first new package since story 03. CI needs no edit — its test step already runs the whole solution, so `.slnx` membership is CI membership.
- **Ordering is knowingly unverified.** Issue #29 records that `TicketRepository.ListAsync` filtering, ordering, and paging have no integration test. Story 05 therefore claims no sort order in its UI copy and adds no sort control; an unordered list in manual testing belongs to #29.

## Deferred to later stories in this feature

| Issue | What it adds | Why not story 05 |
|---|---|---|
| #13 — ticket detail view | Create, edit, transition, assign | Every write action, plus the forms and their validation |
| #14 — kanban board | Board layout, drag between columns | Consumes the transition map story 05 fetches but does not use |
| #16 — permission-gated UI | Hiding actions by role | Needs #5 and #6; there is no identity yet |

## Unblocks

| Issue | Why it was blocked |
|---|---|
| #13 — ticket detail view | Needs the typed client, the page pattern, and a list to navigate from |
| #14 — kanban board | Needs the metadata provider and the typed client |
