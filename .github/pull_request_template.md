## Plan

<!-- Link the SquadKit plan this PR implements, e.g.
     `.squad/plans/ticketing-core/03-story-triage-CRM-101.md`
     If there is no plan, name the constitution §I exemption that applies. -->

Implements:

## What changed

<!-- One short paragraph. The plan already says how; say what landed and what didn't. -->

## Deviations from the plan

<!-- "None" is a valid answer. If the plan was revised, say what changed and why. -->

None.

## Checklist

- [ ] `dotnet build CrmTicketing.slnx` is clean (warnings are errors)
- [ ] `dotnet test CrmTicketing.slnx` passes
- [ ] Every acceptance criterion in the intake is met, or deferred to a linked story
- [ ] No project reference added outside the layer graph in `docs/architecture.md`
- [ ] Any new NuGet package has a version in `Directory.Packages.props` and a
      justification in the plan
- [ ] No secrets, connection strings, or tokens added
- [ ] `.squad/` plan and overview rows updated if this PR added or changed a story
