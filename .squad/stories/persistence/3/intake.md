> **Tracker auto-fetch skipped.**  
> GitHub authentication failed (HTTP 401). Check your PAT in .squad/secrets.yaml; it needs "repo" scope (or "Issues: read" for fine-grained tokens).
> Hint: Run `squad config set tracker` to re-enter credentials, or `squad doctor` to verify.

---

# Story intake

Fill this template for each story you want planned. Keep it copy-paste-friendly: the planner reads **this file and the files in `attachments/`**, nothing else.

- Folder: `.squad/stories/persistence/3/intake.md`
- Binaries (screenshots, PDFs, exports): put them in `attachments/` next to this file and list them below.
- Do **not** rely on external links (tracker URLs, wiki, chat) — the planner cannot open them. Paste the content you want considered.

This is **not** an implementation prompt. It is the input to the plan-generation meta-prompt bundled with squad-kit (`generate-plan.md` in the installed package).

---

## Feature

- **Feature name (display):**
- **Feature slug (folder under `plans/`):** `persistence`

## Tracker (metadata only)

- **Tracker type:** `github`
- **Work item id:** `3` *(used in filenames and plan tables; fill manually if empty)*
- **Work item type:** ``
- **Status:** ``
- **Assignee:** ``
- **Labels:** ``

External tracker links are **not** followed by the planner. Keep the id for naming and traceability only.

---

## Title

Add CrmDbContext and IEntityTypeConfiguration convention

```

```

---

## Description

Create CrmDbContext plus a Configurations/ folder with one
IEntityTypeConfiguration<T> per aggregate. Register it in the Api composition
root behind an abstraction, not as a concrete type.

EF Core package versions go in Directory.Packages.props - never inline in a csproj.

```

```

---

## Acceptance criteria

- [ ] `src/CrmTicketing.Infrastructure/Persistence/CrmDbContext.cs` exists and derives
      from EF Core's DbContext. The Persistence/README.md stub is deleted.
- [ ] EF Core package versions are declared ONLY in Directory.Packages.props; no
      PackageReference in any csproj carries a Version attribute.
      Verify: grep -rn 'PackageReference' --include=*.csproj . | grep -i version  → no output
- [ ] CrmTicketing.Domain.csproj still declares zero PackageReference and zero
      ProjectReference. The domain must not learn about EF Core.
      Verify: grep -cE '(Project|Package)Reference' src/CrmTicketing.Domain/*.csproj → 0
- [ ] Mapping lives in `Persistence/Configurations/`, one IEntityTypeConfiguration<T>
      per aggregate, wired with ApplyConfigurationsFromAssembly. No EF attributes
      ([Table], [Key], [Column]) appear on any type under src/CrmTicketing.Domain.
- [ ] CrmDbContext is registered in the Api composition root behind an abstraction
      declared outside Infrastructure. No file under src/CrmTicketing.Api/Controllers
      names CrmDbContext.
      Verify: grep -rn 'CrmDbContext' src/CrmTicketing.Api/Controllers → no output
- [ ] The connection string comes from configuration (user-secrets locally). No
      connection string, password, or key appears in any committed file.
- [ ] The chosen provider is recorded in docs/architecture.md, moved out of the
      "Decisions deliberately deferred" table with a one-line rationale.
- [ ] `dotnet build CrmTicketing.slnx` succeeds with zero warnings under
      TreatWarningsAsErrors.
- [ ] `dotnet test CrmTicketing.slnx` passes, including a new test that builds the
      context with UseNpgsql and a dummy connection string, then asserts
      `context.Model` materialises without throwing. Reading the model does NOT
      open a connection, so this test needs no running database and no Docker.

```

```

---

## Attachments

Place files in `attachments/` next to this `intake.md`, then list them here so the planner knows what to open.

| File (relative to this folder) | What it is |
| ------------------------------ | ---------- |
| *(e.g. `attachments/flow.png`)* | *(e.g. UX flow)* |

*(Add rows per file. If none, write "None.")*

---

## Dependencies

- **Blocked by / related ids:** (tracker ids only; optional short note)
- **Depends on code areas or other stories:**

## Extra notes (optional)

- Anything not captured above (e.g. chat context) — keep short.

## Technical hints (optional)

- APIs, screens, services already discussed. Repos/roots: `.`. Primary language: `C#`.
Database provider: PostgreSQL (decided at epic #2 level).

Packages - add to Directory.Packages.props ONLY, no Version in any csproj:
  <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
Reference it from src/CrmTicketing.Infrastructure only. Microsoft.EntityFrameworkCore.Design
is NOT added here - it belongs to the migrations story (#4).

Registration: an AddPersistence(this IServiceCollection, IConfiguration) extension in
src/CrmTicketing.Infrastructure calls UseNpgsql(...). Program.cs in the Api calls that
one method and never names CrmDbContext or Npgsql directly - the Api must not gain a
compile-time dependency on the provider.

Connection string key: ConnectionStrings:CrmDatabase
  Local dev via user-secrets, never appsettings.json:
    dotnet user-secrets set "ConnectionStrings:CrmDatabase" \
      "Host=localhost;Port=5432;Database=crmticketing;Username=crm;Password=..." \
      --project src/CrmTicketing.Api
  appsettings.json may contain the key with an empty value to document its existence.

Naming: snake_case tables and columns (Postgres convention), configured centrally in
OnModelCreating rather than per-entity.

Timestamps: use `timestamp with time zone` and map to DateTimeOffset. Npgsql is strict
about UTC - combined with the injected TimeProvider this keeps SLA work deterministic later.

## Out of scope

- What this story explicitly does **not** cover:
- Any entity, aggregate, or value object. No Ticket, Customer, Contact, or Comment
  type is defined here. CrmDbContext may ship with zero DbSet properties; the
  aggregates arrive with their own stories and bring their own configurations.
- Migrations and seed data. That is the next story (issue #4), and it depends on
  this one.
- Repository, specification, or query abstractions beyond the single seam needed to
  register the context in the Api. Constitution VII: three strikes before abstraction.
- Identity or auth tables (issue #5).
- Indexing, query tuning, connection resiliency, retry policies, and pooling.
- Multi-tenancy and soft-delete conventions.
- Database provisioning, hosting, backups, and infrastructure-as-code.
- Any change to the layer graph in docs/architecture.md. If this story appears to
  need one, stop and amend the constitution first.
