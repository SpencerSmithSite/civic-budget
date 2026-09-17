# Interview Prep — CivicBudget

Questions an interviewer is likely to ask about this project, the answer,
and **where to look** to back it up. Grows by one section per phase.
Read `SPEC.md` §10 (glossary) first if the fund-accounting terms are rusty.

How to use: for each question, be able to (1) give the one-sentence answer,
(2) open the file and point at the line, (3) name the alternative you
rejected and why.

---

## The 60-second pitch

CivicBudget is a multi-tenant budgeting tool for local governments — think
a village finance director building next year's appropriations — with a
public transparency portal citizens can browse without logging in. It's
.NET 10 and Blazor end to end: the admin side is Interactive Server because
staff need live grids and validation; the public side is static
server-rendered HTML because it has to be fast, cacheable, accessible, and
cheap at scale. Data is in SQL Server through EF Core, tenancy is enforced
in the data layer with global query filters, and the portal can only read
immutable published snapshots — never live drafts. Infrastructure is AWS
CDK in C#, deployed from GitHub Actions over OIDC.

---

## Phase 0 — Planning & architecture

### Q: Walk me through the architecture.
**A:** Four projects with inward-pointing dependencies: `Domain` (entities
and rules, no framework references), `Application` (use cases, DTOs,
validators, interfaces), `Infrastructure` (EF Core, Identity stores, Excel
I/O — implements the interfaces), and `Web` (Blazor + composition root).
Components call application services; they never touch `DbContext`.
**Look at:** `docs/ARCHITECTURE.md` §1; `src/*/**.csproj` project references
(Phase 1).
**Rejected:** a single web project (rules leak into `.razor`), and a
MediatR/CQRS pipeline (indirection with no payoff at this size; MediatR
also went commercial in 2025). ADR-0001.

### Q: Why two render modes in one app?
**A:** Different audiences, opposite needs. Twenty finance staff editing
grids justify a SignalR circuit each; twenty thousand citizens on
adoption night do not. Static SSR makes every portal hit a cacheable HTTP
response with no per-visitor server state, works without JavaScript, and is
crawlable.
**Look at:** `docs/ARCHITECTURE.md` §3; portal pages will have no
`@rendermode` directive (Phase 5). ADR-0002.

### Q: How do you stop one government from seeing another's budget?
**A:** Every tenant-owned entity carries `GovernmentId`. The `DbContext`
adds a global query filter for each of them bound to an `ITenantContext`
(populated from a claim in the admin app, from the URL slug in the portal).
A `SaveChanges` interceptor stamps new rows and rejects mismatches. If no
tenant is set, the filter returns *nothing* — the safe default. Integration
tests seed two tenants and prove the other's rows are invisible.
**Look at:** `docs/ARCHITECTURE.md` §5; `CivicBudgetDbContext.OnModelCreating`
and `TenantSaveChangesInterceptor` (Phase 1). ADR-0004.
**Rejected:** database-per-tenant (operationally heavier than the project
needs), SQL row-level security (good later as defense-in-depth).

### Q: Why can't the public site just show the adopted budget?
**A:** Three reasons. Security — a forgotten `.Where(status == Adopted)`
would leak drafts, so the portal uses a separate read-only `DbContext` that
doesn't even map the live tables. Immutability — renaming an account next
year shouldn't rewrite what citizens saw last year, so the snapshot carries
its own copies of names and codes. Performance — denormalized rows
aggregate with a `GROUP BY` and invalidate with one cache tag.
**Look at:** `docs/ARCHITECTURE.md` §6; `PublicPortalDbContext` (Phase 4).
ADR-0005, ADR-0006.

### Q: Why `IDbContextFactory` instead of injecting `DbContext`?
**A:** In Blazor Server the DI scope is the *circuit*, not the request. A
scoped `DbContext` would live as long as the browser tab, accumulate tracked
entities, and be shared by concurrent event handlers — and `DbContext` isn't
thread-safe. The factory gives one short-lived context per unit of work.
**Look at:** `docs/ARCHITECTURE.md` §4.1; any application service's
`await using var db = await _dbFactory.CreateDbContextAsync(ct);` (Phase 1+).
ADR-0003.

