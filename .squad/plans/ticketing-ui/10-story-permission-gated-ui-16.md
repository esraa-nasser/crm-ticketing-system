# Story 10 — Show each role only the controls its role can actually use (Story: 16)

## Prerequisites

- Story 06 completed: [`../auth-roles/06-story-identity-and-authorisation-5.md`](../auth-roles/06-story-identity-and-authorisation-5.md) — every authorisation rule this story renders against was written there. **None of them changes.**
- Story 08 completed: [`08-story-ticket-detail-13.md`](08-story-ticket-detail-13.md) — `TicketDetail.razor`, and the explicit deferral: *"Hiding the button is issue #16 and explicitly follows this story."*
- Story 09 completed: [`../ticketing-core/09-story-ticket-comments-11.md`](../ticketing-core/09-story-ticket-comments-11.md) — `TicketComments.razor`, `TokenStore.IsStaff`, and the inline `IsStaff` check this story relocates.
- **No API, no database, and no migration.** Build and test need neither. The manual verification step needs both plus seeded demo data.

---

## Story Goal

Collect role logic into one place and draw only the controls a role can use.

1. **One service decides.** `Capabilities` answers "may this caller?" from `TokenStore`. No component tests a role name or reads `IsStaff`.
2. **The gate matches the API, rule for rule.** Every capability corresponds to a rule that already exists in API source, cited below with a `file:line` read from that source.
3. **Hidden, not disabled**, when the role can never use the control.
4. **State-based unavailability is untouched.** A `Closed` ticket offers no transitions because the metadata map is empty; that stays exactly as it is.
5. **Nothing is removed from the API, the repository, or the domain.** This story adds a display filter in front of rules that keep working.

**A hidden button is a courtesy, not a defence.** The server refuses every gated action whether or not the browser drew it, and this story's tests say so.

---

## Context — Read These Files First

1. `src/CrmTicketing.Api/Controllers/TicketsController.cs` — **read for rules, never to change them.** `[Authorize(Policy = AuthorizationPolicies.StaffOnly)]` on `Assign` (line 263); the requester transition check (lines 246–248); `RequesterAllowedTransitions` (lines 334–341) and its `<remarks>`; the staff-only requester override in `Create` (line 50).
2. `src/CrmTicketing.Api/Controllers/TicketCommentsController.cs` — the internal-comment refusal, `if (request.IsInternal && !User.IsStaff()) return Forbid();` (lines 61–63), and the comment above it stating the rendering is a hint and this is the enforcement.
3. `src/CrmTicketing.Api/Configuration/AuthorizationPolicies.cs` — all 21 lines. `StaffOnly` is `Admin` or `Agent` (line 19), sourced from `RoleNames`.
4. `src/CrmTicketing.Api/Infrastructure/CallerContext.cs` — all 73 lines. `IsStaff()` (lines 52–57) is the server's single declaration of staffness. **The client cannot reference it** — §II forbids the edge — which is why `SignInResponse.IsStaff` exists.
5. `src/CrmTicketing.Client/Services/TokenStore.cs` — all 68 lines. `IsStaff` (line 42) **is already present**, set in `Set` (line 57) and cleared in `Clear` (line 66). Its `<remarks>` already says it is a display hint and never an authorisation decision. The intake asked this to be checked rather than assumed: **it is there, so the capability service reads it and derives nothing.**
6. `src/CrmTicketing.Client/Pages/TicketDetail.razor` — all 396 lines. `CanAssignToMe` (line 185), `ShowAssignToMe` (lines 188–189), `ShowUnassign` (lines 196–197), and **the fallback span at lines 104–107** — see task 3, it is the one non-obvious part of this story.
7. `src/CrmTicketing.Client/Components/TicketComments.razor` — all 264 lines. The `@if (Tokens.IsStaff)` toggle at line 75 and the comment under it. **This check moves; its behaviour does not.**
8. `src/CrmTicketing.Client/Program.cs` — all 39 lines. `AddSingleton<TokenStore>()` (line 26) and the comment explaining why it is not scoped. The new service registers beside it.
9. `tests/CrmTicketing.Client.Tests/Services/ClientCompositionTests.cs` — replicates `Program.cs` and asserts reference equality of the resolved `TokenStore`. Task 6 extends it.
10. `tests/CrmTicketing.Client.Tests/Pages/TicketDetailTests.cs` — all 433 lines. `Arrange` (~line 143) constructs a `TokenStore` with `isStaff: true`; the role-matrix tests parameterise it.
11. `tests/CrmTicketing.Client.Tests/Components/TicketCommentsTests.cs` — `Toggle_IsHiddenForANonStaffUser` and `Toggle_IsShownForStaff`. **These must pass unedited.** A refactor whose tests had to change is not a refactor.
12. `docs/constitution.md` — §II (line 23) the layer graph, §VII (line 86) three strikes before abstraction.

