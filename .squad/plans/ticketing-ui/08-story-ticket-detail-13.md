# Story 08 — Ticket detail view, and the first writes from the UI (Story: 13)

## Prerequisites

- Story 04 completed: [`../ticketing-core/04-story-ticket-endpoints-10.md`](../ticketing-core/04-story-ticket-endpoints-10.md) — `PATCH /api/tickets/{id}`, `POST /api/tickets/{id}/status`, and `POST /api/tickets/{id}/assignee` are merged and verified live. **This story changes none of them.** The only server-side change it makes is the additive `UserId` on `SignInResponse` — see task 1, which argues for it explicitly against the intake's default.
- Story 05 completed: [`05-story-ticket-list-view-12.md`](05-story-ticket-list-view-12.md) — `ITicketsApiClient`, `TicketMetadataProvider`, `ApiProblem`, `ApiRequestException`, and the query-string state store. The list's filter state lives in the URL precisely so the detail view can link back to it.
- Story 06 completed: [`../auth-roles/06-story-identity-and-authorisation-5.md`](../auth-roles/06-story-identity-and-authorisation-5.md) — writes need an actor, and there finally is one. `TokenStore` and `BearerTokenHandler` already attach the bearer token.
- Story 07 completed: [`../demo-data/07-story-demo-data-4.md`](../demo-data/07-story-demo-data-4.md) — twelve seeded tickets across every status, so the transition rendering has something to render against manually.
- No running API or database is needed to build or test. The manual verification step needs both.

---

## Story Goal

Make the product usable without curl. Every write endpoint has existed since story 04 and is still reachable only from a terminal.

1. A detail view at `/tickets/{id}` showing everything the list omits, linking back to the list with the caller's filters and page intact.
2. A create form at `/tickets/new`, with **no requester field** — the requester is always the signed-in user.
3. Edit, transition, and assign, driven from the detail view.
4. **Transitions rendered from the metadata map**, filtered to the ticket's current status. No status name and no transition rule appears as a literal in the Client.
5. **Every write re-fetches from the server.** A screen showing what the client hoped happened is how a silently failed write becomes a lie.

This is the first story where the UI causes state to change, so it is the first place a user can be misled about whether something happened.

---

## Context — Read These Files First

1. `src/CrmTicketing.Client/Services/ITicketsApiClient.cs` — all 24 lines. **It has exactly two methods**, `GetTicketsAsync` and `GetMetadataAsync`. This story adds five more; that is the bulk of the work, and every one of them is new surface rather than a change to existing surface.
2. `src/CrmTicketing.Client/Services/TicketsApiClient.cs` — all 110 lines. The private `GetAsync<T>` (~lines 54–73) and `ReadFailureMessageAsync` (~lines 75–109) are the error-handling seam to reuse: they already parse `ApiProblem`, prefer the first validation message over the generic title, and guard against a non-JSON error body. **Do not write a second copy of that logic for writes** — generalise it.
3. `src/CrmTicketing.Client/Services/ApiRequestException.cs` — all 32 lines. `StatusCode` (line 31) is what pages branch on. **Never match on message text.**
4. `src/CrmTicketing.Client/Pages/Tickets.razor` — all 261 lines. `@page "/tickets"` (line 1), the row loop (~line 79), and `NavigateWith` (~lines 245–251) building URIs through `GetUriWithQueryParameters`. The `[SupplyParameterFromQuery]` properties and the four display states are the pattern the detail view follows.
5. `src/CrmTicketing.Client/Services/TokenStore.cs` — all 35 lines. It holds `AccessToken`, `Email`, `Roles`, and `IsSignedIn`. **It does not hold the user id**, which two acceptance criteria need — see task 1.
6. `src/CrmTicketing.Client/Services/TicketMetadataProvider.cs` — all 37 lines. `GetAsync()` takes no cancellation token and caches the response, clearing it on failure. Story 05 fetched this and never used the `Transitions` map; this story is what it was built for.
7. `src/CrmTicketing.Client/Pages/SignIn.razor` — all 86 lines. The `EditForm` + `InputText` pattern, the busy flag, and the catch filter to match.
8. `src/CrmTicketing.Shared/Contracts/Tickets/` — `TicketResponse`, `CreateTicketRequest`, `UpdateTicketRequest`, `TransitionTicketRequest`, `AssignTicketRequest`. **Read `CreateTicketRequest` carefully: `RequesterId` is a non-nullable `Guid`** — see task 1.
9. `src/CrmTicketing.Api/Controllers/TicketsController.cs` — the `Create` action's requester rule (~lines 45–63) and the `Transition` action's `RequesterAllowedTransitions` (~line 246). Read them to understand what the UI will be refused for, not to change them.
10. `tests/CrmTicketing.Client.Tests/Pages/TicketsTests.cs` — the bUnit conventions: `BunitContext`, `Render<T>()`, `BunitNavigationManager`, a hand-rolled stub over `ITicketsApiClient`, no mocking library.
11. `docs/constitution.md` — §IV (line 55) contracts, §VII (line 86) three strikes before abstraction.