### Q: Explain the appropriation-limit rule.
**A:** In Ohio a subdivision can't appropriate more than the County Budget
Commission certifies as estimated resources: beginning unencumbered balance
plus estimated revenue. Per fund, `Expenditures + Transfers Out` must be
≤ `Beginning balance + Revenues + Transfers In`. It's configurable per
government as *Warn* or *Block*, enforced at workflow transitions rather
than on every keystroke so staff can save work-in-progress.
**Look at:** `docs/SPEC.md` §5.1; `FundBalanceCalculator` and
`AppropriationLimitCheck` in `Domain` (Phase 1).

### Q: How do you handle money?
**A:** `decimal` everywhere, `decimal(18,2)` in SQL via a model-building
convention so it can't be forgotten on a new property. Percent change uses
`MidpointRounding.AwayFromZero` because that's what finance staff see in
Excel; banker's rounding would produce "wrong" numbers in their eyes. Both
are unit-tested.
**Look at:** `Domain/Common/Money.cs` and its tests (Phase 1).

### Q: Why FluentValidation over DataAnnotations?
**A:** Rules live in the Application layer as plain classes, are trivially
unit-testable, and handle cross-field/async rules (department required only
for expenditure accounts) cleanly. DataAnnotations would be zero packages
but pushes rule logic into attribute metadata.
**Look at:** ADR-0007; `Application/**/Validators` (Phase 3).

### Q: You don't have an AWS account. How is this an AWS project?
**A:** The deployment *engineering* is done and verifiable: multi-stage
Dockerfile, full-stack compose, a CDK stack in C# that `cdk synth`s in CI
with no credentials, CDK assertion tests that fail if RDS becomes public or
IAM gets a wildcard, and an OIDC-based `deploy.yml`. If an account appears,
`cdk bootstrap && cdk deploy` is the only new step.
**Look at:** ADR-0008; `infra/` (Phase 7).

### Q: What's in the seed data and why those funds?
**A:** A fictional Ohio village with the funds every village actually has:
General, Street Construction Maintenance & Repair (state gas tax and license
fees — restricted), Capital Projects, and Water and Sewer enterprise funds.
Fund codes follow the Auditor of State's UAN numbering so they look familiar
to Ohio staff. Three fiscal years give a prior-year actual, a current year
with an amendment, and a draft that is deliberately over its limit in one
fund so the validation demo has something to show.
**Look at:** `docs/SPEC.md` §9; `Infrastructure/Seed/` (Phase 1).

### General information worth having ready
- **Governmental vs. proprietary funds:** governmental funds (General,
  Special Revenue, Capital Projects, Debt Service, Permanent) account for
  tax-supported activities; proprietary funds (Enterprise, Internal Service)
  run like businesses on user charges. Fiduciary funds hold money for others.
- **Appropriation vs. expenditure:** appropriation is legal *authority* to
  spend; expenditure is the actual spend. This app budgets appropriations.
- **Amendment = supplemental appropriation ordinance.** Council passes a
  new ordinance; the app models it as a new version that copies the adopted
  one so the original stays immutable.
- **Why "snapshot" rather than "publish flag":** the budget that was
  public on a date is a record, not a view.

---

## Phase 1 — Foundation

### Q: Show me the domain model. Where are the rules?
**A:** `BudgetVersion` is the aggregate root: all changes to lines and
beginning balances go through it and every mutating method calls
`EnsureEditable()`, so "adopted is immutable" exists once. Value rules
(slug format, category-matches-type, expenditure-needs-department) live in
the entity constructors via a tiny `Guard` helper. Calculations that span
entities (`FundBalanceCalculator`, `AppropriationLimitCheck`) are pure
static functions on plain inputs so they're testable without EF.
**Look at:** `src/CivicBudget.Domain/Budgets/BudgetVersion.cs`,
`Common/Guard.cs`, `Budgets/FundBalanceCalculator.cs`.
**Tests:** `tests/CivicBudget.Domain.Tests/Budgets/*` — 160 tests, ~30 ms.

### Q: How does the tenant filter actually work under the hood?
**A:** `OnModelCreating` reflects over every entity implementing
`ITenantOwned` and adds `HasQueryFilter(e => e.GovernmentId == CurrentGovernmentId)`.
`CurrentGovernmentId` is an instance property on the context, so EF Core
compiles it as a SQL parameter evaluated per query rather than a constant
in the cached model. `Guid == Guid?` with a null tenant is false for every
row, so no tenant → no rows. Writes are checked separately by a
`SaveChangesInterceptor` because query filters don't apply to `Add`.
**Look at:** `CivicBudgetDbContext.ApplyTenantQueryFilter`,
`TenantSaveChangesInterceptor.Verify`.
**Tests:** `TenantIsolationTests` (6 tests, real SQL Server).

