# Interview Prep — CivicBudget

Questions an interviewer is likely to ask about this project, the answer,
and **where to look** to back it up. Grows by one section per phase.
Read `SPEC.md` §10 (glossary) first if the fund-accounting terms are rusty.

How to use: for each question, be able to (1) give the one-sentence answer,
(2) open the file and point at the line, (3) name the alternative you
rejected and why.

---

## The 60-second pitch

CivicBudget is a multi-tenant budgeting tool for Ohio local governments: a
village finance director builds next year's appropriations fund by fund,
department heads enter their lines, the app checks every fund against its
certified estimated resources, council adopts, and the adopted budget is
published to a public transparency portal citizens browse without logging
in. It's .NET 10 and Blazor end to end. The admin side is Interactive
Server because staff need live grids and validation; the public side is
static server-rendered HTML with no JavaScript because it has to be fast,
cacheable, and accessible at scale. Data is in SQL Server through EF Core,
tenancy is enforced in the data layer with global query filters, every
change is audited by an interceptor, and the portal reads only immutable
published snapshots through a separate read-only context. Budgets round
trip through Excel (import with a validation preview, export of every
grid) and print as three standard reports. It ships as a container with a
CDK stack in C# that CI synthesizes and asserts, deployed over OIDC with
no stored AWS keys. Version 1.1 reframed it as a plug-in beside a
government ERP: the chart of accounts comes from the ERP through an
adapter, account numbers read the Ohio way (`1000-725-121`), an
administrator creates the logons, and a fire chief who signs in lands in
the fire department, enters the request against its accounts, writes the
narrative, and submits it to the fiscal officer. About 480 tests,
including a real SQL Server in Testcontainers, and every phase is a PR
with a written walkthrough.

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
`PublicPortalDbContext`, which maps only the snapshot tables (four, since 9d), filters to
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

---

## Phase 4.5 — Design pass

### Q: How did you decide what it should look like?
**A:** Research first. Three passes: which products Ohio governments
actually run (UAN, VIP, Tyler Munis, OpenGov, Springbrook, BS&A, with
named Ohio customers), what modern budgeting UIs do (Questica, Workday
Adaptive, OpenGov), and what transparency portals do well and badly (Ohio
Checkbook, OpenGov, ClearGov, Socrata). The brief distilled that into
"a modern civic ERP that fits in next to VIP": module navigation, dense
worksheets, toolbars, breadcrumbs, a workflow stepper, blue as the trust
color, executed with one primary and one accent, white cards on grey,
tabular numerals, one icon set, and designed states. Mockups were approved
before any UI code.
**Look at:** `docs/design/DESIGN-BRIEF.md`, `docs/design/research-*.md`, `docs/design/mockups.html`.

### Q: How is the theme implemented without a front-end build?
**A:** CSS custom properties for the tokens, mapped onto Bootstrap 5's own
`--bs-*` variables in the same `:root` block, plus a handful of component
rules. Bootstrap picks up the theme through its variables; there is no
Sass, Node, or bundler. Bootstrap Icons is vendored as CSS and font files.
**Look at:** `src/CivicBudget.Web/wwwroot/app.css` (first 60 lines).

### Q: How does a page set the breadcrumb in the layout?
**A:** Blazor parameters only flow down, so a scoped `AdminPageState`
service carries the crumbs: `PageHeader` sets them, the layout subscribes
to a `Changed` event and re-renders. Same pattern for toasts
(`ToastService` and a `ToastHost` in the layout).
**Look at:** `Components/Common/AdminPageState.cs`, `AdminLayout.razor`.

### Q: Why is the sticky grid header conditional on screen width?
**A:** A sticky header inside an `overflow-x: auto` wrapper sticks to the
wrapper rather than the page and hides the first rows. Above 1200px the
wrapper is `overflow: visible` and the header sticks under the top bar;
below it the wrapper scrolls sideways and the header does not stick.
**Look at:** `.cb-grid-wrap` in `app.css`.

### Q: What did you do for accessibility?
**A:** Tokens chosen for AA contrast, visible focus rings, Escape on
dialogs and the drawer, labelled landmarks and menus, `aria-current` on
the stepper, decorative icons hidden from assistive tech, reduced-motion
respected. A screen-reader pass is scheduled for Phase 8.

