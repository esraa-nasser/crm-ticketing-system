# Story intake

- Folder: `.squad/stories/ticketing-ui/16/intake.md`

---

## Feature

- **Feature name (display):** Ticketing UI — permission-gated controls
- **Feature slug (folder under `plans/`):** `ticketing-ui`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `16`
- **Work item type:** `Task`
- **Status:** `In progress`
- **Assignee:** `esraa-nasser`
- **Labels:** `area:client`, `sdd:needs-plan`

---

## Title

```
Show each role only the controls its role can actually use
```

---

## Description

```
Two merged stories have now deferred role-gating to this one. Story 08 shipped a
detail view that renders "Assign to me" to a Customer and lets them click it, on
the stated principle that making the refusal legible came first and hiding the
control came later. Story 09 shipped an internal/public toggle that is already
hidden from Customers - one `IsStaff` check, written inline, in one component.

That is the third place role logic has appeared, which is the point at which the
constitution stops arguing for inlining it (§VII). This story collects it.

The story is deliberately narrow: it changes what the browser draws and nothing
else. Every authorisation rule stays exactly where story 06 put it, in the API
and in the repository. A hidden button is a courtesy, not a defence - and the
easiest way to turn a working system into a broken one is to hide a control and
quietly conclude the server no longer has to refuse it.
```

---

## Acceptance criteria

```
- [ ] One place decides. A single service in the Client - the plan names it -
      answers capability questions ("may this caller assign a ticket to
      themselves?"), reading roles from TokenStore. Components ask the capability;
      no component tests a role name.
      Verify: grep -rnE '"Agent"|"Admin"|"Customer"|IsStaff' --include='*.razor'
              --include='*.cs' --exclude-dir=bin --exclude-dir=obj
              src/CrmTicketing.Client/ -> matches only inside that one service.
- [ ] The gate matches the API, rule for rule. The plan carries a table with one
      row per gated control: the control, the client capability, the API rule that
      refuses it, and a file:line for that rule read from the API source - not from
      this intake and not from memory. A control whose API rule cannot be located
      is not gated in this story; it is reported as a finding.
- [ ] Nothing is removed from the API, the repository, or TicketAccess.
      Verify: git diff on the merge base touches no file under
              src/CrmTicketing.Api/, src/CrmTicketing.Infrastructure/, or
              src/CrmTicketing.Domain/.
- [ ] A test proves the server still refuses. For at least one control this story
      hides, an existing API-level test asserting the refusal is named in the plan
      and still passes unchanged. If no such test exists for that control, the
      story adds one before hiding the button.
- [ ] Hidden, not disabled, when the role can never use it: a Customer does not
      see "Assign to me", "Unassign", or the internal/public toggle at all. A
      disabled control invites a hover, a click, and a support question; an absent
      one says the feature is not theirs.
- [ ] State-based unavailability is untouched. A Closed ticket already offers no
      transitions because the metadata map returns an empty list, and this story
      does not turn that into a role check or a disabled button. Role decides
      whether a control exists; state decides whether it is offered.
- [ ] The internal/public toggle keeps working exactly as story 09 shipped it -
      same behaviour, same tests passing - but the `IsStaff` check moves into the
      capability service. This is a refactor with no visible change, and its test
      is that story 09's component tests pass without being edited.
- [ ] Nothing is gated on the client alone. Every capability the service answers
      corresponds to a rule the API already enforces. A capability with no API
      counterpart is out of scope, whatever it would improve.
- [ ] Signed out, everything collapses safely. With an empty TokenStore every
      capability answers false, and no gated control renders. A capability must
      never default to permitted because roles were not loaded yet.
      Verify: a test constructs the service over a cleared TokenStore and asserts
              every capability is false.
- [ ] Component tests cover, for the detail view, each role in turn: a Customer
      sees no assignment controls and no internal toggle; an Agent sees both; and
      the same ticket, same stub, differing only by the roles in TokenStore.
- [ ] A code-reading pass, recorded in the PR body: every file this story touches
      is read for behaviour the plan never described, and what was found (or that
      nothing was) is written down. Four defects have reached or nearly reached
      main that neither specification review nor stubbed tests could catch
      (docs/status.md); this is the remedy that story records.
- [ ] Verified in a browser, not only in tests, as both roles: sign in as
      customer@example.com and as agent@example.com and confirm the detail view
      of the same ticket differs as specified.
- [ ] dotnet build CrmTicketing.slnx is clean under TreatWarningsAsErrors.
- [ ] dotnet test CrmTicketing.slnx passes with no API and no database.
```