### Q: Why is the DbContext factory registered as Scoped?
**A:** The default factory lifetime is singleton, which can't resolve
scoped services. Scoped lets the `(serviceProvider, options)` overload pull
the current scope's tenant-aware interceptor into each context it creates.
That's how the signed-in user's tenant (per circuit) reaches the context
(per unit of work).
**Look at:** `src/CivicBudget.Infrastructure/DependencyInjection.cs`.

### Q: What happens if someone changes an entity and forgets a migration?
**A:** `MigrationTests.Model_snapshot_matches_the_current_model` calls
`HasPendingModelChanges()` against the migrated database and fails CI.
**Look at:** `tests/CivicBudget.IntegrationTests/MigrationTests.cs`.

### Q: How do you know every money column is `decimal(18,2)`?
**A:** A convention in `ConfigureConventions` sets it for every `decimal`
property, and an integration test queries `INFORMATION_SCHEMA.COLUMNS` for
any `decimal`/`float`/`money` column that isn't (18,2).
**Look at:** `CivicBudgetDbContext.ConfigureConventions`,
`MigrationTests.Every_decimal_column_is_decimal_18_2`.

### Q: Why Guid v7 ids?
**A:** Globally unique like any GUID (safe to generate client-side, safe
across tenants and future sharding) but time-ordered, so SQL Server's
clustered primary key doesn't fragment the way random GUIDs cause.
`Guid.CreateVersion7()` is built into .NET 9+.
**Look at:** `src/CivicBudget.Domain/Common/Entity.cs`.

### Q: Why Testcontainers instead of an in-memory provider or SQLite?
**A:** The things worth integration-testing — query filters, interceptors,
unique indexes with NULLs, `decimal(18,2)`, migrations — are exactly the
things in-memory providers don't emulate. Testcontainers runs the same
SQL Server 2022 image as local dev and CI; the suite takes ~2 s after
container start.
**Look at:** `tests/CivicBudget.IntegrationTests/SqlServerFixture.cs`.

### Q: How does the seed avoid being a pile of magic numbers?
**A:** Each line is declared once by its FY2025 budget; other years derive
from it with a deterministic hash-based variation (94–101% actuals, 3%
growth), and a couple of explicit FY2027 overrides create the story (a new
cruiser, a resurfacing program that over-appropriates the Street fund).
It runs through the domain API, so the seed obeys every rule.
**Look at:** `src/CivicBudget.Infrastructure/Seed/SeedLine.cs`,
`MapleRidgeSeed.cs`, `DevelopmentSeeder.cs`.

### Q: Central Package Management — why?
**A:** One `Directory.Packages.props` holds every version; project files
list packages without versions. No version drift between projects, one
place for Dependabot to update, and it pairs with the DECISIONS "Packages"
table that justifies each one.

### General information worth having ready
- **Banker's rounding vs. away-from-zero:** .NET's `Math.Round` default is
  `ToEven` (0.125 → 0.12); Excel and finance expect 0.13. We're explicit.
- **`HasQueryFilter` limits:** filters are per entity type, apply to
  navigations/Include as well, can be bypassed with `IgnoreQueryFilters()`
  (banned here), and don't affect `Add`/`Update` (hence the interceptor).
- **Testcontainers on Apple Silicon:** the SQL Server image is amd64 and
  runs under Rosetta; works, ~15 s to healthy.

---

## Phase 2 — Identity, authorization, admin maintenance

### Q: Walk me through what happens when a Finance Director logs in and opens the Funds page.
**A:** The login page is a static form post. `SignInManager` checks the
password, the claims factory builds the principal (id, name, role, plus our
`government_id`, `display_name`, and `department_id` claims), and Identity
writes the cookie. Opening `/admin/funds` is a normal HTTP request first:
`UseAuthentication` reads the cookie, `[Authorize(Policy = CanMaintainSetup)]`
passes, and the page is pre-rendered. Then the browser opens the SignalR
circuit, which gets its own DI scope; `CurrentUserCircuitHandler` copies the
authentication state into that scope's `CurrentUserContext`, so the
`FundService` the grid calls creates a DbContext whose tenant filter is the
FD's government.
**Look at:** `Web/Security/CurrentUserCircuitHandler.cs`,
`Infrastructure/Identity/ApplicationUserClaimsPrincipalFactory.cs`.