### General information worth having ready
- Bootstrap 5.3 exposes most of its theme as CSS variables; overriding
  `--bs-*` at `:root` is the supported no-build customization path.
- QuickGrid's `Theme` parameter: any value other than `default` disables
  its built-in styling.
- `prefers-reduced-motion` is a media query; respect it for anything that
  animates continuously (skeleton shimmer).

## Phase 5 — Public transparency portal

### Q: Why is the portal static SSR when the admin app is Interactive Server?
**A:** Different audiences with different costs. An Interactive Server
page holds a SignalR circuit (component tree, DI scope, WebSocket) per
visitor, which is right for twenty finance staff editing a worksheet and
wrong for thousands of anonymous citizens. A static SSR page is one HTTP
request that renders once and can be cached; there is no per-visitor
state. The switch is one attribute: the portal folder's `_Imports.razor`
adds `[ExcludeFromInteractiveRouting]`, the admin pages declare
`@rendermode InteractiveServer`.
**Look at:** `Components/Portal/_Imports.razor`, `docs/walkthroughs/06-public-portal.md` section 2.

### Q: How do you guarantee the portal can never show a draft?
**A:** Structurally, not by a filter someone could forget. The portal
reads only through `ISnapshotQueryService`, whose one implementation uses
`PublicPortalDbContext`, which maps just the snapshot tables with an
Active-only query filter and throws on `SaveChanges`. There is no code
path from a portal page to `BudgetVersions`. The integration test
`Unpublishing_removes_the_government_from_the_portal_immediately` shows
the row still exists for auditors while every portal query returns nothing.
**Look at:** `Infrastructure/Persistence/PublicPortalDbContext.cs`, `Infrastructure/Portal/SnapshotQueryService.cs`, ADR-0005/0006.

### Q: Walk me through the output caching.
**A:** Three pieces. A base `IOutputCachePolicy` enables caching only for
`GET /transparency/**`, keys by path and query, tags the entry
`portal:{slug}`, and refuses to store non-200s or anything setting a
cookie. A middleware before `UseOutputCache` rewrites Blazor's
`Cache-Control: no-store` to `public, max-age=600` and drops the
antiforgery cookie in `OnStarting`. And the Application-level
`IPublishedSnapshotCacheInvalidator`, which `PublishingService` has called
since Phase 4, is now implemented with `EvictByTagAsync`. Publish an
amendment and that government's pages render fresh on the next request.
**Look at:** `Web/Caching/*.cs`, `Program.cs`, ADR-0021.

### Q: Why did you need a custom policy instead of `[OutputCache]`?
**A:** Two reasons. `[OutputCache]` is not applied to Razor component
endpoints, so a per-page attribute would silently do nothing. And the
built-in default policy refuses to cache any response marked `no-store`,
which Blazor's SSR endpoint puts on every page. Registering the policy as
the base policy with `excludeDefaultPolicy: true` puts our rules in charge
while keeping the two that matter: GET only, never a response that still
sets a cookie.

### Q: Is it safe to remove the antiforgery cookie?
**A:** For these pages, yes: the antiforgery token protects POST forms,
and the portal has none (search is a GET form). The middleware only drops
the `Set-Cookie` header when every cookie in it is the antiforgery one; if
Identity or anything else ever sets a cookie on a portal response, it is
kept and the policy then refuses to cache that response.
**Look at:** `PortalResponseMiddleware.cs`, `PortalResponseMiddlewareTests.cs`.

### Q: Why no chart library?
**A:** A transparency portal's charts have to be readable without color,
without JavaScript, and on paper, and the numbers have to be checkable.
Horizontal CSS bars with the value beside each one, a `role="img"`
summary, and a `<details>` table with the same figures meet all of that
with no dependency, and the bUnit tests can assert the accessibility
promises directly. Sorted horizontal bars with labels are also what the
best portals converge on.
**Look at:** `Components/Portal/Common/Breakdown.razor`, `tests/CivicBudget.Web.Tests/Portal/BreakdownTests.cs`.