---

## Implementation tasks

### 1 — The signed-in user's id

**Two acceptance criteria need it and the client cannot currently obtain it.** "Assign to me" must send the caller's own id, and `CreateTicketRequest.RequesterId` is a **non-nullable `Guid`** that must be populated. `TokenStore` holds only `AccessToken`, `Email`, and `Roles`; `SignInResponse` carries no id either.

**Resolution: add `UserId` to `SignInResponse`.** This reverses the intake's "change no endpoint" rule, deliberately, after the alternative was costed.

**File: `src/CrmTicketing.Shared/Contracts/Auth/SignInResponse.cs`**

```csharp
public sealed record SignInResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string Email,
    Guid UserId,
    IReadOnlyList<string> Roles);
```

**File: `src/CrmTicketing.Api/Controllers/AuthController.cs`** — populate it from the authenticated user in the sign-in action. One line.

**File: `tests/CrmTicketing.Api.Tests/Controllers/AuthControllerTests.cs`** — update the construction sites, and assert the returned `UserId` is the signed-in user's.

**File: `src/CrmTicketing.Client/Services/TokenStore.cs`** — add `public Guid UserId { get; private set; }`, set from the response in `Set`, reset in `Clear`. **No decoding.**

**Why the intake's rule loses here.** That rule exists to stop unprincipled contract churn, not to force a worse design when a contract is genuinely missing something a consumer needs. The change is additive on the wire — an existing client ignoring the new field is unaffected — and costs one record parameter, one controller line, and a test update.

**Why not decode the JWT client-side.** It was the obvious workaround and it is worse in three ways. It is more code than the contract change, not less: base64url conversion, padding, JSON parsing, claim lookup, and a `Guid.TryParse`, plus tests. It needs a token captured from a live API as a fixture, which goes stale and has to be regenerated. And it couples the client to a claim-type name that story 06 pins in *server* configuration — `AuthenticationSetup` sets `NameClaimType` and `RoleClaimType` explicitly, so a future change there would silently null `UserId` on the client with no error anywhere: the create form would refuse to submit and "Assign to me" would disable, both without explanation.

**The client never inspects the token.** It holds it, attaches it, and reads its own id from the sign-in response. Token contents remain the API's business.

### 2 — The client's write surface

**File: `src/CrmTicketing.Client/Services/ITicketsApiClient.cs`** — five new methods:

```csharp
Task<TicketResponse> GetTicketAsync(Guid id, CancellationToken cancellationToken);
Task<TicketResponse> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken);
Task<TicketResponse> UpdateAsync(Guid id, UpdateTicketRequest request, CancellationToken cancellationToken);
Task<TicketResponse> TransitionAsync(Guid id, TransitionTicketRequest request, CancellationToken cancellationToken);
Task<TicketResponse> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken cancellationToken);
```

**File: `src/CrmTicketing.Client/Services/TicketsApiClient.cs`** — implement them, and **generalise the existing error handling rather than duplicating it**. `GetAsync<T>` and `ReadFailureMessageAsync` already parse `ApiProblem`, prefer the first validation message over the generic title, and survive a non-JSON body. Extract a `SendAsync<T>(HttpRequestMessage, CancellationToken)` that both reads use, and route the writes through it with `PostAsJsonAsync`/`PatchAsJsonAsync` equivalents.