---

## Product rules (from story)

| Control | Today | After this story |
|---|---|---|
| "Assign to me" | Rendered to anyone signed in with a known id | **Staff only.** Absent for a Customer. |
| "Unassign" | Same | **Staff only.** Absent for a Customer. |
| "Assigned to someone else." | Renders whenever both buttons are hidden | **Staff only** — see task 3. |
| Internal/public toggle | Hidden by an inline `Tokens.IsStaff` in the component | Same behaviour, decided by `Capabilities` |
| Transition buttons | Rendered from the metadata map | **Unchanged.** See the finding below. |
| Ticket fields and the thread | Filtered server-side | **Unchanged.** This story hides controls, not data. |

---

## Findings — read before implementing

The intake requires that a control whose API rule cannot be located is reported rather than gated. Two things came out of the discovery pass.

### Finding 1 — transition buttons are not gated in this story

The rule exists and is locatable: `TicketsController.RequesterAllowedTransitions`, **lines 334–341**, five `(from, to)` pairs. So the criterion "the API rule cannot be located" does not apply.

**Gating it is still out of scope, for a different and stronger reason.** The client cannot learn those pairs. `GET /api/tickets/metadata` publishes `TicketStatusTransitions` — what is legal *for anyone* — and deliberately not what is legal *for this caller*; `RequesterAllowedTransitions` is an API-boundary authorisation rule with no endpoint. Gating the buttons would need one of:

- **duplicating the five pairs in the Client** — a second declaration of an authorisation rule, which is what this story exists to prevent, and which §II compounds because the Client cannot reference the first; or
- **extending the metadata endpoint** to publish per-caller transitions — an API change, and the intake's out-of-scope list forbids one.

So story 08's behaviour stands: a Customer sees transition buttons the API may refuse, and the 403 message renders. That was correct then and is still correct. It is the only route that gates these buttons without a second rule table.

**Open the follow-up issue — "Publish per-caller transitions from the metadata endpoint" — before implementation begins.** Its number is then written into this plan as the single authorised revision, and the plan is frozen thereafter. Recording it *during* implementation would mean editing a plan that `squad-plan.md` requires to be read-only once implementation starts.

Follow-up issue: **#51** — "Publish per-caller transitions from the metadata endpoint". Opened before implementation began; this line is the single authorised revision to this plan, and the plan is read-only from here.

### Finding 2 — the criterion's grep can never return nothing, and the count is six, not three

The criterion's command, unfiltered, is:

```bash
grep -rnE '"Agent"|"Admin"|"Customer"|IsStaff' --include='*.razor' --include='*.cs' \
  --exclude-dir=bin --exclude-dir=obj src/CrmTicketing.Client/
```

Run on `main` today it returns **five** matches. Enumerated below, so the number is the length of a list rather than a claim:

| `file:line` | Line | Fate |
|---|---|---|
| `Services/TokenStore.cs:42` | `public bool IsStaff { get; private set; }` | Survives — declares the property |
| `Services/TokenStore.cs:57` | `IsStaff = isStaff;` | Survives — `Set` writing its own field |
| `Services/TokenStore.cs:66` | `IsStaff = false;` | Survives — `Clear` writing its own field |
| `Pages/SignIn.razor:72` | `response.IsStaff);` | Survives — copies the response into the store |
| `Components/TicketComments.razor:75` | `@if (Tokens.IsStaff)` | **Removed** — the line story 10 relocates |

After this story the same command returns **six**: those four survivors, plus the two reads of `tokens.IsStaff` inside the new `Capabilities.cs`. The count goes *up*, and that is correct — the entire purpose of the service is to be the one place `IsStaff` is read in order to decide something.

So the criterion's wording, "matches only inside that one service", can never pass as literally written: the store must declare the field and the sign-in page must populate it. **The intent is that no component decides what to render from a role name or from `IsStaff` directly.** Verification step 3 encodes that intent as the *filtered* command, which returns exactly one line today and none after. This finding is why it differs from the intake's wording. Do not edit the intake.