### Q: Role-based vs policy-based vs resource-based authorization. Which do you use and why?
**A:** All three, in layers. Roles are the data (a user has one). Policies
are the vocabulary pages use (`CanPublish`), mapped to roles in one file so
the mapping is testable and changeable. Resource-based handles the one rule
roles can't express: a Department Head may edit a line only if it is in
their department and the version is still Draft. That rule is a pure
function shared by the handler, the services, and the tests.
**Look at:** `Web/Security/AuthorizationPolicies.cs`,
`Application/Security/BudgetLinePermissions.cs`.
**Tests:** `AuthorizationPolicyTests` (every policy × every role),
`BudgetLinePermissionsTests`.

### Q: How do you test authorization without a browser?
**A:** Build a `ServiceCollection` with `AddAuthorizationBuilder().AddCivicBudgetPolicies()`
and the handler, resolve the real `IAuthorizationService`, and call
`AuthorizeAsync` with hand-built `ClaimsPrincipal`s. It exercises the same
code the `[Authorize]` attribute uses.
**Look at:** `tests/CivicBudget.Web.Tests/Security/AuthorizationPolicyTests.cs`.

### Q: Why is the users table not covered by the tenant query filter?
**A:** Login has to find the user before a tenant is known. So
`ApplicationUser` carries `GovernmentId` but is not `ITenantOwned`, and
`UserAdminService` scopes every query explicitly. It is the one documented
exception (ADR-0015) and it has tests proving one tenant's admin cannot read,
edit, or lock another tenant's user, nor assign one of its departments.
**Look at:** `Infrastructure/Identity/UserAdminService.cs`,
`UserAdminServiceTests`.

### Q: Your Application project references EF Core. Isn't that a layering violation?
**A:** It references the EF Core abstractions package for the LINQ surface
(`DbSet`, `Include`, `ToListAsync`) through an `ICivicBudgetDbContext`
interface; it does not reference the SQL Server provider, Identity, or
ASP.NET Core, and the Domain references nothing. That is the trade-off in
ADR-0014: expressive queries and no hand-written repositories, at the cost
of Application knowing EF Core's query shape. The architecture test pins
exactly which assemblies each layer may reference.
**Look at:** `Application/Persistence/ICivicBudgetDbContext.cs`,
`ArchitectureTests`.

### Q: How do validation errors get from FluentValidation to the screen?
**A:** Services return a `Result` with `(PropertyName, Message)` errors. The
component calls `editContext.ApplyErrors(store, result)`, which adds them to
Blazor's `ValidationMessageStore` so `<ValidationMessage For>` shows each one
under its field. The component doesn't know FluentValidation exists; the
validator doesn't know Blazor exists.
**Look at:** `Application/Common/Result.cs`,
`Web/Components/Common/EditContextResultExtensions.cs`, `FundEdit.razor`.

### Q: Why are the login pages static SSR while the admin pages are Interactive Server?
**A:** A sign-in must set a cookie on an HTTP response; a circuit has no
response to set it on. So Account pages are plain form posts marked
`[ExcludeFromInteractiveRouting]`, and `RedirectToLogin` uses a full-page
navigation. Admin pages need live grids and validation, so they run on a
circuit.
**Look at:** `Components/Account/Pages/_Imports.razor`, `Login.razor`,
`Account/Shared/RedirectToLogin.razor`.

### Q: What happens to an open session when an admin locks a user?
**A:** `UserAdminService` rotates the user's security stamp. The
`IdentityRevalidatingAuthenticationStateProvider` re-checks the stamp every
30 minutes on each circuit, so the session ends without waiting for the
cookie to expire. A shorter interval is a one-line change.

### Q: What did you leave out of Identity and why?
**A:** Self-registration (accounts are provisioned by an admin), external
logins, passkeys, and two-factor. Each adds pages and attack surface
without serving the scenario; a real deployment would more likely federate
to the county's identity provider than turn them on.

### General information worth having ready
- `AddIdentityCore` vs `AddIdentity`: Core registers users/roles/managers
  without the cookie and UI defaults; the app adds cookies explicitly and
  owns its pages.
- The cookie carries the claims. Changing a role takes effect at the next
  sign-in or security-stamp revalidation, not instantly.
