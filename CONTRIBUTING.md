# Contributing

## Before you write code

1. Read [docs/constitution.md](docs/constitution.md). It is enforced in review.
2. Confirm a story intake exists under `.squad/stories/`. If not, create one:
   `squad new-story <feature-slug> --id <TRACKER-ID>`.
3. Confirm a plan exists under `.squad/plans/`. If not, generate one with
   `/squad-plan <intake-path>` (or `squad new-plan`).

Code without a plan is not reviewed, except for the exemptions in Section I of
the constitution (typos, comments, version bumps).

## Branching and commits

```
feature/<story-slug>        new behaviour
fix/<short-description>     defect
chore/<short-description>   tooling, CI, deps
```

Conventional Commits, with the plan reference in brackets:

```
feat(tickets): add SLA due-date calculation [03-story-sla-clock-CRM-118]
fix(api): return 404 instead of 500 for unknown ticket id [05-story-ticket-detail]
test(domain): cover illegal status transitions [03-story-sla-clock-CRM-118]
```

Allowed types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `ci`.

## Pull request checklist

- [ ] Links the plan file being implemented (or names the exemption).
- [ ] `dotnet build CrmTicketing.slnx` is clean — warnings are errors here.
- [ ] `dotnet test CrmTicketing.slnx` passes.
- [ ] Every acceptance criterion in the intake is satisfied or explicitly deferred
      to a follow-up story that is linked.
- [ ] No new project reference that isn't on the layer graph in
      [docs/architecture.md](docs/architecture.md).
- [ ] Any new NuGet package has a version in `Directory.Packages.props` and a
      justification in the plan.
- [ ] No secrets, connection strings, or tokens added.
- [ ] Public domain types and non-obvious decisions carry XML doc comments.

## Local verification

```bash
dotnet restore CrmTicketing.slnx
dotnet build   CrmTicketing.slnx --configuration Release
dotnet test    CrmTicketing.slnx --configuration Release
dotnet format  CrmTicketing.slnx --verify-no-changes
squad doctor
```

## Deviating from a plan

If the plan is wrong, **stop**. Do not improvise in the implementation session.
Revise the plan file, note what changed and why in the pull request, and continue.
A plan that diverges from the code it produced is worse than no plan at all.
