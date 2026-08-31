# Story 06 — Identity, roles, sign-in, and endpoint authorisation (Story: 5, also closes 6)

## Prerequisites

- Story 04 completed: [`../ticketing-core/04-story-ticket-endpoints-10.md`](../ticketing-core/04-story-ticket-endpoints-10.md) — the ticket endpoints and `ITicketRepository` are merged.
- Story 05 completed: [`../ticketing-ui/05-story-ticket-list-view-12.md`](../ticketing-ui/05-story-ticket-list-view-12.md) — the list view and `TicketsApiClient` are merged. **Confirmed on merge:** its page performs no filtering of its own, so row-level rules landing in the repository require no change to its markup. Its typed client does change — see task 9.
- **This story is the largest in the project so far and reopens two merged contracts.** It changes every `Ticket` mutator signature, changes what `GET /api/tickets` returns for a Customer, and adds a database migration with a data problem to solve. Read `## Sequencing` before starting.
- **This is the first story in a new feature folder.** `.squad/plans/auth-roles/00-overview.md` and a row in `.squad/plans/00-index.md` are created alongside the plan.

---

## Sequencing

The tasks below are ordered so the solution compiles at the end of each numbered group, not so they can be done in any order. Two groups are large enough to commit separately:

1. **Tasks 1–4** — Identity, roles, sign-in, configuration. Adds packages; **generates no migration** — the migration is task 10 and covers the Identity tables, both `ticket` columns, and the foreign key as one unit, so generating a partial one here would only have to be discarded. The solution builds and every existing test still passes at the end of this group.

   **It is not, however, independently *mergeable*.** The seeding call added in task 3 runs at startup and queries a table that only task 10's migration creates, so between the end of this group and task 10 **the API does not start**. No test reveals this, because no test boots the host. Commit group 1 on the feature branch as a checkpoint — worth doing before task 5 breaks 44 call sites — but **do not merge it to `main` alone**; `main` would carry an API that cannot run. The whole story merges as one.

   This supersedes any earlier claim that group 1 "changes no existing behaviour". It changes startup behaviour deliberately: task 2's revision chose an awaited call precisely so the ordering dependency fails where someone can act on it.
2. **Tasks 5–7** — actor threading and row-level filtering. **This is the breaking group:** 44 call sites of the `Ticket` mutators stop compiling the moment task 5 lands, and story 04's controller tests change behaviour in task 7. Nothing builds mid-group; finish it before running the suite.
3. **Tasks 8–10** — authorisation attributes, the client, documentation.

If the executor is time-boxed, group 1 is a coherent commit on its own. Groups 2 and 3 are not separable — endpoints that authenticate but do not authorise are worse than neither.

---

## Story Goal

Give the system a notion of who is acting, and close the audit hole before it grows past two rows.

1. ASP.NET Core Identity over the existing PostgreSQL database, with `Guid` keys, entirely inside `CrmTicketing.Infrastructure`.
2. Three seeded roles — `Admin`, `Agent`, `Customer` — seeded idempotently at startup.
3. Bearer-token sign-in. No self-registration; accounts are created by an Admin.
4. Every ticket endpoint requires an authenticated caller, and role rules are enforced at the API boundary.
5. **A Customer sees only tickets they raised**, enforced in `TicketRepository` so no future caller can forget it.
6. `Ticket` records `CreatedBy` and `UpdatedBy`, threaded from the authenticated principal through every mutator. The domain still reads no clock, no `HttpContext`, and no Identity type.

---

## Context — Read These Files First

1. `src/CrmTicketing.Domain/Tickets/Ticket.cs` — all 208 lines. The six mutators task 5 changes: `Open` (~line 72), `TransitionTo` (~line 102), `Assign` (~line 116), `Unassign` (~line 135), `ChangePriority` (~line 146), `UpdateDetails` (~line 156). Note `RequesterId` (line 60) is a bare `Guid` pointing at nothing — this story is what gives it something to point at. **No navigation property to a user type may be added.**
2. `src/CrmTicketing.Domain/Tickets/TicketQuery.cs` — all 67 lines. `Create` (~line 48) clamps paging. Task 6 adds a caller-constraint parameter here.
3. `src/CrmTicketing.Domain/Tickets/ITicketRepository.cs` — all 25 lines. Five methods; `GetAsync` (line 16), `ListAsync` (line 20), and `CountAsync` (line 22) all change in task 6. **Framework-free: no Identity type may appear in any signature.**
4. `src/CrmTicketing.Infrastructure/Persistence/TicketRepository.cs` — all 71 lines. The private `Filter` helper shared by `ListAsync` and `CountAsync` is where the row-level rule goes, for exactly the reason it already exists: so a rule cannot apply to the page but not the count.
5. `src/CrmTicketing.Infrastructure/Persistence/CrmDbContext.cs` — all 21 lines. `OnModelCreating` (~lines 15–20) calls `ApplyConfigurationsFromAssembly` then `ApplySnakeCaseNames`. Task 1 changes the base class; the ordering of those two calls must not change, and **`base.OnModelCreating` must still be called first** — Identity registers its own model there.
6. `src/CrmTicketing.Api/Program.cs` — all 35 lines. **`app.UseAuthorization()` is on line 30 and there is no `UseAuthentication()` anywhere.** Task 3 adds it immediately before line 30; authorisation without authentication silently treats every caller as anonymous.
7. `src/CrmTicketing.Api/Configuration/CorsPolicies.cs` — all 37 lines. The pattern for an API configuration extension: `public static class`, one `Add*` method taking `IConfiguration`, a `const string` for the policy name, and a comment explaining the fail-closed branch (~lines 25–30). Tasks 3 and 8 follow it.
8. `src/CrmTicketing.Api/Controllers/TicketsController.cs` — all 274 lines. Seven actions at lines 22, 61, 72, 127, 178, 213, and 251. Every one needs an authorisation decision in task 8.
9. `src/CrmTicketing.Infrastructure/DependencyInjection.cs` — all 44 lines. `AddPersistence` registers `CrmDbContext` and `ITicketRepository`. Identity registration joins it in task 1.
10. `tests/CrmTicketing.Domain.Tests/Tickets/TicketTests.cs` — **27 mutator call sites**, every one of which stops compiling in task 5.
11. `tests/CrmTicketing.Api.Tests/Controllers/TicketsControllerTests.cs` — **11 mutator call sites**, plus `FakeTicketRepository`, whose interface implementation changes in task 6.
12. `.github/workflows/ci.yml` — the `Test` step (~lines 35–41). **Unlike story 05, this story does edit this file** — task 4 adds a throwaway signing key to the environment.
13. `docs/constitution.md` — §II (line 23) the layer graph, §VI (line 75) configuration and secrets, §VII (line 86) three strikes before abstraction.