- `[Authorize]` on a Razor component is enforced by `AuthorizeRouteView`
  during routing; for static SSR it is enforced by the endpoint metadata.
- Bootstrap is served from `wwwroot/lib` (no CDN) so the admin app has no
  external runtime dependency and works on an air-gapped network.

---

## Phase 3 — Budget entry, fund balances, audit trail

### Q: How does the audit trail work, and why an interceptor?
**A:** `AuditInterceptor` is an EF Core `SaveChangesInterceptor`. Inside
`SaveChanges` it walks the change tracker, and for every entity marked
`[Audited]` it appends `AuditEntry` rows: one for a create or delete, one
per changed property for an update, each with old value, new value, user,
and UTC time. The rows are added to the same context before the save
continues, so they commit in the same transaction as the change. No
service has to remember to write audit rows, and none can forget.
**Look at:** `Infrastructure/Persistence/Interceptors/AuditInterceptor.cs`,
`Domain/Auditing/AuditEntry.cs`, `Domain/Common/AuditedAttribute.cs`.
**Tests:** `AuditInterceptorTests` (5).
**Rejected:** database triggers (invisible to the code, no user context),
temporal tables (great for point-in-time queries, but they do not record
*who*, and they version whole rows rather than answering "what changed").

### Q: What does a Department Head see, and how do you stop them editing other departments?
**A:** `GetWorkspaceAsync` filters lines to the departments in the user's
claims and sets `CanEdit` per line with `BudgetLinePermissions`. Every
mutation re-loads the version and re-checks the same rule against the
line's department and the version's *current* status, so a stale screen or
a guessed id cannot edit what the user may not. Fund balances are always
whole-fund figures, because a partial fund total would make the
appropriation check meaningless.
**Look at:** `Application/Budgets/BudgetEntryService.cs` (`GetWorkspaceAsync`, `LoadLineForEditAsync`).
**Tests:** `BudgetEntryServiceTests` (9).

### Q: Why reload the whole workspace after each edit?
**A:** Correctness over cleverness: the fund balance panel and subtotals
are always computed from saved data. At village scale it is one query for
~100 lines. If scale demanded it, the service could return a delta and the
components would not change, because they only render the DTO.

### Q: Tell me about a bug you hit and what you learned.
**A:** Adding a line through the aggregate threw a concurrency exception:
EF tracked the new child as Modified. Our entities assign Guid v7 keys in
their constructors, and EF's default for Guid keys is "generated on add",
so when it discovered the child through the parent's collection it saw a
set key and assumed the row existed. Marking `Id` as `ValueGeneratedNever`
for every entity (a convention in `OnModelCreating`) fixed it with no
schema change. Lesson: EF decides Added vs Modified for discovered entities
from key-generation metadata, not from whether the row exists.
**Look at:** `CivicBudgetDbContext.UseClientGeneratedKeys`.

### Q: How is the by-department view built? Where are the subtotals computed?
**A:** `BudgetGrouping.ByDepartment` is a pure function in Application that
builds department → fund → category groups; `LineGroup` exposes `Amount`,
`CurrentYearBudget`, `DollarChange`, `PercentChange` as sums of its lines and
children. The component only walks the tree and prints. Unit-tested
without a database or a renderer.
**Look at:** `Application/Budgets/BudgetGrouping.cs`, `BudgetGroupingTests`.

### Q: What about two Finance Directors editing the same line at once?
**A:** Last write wins today, and the audit trail shows both writes. A
`rowversion` concurrency token plus a "this line changed since you loaded
it" message is the standard EF Core answer and would be a small change; it
was not in the spec, so it is documented as a known gap rather than built.

### Q: Why is Bootstrap's table styling fighting QuickGrid?
**A:** QuickGrid ships a default theme with its own cell padding at higher
specificity. Its documented escape hatch is a different `Theme` name, which
disables the built-in styles so `.table-sm` and app CSS take over. Sorting
and pagination are behavior, not styling, so they keep working.

### General information worth having ready
- **Interceptor order:** audit first, tenant check second, so audit rows are
  also verified. Order is the `AddInterceptors` argument order.
- **`TimeProvider`** (in .NET 8+) replaces hand-rolled `IClock`
  abstractions; `TimeProvider.System` in production, a fake in tests.
- **`Modified` vs `Added` on graph discovery:** EF uses key-generation
  metadata; `Add()` on the DbSet forces Added regardless.