---

## The gate table

One row per gated control. Every `file:line` was read from API source during planning, not from the intake.

| Control | Client capability | API rule that refuses it | `file:line` | Existing test that pins the refusal |
|---|---|---|---|---|
| "Assign to me" | `CanAssignTickets` | `[Authorize(Policy = AuthorizationPolicies.StaffOnly)]` on `Assign` | `src/CrmTicketing.Api/Controllers/TicketsController.cs:263` | `Assign_IsRestrictedToStaffByPolicy` — `tests/CrmTicketing.Api.Tests/Controllers/TicketsControllerTests.cs:610` |
| "Unassign" | `CanAssignTickets` | The same action — one route serves both; `AssignTicketRequest.AssigneeId` null means unassign | `src/CrmTicketing.Api/Controllers/TicketsController.cs:263` | as above |
| Internal/public toggle | `CanWriteInternalComments` | `if (request.IsInternal && !User.IsStaff()) return Forbid();` | `src/CrmTicketing.Api/Controllers/TicketCommentsController.cs:61–63` | `Post_AsACustomer_WithIsInternalTrue_ReturnsForbidAndStoresNothing` — `tests/CrmTicketing.Api.Tests/Controllers/TicketCommentsControllerTests.cs:204` |

**Both refusal tests already exist and pass**, so the intake's "adds one before hiding the button" clause does not trigger. Neither may be edited by this story; verification step 5 runs them.

`StaffOnly` resolves to `Admin` or `Agent` (`AuthorizationPolicies.cs:19`), which is the same pair `CallerContext.IsStaff()` uses (`CallerContext.cs:56`) and the same pair `AuthController` reports as `SignInResponse.IsStaff`. One definition, three readers, and the client gets the answer rather than the rule.

**#46 is not settled here.** The current rule is that `Assign` is staff-only and the *screen* offers `Unassign` only when the ticket is assigned to the caller — a story-08 UI decision, not an API rule. This story gates on `CanAssignTickets` and leaves the `AssigneeId == Tokens.UserId` condition exactly as it is. If #46 later decides staff may unassign anyone, that is a change to this screen's condition, not to a capability.

---

## Implementation tasks

### 1 — The capability service

**Create file: `src/CrmTicketing.Client/Services/Capabilities.cs`**

```csharp
/// <summary>
/// What the signed-in caller may do, as the browser understands it.
/// </summary>
/// <remarks>
/// A display filter, never an authorisation decision. Every capability here mirrors
/// a rule the API already enforces and would still enforce if this class returned
/// true for everything - see the gate table in the story-10 plan. The API is the
/// defence; this is a courtesy that stops the UI offering actions it knows will fail.
///
/// Components ask a capability and never read a role name: role names are strings,
/// and one string comparison per component is one place per component to mistype
/// "Agent" and one more to update when a fourth role appears.
/// </remarks>
public sealed class Capabilities(TokenStore tokens)
{
    /// <summary>
    /// Whether the caller may assign or unassign a ticket. Mirrors the StaffOnly
    /// policy on POST /api/tickets/{id}/assignee.
    /// </summary>
    public bool CanAssignTickets => tokens.IsSignedIn && tokens.IsStaff;

    /// <summary>
    /// Whether the caller may write a staff-only comment. Mirrors the IsInternal
    /// refusal in TicketCommentsController.
    /// </summary>
    public bool CanWriteInternalComments => tokens.IsSignedIn && tokens.IsStaff;
}
```

**Both read `IsSignedIn` as well as `IsStaff`, and that is not redundant.** `Clear()` resets `IsStaff` to false, so the two agree today — but a capability that depends on one field is one refactor away from answering true for a signed-out caller whose flag was never reset. Deny by default is the property being pinned, not an implementation detail.