A second copy of that parsing is the failure mode to avoid: the 400-body handling was got right once in story 05 after capturing a real payload, and a duplicate would drift from it.

`AssignAsync` covers both assign and unassign — `AssignTicketRequest.AssigneeId` is nullable and `null` means unassign. There is no separate route.

### 3 — The detail view

**Create file: `src/CrmTicketing.Client/Pages/TicketDetail.razor`** — `@page "/tickets/{Id:guid}"`

Renders every field `TicketResponse` carries, including the `Description` the list omits. Four display states matching `Tickets.razor`: loading, loaded, failed, and — distinct from failed — **not found**, since a 404 means the ticket is gone or not yours and "retry" is not the remedy.

**Back to the list, with state intact:**

```csharp
[SupplyParameterFromQuery] public string? Status { get; set; }
[SupplyParameterFromQuery] public string? Priority { get; set; }
[SupplyParameterFromQuery] public int? Page { get; set; }
```

The link back rebuilds `/tickets` with those three parameters through `NavigationManager.GetUriWithQueryParameters`. **The list must pass them when linking in** — task 6. Story 05 put filter state in the URL for exactly this; a back-link that drops it sends the user to an unfiltered page 1 and loses their place.

**Transitions** come from `TicketMetadataProvider`, indexed by the ticket's **current** status:

```csharp
var available = metadata.Transitions.TryGetValue(ticket.Status, out var targets) ? targets : [];
```

One button per entry. A `Closed` ticket renders none, **because the map returns an empty list** — not because the page tests for `Closed`. Do not write that special case; it would be a second transition rule.

**Assignment** is two actions and no picker: **Assign to me** sends `new AssignTicketRequest(tokens.UserId)`, **Unassign** sends `new AssignTicketRequest(null)`. Disable "Assign to me" when `TokenStore.IsSignedIn` is false or `UserId` is `Guid.Empty` — the property is a non-nullable `Guid` under task 1, so "unknown" is the default value, not null.

**Every write re-fetches.** The endpoints return the updated `TicketResponse`, but the page discards it and calls `GetTicketAsync` again, which also catches a concurrent change by someone else. Do not patch local state from the response.

**Error rendering, branching on `ApiRequestException.StatusCode` only:**

| Status | Message |
|---|---|
| 400 | the exception's message — already the validation text, not the generic title |
| 403 | "You do not have permission to do that." |
| 404 | "That ticket no longer exists, or is not yours to see." |
| 409 | the exception's message — already carries what the conflict was |
| 401 | **navigate to `/signin?returnUrl=…`**, render no error |
| other | a generic failure message |

**Never match on message text**, and never render `traceId` or a stack trace. A 401 is not an error state — it is a redirect.

### 3b — `SignIn.razor` honours `returnUrl`

Task 3 sends an unauthenticated caller to `/signin?returnUrl=…`, and the edge cases promise they "land back where they were". **Nothing currently reads that parameter** — `SignIn.razor` was written by story 06 with no notion of one, so as it stands the promise is a parameter with no consumer.

**File: `src/CrmTicketing.Client/Pages/SignIn.razor`** — add:

```csharp
[SupplyParameterFromQuery] public string? ReturnUrl { get; set; }
```

On a successful sign-in, navigate to `ReturnUrl` when it is present and **relative**, and to the existing default otherwise.

**The relative check is not optional.** An absolute `returnUrl` turns the sign-in page into an open redirect: a link to `/signin?returnUrl=https://evil.example` sends a user who has just typed their password to an attacker's site, and it will look like the application sent them. Accept only a value starting with a single `/` and not `//` — the second form is protocol-relative and reaches another host.

### 4 — The create form

**Create file: `src/CrmTicketing.Client/Pages/TicketCreate.razor`** — `@page "/tickets/new"`

A separate route, not a modal: it is linkable, has its own validation states, and a modal over a possibly-filtered list is a navigation trap.

Fields: **title, description, category, priority**. Priority options come from `TicketMetadataProvider.Priorities`. **No requester field.**