- **Ohio context:** the fund balance panel is what a fiscal officer checks
  against the Certificate of Estimated Resources; the amendment workflow in
  Phase 4 is the supplemental appropriation ordinance.

---

## Phase 4 — Workflow, amendments, publishing

### Q: Walk me through what happens when the Finance Director clicks Adopt.
**A:** The dialog collects the resolution number (and an acknowledgement if
the government is in Warn mode and a fund is over its limit). The page
calls `BudgetWorkflowService.AdoptAsync`, which checks the role, reloads the
aggregate, evaluates the appropriation limit in the government's mode,
calls the domain's `version.Adopt(...)` (which itself refuses anything but
Proposed), writes an `AuditKind.Event` row "Adopted by resolution X", marks
any previously adopted version of that year superseded if this is an
amendment, and saves. The workspace reloads and is read-only.
**Look at:** `Application/Budgets/BudgetWorkflowService.cs` (`TransitionAsync`).
**Tests:** `WorkflowAndPublishingTests` (9), `BudgetWorkflowTests` (domain).

### Q: How is the Ohio appropriation limit enforced, and why at the transition rather than on save?
**A:** Every save recalculates and shows the fund balances (Phase 3), but
enforcement happens at Draft→Proposed and Proposed→Adopted so staff can
save a budget that is temporarily out of balance. In Block mode the
transition is refused with a message naming the problem; in Warn mode it
needs an explicit acknowledgement. Both are tested against seeded data
where the Street fund is $35,908 over.
**Look at:** `BudgetWorkflowService.TransitionAsync`, `WorkflowStateDto`.

### Q: Why is an amendment a whole copy rather than a diff?
**A:** Because the adopted original must stay exactly as council adopted
it, and because in Ohio a supplemental appropriation ordinance replaces the
appropriation measure. The copy is a new Draft that goes through the same
workflow with its own resolution number; on adoption the original is marked
superseded but never changed.
**Look at:** `BudgetVersion.CreateAmendment`, `BudgetWorkflowService.CreateAmendmentAsync`.

### Q: How does publishing work, and what stops the portal showing a draft?
**A:** Publishing captures an adopted version into three denormalized
tables with every name copied. The portal reads those through
`PublicPortalDbContext`, which maps only those three tables, filters to
active snapshots globally, and throws on `SaveChanges`. So a draft is
unreachable by construction: not in the tables, not in the model, not in
the filter. Unpublish flips a status and keeps the rows.
**Look at:** `Domain/Publishing/PublishedBudgetSnapshot.cs`,
`Application/Publishing/PublishingService.cs`,
`Infrastructure/Persistence/PublicPortalDbContext.cs`.
**Tests:** `Portal_context_sees_only_active_snapshots_by_slug_and_nothing_live`,
`Portal_context_cannot_write`, `Unpublish_keeps_the_snapshot_but_hides_it_from_the_portal`.

### Q: Two DbContexts on one database. How do you keep their mappings in sync?
**A:** One shared `PublishedSnapshotModel.Configure(ModelBuilder)` that both
contexts call. The admin context adds only the foreign key to Governments
(which the portal must not map). The admin context owns the migrations; the
portal context has none. `dotnet ef` needs `--context` now.
**Look at:** `Persistence/Configurations/PublishedSnapshotConfiguration.cs`.

### Q: What is superseded vs unpublished?
**A:** Superseded: replaced by a newer publish of the same fiscal year
(after an amendment). Unpublished: withdrawn by the Finance Director. Both
keep every row for history and both are invisible to the portal; the
publishing history table in the admin app shows all of them.

### Q: How did you build confirmation dialogs in Blazor Server?
**A:** A `ConfirmDialog` component that renders Bootstrap's modal markup
from a boolean, with the body as a `RenderFragment` and an `OnConfirm`
callback returning whether to close. No JavaScript, so the dialog can hold
inputs and validation. The workflow bar owns six of them.

### General information worth having ready
- **Audit events vs field changes:** the interceptor records what changed;
  services record why ("Proposed to council"). Both land in one table.
- **`db.BudgetVersions.Add(amendment)`** tracks the whole new graph as
  Added because the root is added explicitly, unlike children discovered
  through an existing root (ADR-0018).
- **Cache invalidation hook:** `IPublishedSnapshotCacheInvalidator` is
  called on publish/unpublish today and is a no-op until Phase 5.