### Q: Where do the breakdowns get computed, and would that scale?
**A:** In memory, in `SnapshotQueryService`: one query loads the
snapshot's lines and funds (about a hundred rows for a village), then
LINQ `GroupBy`. With output caching in front that is one query per page
per six hours. A county with tens of thousands of lines would move the
grouping into SQL; the `ISnapshotQueryService` contract would not change.

### Q: What was the split-query change about?
**A:** Every aggregate load includes two collections (a version's Lines
and BeginningBalances; a snapshot's Lines and Funds). EF Core's default
single query JOINs both, repeating each line once per row of the other
collection, and logs a cartesian-product warning. Both contexts now
default to `QuerySplittingBehavior.SplitQuery`: one SQL statement per
collection. The trade-off is consistency between the statements, which
does not matter for immutable snapshots or a single user's draft.
**Look at:** `Infrastructure/DependencyInjection.cs`.

### Q: How do the downloads work?
**A:** Two minimal API GET endpoints under the portal prefix build an
`ExportTable` from `GetLinesAsync` and hand it to `CsvWriter` (no
package; UTF-8 BOM, CRLF, RFC 4180 quoting, invariant numbers) or
`ISpreadsheetExporter` (ClosedXML; typed cells, frozen bold header).
Being GETs under `/transparency`, the files are output-cached and evicted
with the pages.
**Look at:** `Components/Portal/PortalEndpoints.cs`, `Application/Export/`, `Infrastructure/Export/`.

### General information worth having ready
- Static SSR components get parameters once, cannot use `@onclick`, and
  bind forms via `[SupplyParameterFromForm]`/`[SupplyParameterFromQuery]`.
- `OutputCacheContext` flags: `EnableOutputCaching`, `AllowCacheLookup`,
  `AllowCacheStorage`, `Tags`, `CacheVaryByRules.QueryKeys`.
- `HttpResponse.OnStarting` is the last hook before headers are sent;
  inside the output cache's `ServeResponseAsync` headers are already read-only.
- `IOutputCacheStore` has an in-memory default and a Redis package
  (`Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`) for multi-instance hosting.

## Phase 6 — Import, export, reports

### Q: How does the import make sure a bad file cannot half-apply?
**A:** Two calls. `PreviewAsync` parses and classifies every row (Add,
Update, Unchanged, Error with the reason) and writes nothing.
`CommitAsync` takes the raw rows back, re-analyses them against the
database at that moment, refuses if any row errs, and applies the rest
through the `BudgetVersion` aggregate in one `SaveChangesAsync`. The
preview is a courtesy; the commit-time check is the gate, which also
covers the race where someone edits the budget between the two.
**Look at:** `Application/Import/BudgetImportService.cs`, `BudgetImportServiceTests`.

### Q: Where do the import rules live and how are they tested?
**A:** In `ImportAnalyzer.Analyze`, a static function over records:
codes resolve case-insensitively, inactive and unknown codes are named
in the error, expenditures need a department, money parses "$1,250.50"
and "(500)", negatives are refused, duplicate keys in the file flag the
second row, blank optional columns mean "leave as is". Nine unit tests
with no database, then the service test proves the same over SQL Server.
It mirrors `BudgetVersion.AddLine`'s guards, and commit still catches
`DomainException` as belt and braces.
**Look at:** `Application/Import/ImportAnalyzer.cs`, `ImportAnalyzerTests.cs`.

### Q: Why codes rather than ids in the file?
**A:** Clerks build the file in Excel from the chart of accounts they
know; a GUID column would be unusable. The import's column layout is the
same one the workspace exports, so export, edit, import is the round trip
and the export doubles as the template.

### Q: Does the import delete lines that are missing from the file?
**A:** No, and the page says so. A department's partial file must not
wipe another department's lines. Deleting is a deliberate act in the
workspace with its own audit row.

### Q: What does the audit trail show for an import?
**A:** One `AuditKind.Event` row on the version ("Imported budget.csv: 1
added, 1 updated, 0 unchanged") plus the interceptor's normal field-level
rows for each changed line, all under the importing user's name, in the
same transaction.

### Q: How did you parse CSV and XLSX?
**A:** CSV with a sixty-line RFC 4180 parser in Application (`CsvReader`:
quotes, doubled quotes, embedded newlines, BOM). XLSX with ClosedXML
behind `ISpreadsheetReader` in Infrastructure, which returns numbers as
invariant strings so both formats reach the analyser as text. Both
produce a `TabularFile`; `ImportFileParser` maps columns by name in any
order and numbers rows the way the spreadsheet does.

### Q: How do the export endpoints stay secure and tenant-scoped?
**A:** They are minimal API GETs under `/admin/export` with
`RequireAuthorization(policy)`, so the cookie sign-in and the same named
policies as the pages apply. `CurrentUserMiddleware` fills the tenant
context for a plain HTTP request, so the Application services see exactly
what a page would; a wrong tenant gets a 404 from the query filter, an
anonymous request is redirected to sign in.
**Look at:** `Components/Admin/AdminExportEndpoints.cs`.

### Q: Why are reports built from the workspace DTO?
**A:** So a report can never disagree with the screen beside it, and so
the tenant filter and the Department Head visibility rule are applied
once. `ReportBuilder` is pure over `BudgetWorkspaceDto`; `ReportTables`
maps each report to an `ExportTable` for XLSX next to its DTO. A
county-scale tenant would push grouping into SQL behind the same
`IReportService`.
**Look at:** `Application/Reports/ReportBuilder.cs`, `ReportBuilderTests.cs`, `ReportServiceTests`.

### Q: How does printing work?
**A:** CSS. `@media print` hides the shell (sidebar, top bar, page
header, toasts), removes the sticky header, and leads with the report
block that carries the government, version, and who prepared it. The
Print button calls `window.print`, the admin app's one JavaScript call.
No PDF library.

### General information worth having ready
- `InputFile` streams over the SignalR circuit; `OpenReadStream(maxAllowedSize)`
  throws `IOException` past the cap. Buffer to memory before handing to a service.
- Minimal API endpoints can be grouped (`MapGroup`) and take
  `RequireAuthorization` per group; `[FromServices]` resolves services in
  a lambda.
- `Results.File(bytes, contentType, fileName)` sets `Content-Disposition`.
- ClosedXML: `RangeUsed()` for the populated block, `DataType` per cell,
  sheet names max 31 characters.
- Blazor: a named `RenderFragment` parameter (`<Filters>`) means the rest
  of the child content must be wrapped in `<ChildContent>`.

## Phase 7 — AWS, deploy-ready

### Q: You never deployed this. What does "deploy-ready" mean here?
**A:** Everything up to `cdk deploy` exists and is exercised on every
commit: a multi-stage Dockerfile that CI builds, a CDK app in C# that CI
synthesizes with no credentials, 17 assertion tests on the CloudFormation
it produces, and a complete OIDC-based deploy workflow that is gated on a
repository variable. With an account, the steps are `cdk bootstrap`,
deploy the OIDC stack once, set one variable, push a tag. I chose that
over a free PaaS because a live URL on Fly.io says nothing about AWS.
**Look at:** ADR-0008, ADR-0023, `infra/README.md`.

### Q: Why Fargate behind an ALB instead of ECS Express Mode or Beanstalk?
**A:** I checked in September 2026. Express Mode is L1-only in CDK, one
container, default VPC public subnets, no custom domain: right for a
stateless API, wrong for an app with a private SQL Server. Beanstalk hides
the network and database wiring, which is exactly what I want to be able
to explain. `ApplicationLoadBalancedFargateService` is the ECS L2 pattern:
public ALB, private task, target group with health checks and sticky
sessions, circuit breaker with rollback, in about forty lines.
**Look at:** `infra/CivicBudget.Infra/CivicBudgetStack.cs`.

### Q: How do secrets reach the container?
**A:** By reference. The task definition names Secrets Manager entries;
ECS reads them at task start and injects them as environment variables.
The database password comes straight from the secret RDS generated, so
rotation needs no coordination. The app composes its connection string
from `Database:*` settings plus that password (`DatabaseOptions`). A test
asserts the two passwords are in `Secrets`, not `Environment`, and that
no `MasterUserPassword` literal exists in the template.
**Look at:** `DatabaseOptions.cs`, `CivicBudgetStackTests.Passwords_reach_the_container_as_secrets_never_as_environment_variables`.

### Q: How does GitHub deploy without AWS keys?
**A:** OIDC. GitHub signs a short-lived token for each workflow run; IAM
trusts GitHub's provider for one repository on `v*` tags or the
`production` environment, and returns one-hour credentials for a role that
can push to one ECR repository and assume the CDK bootstrap roles. No
`AWS_ACCESS_KEY_ID` exists in GitHub; the only value stored is the role
ARN, as a variable, not a secret. A test checks the trust condition and
that no IAM user or access key is created.
**Look at:** `GitHubOidcStack.cs`, `.github/workflows/deploy.yml`, `GitHubOidcStackTests`.

### Q: What breaks when you run a Blazor Server app in a container, and what did you do about it?
**A:** Three things. Data Protection keys default to the filesystem, so a
restart signs everyone out: I persist them in SQL Server
(`PersistKeysToDbContext`). Circuits are per instance, so the ALB has
sticky sessions. And TLS ends at the ALB, so forwarded headers are on and
HSTS and redirects see the original scheme. The one thing I left for a
second instance is a shared output cache store for the portal (Redis),
because an in-memory eviction on one task is invisible to the other.

### Q: What does it cost and how do you turn it off?
**A:** About $90 a month at list price: RDS SQL Server Express about $20,
Fargate about $18, the ALB about $16, and the NAT gateway about $33, which
is why the alarm is set at 80% of $60. `cdk destroy CivicBudget-App`
leaves nothing billing because every resource is `RemovalPolicy.DESTROY`
for the demo; a production account would flip RDS to deletion protection
and snapshot-on-delete, and the comments say so.

### Q: Why migrate on startup in AWS when you said a pipeline should do it?
**A:** One task, EF's migration lock, and a demo. `Database:MigrateOnStartup`
is a switch, on for the demo, off by default. With more than one task or a
real change-management process, the deploy workflow would run migrations
as a step (an ECS run-task or a migrations bundle) before the service
update. The switch keeps both stories honest.

### General information worth having ready
- CDK bootstrap creates the `cdk-*` roles (lookup, file publishing, image
  publishing, deploy) that the CLI assumes; the caller needs `sts:AssumeRole`
  on them and nothing else for CloudFormation.
- `Amazon.CDK.Assertions.Template.FromStack` synthesizes in-process; JSII is
  one Node process per test host, hence no xUnit parallelization.
- ECS `Secrets` versus `Environment` in a container definition; the
  execution role needs `secretsmanager:GetSecretValue` (the L2 grants it).
- RDS for SQL Server does not take a `DBName`; the app creates the database
  on first migration.
- ALB WebSockets need no configuration; stickiness is a target group attribute.

## Phase 8 — Polish

### Q: What changed in the final pass and why?
**A:** Spencer's review: every screen had a title and a sentence
explaining it, which is clutter to someone who uses the screen daily. The
sentences are gone. Where one actually helped (what "estimated resources"
means, how the import matches rows) it lives behind an ⓘ: a CSS-only tip
that is keyboard focusable and whose text is its accessible name, so a
screen reader still hears it once. Subtitles now carry data only. Same
pass fixed a real layout bug (a sticky-header rule meant for the workspace
made the overview's table overflow its card) and two keyboard defects
(dialogs and the drawer did not take focus when they opened).
**Look at:** `Components/Common/InfoTip.razor`, `ConfirmDialog.razor` (`OnAfterRenderAsync`), the `.cb-tip` block in `app.css`.

### Q: How did you check accessibility?
**A:** Three ways, none of them a badge: the accessibility tree of each
key screen (what a screen reader is given) to find unlabeled controls,
which turned up an unlabeled account-menu button, brand links without
names, and amount fields labeled only by an account code that repeats
across departments; a keyboard walk of the workflow, which found the focus
problem in the dialogs; and the rules already in the design system
(contrast tokens, focus rings, reduced motion, landmarks). What I did not
do is a full audit with a screen reader user, and I'd say so.

### Q: Where did the README screenshots come from?
**A:** A Playwright script in `scripts/screenshots/` that signs in, uploads
a sample import file, and captures each screen at 1440 px and the portal at
390 px, so they can be regenerated after any UI change instead of drifting.


## v1.1 — Plugging in beside the ERP (Phases 9a–9d)

### Q: What changed in v1.1 and why?
**A:** The framing. v1.0 was a stand-alone budgeting app that owned its
chart of accounts. The customer I'm interviewing with sells the ERP that
already owns the chart, so v1.1 makes CivicBudget the thing that plugs in
beside it: the chart arrives from the ERP (9b), account numbers read the
way an Ohio auditor writes them (9a), the government's own administrator
creates logons and assigns each to a department (9c), and a department
user's whole experience is their department (9d). Nothing in the fund
accounting core changed; the edges did.
**Look at:** `ROADMAP.md` v1.1 section; ADR-0024 to ADR-0027.

### Q: How does an Ohio account number work, and how do you store it?
**A:** Three ideas: fund, then program (what UAN calls the department for
appropriations) or receipt for revenues, then object. A village on UAN
writes `1000-725-121`: General Fund, Clerk/Treasurer, Salary; a revenue
line is two segments, `1000-110`. Counties and vendor ERPs write the same
three ideas with their own widths and words. So I store the three codes on
the line, as before, and compose the full number under a per-government
`AccountNumberFormat` (widths, separator, what the middle segment is
called). `AccountNumber.Compose` and `TryParse` are the whole of it.
Published snapshot lines freeze the composed number so a later format
change cannot rewrite what citizens saw.
**Look at:** `Domain/Accounts/AccountNumber.cs`, `AccountNumberFormat.cs`, `docs/research/ohio-account-numbers.md`, ADR-0024.

### Q: You don't have the ERP's export format. What did you build?
**A:** An adapter boundary and one honest implementation. `IErpChartSource`
returns an `ErpChart` (funds, departments, objects with code, name, type,
category, active). `ErpChartFileSource` reads a one-row-per-code CSV or
XLSX, forgiving about column order and spelling, and is the single class
to replace when the real layout is known. Everything after the adapter is
independent of it: a pure `ChartDiff` classifies each code as add, update,
deactivate, reactivate, or unchanged; `ChartSyncService` previews, then
applies through the entities so the audit interceptor records every field;
it never deletes, and it warns when a file would deactivate more than a
quarter of the chart, which usually means a partial export. Under
`ChartSource = Erp` the setup screens go read-only and the services refuse
writes, so there is one owner of the chart at a time.
**Look at:** `Application/Erp/` (`IErpChartSource`, `ChartDiff`, `ChartSyncService`, `ChartOwnership`), ADR-0025.

### Q: How does "the fire chief only sees the fire department" actually work?
**A:** Assignment is a claim. The administrator assigns departments to a
user; the claims factory puts each as a `department_id` claim in the
cookie; `ICurrentUser.DepartmentIds` reads them; and one pure function,
`BudgetLinePermissions.CanEdit`, decides for a line. Reads are scoped in
the services, not the pages: the workspace, reports, exports, search, and
the audit trail all filter by those ids. There is no `if (role ==
DepartmentHead)` in a component; components get `CanEdit` on each DTO.
**Look at:** `Application/Security/BudgetLinePermissions.cs`, `BudgetEntryService.GetWorkspaceAsync`, `AuditQueryService`, `PermissionsTests`.

### Q: How do administrators set passwords without knowing them?
**A:** They set a temporary one. Creating an account or resetting a password
sets `MustChangePassword`; the claims factory turns it into a claim;
middleware redirects any request carrying the claim to the change-password
page (except the account pages and static assets); the page clears the
flag and refreshes the sign-in so the cookie loses the claim. A reset also
rotates the security stamp, ending any session the user had. A claim
rather than a database lookup keeps the middleware free of I/O.
**Look at:** `Web/Security/MustChangePasswordMiddleware.cs`, `ChangePassword.razor`, `UserAdminService`, ADR-0026.

### Q: Why is "Administrator" a superset of the fiscal officer?
**A:** Because the customer said "complete and total access", and nothing
in a village or county's world needs a super-admin who cannot enter a
budget line. The change was mechanical: one helper,
`IsFiscalAuthority()`, replaced every `IsInRole(FinanceDirector)` in the
services, and the four fiscal policies include Admin. Role values in the
database did not change; the display names became the customer's words.
**Look at:** `CurrentUserExtensions.cs`, `AuthorizationPolicies.cs`, `AuthorizationPolicyTests`.

### Q: Walk me through the department round in 9d.
**A:** A `DepartmentRequest` row per department lives on the budget
version: in progress, submitted (who, when), or returned (the officer's
note). The version owns the rules: submit only while Draft and only with
lines; return only what was submitted, with a note; amendments copy the
narrative but start a new round. Submitting locks the department's lines
and narrative for department users through the same `CanEdit` rule, with
one more argument; the fiscal officer is unaffected. The workspace DTO
lists every department with its status and three flags decided for the
current user, so the department page, the board, the strip above the
grid, and the detail report read one shape. Submit and return are audited
as named events on the version.
**Look at:** `Domain/Budgets/DepartmentRequest.cs`, the department section of `BudgetVersion.cs`, `DepartmentRequestService.cs`, `DepartmentEntry.razor`, ADR-0027.

### Q: Why lock the department rather than block Propose until everyone submits?
**A:** Because the fiscal officer decides when the round is over. A
department that never submits should not hold council up, and the officer
can enter on a department's behalf. Locking the department instead gives
the officer a stable request to review and a Return action to reopen it,
which is what happens on paper: the request comes back with a note.
**Look at:** `BudgetVersion.SubmitDepartment` / `ReturnDepartment`; the alternatives paragraph of ADR-0027.

### Q: Where does the narrative go?
**A:** With the numbers, everywhere they go. It prints under the
department's heading on the Department Budget Detail report; it is frozen
into the published snapshot as one row per department
(`PublishedBudgetSnapshotDepartments`, mapped by the read-only portal
context too); and the portal's department page shows it as "From the
department". The seed writes narratives before FY2026 is adopted so they
travel through the amendment to the portal.
**Look at:** `PublishedBudgetSnapshot.Capture`, `SnapshotQueryService.GetDepartmentAsync`, `PortalDepartment.razor`.

### Q: What would you ask the customer before shipping this?
**A:** Three things I assumed. The ERP's real chart export layout (I built
against a stand-in and isolated it to one class). Whether the middle
segment is per government or per fund (I made it per government). Whether
a department's narrative should be public at all (I publish it, because
budget books do, but that is a policy question for the fiscal officer).

## Phase 10 — Branding and profile pictures

### Q: How do profile pictures work without an image library or blob storage?
**A:** The browser does the resizing. Blazor's `RequestImageFileAsync` draws
the chosen file onto a canvas at 256 px and gives me a PNG; the server checks
type and size and stores the bytes in their own table, separate from the
user row so lists never load images. The image is served from a URL that
carries the upload time as a version, with a year-long private cache: a new
upload is a new URL, so nothing is stale and nothing is re-fetched. The
endpoint requires sign-in and the read joins to the user's government, the
same rule as every Identity read. When there is an AWS account, S3 is a
one-class change behind `IUserAvatarService`.
**Look at:** `ProfilePicture.razor`, `UserAvatarService.cs`, the `/Account/Avatar/{id}` endpoint in `IdentityEndpoints.cs`, ADR-0028.

### Q: How does the top bar update after an upload without a reload?
**A:** The scoped service caches versions per circuit and raises `Changed`
after a set or remove; every `Avatar` component that asked the service for
its version subscribes and re-queries. Lists that already carry the version
in their DTO do not subscribe; they just render.
**Look at:** `Components/Common/Avatar.razor`.

### Q: Why is there a `[NotAudited]` attribute now?
**A:** Because the department round writes a named event ("Police submitted
its budget request") and, until this phase, the interceptor also wrote
"changed Submitted by user id: seed → c199…" and a raw timestamp. Same fact
twice, one of them unreadable. The attribute opts a property out when a
named event already records the change; Status, Narrative, and the return
note are still audited field by field.
**Look at:** `Domain/Common/AuditedAttribute.cs`, `AuditInterceptor.IsOptedOut`.

## Phase 12 — Portal polish

### Q: The portal context was "snapshot tables only". Why does it map the logo table now, and is that a hole?
**A:** A government's logo is live data the portal has to show, and copying it into every
snapshot would mean republishing a budget to change a logo. So I made one deliberate exception
and wrote it down: `GovernmentLogos` holds a government id, a content type, bytes, and a
timestamp, nothing else. Mapping it read-only cannot expose a draft line, a user, or a setting.
The lookup goes through an active snapshot's government id, so a government with nothing
published has no public face. The integration test that lists the portal context's tables pins
it to exactly five.
**Look at:** `PublicPortalDbContext`, `GovernmentLogoService`, `SnapshotQueryService.GetLogoAsync`, ADR-0029.

### Q: How do the sliding panels work without JavaScript?
**A:** Two radio inputs styled as tabs and a track two panels wide. `:checked` on the second
radio translates the track by one panel and hides the other with `visibility` (so its links
leave the tab order) and `max-height: 0` after the slide (so the page is only as tall as the
panel on screen). Both panels are in the HTML, so find-in-page, reader mode, and screen
readers see everything. The $/% toggle reloads the page, so it carries `?view=revenue` to land
on the same panel. Reduced motion turns the slide off.
**Look at:** `Components/Portal/Common/PortalPanels.razor`, the `.pt-panels` block in `app.css`.

## Phase 13 — Live demo on Azure

### Q: The repo has an AWS CDK stack. Why is the live demo on Azure?
**A:** Money and SQL Server. The app needs a real SQL Server and a persistent process, and
Azure is the one place both are free: the Azure SQL free offer is serverless General Purpose
under a monthly vCore-second limit, and Container Apps' consumption plan has a free grant and
scales to zero. AWS's free tier is now a six-month credit that does not cover RDS SQL Server,
and the NAT gateway in the CDK stack alone is about $32 a month. So the AWS stack is the
production-shaped design, tested in CI, and Azure is the free showcase. Same image, same
`DatabaseOptions`; the template is 150 lines of Bicep.
**Look at:** `infra/azure/main.bicep`, `scripts/azure-setup.sh`, `.github/workflows/deploy-azure.yml`, ADR-0030.

### Q: Five logins and a password are in the README. How is that safe?
**A:** They reach the demo tenant's data and nothing else: no host, no secrets, no other
government. Whatever a visitor does, including changing a password or locking the admin out,
is undone by a scheduled job at 08:00 UTC that runs the same image with `--reseed`. That drops
every table, migrates from nothing, and seeds; it keeps the database object because on Azure
that is the free-offer resource. The demo password is used nowhere else.
**Look at:** `DatabaseInitializer.ResetAsync`, the `resetJob` resource in `main.bicep`, `DatabaseResetTests`.

### Q: What does scale-to-zero do to a Blazor Server app?
**A:** The first request after an idle hour wakes two things: the container (about 15 seconds
of platform time) and the serverless database (about 45 seconds resuming from auto-pause). At
first the app awaited migrations before `RunAsync`, so Kestrel was not listening and the
visitor stared at a blank page for the whole minute. Now `DatabaseStartupService` migrates and
seeds in the background, the host listens within seconds, and `WakingUpMiddleware` answers page
requests with a 503 waiting screen that polls `/health/startup` and continues on its own
(ADR-0031). The platform's startup probe hits `/health`, which is liveness only, every two
seconds. Once warm it behaves normally. One replica at most, which the in-process output cache
already required; a second replica would need a distributed cache and a SignalR backplane, and
the ADR says so.
The first version of that change shipped and did nothing, which is the more interesting half of
the story. The logs showed `Now listening` seventeen milliseconds after the migration check and
fifty-two seconds after the process started, so something ahead of the web host service was
blocking. It was Data Protection: `AddDataProtection` registers a hosted service that reads the
key ring at startup, the keys are in SQL Server, and EF retried that read against the resuming
database for the better part of a minute. Dropping that one registration leaves the provider's
lazy load and took time-to-first-page from 31 seconds to 1 against a database that hangs.
**Look at:** `Web/Startup/` (five small files), the health-check mapping in `Program.cs`, `WakingUpMiddlewareTests`, `DataProtectionStartupTests`.