`RequesterId` is populated from `TokenStore.UserId` and nothing else. **If it is `Guid.Empty`, do not submit** — show that the session could not be identified and link to `/signin`. It is non-nullable under task 1, so the unset case is the default value. Submitting `Guid.Empty` is worse than refusing: story 06's `Create` forces a Customer's own id and ignores the body, but for **staff** it uses the body value, and `Guid.Empty` fails `IUserDirectory.ExistsAsync` and returns 400. A form that silently 400s for Agents and silently works for Customers is the confusing outcome to prevent.

On success, navigate to `/tickets/{newId}` — the detail view of the thing just created.

### 5 — Metadata for the priority list

`TicketMetadataProvider.GetAsync()` already returns the whole `TicketMetadataResponse`. Both new pages use it: the create form for `Priorities`, the detail view for `Transitions`. **No status or priority literal in either file.**

Metadata failing must not blank the page. In the detail view, render the ticket and disable the transition buttons; in the create form, disable submit and say why. The same rule story 05 applied to its filter dropdowns.

### 6 — Linking from the list

**File: `src/CrmTicketing.Client/Pages/Tickets.razor`** — the title cell (~line 82) becomes a link to the detail view, carrying the current filter state:

```razor
<td><a href="@DetailUri(ticket.Id)">@ticket.Title</a></td>
```

`DetailUri` builds `tickets/{id}` with the page's current `Status`, `Priority`, and `Page` as query parameters, so the detail view can hand them back. **This is the only change to `Tickets.razor`** — its four display states, paging, and filter handling are untouched.

**File: `src/CrmTicketing.Client/Layout/NavMenu.razor`** — no change. The create form is reached from the list, not the nav.

Add a **New ticket** link on the list page pointing at `/tickets/new`.

### 7 — Concurrency, stated rather than discovered

**Last write wins.** No ETag, no version column, no optimistic concurrency. Two agents editing the same ticket overwrite each other silently, and the re-fetch after each write means the loser sees the winner's values without being told a collision happened.

That is acceptable for now and **must be written down**: add a line to `docs/status.md` under `## Known defects`. A concurrency story needs a version field on the aggregate — a domain change and a migration — and inventing half of it here would be worse than naming the gap.

---

## Edge Cases & Failure Modes

- **`TokenStore.UserId` unset on a create.** It is a non-nullable `Guid`, so unset means `Guid.Empty`. Refuse to submit rather than sending it. For a Customer the API forces their own id and it would appear to work; for staff it returns 400. The same form behaving differently by role, with no explanation, is the worst of both.
- **A 401 mid-session.** The token expires after 60 minutes (`JwtOptions.LifetimeMinutes`). Any call may 401 at any time; every page navigates to `/signin` rather than rendering an error, and passes `returnUrl` so the user lands back where they were.
- **A 403 on "Assign to me" as a Customer.** Expected, and this story's job is to make it legible, not to prevent it. Hiding the button is issue #16 and explicitly follows this story.
- **A 403 on a transition a Customer may not make.** Story 06 permits a requester only five `(from, to)` pairs. The metadata map does **not** encode that rule — it is the workflow's map, not the caller's — so the UI will render buttons the API refuses. Rendering the 403 message is correct behaviour here; filtering the buttons by role is #16.
- **A 409 on a transition.** Means the ticket moved underneath the user, most likely by someone else. Render the conflict message and **re-fetch**, so the displayed status becomes the true one rather than the stale one the buttons were built from.
- **A 404 after a successful load.** The ticket was deleted or ownership changed between the load and the write. Distinct from the failed state: offer a link back to the list, not a retry.
- **Metadata unavailable but the ticket loads.** Render the ticket with transitions disabled. Blanking the page because a dropdown could not populate is the failure story 05 already rejected.
- **A `Closed` ticket.** `Transitions["Closed"]` is an empty list, so no buttons render. **Verify this comes from the map**, not from a `if (status == "Closed")` in the page — the latter is a second transition rule and the constitution's single-source clause forbids it.
- **Re-fetch failing after a successful write.** The write succeeded and the read did not. Show the ticket as stale with the read's error, and do **not** roll the display back — the server state changed and pretending otherwise is the lie this story exists to prevent.
- **Concurrent edits.** Last write wins, silently. Task 7 records it in `docs/status.md`.
- **The detail view reached without filter parameters.** Someone pasted `/tickets/{id}` directly. The back-link goes to a bare `/tickets`, which is correct — there is no state to preserve.
- **An invalid Guid in the route.** `{Id:guid}` refuses to match, so the router falls through to `NotFound`. No page code needed; do not add a parse guard.
- **An absolute or protocol-relative `returnUrl`.** `/signin?returnUrl=https://evil.example` or `//evil.example` would send a user who has just typed their password to another host, and the application would appear to have sent them. Task 3b accepts only a value beginning with a single `/`. Tests 16 and 17 pin it.

