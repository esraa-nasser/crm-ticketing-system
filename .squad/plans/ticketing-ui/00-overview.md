# ticketing-ui — plan overview

Entry point for the **ticketing-ui** feature. Stories execute in order by their `NN` prefix.

## Stories

| NN | File | Title | Tracker id | Depends on |
|----|------|-------|------------|------------|
| 05 | [05-story-ticket-list-view-12.md](05-story-ticket-list-view-12.md) | Ticket list view with filtering, paging, and metadata-driven controls | 12 | Story 04 (ticketing-core), Story 01 (scaffold) |
| 08 | [08-story-ticket-detail-13.md](08-story-ticket-detail-13.md) | Ticket detail view, and the first writes from the UI | 13 | Story 05, Story 06 (auth-roles), Story 07 (demo-data) |

## Dependency notes

- **The first feature screen in the product.** Story 01 shipped the Blazor scaffold — routing, `MainLayout`, `NavMenu`, and the typed-client convention worked through `SystemApiClient` — but deliberately no feature pages. Story 05 establishes the page pattern that #13 and #14 copy, so the shape matters more here than the styling.
- **Consumes story 04, changes nothing in it.** `GET /api/tickets` and `GET /api/tickets/metadata` are merged and verified. This feature adds no endpoint, no contract, and no server-side file; a diff touching `src/CrmTicketing.Api` or `src/CrmTicketing.Shared` means something went wrong.
- **The metadata endpoint earns its keep here.** Status and priority names are fetched, never hardcoded, and a verification grep enforces it. That endpoint was built in story 04 with no consumer; story 05 is why it exists, and #14 will reuse the transition map it also carries.
- **The URL is the state store.** Filters and page number are query-string parameters, decided now rather than retrofitted, because #13 links back into a filtered list from a ticket detail view.
- **`ITicketsApiClient` is the first interface in the Client.** `SystemApiClient` has none and is deliberately not retrofitted: the new interface exists because a component test needs a second implementation, and the repo's `sealed`-by-default convention rules out a subclassed double. Constitution §VII bans speculative abstraction, not an abstraction with two implementations on day one.
- **First new test project since the scaffold.** `tests/CrmTicketing.Client.Tests` adds bUnit, the first new package since story 03. CI needs no edit — its test step already runs the whole solution, so `.slnx` membership is CI membership.
- **Story 08 is the first time the UI changes state.** Every write endpoint has existed since story 04 and been reachable only from a terminal. That makes it the first place a user can be misled about whether something happened, which is why every write re-fetches from the server rather than patching local state — a screen showing what the client hoped happened is how a silently failed write becomes a lie.
- **The transition map finally gets its consumer.** Story 04 built `GET /api/tickets/metadata` with no caller; story 05 fetched it and used only the status and priority lists. Story 08 renders transition buttons from the `Transitions` map keyed on the ticket's current status, so a `Closed` ticket offers none *because the map is empty* rather than because a page tests for it.
- **The map is the workflow's, not the caller's.** Story 06 permits a requester only five `(from, to)` pairs, and the metadata map does not encode that. Story 08 therefore renders buttons the API will refuse with 403, and its job is to make that refusal legible rather than to prevent the click. Hiding controls by role is #16 and deliberately follows.
- **Story 08 reopens one contract, deliberately.** The client needs the signed-in user's id for two features and neither `TokenStore` nor `SignInResponse` carried it. The intake's default is "change no endpoint", and story 08 argues against it explicitly: `SignInResponse` gains an additive `UserId`. The alternative — decoding the claim from the token client-side — is more code, needs a live-captured fixture that goes stale, and couples the client to a claim type story 06 pins in *server* configuration, where a later change would silently blank the id with no error anywhere. No ticket endpoint changes.
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