**Two capabilities, not five.** Only controls with an API counterpart appear. There is no `CanChooseRequester` (the create form has no requester field — story 08 removed it; the on-behalf-of case is #43) and no `CanTransition` (finding 1).

**They currently return the same value.** Do not collapse them into one `IsStaff` property: they mirror two independently-declared API rules, and the day one of those rules changes — #46 is a live candidate — a merged property would have to be split under time pressure at the call sites.

**Not in `Shared`.** A contract telling the client what it may do would be a second authorisation table.

**File: `src/CrmTicketing.Client/Program.cs`** — register beside `TokenStore` at line 26:

```csharp
// Singleton, matching TokenStore. It holds no state of its own and reads a
// singleton, so the two lifetimes cannot diverge - the failure story 08 found when
// a scoped TokenStore gave BearerTokenHandler a different instance than the page.
builder.Services.AddSingleton<Capabilities>();
```

### 2 — The comment toggle moves

**File: `src/CrmTicketing.Client/Components/TicketComments.razor`**

Inject `Capabilities` beside the existing injections and change **line 75** from `@if (Tokens.IsStaff)` to `@if (Can.CanWriteInternalComments)`. Keep the explanatory comment beneath it — it is still true and still the point.

**Remove the `@inject TokenStore Tokens` line (line 2).** Checked during planning, not assumed: `Tokens` appears exactly twice in this file — the injection on line 2 and the read on line 75 — so once line 75 asks `Capabilities` instead, the injection has no remaining use.

**Nothing else in this file changes.** No markup, no ids, no behaviour. `Toggle_IsHiddenForANonStaffUser` and `Toggle_IsShownForStaff` must pass **without being edited** — that is the test of the refactor.

### 3 — The detail view's assignment section

**File: `src/CrmTicketing.Client/Pages/TicketDetail.razor`**

Inject `Capabilities`. Change `CanAssignToMe` (line 185):

```csharp
// Role first: a Customer never assigns, whatever the ticket's state. The id check
// stays because "Assign to me" needs an id to send.
private bool CanAssign => Can.CanAssignTickets && Tokens.UserId != Guid.Empty;
```

`ShowAssignToMe` and `ShowUnassign` keep their existing `AssigneeId` conditions and read `CanAssign` in place of `CanAssignToMe`. **The `AssigneeId` comparisons are state, not role** — they stay exactly as story 08 wrote them, including the self-service `Unassign` rule #46 is about.

**The fallback span at lines 104–107 must also be gated, and this is the one part that is easy to get wrong.** It renders whenever both buttons are hidden. Once a Customer hides both, that Customer would see *"Assigned to someone else."* — text about an assignment workflow they have no part in, and which is false whenever the ticket is unassigned. Wrap the whole assignment block:

```razor
@if (Can.CanAssignTickets)
{
    <h2 class="h5">Assignment</h2>
    <div class="d-flex gap-2 mb-3">
        @* buttons and the fallback span, unchanged inside *@
    </div>
}
```

The heading goes inside. A Customer sees no "Assignment" section at all, rather than a heading over an empty div — the intake's "an absent one says the feature is not theirs".

**Do not touch** `AvailableTransitions` (line 178) or the transitions block (lines 66–82). Finding 1.

### 4 — Nothing else in the Client changes

`Tickets.razor`, `TicketCreate.razor`, `SignIn.razor`, and `NavMenu.razor` are untouched. The list has no role-specific controls; adding them is explicitly not this story.

### 5 — No API, Infrastructure, or Domain change

Not one line. Verification step 6 is a `git diff` that proves it.

---

## Edge Cases & Failure Modes

- **Signed out entirely.** `TokenStore` cleared: `IsSignedIn` false, `IsStaff` false. Every capability answers false and no gated control renders. Test 1 constructs the service over a cleared store and asserts every capability, by reflection over the public properties, so a capability added later is covered without editing the test.
- **Signed in, `IsStaff` false, roles empty.** A token issued to an account with no role. Treated as a Customer — less capability, not more, matching `CallerContext.Access()`'s own default.
- **`IsStaff` true but `UserId` empty.** Cannot happen through `Set`, but `CanAssign` still requires a non-empty id because "Assign to me" has nothing to send otherwise. The button hides; it does not send `Guid.Empty`.
- **A stale `IsStaff` after a role change.** The token outlives the change, so an ex-Agent still sees the controls and the API answers 403 — the same 60-minute window story 08 accepted. Correct behaviour: the browser is wrong and the server is right, which is the ordering this story preserves. The reverse — newly promoted, controls missing until re-sign-in — is the same window.
- **A Customer on their own ticket.** Sees the thread, the public composer, the edit form, and the transition buttons; no assignment section and no internal toggle. **The edit form is deliberately not gated:** `PATCH /api/tickets/{id}` carries no staff policy (`TicketsController.cs:161`), so a requester editing their own ticket is a rule the API permits, and hiding it would be a client-only gate the intake forbids.
- **A Customer viewing a `Closed` ticket.** No transition buttons — because `Transitions["Closed"]` is empty, not because of a role check. Test 7 asserts the empty-map path still drives it.
- **An Agent viewing a ticket assigned to a colleague.** Sees "Assign to me" and the *"Assigned to someone else."* span, exactly as before. #46 owns whether that is right.
- **`Capabilities` resolved in a different DI scope than `TokenStore`.** The failure story 08 found. Both are singletons and test 3 pins it, so a later change to either lifetime fails a test rather than silently disabling every control.
- **A component reading `Tokens.IsStaff` directly after this story.** Verification step 3's grep fails. That is the regression the step exists to catch, and it is the fourth-strike scenario §VII warns about.

---

## Test Plan

### Client — `tests/CrmTicketing.Client.Tests/`

**Create file: `Services/CapabilitiesTests.cs`**

1. `EveryCapability_IsFalse_OverAClearedStore` — construct over a `new TokenStore()`, then **reflect over every public `bool` property** and assert all false. Written by reflection deliberately: a capability added later is covered without anyone remembering to extend this test. **This is the intake's named verification for deny-by-default.**
2. `EveryCapability_IsFalse_AfterClear` — `Set(...)` with staff roles, then `Clear()`, then the same reflection sweep. Signing out is the path a real session takes.
3. `Capabilities_AreTrueForStaff` — `Set(..., isStaff: true)`; both capabilities true. Without this, test 1 passes for a service that answers false unconditionally.
4. `Capabilities_AreFalseForACustomer` — `Set(..., ["Customer"], isStaff: false)`; both false.
5. `Capabilities_IgnoreRoleNames` — `Set(..., ["Agent"], isStaff: false)`. Deliberately contradictory: the flag decides, because the grouping is the server's. Both capabilities false. **This pins that the service reads `IsStaff` rather than re-deriving staffness from role strings** — the thing story 09's `TokenStore` comment argues for and which nothing currently tests.

**File: `Services/ClientCompositionTests.cs`** — extend:

6. `Capabilities_ShareTheTokenStoreTheComponentsGet` — resolve `Capabilities` and `TokenStore` from the provider, `Set` on the store, and assert the capability flips. Reference equality via behaviour, matching the existing test's approach. Story 08's defect was exactly this shape.

**File: `Pages/TicketDetailTests.cs`** — extend. `Arrange` gains a `bool isStaff = true` parameter threaded into `tokens.Set(...)`, and `Services.AddSingleton(new Capabilities(tokens))`. **Existing tests keep their current behaviour** because the default stays `true`.

7. `Detail_AsACustomer_ShowsNoAssignmentSection` — the whole block absent: no "Assign to me", no "Unassign", **and no "Assigned to someone else."**. The third assertion is the one that catches the fallback span.
8. `Detail_AsStaff_ShowsTheAssignmentSection` — the mirror. Same ticket, same stub, differing only by `isStaff`. **The intake's named matrix requirement.**
9. `Detail_AsACustomer_StillShowsTransitions` — finding 1 made visible: a Customer still sees the buttons the metadata map offers. If a later story gates them, this test is the one that should fail and be deliberately changed.
10. `Detail_AsACustomer_StillShowsTheEditForm` — the edit form is not staff-gated, because `PATCH` is not.
11. `ClosedTicket_OffersNoTransitions_ForEitherRole` — `[Theory]` over both roles; the empty map drives it, not a role check. Pins "state decides whether it is offered".

**File: `Components/TicketCommentsTests.cs`** — `Arrange` registers a `Capabilities` over the same `TokenStore` it already builds. **`Toggle_IsHiddenForANonStaffUser` and `Toggle_IsShownForStaff` are not edited** — only the fixture gains a registration. If either assertion needs changing, the refactor was not one.

### What is not tested

12. **No test proves the API still refuses what the UI now hides** *in this story's own files* — that is what the two existing tests in the gate table do, and verification step 5 runs them by name. This story's tests assert the browser draws less; those assert the server still refuses. Neither substitutes for the other, and the pairing is the whole argument that hiding a control is safe.
13. **No end-to-end test.** #45, unchanged. Verification step 8 is manual.

---

## Verification Steps

1. **Build:** `dotnet build CrmTicketing.slnx` — zero warnings, zero errors under `TreatWarningsAsErrors`.
2. **Tests:** `dotnet test CrmTicketing.slnx` — four projects, no API and no database.
3. **No component decides from a role name or `IsStaff`:**

    ```bash
    grep -rnE '"Agent"|"Admin"|"Customer"|IsStaff' --include='*.razor' --include='*.cs' \
      --exclude-dir=bin --exclude-dir=obj src/CrmTicketing.Client/ \
      | grep -v 'Services/TokenStore.cs\|Pages/SignIn.razor\|Services/Capabilities.cs'
    ```

    Returns **no output**. The three exclusions are the only legitimate readers: the store declaring its own field, the sign-in page populating it from the response, and the one service the criterion permits — see finding 2.

    **Run this before starting.** It returns exactly one line today:

    ```
    src/CrmTicketing.Client/Components/TicketComments.razor:75:        @if (Tokens.IsStaff)
    ```

    That is the third strike this story collects. One match before, zero after, is the whole check — it needs no eyeballing and cannot pass by accident.
4. **Capabilities are asked, not roles:** `grep -rn "Can\." --include='*.razor' src/CrmTicketing.Client/` shows the call sites; each names a capability, none names a role.
5. **The server still refuses, by name:**

    ```bash
    dotnet test tests/CrmTicketing.Api.Tests --filter "FullyQualifiedName~Assign_IsRestrictedToStaffByPolicy|FullyQualifiedName~Post_AsACustomer_WithIsInternalTrue_ReturnsForbidAndStoresNothing"
    ```

    Both pass, **unedited**. `git diff main...HEAD -- tests/CrmTicketing.Api.Tests/Controllers/TicketsControllerTests.cs tests/CrmTicketing.Api.Tests/Controllers/TicketCommentsControllerTests.cs` returns nothing. Three dots, not two: a two-dot diff compares against main's *tip*, so anything merged to main after this branch was cut would be reported as this story's change.
6. **Nothing outside the Client changed:**

    ```bash
    git diff --stat main...HEAD -- src/CrmTicketing.Api src/CrmTicketing.Infrastructure src/CrmTicketing.Domain src/CrmTicketing.Shared
    ```

    Returns no output. **The intake's named check.**
7. **No migration:** `git status --short src/CrmTicketing.Infrastructure/Persistence/Migrations/` returns no output.
8. **Manual, both roles, same ticket, with the API and client running against seeded demo data.** Open "Laptop will not charge" as `agent@example.com`: assignment section present, internal toggle present. Sign out, sign in as `customer@example.com`, open the same ticket: **no assignment section, no "Assigned to someone else.", no internal toggle**, and the comment composer, the edit form, and the transition buttons all still present. Then click a transition the API refuses and confirm the 403 message still renders — the proof that hiding controls did not become the enforcement. **Report what each role saw, not that it passed.**
9. **Code-reading pass, recorded in the PR body.** Every file this story touches, read for behaviour the plan never described. Write down what was found, or that nothing was. Four defects have reached or nearly reached `main` that neither specification review nor stubbed tests caught (`docs/status.md`); this is the remedy that story names.

---

## Done Criteria

- [ ] One `Capabilities` service answers every capability question from `TokenStore`; no component reads a role name or `IsStaff`.
- [ ] Every capability corresponds to a rule in the gate table, each with a `file:line` read from API source.
- [ ] A Customer sees no "Assign to me", no "Unassign", no "Assigned to someone else.", and no internal/public toggle.
- [ ] An Agent sees all of them, on the same ticket with the same stub.
- [ ] Controls are hidden, not disabled.
- [ ] State-based unavailability is untouched: a `Closed` ticket offers no transitions because the map is empty.
- [ ] Story 09's `Toggle_IsHiddenForANonStaffUser` and `Toggle_IsShownForStaff` pass **without being edited**.
- [ ] Every capability answers false over a cleared `TokenStore`, proven by a reflection sweep.
- [ ] The two named API refusal tests pass unedited.
- [ ] No file under `src/CrmTicketing.Api/`, `src/CrmTicketing.Infrastructure/`, `src/CrmTicketing.Domain/`, or `src/CrmTicketing.Shared/` changes.
- [ ] Transition buttons are **not** gated; finding 1 is recorded, and the follow-up issue was opened *before* implementation began and its number written into finding 1 as the single authorised revision.
- [ ] Finding 2 is recorded; the intake is not edited.
- [ ] The code-reading pass and the two-role browser check appear in the PR body.
- [ ] `dotnet build` clean; `dotnet test` passes with no API and no database.
- [ ] `00-overview.md` updated with this story.

**STOP HERE. Report to the user and wait for confirmation before proceeding to the next story.**