---

## Test Plan

### 8 — Component tests

**Create file: `tests/CrmTicketing.Client.Tests/Pages/TicketDetailTests.cs`**

Extend the existing `StubTicketsApiClient` pattern from `TicketsTests.cs` — hand-rolled, recording calls, with settable responses and a settable exception. No mocking library, no HTTP.

1. `Detail_RendersTheFullTicket` — every `TicketResponse` field appears, **including `Description`**, which the list omits.
2. `Detail_RendersOnlyTheLegalTransitionsForTheCurrentStatus` — a stubbed map where `Open` allows `Pending`/`Resolved`/`Closed` renders exactly three buttons, and none for statuses the map does not list. Assert against the stub's map, never literals.
3. `Detail_ClosedTicketOffersNoTransitions` — the map returns an empty list for `Closed`; assert zero transition buttons **and** that the page contains no `Closed` literal driving it.
4. `Transition_RefetchesRatherThanPatchingLocalState` — after a successful transition, the stub records a second `GetTicketAsync`, and the rendered status comes from the re-fetch, not the write's response. Make the two differ so the assertion can tell them apart.
5. `Transition_Conflict_RendersTheMessageAndLeavesTheStatusUnchanged` — the stub throws `ApiRequestException("…", 409)`; the conflict message renders and the displayed status is the pre-write one.
6. `Forbidden_RendersAPermissionMessage` — a 403 renders the permission text and no stack trace, `traceId`, or exception type name.
7. `Unauthorised_NavigatesToSignIn` — a 401 leaves the URI at `/signin` **carrying a `returnUrl` pointing back at the ticket**, and renders no error alert.
8. `NotFound_RendersDistinctlyFromFailed` — a 404 renders the gone-ticket message and **not** the generic failure element.
9. `BackLink_PreservesFilterAndPage` — rendered with `?status=Open&page=3`, the back-link URI carries both.

**Create file: `tests/CrmTicketing.Client.Tests/Pages/TicketCreateTests.cs`**

10. `Create_PostsWithoutARequesterField` — the rendered form has no input bound to a requester, and the recorded `CreateTicketRequest.RequesterId` equals `TokenStore.UserId`.
11. `Create_RefusesWhenTheUserIdIsUnknown` — with `TokenStore` never `Set` (so `UserId` is `Guid.Empty`), submitting records **no** call and renders the sign-in prompt.
12. `Create_PriorityOptionsComeFromMetadata` — options equal the stub's `Priorities`, asserted against the stub.
13. `Create_NavigatesToTheNewTicket` — on success the URI becomes `/tickets/{returnedId}`.

**Create file: `tests/CrmTicketing.Client.Tests/Services/TokenStoreTests.cs`**

14. `Set_StoresTheUserIdFromTheResponse` — the store returns the `UserId` the `SignInResponse` carried. No token parsing is involved, which is the point of task 1's resolution.
15. `Clear_ResetsTheUserId`.

**Create file: `tests/CrmTicketing.Client.Tests/Pages/SignInTests.cs`**

16. `SignIn_RedirectsToAReturnUrl` — a relative `returnUrl` is honoured after a successful sign-in.
17. `SignIn_IgnoresAnAbsoluteReturnUrl` — `https://evil.example` and the protocol-relative `//evil.example` are both refused, and the default destination is used. **This is the open-redirect test**; without it task 3b's guard is unverified.

**File: `tests/CrmTicketing.Client.Tests/Services/TicketsApiClientTests.cs`** — extend:

19. `Write_SurfacesTheValidationMessage` — a 400 from a write carries the `errors` message, proving the generalised `SendAsync` kept story 05's behaviour rather than a duplicate drifting from it.
20. `AssignAsync_WithNull_UnassignsThroughTheSameRoute` — the request body is `{"assigneeId":null}` against `/assignee`.

