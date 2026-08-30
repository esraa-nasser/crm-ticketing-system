# Story 05 — Ticket list view with filtering, paging, and metadata-driven controls (Story: 12)

## Prerequisites

- Story 04 completed: [`../ticketing-core/04-story-ticket-endpoints-10.md`](../ticketing-core/04-story-ticket-endpoints-10.md) — `GET /api/tickets` and `GET /api/tickets/metadata` are merged on `main` and have been exercised live against a real database.
- Story 01 completed: [`../crm-ticketing-foundation/01-story-crm-ticketing-mvp-foundation.md`](../crm-ticketing-foundation/01-story-crm-ticketing-mvp-foundation.md) — the Blazor scaffold, `MainLayout`, `NavMenu`, the `Api:BaseAddress` configuration seam, and the typed-client convention (`SystemApiClient`) exist.
- No running API and no database are required to build or test. Every verification step except the last works offline.
- **This is the first story in a new feature folder.** `.squad/plans/ticketing-ui/00-overview.md` and a row in `.squad/plans/00-index.md` are created alongside the plan.

---

## Story Goal

Give the ticket endpoints their first consumer, and establish the page pattern that the detail view (#13) and the kanban board (#14) will copy.

1. A route at `/tickets` renders a table of tickets from `GET /api/tickets`, reachable from `NavMenu`.
2. Status and priority filters are populated from `GET /api/tickets/metadata`. **No status or priority name is written as a literal anywhere in the Client project.**
3. Filter and page state lives in the query string, so a filtered list is linkable and the back button works.
4. Four display states — loading, rows, empty, failed — are distinct and each is reachable in a test.
5. A new `tests/CrmTicketing.Client.Tests` project using bUnit proves all of it with no API, no HTTP, and no database.

The story ends at read-only display. Every write action belongs to #13.

---

## Context — Read These Files First

1. `src/CrmTicketing.Client/Services/SystemApiClient.cs` — all 15 lines. The typed-client convention this story follows: `public sealed class` with a primary constructor taking `HttpClient` (line 11), one method per endpoint (lines 13–14), and a `<summary>` stating that components never touch `HttpClient`. Note it uses `GetFromJsonAsync`, which this story's client deliberately does **not** — see task 3.
2. `src/CrmTicketing.Client/Pages/Diagnostics.razor` — all 56 lines. The nearest precedent for a page that calls the API: the error block (~lines 12–24), the loading branch (~lines 25–28), the loaded branch (~lines 29–39), and the `try`/`catch` in `OnInitializedAsync` (~lines 45–55) catching only `HttpRequestException or TaskCanceledException`. This story's page has **four** states where this one has three, and loads in `OnParametersSetAsync` rather than `OnInitializedAsync` — see task 5.
3. `src/CrmTicketing.Client/Program.cs` — all 20 lines. `apiBaseAddress` is read from configuration and throws if absent (~lines 13–15); `AddHttpClient<SystemApiClient>` is registered on lines 17–18. Task 6 adds two registrations immediately after and changes nothing else.
4. `src/CrmTicketing.Client/Layout/NavMenu.razor` — all 36 lines. The two existing `<div class="nav-item px-3">` blocks (~lines 12–21) are the markup to copy. **The placeholder comment on lines 22–23** ("Feature navigation … is added by the stories that implement those features") is consumed by this story and must be removed.
5. `src/CrmTicketing.Client/CrmTicketing.Client.csproj` — all 17 lines. One `ProjectReference`, to `Shared` (line 14). **It stays at one.** The Client must not reference `Domain` (constitution §II).
6. `src/CrmTicketing.Client/_Imports.razor` — all 10 lines. Global usings for `.razor` files. Task 5 adds two lines here rather than repeating `@using` in the page.
7. `tests/CrmTicketing.Api.Tests/CrmTicketing.Api.Tests.csproj` — all 24 lines. The test-project shape to copy: `IsPackable`/`IsTestProject`, four package references with **no `Version` attributes**, `<Using Include="Xunit" />`, one `ProjectReference`.
8. `Directory.Packages.props` — the `Label="Testing"` `ItemGroup` holds `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4. Task 8 adds one line here. **Every version lives in this file; a `.csproj` carrying an inline `Version` is a defect.**
9. `CrmTicketing.slnx` — the `/tests/` folder lists three test projects. Task 8 adds a fourth.
10. `.github/workflows/ci.yml` — the `Test` step (~lines 35–41) runs `dotnet test CrmTicketing.slnx`, i.e. the whole solution. **No workflow edit is needed**; adding the project to `.slnx` is what puts it in CI. See task 8.
11. `.editorconfig` — the `[tests/**.cs]` block (~lines 49–51) already sets `CA1707` and `CA1822` to `none`. The new test project inherits it; do not add per-project suppressions.
12. `src/CrmTicketing.Shared/Contracts/Tickets/` — `TicketSummaryResponse.cs`, `PagedResponse.cs`, and `TicketMetadataResponse.cs`. These are the only shapes this story deserialises. **The Client must not declare view models mirroring them.**
13. `src/CrmTicketing.Api/Controllers/TicketsController.cs` — read the `List` action's `[FromQuery]` parameters and the `GetMetadata` action. The query-string names this story emits must match those parameter names exactly.

---

## Implementation tasks

### 1 — The query-string parameter names

`GET /api/tickets` binds `status`, `priority`, `assigneeId`, `requesterId`, `page`, and `pageSize`. This story emits **only** `status`, `priority`, and `page`; `pageSize` is fixed at 25 and the two id filters are out of scope.

The page's own URL uses the same three names (`/tickets?status=Open&page=2`), so the browser URL and the API request carry identical vocabulary and there is no translation layer to get wrong.

### 2 — The error contract

**Create file: `src/CrmTicketing.Client/Services/ApiProblem.cs`**

```csharp
public sealed record ApiProblem(
    string? Title,
    int? Status,
    string? Detail,
    IReadOnlyDictionary<string, string[]>? Errors);
```

RFC 9457 problem details as the Client needs them. `ProblemDetails` from `Microsoft.AspNetCore.Mvc` is **not** available to a Blazor WebAssembly project and pulling in a package to obtain it is not justified for four fields.

**`Errors` is not optional decoration.** The list view's realistic failure is a bad filter in a hand-edited URL, and that response's `title` is the generic `"One or more validation errors occurred."` — the sentence a user needs sits in `errors`. A three-field record would render the generic title and silently drop the only useful text on the page's most likely error path. This record is deliberately in the Client, not in `Shared`: nothing serialises it outbound, and `Shared` is the contract between client and API for request/response bodies the API defines.

**Create file: `src/CrmTicketing.Client/Services/ApiRequestException.cs`**

```csharp
public sealed class ApiRequestException : Exception
{
    public ApiRequestException() { }
    public ApiRequestException(string message) : base(message) { }
    public ApiRequestException(string message, Exception innerException) : base(message, innerException) { }
    public ApiRequestException(string message, int? statusCode) : base(message) => StatusCode = statusCode;

    public int? StatusCode { get; }
}
```

The message is **user-facing** — it carries the problem-details `Title`, never a stack trace and never the `traceId`. Include the three standard constructors CA1032 requires; the extra `(string, int?)` overload does not clash with them, so **no suppression is needed**. Mirror `src/CrmTicketing.Domain/Tickets/TicketClosedException.cs`, which solved the same CA1032 problem the same way.

### 3 — The typed client

**Create file: `src/CrmTicketing.Client/Services/ITicketsApiClient.cs`**

```csharp
public interface ITicketsApiClient
{
    Task<PagedResponse<TicketSummaryResponse>> GetTicketsAsync(
        string? status, string? priority, int page, CancellationToken cancellationToken);

    Task<TicketMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken);
}
```

**Create file: `src/CrmTicketing.Client/Services/TicketsApiClient.cs`**

```csharp
public sealed class TicketsApiClient(HttpClient httpClient) : ITicketsApiClient
```

- `GetTicketsAsync` builds the query string from the non-null arguments only — an absent filter must not appear as `status=` in the URI, because the API would parse the empty string and return **400**.
- Page size: send `pageSize=25`. Declare it as `internal const int PageSize = 25;` on `TicketsApiClient` so the page and the tests reference one constant.
- Both methods call `httpClient.GetAsync`, then branch on `response.IsSuccessStatusCode`:
  - **Success** — `ReadFromJsonAsync<T>`; a null body throws `ApiRequestException("The API returned an empty response.", (int)response.StatusCode)`.
  - **Failure** — read the body as `ApiProblem` and throw `ApiRequestException(message, (int)response.StatusCode)`, where `message` is **the first value of the first `Errors` entry when `Errors` is non-empty, and `Title` otherwise**, falling back to a generic string when both are absent. Render only the message text; do **not** prefix it with the field name — see the casing warning in Edge Cases. Reading the error body is why this client does not use `GetFromJsonAsync`: that helper throws `HttpRequestException` and discards the response body, so the problem-details `Title` would be lost.
  - Wrap the `ReadFromJsonAsync` of the error body in its own `try`/`catch (JsonException)` — a proxy or a dev-server can return HTML for a 502, and a failed error-parse must not mask the original failure.
- `HttpRequestException` (the API is unreachable) is allowed to propagate; the page catches it. Do **not** catch and rethrow it as `ApiRequestException` — an unreachable host and a rejected request are different conditions and the page renders the same state for both anyway.

**Why an interface, when `SystemApiClient` has none.** The page is component-tested with a stub, and the repo's convention is `sealed` by default (`docs/architecture.md`), so a subclassable test double is not available. The interface has two implementations on day one — the real client and the test stub — which is not the speculative abstraction constitution §VII bans. **Do not retrofit an interface onto `SystemApiClient`**; it has no second implementation and no component test.

**The seam has a cost, and tests 11 and 12 pay it.** Stubbing `ITicketsApiClient` everywhere would leave the branching specified above - `IsSuccessStatusCode`, the null-body case, the `ApiProblem` parse, the `JsonException` guard, and the omit-absent-filter rule - executed by no test at all. That is the behaviour this client exists for. `TicketsApiClient` is therefore also tested directly over a stubbed `HttpMessageHandler`, where the transport is faked and the real class runs.

### 4 — The metadata cache

**Create file: `src/CrmTicketing.Client/Services/TicketMetadataProvider.cs`**

```csharp
public sealed class TicketMetadataProvider(ITicketsApiClient client)
{
    public Task<TicketMetadataResponse> GetAsync(CancellationToken cancellationToken);
}
```

Hold the in-flight `Task<TicketMetadataResponse>` in a private field and return the same instance on every call, so concurrent first callers share one request rather than racing. **On failure, clear the field** so a later navigation retries instead of replaying a cached exception forever.

Return the whole `TicketMetadataResponse`, not a projection of its `Statuses`. This story does not use `Transitions`, but #14 does, and the same response already carries it.

### 5 — The page

**Create file: `src/CrmTicketing.Client/Pages/Tickets.razor`** — `@page "/tickets"`

```csharp
[SupplyParameterFromQuery] public string? Status { get; set; }
[SupplyParameterFromQuery] public string? Priority { get; set; }
[SupplyParameterFromQuery] public int? Page { get; set; }
```

**Load in `OnParametersSetAsync`, not `OnInitializedAsync`.** Blazor reuses the component instance when only the query string changes, so `OnInitializedAsync` runs once and a filter change would update the URL while leaving the table stale. This is the single most likely bug in this story.

Guard the reload: keep the last-loaded `(Status, Priority, Page)` tuple in a field and return early when it is unchanged, or `OnParametersSetAsync` will refetch on every re-render.

Four states, rendered from two fields (`_result` and `_error`) plus a `_loading` flag:

| State | Condition | Renders |
|---|---|---|
| Loading | `_loading` | "Loading tickets…" |
| Failed | `_error is not null` | `alert alert-danger` with the message, a **Retry** button |
| Empty | `_result.Items.Count == 0` | `alert alert-secondary`: no tickets match, and a **Clear filters** link when a filter is set |
| Rows | otherwise | the table |

**Empty and failed must not render the same element.** A test asserts on each, so give them distinct CSS classes as above.

Table columns, all from `TicketSummaryResponse` and nothing else: **Title**, **Status**, **Priority**, **Category** (em dash when null), **Assignee** (the word `Unassigned` when `AssigneeId` is null; otherwise the first 8 characters of the Guid — there is no user aggregate to resolve a name from), **Created** (`CreatedAt.ToString("u")`, matching `Diagnostics.razor` line 36).

**Do not call `GET /api/tickets/{id}` to fill a row.** The list endpoint returns everything the table shows.

Filter controls: two `<select>` elements whose `<option>` lists come from `TicketMetadataProvider`. Each gets an "All" option with an empty value. **No status or priority name appears in this file.** While metadata is still loading, render the selects disabled rather than empty — an enabled empty dropdown reads as "no options exist".

Paging: **Previous**/**Next** buttons driven by the response, never by the query-string parameter. `PagedResponse<T>` carries `Page` and `PageSize` as actually served after clamping, and the component's `[SupplyParameterFromQuery] Page` property shares a name with `PagedResponse.Page` — read every paging value off the response object. Disable Previous when `result.Page <= 1`; disable Next when `result.Page * result.PageSize >= result.TotalCount`. Show `Showing X–Y of TotalCount`, computed the same way. `TicketsApiClient.PageSize` is what the client *requests*; `result.PageSize` is what it *got*, and only the second may drive the UI.

Navigation on any filter or page change goes through `NavigationManager.GetUriWithQueryParameters(...)` then `NavigationManager.NavigateTo(...)`. **Component fields never hold filter state** — the URI is the store. Changing a filter resets `page` to 1; leaving a filtered page-3 view on a narrower filter would land past the end.

Catch `ApiRequestException` and `HttpRequestException or TaskCanceledException` around both calls, matching the catch filter in `Diagnostics.razor` line 51.

**File: `src/CrmTicketing.Client/_Imports.razor`** — append:

```razor
@using CrmTicketing.Client.Services
@using CrmTicketing.Shared.Contracts.Tickets
```

**File: `src/CrmTicketing.Client/Layout/NavMenu.razor`** — add a Tickets `nav-item` after the Diagnostics block (~line 21), copying its markup with `href="tickets"`, and **delete the placeholder comment on lines 22–23**.

### 6 — Registration

**File: `src/CrmTicketing.Client/Program.cs`** — after the `AddHttpClient<SystemApiClient>` call on lines 17–18:

```csharp
builder.Services.AddHttpClient<ITicketsApiClient, TicketsApiClient>(client =>
    client.BaseAddress = new Uri(apiBaseAddress));
builder.Services.AddScoped<TicketMetadataProvider>();
```

Two lines. **Change nothing else in this file** — the `apiBaseAddress` guard on lines 13–15 already covers the new client.

### 7 — No API change

`GET /api/tickets` gains no sort parameter, no free-text search, and no new filter. The endpoint was merged and verified in story 04; this story consumes it exactly as it stands. **If the list appears unordered during manual testing, that is issue #29, not a defect here.**

### 8 — The test project

**Create file: `tests/CrmTicketing.Client.Tests/CrmTicketing.Client.Tests.csproj`**

Copy `tests/CrmTicketing.Api.Tests/CrmTicketing.Api.Tests.csproj` and change the `ProjectReference` to `..\..\src\CrmTicketing.Client\CrmTicketing.Client.csproj`, adding `<PackageReference Include="bunit" />` to the package group. **No `Version` attribute on any reference.**

**File: `Directory.Packages.props`** — add to the `Label="Testing"` group:

```xml
<PackageVersion Include="bunit" Version="2.9.0" />
```

2.9.0 is the current `bunit` release on nuget.org, verified at planning time. It is the meta-package; `bunit.core` and `bunit.web` are the superseded 1.x split and must not be referenced.

**Justification for the new package (constitution §VII):** component rendering cannot be asserted without a renderer, and the alternative — asserting on hand-built `RenderTreeBuilder` output — is more code and less readable. bUnit is the standard choice for Blazor and adds no production dependency.

**File: `CrmTicketing.slnx`** — add to the `/tests/` folder:

```xml
<Project Path="tests/CrmTicketing.Client.Tests/CrmTicketing.Client.Tests.csproj" />
```

**No change to `.github/workflows/ci.yml`.** Its `Test` step runs `dotnet test CrmTicketing.slnx`, so a project in the solution is in CI by construction. The intake's acceptance criterion "added to the CI workflow's test run" is satisfied by the `.slnx` edit alone. If `ci.yml` appears in the diff, something unnecessary was changed.

---

## Edge Cases & Failure Modes

- **`OnInitializedAsync` instead of `OnParametersSetAsync`.** The page renders once, then every filter change updates the URL and leaves the table showing the previous result. Covered by test 6, which changes a filter and asserts a second request was issued.
- **Reload loop.** `OnParametersSetAsync` runs on every parameter set, including after `StateHasChanged`. Without the last-loaded guard in task 5 the page requests continuously. Symptom is a network tab filling up, not a visible defect.
- **Overlapping loads paint the wrong rows.** Rapid filter changes issue concurrent requests. A slow response for one filter can land *after* a fast response for a later filter, leaving the table showing rows that contradict the active selection. `OnParametersSetAsync` cancels and replaces a `CancellationTokenSource` on entry, passes its token down to the client, and a cancelled load must not write `_result`, `_error`, or `_loading`. Timing-dependent and intermittent, so it will not surface in manual testing. Note this is the opposite failure from the reload-loop guard above: that one suppresses redundant loads, this one orders the loads that do happen.
- **Empty filter sent as `status=`.** The API parses the empty string, fails to match a declared name, and returns **400** with `errors.Status`. The client must omit absent filters from the query string entirely, not send them empty.
- **The `errors` key casing is not stable and must never be matched on.** A bad filter in the query string keys the entry `status` (lower-case, after the action's parameter name); the same invalid value in a request *body* keys it `Status` (upper-case, after the DTO property). Read the first entry's first value positionally. Any code doing `errors["Status"]` works on one path and returns nothing on the other.
- **Hand-edited URL with a bad filter.** `/tickets?status=Frozen` reaches the API and returns 400. The page must render the **failed** state carrying the problem-details title, not crash and not show the empty state. This is the one path where a user sees a 400 they did not cause through the UI.
- **`page=0` or a negative page.** The API clamps to 1 (`TicketQuery.Create`) and the response reports `Page = 1`. Render paging from the **response**, never from the query-string parameter, or the "Showing X–Y" line disagrees with the rows.
- **A page past the end.** `/tickets?page=99` returns 200 with zero items and the true `TotalCount`. That is the **empty** state, not the failed state. Previous must still work.
- **Empty versus failed.** A filter matching nothing and an unreachable API must render different elements. Rendering "No tickets" for a connection failure tells the user their data is gone.
- **Metadata fails but the list succeeds.** Both calls are independent. If metadata throws, the filter selects stay disabled and the page still renders rows — do not blank the whole page because a dropdown could not populate.
- **Cached metadata failure.** `TicketMetadataProvider` holds a `Task`; a faulted task cached forever means the filters never populate for the app's lifetime. Clearing the field on failure is what makes a retry possible.
- **Non-JSON error body.** A 502 from a proxy returns HTML. `ReadFromJsonAsync<ApiProblem>` throws `JsonException`, which must be caught so the original status code is still reported.
- **`TicketSummaryResponse.Category` is nullable.** Rendering `@ticket.Category` for a null prints nothing and leaves a visually broken cell; render an em dash.
- **Guid columns.** `AssigneeId` and `RequesterId` resolve to no name because no user aggregate exists (#5, #6). Truncating a Guid to 8 characters is a placeholder, not an identifier a user can act on — do not add a lookup, and do not invent a display name.
- **bUnit 2.x renamed the 1.x types.** Test classes derive from **`BunitContext`**, not the 1.x `TestContext`. Two adjacent renames this story will hit: the `Fake*` doubles are now `Bunit*`, so the query-string work uses **`BunitNavigationManager`**, not `FakeNavigationManager`; and `IRenderedFragment`, `IRenderedComponentBase`, and `IRenderedMarkup` have collapsed into a single **`IRenderedComponent`**. Verified against the bUnit 1.x-to-2.x migration guide (https://bunit.dev/docs/migrations/1to2.html), not inferred from 1.x samples — any tutorial showing `TestContext` is 1.x and does not apply here.
- **bUnit and xunit v2.** `bunit` 2.9.0 must resolve against `xunit` 2.9.3 as pinned in `Directory.Packages.props`. If restore reports a missing xunit adapter, add the bUnit xunit integration package to `Directory.Packages.props` — **do not** bump or inline an xunit version to satisfy it.

---

## Test Plan

### 9 — Component tests

**Create file: `tests/CrmTicketing.Client.Tests/Pages/TicketsTests.cs`**

Build a `StubTicketsApiClient : ITicketsApiClient` holding canned responses, a recorded list of `(status, priority, page)` calls, and an optional exception to throw — hand-rolled, in the style of `FakeTicketRepository` in `tests/CrmTicketing.Api.Tests/Controllers/TicketsControllerTests.cs`. **No mocking library, no `HttpClient`, no API.** Register it and a `TicketMetadataProvider` over it in the bUnit test context's services.

1. `Rows_RenderFromTheClient` — three stubbed tickets produce three body rows, and the title, status, priority, and category of the first appear in the markup.
2. `EmptyState_RendersWhenThePageHasNoItems` — zero items with `TotalCount = 0` renders `alert-secondary` and **not** `alert-danger`, and no table.
3. `ErrorState_RendersWhenTheClientThrows` — the stub throws `ApiRequestException("The request was not valid.")`; the page renders `alert-danger` containing that message, **not** `alert-secondary`, and no table.
4. `ErrorState_DoesNotLeakDiagnostics` — the thrown exception's `ToString()` (stack trace) and a `traceId` value do not appear anywhere in the rendered markup.
5. `FilterControls_RenderTheOptionsFromMetadata` — the status select's options are exactly the metadata's `Statuses` plus one "All" option; likewise priorities. Assert against the stub's metadata, **not** against literal names.
6. `ChangingAFilter_IssuesARequestCarryingThatFilter` — change the status select, then assert the stub recorded a call whose status is the chosen value **and** whose page is 1.
7. `Paging_NextRequestsTheFollowingPage` — with `TotalCount = 60` at page 1, Next is enabled and requests page 2; Previous is disabled at page 1.
8. `Paging_NextIsDisabledOnTheLastPage` — `TotalCount = 10` with a page size of 25 leaves Next disabled.
9. `PagePastTheEnd_RendersEmptyNotError` — zero items with `TotalCount = 60` renders the empty state.
10. `MetadataFailure_StillRendersRows` — the metadata call throws while the list succeeds; rows render and the selects are disabled.

**Create file: `tests/CrmTicketing.Client.Tests/Services/TicketsApiClientTests.cs`**

The only tests in this story that exercise the real `TicketsApiClient`. The seam is a
stubbed `HttpMessageHandler` returning a canned `HttpResponseMessage`, so the transport
is faked and the class under test is the production one. Still no socket, no API, no
database.

**Use captured payloads, not invented ones.** Both bodies below were taken verbatim
from the live API on 30 August 2026. A hand-written fixture only proves the client
agrees with the plan's *guess* at the wire format; a captured one proves it agrees with
what the API actually emits.

`409 Conflict` — note that `from` and `to` are **top-level properties, not nested under
an `extensions` object**. ASP.NET Core flattens `ProblemDetails.Extensions` into the
root when it serialises. A test asserting `extensions.from` fails against the real API.
Note also that `title` is generic and carries no status names, and that there is **no
`detail` member** — so `ApiProblem.Detail` deserialises to null here:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The request conflicts with the current state of the ticket.",
  "status": 409,
  "from": "Closed",
  "to": "Open",
  "traceId": "00-df25504426db099d2c88553dfaae58f0-ea389e60daf998d3-00"
}
```

`400 Bad Request` from `GET /api/tickets?status=Frozen` — the page's most likely
user-visible error, and the reason `ApiProblem` carries `Errors`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "status": [
      "'Frozen' is not a recognised value."
    ]
  },
  "traceId": "00-60b99395e45fa860cabc135929420e97-cbf38d445ea91834-00"
}
```

`200 OK` from `GET /api/tickets?page=1&pageSize=2`:

```json
{
  "items": [
    {
      "id": "01a052c3-534d-7282-90db-c9c738ac4ad0",
      "title": "Second ticket",
      "status": "Open",
      "priority": "High",
      "category": "Billing",
      "requesterId": "11111111-1111-1111-1111-111111111111",
      "assigneeId": null,
      "createdAt": "2026-08-30T13:02:07.693297+00:00",
      "updatedAt": "2026-08-30T13:02:07.981166+00:00"
    },
    {
      "id": "01a04e63-7463-7f30-8477-3a033ba4261f",
      "title": "First real ticket",
      "status": "Closed",
      "priority": "High",
      "category": "Billing",
      "requesterId": "11111111-1111-1111-1111-111111111111",
      "assigneeId": null,
      "createdAt": "2026-08-29T16:38:55.84318+00:00",
      "updatedAt": "2026-08-29T16:42:38.727353+00:00"
    }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 2
}
```

Three things in that list body the tests must respect. `assigneeId` is null on both
rows, so this fixture exercises the `Unassigned` branch but **not** the truncated-Guid
branch — a test for that needs a row with an assignee, constructed in the test rather
than captured. The rows arrive newest-first, consistent with an
`OrderByDescending(CreatedAt)`, but that ordering is still unverified by any test
(issue #29) so **no assertion may depend on it** — match rows by `id`, never by
position. And `createdAt` here reads `...693297+00:00` where the same ticket's creation
response read `...6932972+00:00`: PostgreSQL stores microseconds, .NET holds ticks, so a
timestamp loses a digit crossing the persistence boundary. **Never assert equality
between a timestamp returned by a write and the same timestamp read back.**

11. `GetTicketsAsync_ThrowsWithTheProblemTitle_WhenTheApiRejectsTheRequest` — the handler
    returns 409 with the captured problem-details body. Assert the thrown
    `ApiRequestException` carries the body's `title` as its `Message` and `409` as its
    `StatusCode`, and that its `Message` contains neither `traceId` nor a stack frame.
12. `GetTicketsAsync_DeserialisesACapturedPagedResponse` — the handler returns 200 with
    the captured `PagedResponse<TicketSummaryResponse>` body. Assert `Items.Count`,
    `Page`, `PageSize`, and `TotalCount` all round-trip, and that the first item's
    `Title`, `Status`, and `Category` match the payload. This is what catches a
    property-name or casing mismatch against the live JSON, which the stub seam cannot.
13. `GetTicketsAsync_OmitsAbsentFilters` — call with `status: null, priority: null,
    page: 1` and assert the URI the handler received contains no `status=` and no
    `priority=`. Sending an empty filter is a 400 from the API, so this rule has to be
    pinned somewhere.
14. `GetTicketsAsync_PrefersTheValidationMessageOverTheGenericTitle` — the handler
    returns the captured 400 body. Assert the thrown `ApiRequestException.Message` is
    `"'Frozen' is not a recognised value."` and **not** `"One or more validation errors
    occurred."`. This is the assertion that keeps the page useful when a user edits the
    URL by hand.
15. `GetTicketsAsync_SurvivesANonJsonErrorBody` — the handler returns 502 with
    `text/html`. Assert an `ApiRequestException` still reports `502` rather than a
    `JsonException` escaping.

**Create file: `tests/CrmTicketing.Client.Tests/Services/TicketMetadataProviderTests.cs`**

16. `GetAsync_FetchesOnceAcrossMultipleCalls` — three sequential calls produce exactly one client call.
17. `GetAsync_RetriesAfterAFailure` — the first call throws, the second succeeds; the provider does not replay the cached failure.

### 10 — What is not tested here

18. **No test starts the API, opens a socket, or touches a database.** The layer this story does *not* cover is the real HTTP round trip between `TicketsApiClient` and a running API: tests 11–14 fake the transport, so a mismatch introduced by model binding, middleware, or serialiser configuration on the server would still pass. Issue #29 owns the missing integration-test host. **Do not cite #29 as cover for the client's own branching** — that is tests 11–14's job, and they are in scope here.

### 11 — Regression

19. All three existing test projects pass **unchanged**: `CrmTicketing.Domain.Tests`, `CrmTicketing.Api.Tests` (which contains `SystemControllerTests` and `TicketsControllerTests`), and `CrmTicketing.Infrastructure.Tests`. Three existing plus `Client.Tests` is **four** in the solution after this story. This story touches no `src/CrmTicketing.Api`, `Domain`, `Infrastructure`, or `Shared` file. If any of them appears in the diff, something moved that this story should not have moved.

---

## Verification Steps

1. **Backend and client build:** `dotnet build CrmTicketing.slnx` — zero warnings, zero errors under `TreatWarningsAsErrors`.
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — **four** test projects now (`Domain.Tests`, `Api.Tests`, `Infrastructure.Tests`, and the new `Client.Tests`), with no API and no database running.
3. **No component holds an `HttpClient`:** `grep -rn "HttpClient" src/CrmTicketing.Client/Pages/` returns no output.
4. **No workflow vocabulary is hardcoded:** `grep -rn "\"New\"\|\"Open\"\|\"Pending\"\|\"Resolved\"\|\"Closed\"\|\"Low\"\|\"Normal\"\|\"High\"\|\"Urgent\"" --include='*.razor' --include='*.cs' --exclude-dir=bin --exclude-dir=obj src/CrmTicketing.Client/` returns no output. **The `--exclude-dir` flags are load-bearing:** `obj/` holds the `.g.cs` files generated from every `.razor`, so without them this step can report the page's own markup back as a violation. Stories 02 and 04 both shipped a grep step that failed this way.
5. **The Client still references only `Shared`:** `grep -c ProjectReference src/CrmTicketing.Client/CrmTicketing.Client.csproj` returns `1`.
6. **The test project is in the solution:** `grep -c "CrmTicketing.Client.Tests" CrmTicketing.slnx` returns `1`.
7. **No inline package version:** `grep -c "Version=" tests/CrmTicketing.Client.Tests/CrmTicketing.Client.Tests.csproj` returns `0`.
8. **CI was not edited:** `git status --short .github/` returns no output.
9. **No server-side project was touched:** `git status --short src/CrmTicketing.Api src/CrmTicketing.Domain src/CrmTicketing.Infrastructure src/CrmTicketing.Shared` returns no output.
10. **Manual, with the API and PostgreSQL running:** start the API with `dotnet run --project src/CrmTicketing.Api --launch-profile https`, start the client with `dotnet run --project src/CrmTicketing.Client`, open `/tickets`. Confirm the ticket created earlier renders (there is **no seed data** - issue #4 is still open, so `POST /api/tickets` one first if the table is empty); that selecting a status changes the URL and the rows; that `?status=Frozen` renders the failed state; and that the browser back button restores the previous filter.

---

## Done Criteria

- [ ] `/tickets` renders a table from `GET /api/tickets`, and `NavMenu` has a Tickets entry with the placeholder comment removed.
- [ ] All API access goes through `ITicketsApiClient`; no component injects `HttpClient`.
- [ ] Status and priority selects are populated from `GET /api/tickets/metadata`; no status or priority name is a literal anywhere in the Client.
- [ ] Filter and page state lives in the query string; component fields hold no filter state.
- [ ] Loading, rows, empty, and failed are four distinct renderings, each covered by a test.
- [ ] A failed request shows the validation message when the response carries one, the problem-details title otherwise; no stack trace and no `traceId` reach the markup.
- [ ] Only `TicketSummaryResponse` fields are rendered; no per-row call to `GET /api/tickets/{id}`.
- [ ] Paging is driven by `PagedResponse<T>.TotalCount`; a page past the end shows the empty state.
- [ ] `tests/CrmTicketing.Client.Tests` exists, uses bUnit, and is in `CrmTicketing.slnx`; `bunit` is pinned in `Directory.Packages.props`.
- [ ] `TicketsApiClient` is tested directly over a stubbed `HttpMessageHandler` using payloads captured from the live API, not only through the `ITicketsApiClient` stub.
- [ ] Overlapping loads are ordered by a `CancellationTokenSource`; a cancelled load writes no component state.
- [ ] `.github/workflows/ci.yml` is unchanged.
- [ ] No file under `src/CrmTicketing.Api`, `Domain`, `Infrastructure`, or `Shared` is modified.
- [ ] `dotnet build` clean; `dotnet test` passes with no API and no database.
- [ ] Overview `00-overview.md` created and `00-index.md` updated with the new feature.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 06 (issue #13, ticket detail view).**