---

## Attachments

None.

---

## Dependencies

- **Blocked by / related ids:** #13 (detail view) and #11 (comments) — both merged, and both explicitly deferred their role-gating here. #5 and #6 (identity) merged. Closes #16. Unblocks #46 (the unassign rule), which cannot be finished while the button it argues about is rendered to everyone.
- **Depends on code areas or other stories:** `TokenStore` (already carries `UserId` and `Roles`), `TicketDetail.razor`, the comment composer from story 09, `TicketAccess`, and the API's authorisation attributes and role checks.

## Extra notes

- **#46 is not settled here, and this story must not settle it by accident.** #46 asks whether staff may unassign anyone or only themselves. That is an API rule; this story renders whatever the API currently does. The plan reads the current rule, gates to it, and says so in the table — and if the current rule turns out to be "anyone", hiding the button from Customers is still correct and #46 remains open.
- **TokenStore may already carry `IsStaff`.** Story 09 added `IsStaff` to `SignInResponse`. Whether it also reached `TokenStore` is a fact to check, not to assume: if it is there, the capability service reads it rather than re-deriving staffness from role names; if it is not, deriving it in one place is exactly what this story is for.
- The capability service is a Client concern only. It is not in `Shared`, because a contract that told the client what it may do would be a second authorisation table.

## Technical hints

```
DECISIONS MADE

Hide, do not disable, when the answer is never. A disabled button is the right
control when the same user could enable it by doing something - filling a field,
waiting for a save. A Customer will never be able to assign a ticket to
themselves, and showing them a permanently dead control is a worse explanation
than showing them nothing. Where a control is unavailable because of the ticket's
state rather than the caller's role, the existing behaviour already handles it
and is not touched.

Capabilities, not roles, at every call site. Components ask "may I?" and never
"which role is this?". The reason is not tidiness: role names are strings, and a
string comparison spread across seven components is seven places to mistype
"Agent" and seven places to update when a fourth role appears. One service, one
list of capabilities, and a grep that proves it.

The API keeps every rule it has. This story adds a display filter in front of
rules that already exist; it removes none of them and weakens none of them. The
acceptance criteria make that checkable by diff, deliberately, because "the UI
already prevents that" is the sentence that precedes most authorisation bugs.

Deny by default when roles are unknown. The service answers false for an empty
TokenStore rather than true, and the test says so. A permissions check that fails
open during a load is worse than no check, because it looks like one.

The internal toggle moves without changing. Story 09's behaviour is already
correct; this story relocates its one inline check and proves the relocation by
leaving story 09's tests untouched and passing. A refactor whose tests had to be
edited is not a refactor.

No new roles, no new endpoints, no permissions screen. Three roles exist. Whether
an Admin can do anything an Agent cannot is a question this story only answers by
reading what the API already does - not by deciding it.
```

## Out of scope

```
- No change to any authorisation rule, anywhere. No API change, no repository
  change, no domain change, no new attribute, no relaxed check.
- No resolution of #46 (may staff unassign anyone, or only themselves). This
  story renders the rule that exists.
- No user management UI. Story 06 added POST /api/auth/users with no screen, and
  giving it one is its own story with its own authorisation question.
- No new roles, no role assignment UI, no per-user permissions, no permission
  matrix screen.
- No route guarding beyond what exists. A 401 already sends the user to /signin;
  this story does not add per-route role attributes or a redirect for a page a
  role may not see, because no such page exists yet.
- No change to the ticket list view. If the list grows role-specific controls
  later, they use this service; adding them is not this story.
- No hiding of data. This story hides controls, not fields. What a Customer may
  read is already decided in the repository, and duplicating that decision in the
  browser would create a second answer to the same question.
- No end-to-end test (#45). This story's browser check is manual and recorded in
  the PR, exactly as the last four stories' were.
```