### 9 — What is not tested

21. **No test exercises a real write against the API.** The stub seam covers the page logic and the handler seam covers the client's parsing, but no automated test posts to a running API. That gap is issue #29's, unchanged by this story. The manual step below is not automated coverage.

### 10 — Regression

22. All four test projects pass. `TicketsTests.cs` gains no failures from task 6's single markup change; if it does, the link was added in a way that altered the row structure the existing assertions depend on.

---

## Verification Steps

1. **Backend and client build:** `dotnet build CrmTicketing.slnx` — zero warnings, zero errors.
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — four test projects, no API and no database.
3. **No workflow vocabulary hardcoded:** `grep -rnE "\"(New|Open|Pending|Resolved|Closed|Low|Normal|High|Urgent)\"" --include='*.razor' --include='*.cs' --exclude-dir=bin --exclude-dir=obj src/CrmTicketing.Client/` returns no output.
4. **No component holds an `HttpClient`:** `grep -rn "HttpClient" --include='*.razor' --exclude-dir=bin --exclude-dir=obj src/CrmTicketing.Client/Pages/` returns no output.
5. **No duplicated problem-details parsing:** `grep -c "ReadFromJsonAsync<ApiProblem>" src/CrmTicketing.Client/Services/TicketsApiClient.cs` returns `1`.
6. **Only the two files task 1 sanctions change outside the Client:**

    ```bash
    git status --short src/CrmTicketing.Domain src/CrmTicketing.Infrastructure
    ```

    Returns no output. `src/CrmTicketing.Shared/Contracts/Auth/SignInResponse.cs` and `src/CrmTicketing.Api/Controllers/AuthController.cs` **do** change — that is task 1 — and nothing else under `src/CrmTicketing.Api` or `src/CrmTicketing.Shared` may. No ticket endpoint, no contract other than `SignInResponse`, no domain type.
7. **No migration:** `git status --short src/CrmTicketing.Infrastructure/Persistence/Migrations/` returns no output.
8. **Manual, with the API, client, and PostgreSQL running and demo data seeded:** sign in as the Agent. From `/tickets`, filter to `Open`, go to page 1, open a ticket, and confirm the back-link returns to the filtered list. Transition it and confirm the status changes and the buttons change with it. Open a `Closed` ticket and confirm no transition buttons appear. Use **Assign to me**, then **Unassign**. Create a ticket from `/tickets/new` and confirm it lands on its detail view with you as requester. Then sign in as the Customer, open one of their own tickets, click a transition the API refuses, and confirm a **403 message** appears rather than a stack trace or a silent no-op.

---

## Done Criteria

- [ ] `/tickets/{id}` renders every `TicketResponse` field including `Description`.
- [ ] The list links to the detail view; the detail view links back preserving filter and page.
- [ ] `/tickets/new` creates a ticket with **no requester field**; the requester is the signed-in user.
- [ ] Transitions render from the metadata map for the current status only; a `Closed` ticket offers none **because the map is empty**, not because the page special-cases it.
- [ ] No status name, priority name, or transition rule is a literal anywhere in the Client.
- [ ] Edit submits title, description, category, and priority through `PATCH`, populated from current values.
- [ ] Assignment is **Assign to me** and **Unassign** only; no user picker.
- [ ] Every write re-fetches from the server; no local state patching.
- [ ] 400, 403, 404, and 409 render distinct, specific messages; none shows a stack trace or `traceId`; 401 navigates to `/signin`.
- [ ] `SignInResponse` carries `UserId`; `TokenStore` stores it from the response and decodes nothing.
- [ ] `SignIn.razor` honours a relative `returnUrl` and refuses an absolute or protocol-relative one.
- [ ] `TicketsApiClient` has one copy of the problem-details parsing, shared by reads and writes.
- [ ] No ticket endpoint, no domain type, and no migration is changed. The only non-Client changes are `SignInResponse` and the sign-in action that populates it.
- [ ] Last-write-wins is recorded in `docs/status.md` under Known defects.
- [ ] `dotnet build` clean; `dotnet test` passes with no API and no database.
- [ ] Overview `00-overview.md` updated with this story.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 09 (issue #16, permission-gated UI).**