---

## Implementation tasks

### 1 — Identity over the existing context

**File: `Directory.Packages.props`** — add to a new `Label="Identity"` group:

```xml
<PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.11" />
<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.11" />
```

Both verified on nuget.org at planning time and both match the 10.0.11 already pinned for the ASP.NET Core packages. `Identity.EntityFrameworkCore` goes on `CrmTicketing.Infrastructure`; `Authentication.JwtBearer` goes on `CrmTicketing.Api`.

**Create file: `src/CrmTicketing.Infrastructure/Identity/ApplicationUser.cs`**

```csharp
public sealed class ApplicationUser : IdentityUser<Guid>;
```

**Create file: `src/CrmTicketing.Infrastructure/Identity/ApplicationRole.cs`**

```csharp
public sealed class ApplicationRole : IdentityRole<Guid>;
```

`Guid` keys, matching `Ticket.RequesterId` and `Ticket.AssigneeId`. Both types live in Infrastructure and **must not be referenced from `Domain`, `Shared`, or `Client`**.

**File: `src/CrmTicketing.Infrastructure/Persistence/CrmDbContext.cs`** — change the base class:

```csharp
public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
```

Keep `OnModelCreating` exactly as it is: `base.OnModelCreating` first (Identity's model is built there), then `ApplyConfigurationsFromAssembly`, then `ApplySnakeCaseNames`. The snake_case convention will rewrite Identity's table names too — `AspNetUsers` becomes `asp_net_users`. That is correct and consistent; **do not exempt Identity from the convention**, and note it in the story's PR because a reader expecting the framework defaults will be surprised.

**File: `src/CrmTicketing.Infrastructure/DependencyInjection.cs`** — inside `AddPersistence`, after the `CrmDbContext` registration:

```csharp
services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<CrmDbContext>();
```

`AddIdentityCore`, not `AddIdentity`: the latter wires cookie authentication, which this API does not use and which would add a second authentication scheme nobody asked for.

### 2 — Role seeding

**Create file: `src/CrmTicketing.Infrastructure/Identity/RoleNames.cs`**

```csharp
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Agent = "Agent";
    public const string Customer = "Customer";

    public static IReadOnlyList<string> All { get; } = [Admin, Agent, Customer];
}
```

One declaration of the three role names, for the same reason `TicketStatusTransitions` is the one declaration of the transition table. Authorisation policies in task 8 reference these constants, never string literals.

**Create file: `src/CrmTicketing.Infrastructure/Identity/IdentitySeeder.cs`**

```csharp
internal static class IdentitySeeder
{
    internal static Task SeedRolesAsync(IServiceProvider services, CancellationToken cancellationToken);
}
```

**Create the public entry point in `src/CrmTicketing.Infrastructure/DependencyInjection.cs`**, beside `AddPersistence`:

```csharp
public static Task SeedIdentityRolesAsync(
    this IServiceProvider services,
    CancellationToken cancellationToken);
```

It opens a scope through `IServiceScopeFactory` — `RoleManager` is scoped and the
root provider cannot resolve it — and delegates to `IdentitySeeder`.

**Why an extension rather than calling `IdentitySeeder` from `Program.cs`.** The
API composition root already calls exactly one Infrastructure extension,
`AddPersistence`, and names no persistence type. A sibling extension keeps that
property: `IdentitySeeder`, `ApplicationRole`, and `RoleManager` stay invisible to
`CrmTicketing.Api`, and `IdentitySeeder` itself becomes `internal` to enforce it.

**Why not an `IHostedService` registered inside `AddPersistence`.** It would keep
`Program.cs` untouched, and no test boots the host so it would not affect the
suite — but roles cannot be seeded before the Identity tables exist, and this
project applies migrations manually rather than at startup. A hosted service makes
that ordering implicit and surfaces a failure as a background exception detached
from the composition that caused it. An awaited call at the composition root makes
the dependency visible and lets it fail where someone can act on it.

For each name in `RoleNames.All`, create the role only when `RoleExistsAsync` is false. **Idempotent by construction** — the acceptance criterion is that re-running startup does not duplicate them, and a `CreateAsync` without the existence check throws on the second run.

Roles are seeded, never database-editable: a role name appears in a policy in code, so adding one is a code change. Same reasoning that made `TicketPriority` a fixed enum in story 03.

### 3 — Authentication and the signing key

**Create file: `src/CrmTicketing.Api/Configuration/JwtOptions.cs`**

```csharp
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "CrmTicketing";
    public string Audience { get; set; } = "CrmTicketing.Client";
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 60;
}
```

**Create file: `src/CrmTicketing.Api/Configuration/AuthenticationSetup.cs`** — following `CorsPolicies.cs`:

```csharp
public static class AuthenticationSetup
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration);
}
```

- Bind `JwtOptions` from the `Jwt` section.
- **Fail closed**, exactly as `AddPersistence` does for its connection string (`DependencyInjection.cs` ~lines 28–36): throw `InvalidOperationException` naming the configuration key and the `dotnet user-secrets` command when `SigningKey` is empty or shorter than 32 characters. A short key produces a runtime failure inside the JWT library with a far worse message.
- `TokenValidationParameters` validates issuer, audience, lifetime, and signing key. `ClockSkew = TimeSpan.FromSeconds(30)` — the five-minute default makes an expiry test either slow or a lie.
- **Set `RoleClaimType` and `NameClaimType` explicitly**, and emit the token's claims using the same types. Role claims in a JWT do not map to `ClaimsIdentity.RoleClaimType` by default, so every `[Authorize(Roles = ...)]` and the `StaffOnly` policy from task 8 will reject a user who genuinely holds the role. The symptom is a 403 for correct credentials and it reads as a policy bug rather than a claim-mapping one. Whichever type the sign-in endpoint writes in task 9, this must match it — pick one and pin it in both places.

**File: `src/CrmTicketing.Api/Program.cs`** — three changes and nothing else. **This supersedes any earlier statement in this plan that `Program.cs` takes only two:** the seeder from task 2 has no other call site, and without the third line the acceptance criterion "three roles are seeded idempotently at startup" is unreachable.

```csharp
builder.Services.AddJwtAuthentication(builder.Configuration);   // after line 13
```

```csharp
app.UseAuthentication();                                        // immediately before line 30
app.UseAuthorization();
```

```csharp
await app.Services.SeedIdentityRolesAsync(CancellationToken.None);   // after var app = builder.Build()
```

Placed after `Build()` and before `app.Run()`. `Program.cs` names one Infrastructure
extension and no Identity type, matching how it already calls `AddPersistence`.

**`UseAuthentication()` is genuinely absent today** — line 30 calls `UseAuthorization()` with nothing populating `HttpContext.User`. Without this line every `[Authorize]` added in task 8 rejects every caller including valid ones, and the failure looks like a token bug rather than a pipeline bug.

**Secrets.** The signing key never enters the repository (constitution §VI). Locally:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<32+ character value>" --project src/CrmTicketing.Api
```

`src/CrmTicketing.Api/appsettings.json` gains a `Jwt` section with `Issuer`, `Audience`, and `LifetimeMinutes` **but no `SigningKey` key at all** — an empty-string placeholder invites someone to fill it in and commit it.

### 4 — CI supplies a throwaway key

**File: `.github/workflows/ci.yml`** — add to the `build-and-test` job's `Test` step (~lines 35–41):

```yaml
        env:
          Jwt__SigningKey: ci-only-throwaway-signing-key-not-a-secret-0123456789
```

The double underscore is the configuration-provider separator for nested keys. This value is deliberately public and meaningless: it exists so tests that construct the API's configuration do not hit the fail-closed guard from task 3. **It is not a secret and must never be reused anywhere else.**

This is the one story so far that edits `ci.yml`; story 05's plan asserted the file stays untouched, and that assertion does not carry over.

### 5 — Threading the actor through the domain

**File: `src/CrmTicketing.Domain/Tickets/Ticket.cs`**

Add two properties beside the existing timestamps:

```csharp
public Guid CreatedBy { get; private set; }
public Guid UpdatedBy { get; private set; }
```

**`private set`, not a get-only property.** A get-only auto-property can be assigned only inside a constructor, but `Open` is a static factory that constructs through the private constructor at ~line 43 — so a get-only `CreatedBy` either forces that constructor's signature to change or does not compile at all. A private setter assigned once inside `Open` keeps the constructor untouched and keeps the property closed to callers, which is what the invariant actually requires. Test 4 pins that it never changes afterwards.

Every mutator takes `Guid actorId` alongside the `DateTimeOffset at` it already takes, rejects `Guid.Empty` with `ArgumentException` naming the parameter, and assigns `UpdatedBy` wherever it assigns `UpdatedAt`:

```csharp
public static Ticket Open(..., DateTimeOffset createdAt, Guid actorId, ...);
public void TransitionTo(TicketStatus target, DateTimeOffset at, Guid actorId);
public void Assign(Guid assigneeId, DateTimeOffset at, Guid actorId);
public void Unassign(DateTimeOffset at, Guid actorId);
public void ChangePriority(TicketPriority priority, DateTimeOffset at, Guid actorId);
public void UpdateDetails(TicketTitle title, string description, string? category, DateTimeOffset at, Guid actorId);
```

`Open` sets `CreatedBy` and `UpdatedBy` to the same actor, through the private setters. The EF constructor (~line 43) needs no change: both are value types EF materialises through their setters by convention.

**The domain still reads no clock, no `HttpContext`, and no Identity type.** The actor is a bare `Guid` handed in by the caller, exactly as the instant is.

**This breaks 44 call sites** — 27 in `TicketTests.cs`, 11 in `TicketsControllerTests.cs`, the rest in `TicketsController.cs`. That is the cost of doing this at two tickets rather than at two thousand, and it is why the intake put it in this story.

**File: `src/CrmTicketing.Infrastructure/Persistence/Configurations/TicketConfiguration.cs`** — map both columns as required, alongside the existing `CreatedAt` mapping, and add the foreign key:

```csharp
builder.Property(t => t.CreatedBy).IsRequired();
builder.Property(t => t.UpdatedBy).IsRequired();
builder.HasIndex(t => t.RequesterId);   // already present
```

The `requester_id` foreign key to the user table is declared here **without a navigation property on `Ticket`**, using `HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.RequesterId)`. `Ticket` never learns that `ApplicationUser` exists.

**The foreign key changes how `POST /api/tickets` fails, and task 8 must handle it.** Until now `RequesterId` was an opaque `Guid` referencing nothing, so any value inserted successfully. With the key in place, a `requesterId` that is not a real user id makes PostgreSQL reject the insert; EF surfaces that as `DbUpdateException`, which the exception handler does not map, so the caller gets **500**. A Customer is unaffected because task 8 forces their own id — but **an Admin or Agent raising a ticket on behalf of a customer still takes `requesterId` from the request body**, which is exactly the path that now breaks.

Resolution: `Create` verifies the requester exists before constructing the aggregate and returns **400** naming `requesterId` when it does not. That check belongs at the API boundary, not in the domain, because existence of a user is not a `Ticket` invariant — `Ticket` still knows nothing about users. Add an `ExistsAsync(Guid userId, CancellationToken)` method to a small `IUserDirectory` declared in `CrmTicketing.Domain/Tickets/` and implemented over `UserManager` in Infrastructure, so the controller does not name an Identity type.

**No foreign key on `assignee_id` in this story.** Adding one would make `POST /api/tickets/{id}/assignee` fail the same way, and assignment is staff-only with its own validation story. Declaring one key and not the other is a deliberate asymmetry, not an oversight — record it in `docs/architecture.md` so the next reader does not "fix" it.

### 6 — Row-level filtering in the repository

**Create file: `src/CrmTicketing.Domain/Tickets/TicketAccess.cs`**

```csharp
public sealed record TicketAccess
{
    public static TicketAccess All();
    public static TicketAccess OwnedBy(Guid requesterId);
    public Guid? RestrictedToRequesterId { get; }
}
```

Framework-free, and deliberately **not** an enum of role names: the repository must not know what a role is, only whether this caller is confined to their own rows. Roles map to a `TicketAccess` at the API boundary in task 8.

**File: `src/CrmTicketing.Domain/Tickets/TicketQuery.cs`** — `Create` gains a `TicketAccess access` parameter, stored on a new `Access` property. It is **not optional and has no default**: a caller that forgets it must fail to compile rather than silently get unfiltered rows.

**File: `src/CrmTicketing.Domain/Tickets/ITicketRepository.cs`** — `GetAsync` gains a `TicketAccess access` parameter for the same reason. `ListAsync` and `CountAsync` already carry it inside `TicketQuery`.

**File: `src/CrmTicketing.Infrastructure/Persistence/TicketRepository.cs`** — in the private `Filter` helper, apply the constraint first:

```csharp
if (query.Access.RestrictedToRequesterId is { } requesterId)
{
    tickets = tickets.Where(t => t.RequesterId == requesterId);
}
```

`GetAsync` applies the same predicate and **returns null** for a ticket the caller may not see, so the controller's existing 404 path handles it with no new branch. Returning the ticket and letting the controller decide would put a security rule one refactor away from being dropped.

### 7 — Unauthorised reads are 404, never 403

A Customer requesting another customer's ticket by id gets **404**. A 403 confirms the ticket exists, which leaks the existence of other customers' tickets to anyone who can guess a Guid.

This falls out of task 6 for free: `GetAsync` returns null, and `TicketsController.GetById` already answers null with `TicketNotFound()`. **No new controller branch, and no new status code.** Verify it rather than implement it.

### 8 — Authorisation at the API boundary

**Create file: `src/CrmTicketing.Api/Configuration/AuthorizationPolicies.cs`** — following `CorsPolicies.cs`:

```csharp
public static class AuthorizationPolicies
{
    public const string StaffOnly = "StaffOnly";       // Admin or Agent
    public static IServiceCollection AddTicketAuthorization(this IServiceCollection services);
}
```

`StaffOnly` requires `RoleNames.Admin` or `RoleNames.Agent`. Policies reference the constants from task 2, never literals.

**Create file: `src/CrmTicketing.Api/Infrastructure/CallerContext.cs`** — an extension over `ClaimsPrincipal`:

```csharp
internal static class CallerContext
{
    public static Guid UserId(this ClaimsPrincipal principal);      // throws when absent
    public static TicketAccess Access(this ClaimsPrincipal principal);
}
```

`Access` returns `TicketAccess.All()` for Admin and Agent, and `TicketAccess.OwnedBy(userId)` for Customer. **This is the single place role maps to data visibility.**

**File: `src/CrmTicketing.Api/Controllers/TicketsController.cs`** — `[Authorize]` on the class, then per action:

| Action | Line | Rule |
|---|---|---|
| `Create` | 22 | any authenticated role; `requesterId` for a Customer is **forced to their own user id**, not taken from the request body |
| `GetById` | 61 | any role; visibility comes from `TicketAccess` |
| `List` | 72 | any role; visibility comes from `TicketAccess` |
| `Update` | 127 | any role; a Customer may update only their own, enforced by the repository returning null |
| `Transition` | 178 | any role; a Customer's target status is checked against the requester-allowed set below |
| `Assign` | 213 | `[Authorize(Policy = StaffOnly)]` — a Customer may never assign |
| `GetMetadata` | 251 | any authenticated role |

**A Customer's allowed transitions.** A requester may move their own ticket to `Closed` (withdrawing it) and may reopen a `Resolved` ticket to `Open` (rejecting a resolution). Every other move is staff-only and returns **403**, not 409 — the move is legal in the workflow, the caller is not permitted to make it. **This is an authorisation rule at the API boundary and must not be written into `TicketStatusTransitions`**, which stays the single declaration of which moves are legal for anyone.

`Create` forcing the requester id is not decoration: without it a Customer can raise a ticket in someone else's name and then be unable to see it.

### 9 — Sign-in, and the client that carries the token

**Create file: `src/CrmTicketing.Shared/Contracts/Auth/SignInRequest.cs`** and **`SignInResponse.cs`** — `sealed record`s in the established style:

```csharp
public sealed record SignInRequest(string Email, string Password);
public sealed record SignInResponse(string AccessToken, DateTimeOffset ExpiresAt, string Email, IReadOnlyList<string> Roles);
```

**Create file: `src/CrmTicketing.Api/Controllers/AuthController.cs`** — `[Route("api/auth")]`, `[AllowAnonymous]` on the sign-in action only:

- `POST /api/auth/signin` — verifies the password through `UserManager.CheckPasswordAsync` and returns a signed JWT carrying the user id and role claims.
- **A bad password and an unknown email return the identical 401 body.** Branching the message discloses which accounts exist. Call `CheckPasswordAsync` against a dummy hash when the user is not found, so the response time does not disclose it either.
- `POST /api/auth/users` — `[Authorize(Roles = RoleNames.Admin)]`, creates an account in a role. **This is the only way an account comes into existence.**

**No self-registration.** No `MapIdentityApi` and no public register route: `AddIdentityCore` plus a hand-written controller means no register endpoint exists to disable. A test asserts `POST /api/auth/register` returns 404.

**Client (`src/CrmTicketing.Client`).** Story 05's list page will return 401 the moment task 8 lands, so the client must carry a token or this story ships a regression:

- **Create `Pages/SignIn.razor`** (`@page "/signin"`) — email and password, posts through a typed `AuthApiClient`, stores the token.
- **Create `Services/AuthApiClient.cs`** and **`Services/TokenStore.cs`** — a scoped store holding the token in memory. **Not `localStorage`:** persisting a bearer token to storage readable by any script on the origin is an XSS-amplification decision that needs its own story, and an in-memory token merely means a refresh returns you to sign-in.
- **Create `Services/BearerTokenHandler.cs`** — a `DelegatingHandler` attaching `Authorization: Bearer`, registered on the `ITicketsApiClient` registration in `Program.cs` with `.AddHttpMessageHandler<BearerTokenHandler>()`.
- **`Pages/Tickets.razor`** — a 401 renders the failed state with a link to `/signin`. `TicketsApiClient` already surfaces the status code on `ApiRequestException.StatusCode`; the page reads it rather than parsing the message.

Permission-gated UI — hiding buttons a role cannot use — remains issue #16. This story only ensures an authenticated user can reach the screen that already exists.

### 10 — The migration and the two existing rows

```bash
dotnet ef migrations add AddIdentityAndTicketActor --project src/CrmTicketing.Infrastructure --startup-project src/CrmTicketing.Api
```

The generated `Up()` creates the Identity tables, adds `ticket.created_by` and `ticket.updated_by`, and adds the `requester_id` foreign key.

**The database already holds two rows that will break this migration.** Both tickets carry `requester_id = 11111111-1111-1111-1111-111111111111`, a Guid with no user behind it, and the new columns are non-nullable with no default. Confirmed against the live database at planning time. As generated, the migration fails twice: on the not-null columns, and again on the foreign key.

**Resolution: delete the two rows in the migration, before the schema changes.** Add as the first statement of `Up()`:

```csharp
migrationBuilder.Sql("DELETE FROM ticket;");
```

They are throwaway rows created by hand while exercising the API on 29–30 August 2026. There is no production environment, no user has seen them, and inventing a placeholder user to satisfy a foreign key would leave a fictitious account in the users table forever. **State this in the PR body** — a migration that deletes data must never be a surprise found by reading the diff.

This is the one hand-edit to a generated migration this project permits, and it exists because the alternative is worse. Everything else in the file is generated and untouched. `Down()` drops the columns and the tables; it does not restore the rows.

---

## Edge Cases & Failure Modes

- **`UseAuthentication()` omitted.** `Program.cs:30` calls `UseAuthorization()` today with nothing populating `HttpContext.User`. Add authorisation without authentication and every request is anonymous, so every `[Authorize]` returns 401 including valid tokens. The symptom looks like a token-signing bug; the cause is one missing line.
- **Seeding runs before the tables exist.** `SeedIdentityRolesAsync` queries the roles table, which task 10's migration creates. On a database where migrations have not been applied, `RoleExistsAsync` throws a Npgsql "relation does not exist" error and the API fails to start with a message that does not name the cause. Catch that case and rethrow an `InvalidOperationException` naming the required command — `dotnet ef database update --project src/CrmTicketing.Infrastructure --startup-project src/CrmTicketing.Api` — exactly as `AddPersistence` does for a missing connection string. **This is a startup-behaviour change:** the API currently starts fine without migrations and fails only on the first query. Failing at startup with an actionable message is the better trade, but it must be recorded in `README.md`, whose setup section already applies migrations before running the API.
- **No test boots the host,** so the seeding call cannot affect the suite. All three test projects instantiate controllers directly; nothing references `Microsoft.AspNetCore.Mvc.Testing`, `WebApplicationFactory`, or `TestServer`. Verified at planning time. If a future story adds an integration-test host, the seeding call becomes its problem to configure.
- **`OnModelCreating`'s parameter must be renamed to `builder`.** `IdentityDbContext` declares it as `builder`, and CA1725 (parameter names must match the base declaration) is an error under `TreatWarningsAsErrors`. Rename it; the call ordering inside the method does not change. This is required by the analyser, not a deviation.
- **Snake_case renames the Identity tables.** `ApplySnakeCaseNames` runs over every entity, so `AspNetUsers` becomes `asp_net_users`. Correct and intended, but a reader expecting framework defaults will think the migration is wrong.
- **Existing rows break the migration.** Covered in task 10. Without the `DELETE`, `dotnet ef database update` fails on the not-null columns and, if those were made nullable, again on the foreign key.
- **A staff member creating a ticket for a customer who is not a user.** The new `requester_id` foreign key rejects a `Guid` with no matching user, and `DbUpdateException` is unmapped, so the caller gets 500 instead of 400. Validate existence in `Create` (task 5's note). This is a *new* failure mode: the same request succeeded before this story.
- **Role claims that do not map.** `[Authorize(Roles = ...)]` reads `ClaimsIdentity.RoleClaimType`, which a JWT's role claims do not populate by default. Correct credentials get 403 and it looks like a broken policy. Task 3 pins `RoleClaimType`; the sign-in endpoint in task 9 must emit the matching claim type.
- **A Customer creating a ticket for someone else.** `Create` must force `requesterId` from the principal. Taking it from the request body lets a Customer raise a ticket they immediately cannot see, which reads as data loss.
- **403 where 404 is required.** A Customer fetching another customer's ticket must get 404. The repository returning null is what makes this automatic; any controller-level ownership check would produce 403 and leak existence.
- **Filtering the page but not the count.** `TicketRepository.Filter` is shared by `ListAsync` and `CountAsync` precisely so this cannot happen. A Customer seeing `totalCount: 40` above three rows discloses how many tickets exist.
- **`TicketAccess` defaulted rather than required.** Give `TicketQuery.Create` a default `TicketAccess` and every existing call site keeps compiling while silently returning unfiltered rows. It must be required, and the compiler break is the point.
- **A legal transition a Customer may not make.** Returns 403, not 409. 409 says the workflow forbids the move; 403 says this caller does. Conflating them makes the metadata endpoint appear to lie.
- **Timing disclosure on sign-in.** Returning 401 immediately for an unknown email and slowly for a known one with a bad password discloses which accounts exist. Hash against a dummy when the user is absent.
- **Password hashing in tests.** Identity's hasher is deliberately slow. A test suite that signs in for real adds seconds per test; use a test authentication handler that mints a principal directly.
- **`ClockSkew` default.** Five minutes by default, so an expiry test either sleeps for five minutes or asserts nothing. Set 30 seconds.
- **The signing key in CI.** Absent, and the fail-closed guard in task 3 turns every API test into a startup failure with an unrelated-looking message. Task 4 supplies it.
- **Story 04's controller tests change behaviour, and that is expected.** They assert unfiltered reads and anonymous access. Updating them is part of this story, not a regression to investigate. **A failing story-04 test here is not a blocker** — but a failing story-03 *domain* test is, because the transition table must not change.
- **Story 05's client breaks without task 9.** Endpoints requiring auth plus a client sending no token equals a 401 on `/tickets`. Task 9 is not optional polish; without it this story ships a regression to a screen that worked yesterday.
- **Uncertainty to surface — Identity's own snake_case index names.** `ApplySnakeCaseNames` rewrites index and key names as well as tables. Identity's generated names are long, and PostgreSQL truncates identifiers at 63 characters. **This was not verified against a real migration at planning time.** Generate the migration and read the emitted index names before applying it; if any is truncated to a collision, exempt Identity's indexes by name in `SnakeCaseNaming` and say so in the PR.

---

## Test Plan

### 11 — Domain tests

**File: `tests/CrmTicketing.Domain.Tests/Tickets/TicketTests.cs`** — 27 call sites gain an actor argument.

1. `Open` sets `CreatedBy` and `UpdatedBy` to the acting user, and both equal each other.
2. Every mutator rejects `Guid.Empty` as the actor with `ArgumentException` naming `actorId`.
3. Each mutator updates `UpdatedBy` alongside `UpdatedAt` — a `[Theory]` over the five mutators, asserting a second actor replaces the first.
4. `CreatedBy` never changes after `Open`.

**Create file: `tests/CrmTicketing.Domain.Tests/Tickets/TicketAccessTests.cs`**

5. `All()` has a null `RestrictedToRequesterId`; `OwnedBy(id)` carries that id.
6. `TicketQuery.Create` round-trips the access argument: construct one with `OwnedBy(id)` and read `Access` back. **This does not prove the parameter is required** — the same test passes if someone later gives it a default. That guarantee is compile-time only and is not runtime-assertable; the protection against a default being added is the note in Edge Cases plus review, not this test. Do not describe it as proof.

### 12 — Repository tests

**Create file: `tests/CrmTicketing.Infrastructure.Tests/Persistence/TicketRepositoryAccessTests.cs`**

7. **The acceptance criterion's own test:** call the repository directly with `TicketAccess.OwnedBy(customerId)` and assert another user's ticket is absent from `ListAsync`, absent from `CountAsync`, and null from `GetAsync`.
8. `TicketAccess.All()` returns both.

**These need a database and CI has none, so they must not go through `TicketRepository`'s constructor** — it requires a `CrmDbContext`. Change `Filter` from `private` to `internal static`, taking `IQueryable<Ticket>` and `TicketQuery` and returning `IQueryable<Ticket>`, and call it directly over an in-memory `IQueryable<Ticket>`. `CrmTicketing.Infrastructure.csproj` already carries an `InternalsVisibleTo` for its test project, so no new grant is needed — but the accessibility change is required and test 7's wording above ("call the repository directly") is wrong: the tests call `Filter`, not the repository.

`ListAsync`, `CountAsync`, and `GetAsync` must all route through that one helper, so a rule cannot apply to the page but not the count — which is why `Filter` already exists. **Do not add the EF in-memory provider**; issue #29 covers real integration tests.

### 13 — API tests

**File: `tests/CrmTicketing.Api.Tests/Controllers/TicketsControllerTests.cs`** — 11 call sites gain an actor; `FakeTicketRepository` implements the new signatures.

9. An anonymous request returns 401 — asserted at the attribute level: every action on `TicketsController` is covered by `[Authorize]`, by reflection over the type.
10. A Customer's `Create` records their own id as `RequesterId` even when the body names another.
11. A Customer calling `Assign` is refused by the `StaffOnly` policy.
12. A Customer transitioning to a staff-only status gets 403, and to `Closed` gets 200.
13. `CallerContext.Access` maps Admin and Agent to `All()`, Customer to `OwnedBy`.

**Create file: `tests/CrmTicketing.Api.Tests/Controllers/AuthControllerTests.cs`**

14. Sign-in with a bad password and sign-in with an unknown email return **byte-identical** 401 bodies.
15. `POST /api/auth/register` returns 404 — the no-self-registration criterion.
16. Account creation requires the Admin role.

Use a test authentication handler minting a principal directly. **No real password hashing in tests.**

### 14 — Regression

17. Story 03's domain tests pass **unchanged** except for the actor argument. `TicketStatusTransitionsTests` must not change at all — the transition table is untouched by this story, and an edit there means an authorisation rule leaked into the domain.
18. Story 05's `Client.Tests` pass; its component tests stub `ITicketsApiClient` and are unaffected by authorisation. The new `BearerTokenHandler` gets its own test asserting the header is attached.

---

## Migration / Rollback

`AddIdentityAndTicketActor` is **destructive**: it deletes both existing ticket rows (task 10). That is acceptable only because no environment holds real data, and it must be stated plainly in the PR rather than discovered in the diff.

Rollback before deployment is `dotnet ef migrations remove`. After applying, `dotnet ef database update AddTicket` runs `Down()`, dropping the Identity tables and the two columns — it does **not** restore the deleted rows. They are gone from the moment `Up()` runs.

Half-applied risk: if `Up()` fails partway — most likely on the foreign key — the Identity tables may exist without the ticket columns. PostgreSQL runs migrations transactionally, so a failure rolls back cleanly, but verify `__EFMigrationsHistory` before retrying rather than assuming.

---

## Verification Steps

1. **Backend builds:** `dotnet build CrmTicketing.slnx` — zero warnings, zero errors.
2. **Tests pass:** `dotnet test CrmTicketing.slnx` — all **four** test projects (`Domain.Tests`, `Api.Tests`, `Infrastructure.Tests`, `Client.Tests`), no database running. This story adds test *files*, not a test project.
3. **Domain stays pure:** `grep -cE "(Project|Package)Reference" src/CrmTicketing.Domain/CrmTicketing.Domain.csproj` returns `0`.
4. **No Identity in the domain:** grep for Identity *types*, not the word, and exclude build output:

   ```bash
   grep -rnE "Microsoft\.AspNetCore\.Identity|IdentityUser|IdentityRole|IdentityDbContext|RoleManager|UserManager|ApplicationUser|ApplicationRole" \
     --include='*.cs' --exclude-dir=bin --exclude-dir=obj src/CrmTicketing.Domain/
   ```

   Returns no output. **The bare word does not work:** `obj/project.assets.json` carries the restore graph, and `Entity.cs` uses "identity" in a doc comment about entity identity, which predates this story and is not a violation.
5. **No Identity type leaks upward:**

   ```bash
   grep -rnE "ApplicationUser|ApplicationRole|IdentityUser|IdentityRole|RoleManager|UserManager" \
     --include='*.cs' --include='*.razor' --exclude-dir=bin --exclude-dir=obj \
     src/CrmTicketing.Shared/ src/CrmTicketing.Client/
   ```

   Returns no output.
6. **The composition root names no Identity type:**

   ```bash
   grep -rnE "ApplicationUser|ApplicationRole|RoleManager|UserManager|IdentitySeeder" \
     --include='*.cs' --exclude-dir=bin --exclude-dir=obj src/CrmTicketing.Api/
   ```

   Returns no output. `Program.cs` calls two Infrastructure extensions — `AddPersistence` and `SeedIdentityRolesAsync` — and knows nothing else about Identity. Task 9's `AuthController` is the one place `UserManager` legitimately appears; when it lands, narrow this grep to exclude that file rather than deleting the step.

7. **Every ticket action is authorised:** test 9's reflection check is the real proof — a `grep` count passes on a single class-level attribute even if an action carries `[AllowAnonymous]`. Assert the negative instead: `grep -rn "AllowAnonymous" src/CrmTicketing.Api/Controllers/ --exclude-dir=bin --exclude-dir=obj` returns hits **only** in `AuthController.cs`, and only on the sign-in action.
8. **No signing key in the repository:** `git grep -iE "signingkey|jwt.*secret" -- ':!*.md' ':!.github/workflows/ci.yml'` returns no literal key. The CI file is excluded because task 4 puts a deliberately public throwaway value there.
9. **No ambient clock:** `grep -rnE "DateTime\\.UtcNow|DateTime\\.Now" --include='*.cs' --exclude-dir=bin --exclude-dir=obj src/` returns no output.
10. **The transition table is unchanged:** `git diff main -- src/CrmTicketing.Domain/Tickets/TicketStatusTransitions.cs` returns no output.
11. **Optional, with PostgreSQL running:** `dotnet ef database update`, create an Admin through `POST /api/auth/users`, sign in, and confirm a Customer's `GET /api/tickets` returns only their own rows while an Agent's returns all.

---

## Done Criteria

- [ ] Identity is configured over `CrmDbContext` with `Guid` keys; Identity types exist only in `CrmTicketing.Infrastructure`.
- [ ] `Admin`, `Agent`, and `Customer` are seeded idempotently at startup, invoked by `await app.Services.SeedIdentityRolesAsync(...)` in `Program.cs`. Running startup twice does not duplicate them.
- [ ] `IdentitySeeder` is `internal`; `grep -rn "IdentitySeeder\|RoleManager\|ApplicationRole" src/CrmTicketing.Api/` returns no output.
- [ ] Sign-in issues a bearer token; a bad password and an unknown email return identical 401s.
- [ ] No self-registration; `POST /api/auth/register` returns 404 and a test asserts it.
- [ ] Every `TicketsController` action requires an authenticated caller; anonymous requests get 401.
- [ ] Role rules enforced: Admin full, Agent all-tickets, Customer own-tickets-only and never assign.
- [ ] Row-level filtering lives in `TicketRepository`; a Customer's `GET /api/tickets/{id}` for another's ticket returns **404**, not 403.
- [ ] `Ticket` exposes `CreatedBy` and `UpdatedBy`, non-empty, set from the principal at the API boundary; the domain reads no clock, no `HttpContext`, and no Identity type.
- [ ] The migration adds the Identity tables, both columns, and the `requester_id` foreign key, and **explicitly deletes the two pre-existing rows**.
- [ ] `POST /api/tickets` with a `requesterId` that is not a real user returns **400** naming the parameter, not 500 — verified against a running database, since the foreign key is what produces the failure.
- [ ] `RoleClaimType` is set explicitly and matches the claim type the sign-in endpoint emits; a staff token satisfies `StaffOnly`.
- [ ] The signing key is absent from the repository; CI supplies a throwaway value.
- [ ] The Blazor client signs in and carries the token; `/tickets` still works end to end.
- [ ] `TicketStatusTransitions` is byte-identical to `main`.
- [ ] `dotnet build` clean; `dotnet test` passes with no database.
- [ ] Overview `00-overview.md` created and `00-index.md` updated with the new feature.

**STOP HERE. Report to the user and wait for confirmation before proceeding to Story 07 (issue #16, permission-gated UI).**
