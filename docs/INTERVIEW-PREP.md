# Interview prep

Questions an interviewer is likely to ask about this project, the answer,
and **where to look** to back it up. Grows by one section per phase.
Read `SPEC.md` §10 (glossary) first if the fund-accounting terms are rusty.

How to use: for each question, be able to (1) give the one-sentence answer,
(2) open the file and point at the line, (3) name the alternative you
rejected and why.

---

## The 60-second pitch

CivicBudget is budgeting and transparency software for Ohio local governments. Each
department enters its request against its own accounts and submits it with a written
narrative; the fiscal officer assembles the budget and sees every fund checked live
against its certified estimated resources, the Ohio appropriation limit; council adopts
it by resolution; amendments during the year are new versions; and the adopted budget is
published to a public portal that citizens browse without signing in. It is designed to
sit beside the government's ERP: the chart of accounts syncs in from the ERP through a
small adapter, and account numbers read the way Ohio writes them (`1000-725-121`).

Technically it is .NET 10 and Blazor end to end. The admin app is Interactive Server
because staff need live grids; the portal is static HTML with no JavaScript because it
has to be fast, cacheable, and accessible. Data is SQL Server through EF Core. Tenancy is
enforced in the data layer by query filters and a save interceptor, every change is
audited by another interceptor, concurrent edits are caught by a revision on the budget,
and the portal reads only immutable published snapshots through a separate read-only
context, so it cannot show a draft even by mistake. It is live on Azure's free tier,
deploy-ready on AWS with a CDK stack in C#, has about 640 tests including a real SQL
Server in Docker, and every phase is a pull request with a written walkthrough.

---

## Phase 0: Planning and architecture

### Q: Walk me through the architecture.
**A:** Four projects with inward-pointing dependencies: `Domain` (entities
and rules, no framework references), `Application` (use cases, DTOs,
validators, interfaces), `Infrastructure` (EF Core, Identity stores, Excel
I/O; it implements the interfaces), and `Web` (Blazor + composition root).
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
**Look at:** `docs/ARCHITECTURE.md` §3; `App.razor` (the render mode is set once,
and the portal folder opts out with `[ExcludeFromInteractiveRouting]`). ADR-0002.

### Q: How do you stop one government from seeing another's budget?
**A:** Every tenant-owned entity carries `GovernmentId`. The `DbContext`
adds a global query filter for each of them bound to an `ITenantContext`,
which is filled from the signed-in user's `government_id` claim. A
`SaveChanges` interceptor rejects any write for another government. If no
tenant is set, the filter returns *nothing*, which is the safe default. The
portal never sets a tenant: it reads through its own context and takes the
government's slug from the URL. Integration
tests seed two tenants and prove the other's rows are invisible.
**Look at:** `docs/ARCHITECTURE.md` §5; `CivicBudgetDbContext.OnModelCreating`
and `TenantSaveChangesInterceptor` (Phase 1). ADR-0004.
**Rejected:** database-per-tenant (operationally heavier than the project
needs), SQL row-level security (good later as defense-in-depth).

### Q: Why can't the public site just show the adopted budget?
**A:** Three reasons. Security: a forgotten `.Where(status == Adopted)`
would leak drafts, so the portal uses a separate read-only `DbContext` that
doesn't even map the live tables. Immutability: renaming an account next
year shouldn't rewrite what citizens saw last year, so the snapshot carries
its own copies of names and codes. Performance: denormalized rows
aggregate with a `GROUP BY` and invalidate with one cache tag.
**Look at:** `docs/ARCHITECTURE.md` §6; `PublicPortalDbContext` (Phase 4).
ADR-0005, ADR-0006.

### Q: Why `IDbContextFactory` instead of injecting `DbContext`?
**A:** In Blazor Server the DI scope is the *circuit*, not the request. A
scoped `DbContext` would live as long as the browser tab, accumulate tracked
entities, and be shared by concurrent event handlers, and `DbContext` isn't
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
**Look at:** ADR-0007; each validator sits beside its request, e.g. `SaveFundRequestValidator` in `Application/Setup/FundService.cs`.

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
fees, restricted by law), Capital Projects, and Water and Sewer enterprise funds.
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

## Phase 1: Foundation

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
**Tests:** `tests/CivicBudget.Domain.Tests/Budgets/*` (160 tests in about 30 ms at the time; 222 now).

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
**A:** The things worth integration-testing (query filters, interceptors,
unique indexes with NULLs, `decimal(18,2)`, migrations) are exactly the
things in-memory providers don't emulate. Testcontainers runs the same
SQL Server 2022 image as local dev and CI. The whole suite (100 tests) runs in
about 40 seconds, most of it starting the container.
**Look at:** `tests/CivicBudget.IntegrationTests/SqlServerFixture.cs`.

### Q: How does the seed avoid being a pile of magic numbers?
**A:** Each line is declared once by its FY2025 budget; other years derive
from it with a deterministic hash-based variation (94–101% actuals, 3%
growth), and a couple of explicit FY2027 overrides create the story (a new
cruiser, a resurfacing program that over-appropriates the Street fund).
It runs through the domain API, so the seed obeys every rule.
**Look at:** `src/CivicBudget.Infrastructure/Seed/SeedLine.cs`,
`MapleRidgeSeed.cs`, `DevelopmentSeeder.cs`.

### Q: Why Central Package Management?
**A:** One `Directory.Packages.props` holds every version; project files
list packages without versions. No version drift between projects, one
place for Dependabot to update, and it pairs with the DECISIONS "Packages"
table that justifies each one.

### General information worth having ready
- **Banker's rounding vs. away-from-zero:** .NET's `Math.Round` default is
  `ToEven` (0.125 → 0.12); Excel and finance expect 0.13. I'm explicit.
- **`HasQueryFilter` limits:** filters are per entity type, apply to
  navigations/Include as well, can be bypassed with `IgnoreQueryFilters()`
  (kept out of application code), and don't affect `Add`/`Update` (hence the interceptor).
- **Testcontainers on Apple Silicon:** the SQL Server image is amd64 and
  runs under Rosetta; works, ~15 s to healthy.

---

## Phase 2: Identity, authorization, admin maintenance

### Q: Walk me through what happens when the Fiscal Officer signs in and opens the Funds page.
**A:** The login page is a static form post. `SignInManager` checks the
password, the claims factory builds the principal (id, name, role, plus our
`government_id`, `display_name`, and `department_id` claims), and Identity
writes the cookie. Opening `/admin/funds` is a normal HTTP request first:
`UseAuthentication` reads the cookie, `[Authorize(Policy = CanMaintainSetup)]`
passes, and the page is pre-rendered. Then the browser opens the SignalR
circuit, which gets its own DI scope; `CurrentUserCircuitHandler` copies the
authentication state into that scope's `CurrentUserContext`, so the
`FundService` the grid calls creates a DbContext whose tenant filter is the
Fiscal Officer's government.
**Look at:** `Web/Security/CurrentUserCircuitHandler.cs`,
`Infrastructure/Identity/ApplicationUserClaimsPrincipalFactory.cs`.

### Q: Role-based vs policy-based vs resource-based authorization. Which do you use and why?
**A:** All three, in layers. Roles are the data (a user has one). Policies
are the vocabulary pages use (`CanPublish`), mapped to roles in one file so
the mapping is testable and changeable. Resource-based handles the one rule
roles can't express: a department user may edit a line only if it is in
their department, the version is still Draft, and the department has not
submitted its request. That rule is a pure
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

## Phase 3: Budget entry, fund balances, audit trail

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

### Q: What does a department user see, and how do you stop them editing other departments?
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
EF tracked the new child as Modified. The entities assign Guid v7 keys in
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

### Q: What about two people editing the same budget at once?
**A:** At this stage the last write won, and the audit trail showed both. I
wrote it down as a known gap and closed it in Phase 18 with optimistic
concurrency on the budget version (see the Phase 18 answers below): an edit
racing an adoption was the case that mattered, not two edits to one line.

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

## Phase 4: Workflow, amendments, publishing

### Q: Walk me through what happens when the Fiscal Officer clicks Adopt.
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
(after an amendment). Unpublished: withdrawn by the Administrator or Fiscal
Officer. Both
keep every row for history and both are invisible to the portal; the
publishing history table in the admin app shows all of them.

### Q: How did you build confirmation dialogs in Blazor Server?
**A:** A `ConfirmDialog` component that renders Bootstrap's modal markup
from a boolean, with the body as a `RenderFragment` and an `OnConfirm`
callback returning whether to close. No Bootstrap JavaScript, so the dialog
can hold inputs and validation. The workflow bar owns six of them. (Phase 18
added a few lines of script that trap Tab inside an open dialog and return
focus to the button that opened it.)

### General information worth having ready
- **Audit events vs field changes:** the interceptor records what changed;
  services record why ("Proposed to council"). Both land in one table.
- **`db.BudgetVersions.Add(amendment)`** tracks the whole new graph as
  Added because the root is added explicitly, unlike children discovered
  through an existing root (ADR-0018).
- **Cache invalidation hook:** `IPublishedSnapshotCacheInvalidator` is
  was called on publish and unpublish from the start, as a no-op until
  Phase 5 built the cache it clears.

---

## Phase 4.5: Design pass

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
respected. Phase 8 added a pass over the accessibility tree and a keyboard walk.

### General information worth having ready
- Bootstrap 5.3 exposes most of its theme as CSS variables; overriding
  `--bs-*` at `:root` is the supported no-build customization path.
- QuickGrid's `Theme` parameter: any value other than `default` disables
  its built-in styling.
- `prefers-reduced-motion` is a media query; respect it for anything that
  animates continuously (skeleton shimmer).

## Phase 5: Public transparency portal

### Q: Why is the portal static SSR when the admin app is Interactive Server?
**A:** Different audiences with different costs. An Interactive Server
page holds a SignalR circuit (component tree, DI scope, WebSocket) per
visitor, which is right for twenty finance staff editing a worksheet and
wrong for thousands of anonymous citizens. A static SSR page is one HTTP
request that renders once and can be cached; there is no per-visitor
state. The switch is one attribute: the portal folder's `_Imports.razor`
adds `[ExcludeFromInteractiveRouting]`, and everything else gets Interactive
Server from `App.razor`, which sets the mode once on `Routes`.
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
`GET /transparency/**`, keys by path and the three query values the pages
read (`q`, `show`, `view`), tags the entry
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

## Phase 6: Import, export, reports

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
the tenant filter and the department user's visibility rule are applied
once. `ReportBuilder` is pure over `BudgetWorkspaceDto`; `ReportTables`
maps each report to an `ExportTable` for XLSX next to its DTO. A
county-scale tenant would push grouping into SQL behind the same
`IReportService`.
**Look at:** `Application/Reports/ReportBuilder.cs`, `ReportBuilderTests.cs`, `ReportServiceTests`.

### Q: How does printing work?
**A:** CSS. `@media print` hides the shell (sidebar, top bar, page
header, toasts), removes the sticky header, and leads with the report
block that carries the government, version, and who prepared it. The
Print button calls `window.print`. No PDF library.

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

## Phase 7: AWS, deploy-ready

### Q: You never deployed this. What does "deploy-ready" mean here?
**A:** Everything up to `cdk deploy` exists and is exercised on every
commit: a multi-stage Dockerfile that CI builds, a CDK app in C# that CI
synthesizes with no credentials, 18 assertion tests on the CloudFormation
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

## Phase 8: Polish

### Q: What changed in the final pass and why?
**A:** My own review: every screen had a title and a sentence
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


## v1.1: Plugging in beside the ERP (Phases 9a–9d)

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

## Phase 10: Branding and profile pictures

### Q: How do profile pictures work without an image library or blob storage?
**A:** The browser does the resizing. Blazor's `RequestImageFileAsync` draws
the chosen file onto a canvas at 256 px and gives me a PNG; the server checks
the size and that the first bytes really are a PNG, JPEG, or WebP, and stores the bytes in their own table, separate from the
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

## Phase 12: Portal polish

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

## Phase 13: Live demo on Azure

### Q: The repo has an AWS CDK stack. Why is the live demo on Azure?
**A:** Money and SQL Server. The app needs a real SQL Server and a persistent process, and
Azure is the one place both are free: the Azure SQL free offer is serverless General Purpose
under a monthly vCore-second limit, and Container Apps' consumption plan has a free grant and
scales to zero. AWS's free tier is now a six-month credit that does not cover RDS SQL Server,
and the NAT gateway in the CDK stack alone is about $32 a month. So the AWS stack is the
production-shaped design, tested in CI, and Azure is the free showcase. Same image, same
`DatabaseOptions`; the template is 150 lines of Bicep.
**Look at:** `infra/azure/main.bicep`, `scripts/azure-setup.sh`, `.github/workflows/deploy-azure.yml`, ADR-0030.

### Q: Six logins and a password are in the README. How is that safe?
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
What is left is Azure's: about 15 seconds provisioning a sandbox before the image is pulled,
against 0.3 seconds of the app's own startup. One warm replica would remove it for about $4 to
$5 a month; for a portfolio demo I kept it free and wrote the trade-off down (ADR-0031). A warm
container also exposed a subtler case: the database can pause behind it, so after 55 minutes
without a page request the next one checks the database before it is let through.
**Look at:** `Web/Startup/` (six small files), the health-check mapping in `Program.cs`, `WakingUpMiddlewareTests`, `DataProtectionStartupTests`.

## Phase 17: Maintenance pass

### Q: How do you find bugs in a codebase that already passes its tests?
**A:** Two ways at once. A review of each layer in turn, where every finding had to quote the code and
give a concrete failure; nothing was fixed on a finding's say-so, and the worst admin findings were
reproduced in a browser first. And a sweep that signed in as every demo user and opened every page:
about 220 page loads, zero errors. That second result is the interesting one. Loading pages is not
using them; the bugs were all behind a click (Add line, Deactivate, Start an amendment).
**Look at:** `docs/walkthroughs/19-maintenance.md`, `scripts/screenshots/role-sweep.mjs`.

### Q: Name a Blazor bug that tests and a page-load sweep both miss.
**A:** A `Func` callback. `ConfirmDialog.OnConfirm` is a `Func<Task<bool>>`, so after it runs Blazor
re-renders the dialog (whose button was clicked), not the page that owns the list. Five pages
reloaded their data and never showed it. An `EventCallback` would have re-rendered the owner; with a
`Func` you call `StateHasChanged()` yourself. There is now a bUnit test that fails without it. Two
cousins: a page reused when only its route parameter changes (load in `OnParametersSetAsync`, not
`OnInitializedAsync`), and an input that keeps a refused value because Blazor diffs against what it
rendered, not what the browser holds (`@key` bump in `AmountCell`).
**Look at:** `FundListTests.Deactivating_a_fund_updates_the_row_as_soon_as_the_dialog_closes`, `AmountCell.razor`.

### Q: Why check roles in the service when the page already has `[Authorize]`?
**A:** Because the attribute is one typo from gone and nothing else would notice. Workflow,
publishing, and import already checked; the setup services did not. Now they do, and a reflection
test pins every admin route to its policy, so a new page fails until someone decides who may open it.
**Look at:** `Application/Setup/SetupNotAllowed.cs`, `AdminPagePolicyTests`.

### Q: What is an open redirect, and how did you have one?
**A:** A sign-in link that sends you somewhere else after you type your password. The check was
`Uri.IsWellFormedUriString(url, UriKind.Relative)`, which accepts `//evil.example`; browsers treat
that as another host. `LocalUrl` follows ASP.NET Core's `IsLocalUrl` rules, and the tests list
`//host`, `/\host`, `javascript:`, and a smuggled CRLF.
**Look at:** `Web/Components/Account/LocalUrl.cs`, `LocalUrlTests`.

### Q: Can cost be a security problem?
**A:** On a free tier, yes. The portal cache varied on every query key, so `?x=1`, `?x=2`, ... each
rebuilt a page from the database, and `/health/ready` opened a connection for anyone. Either could
use up the month's free database allowance, and the database then pauses until the month turns over.
The cache varies only on the keys pages read, and no anonymous endpoint queries the database per call.
**Look at:** `PortalOutputCachePolicy.VaryByQueryKeys`, ADR-0032.

### Q: What did you deliberately not fix?
**A:** Optimistic concurrency. An amount edit can save after a concurrent adoption. The fix is a row
version on `BudgetVersion` touched by every line change, which is real work across the aggregate,
so I wrote it down as a known gap rather than half-do it in a cleanup, and Phase 18 built it properly.
Four budgeting questions were also written down to decide deliberately instead of being settled by
whatever the code happened to do.
**Look at:** walkthrough 19, sections 6 and 7.

## Phase 18: Budget rules and known gaps

### Q: What counts toward a department's budget total, and how did you decide?
**A:** Its expenditure appropriations only. I looked it up rather than guess: ORC 5705.38(C) classifies appropriations by
office, department, and division with personal services within each; the Auditor of State's UAN chart
budgets transfers out under their own "Other Financing Uses" program (910), not in a department; and
Michigan's uniform chart does the same with activity 965. Revenue a department collects is the fund's
estimated resource. Three screens had three answers before; one extension method, `CountsTowardDepartmentTotal`,
is now the only answer, and the other lines are shown beside the department, labelled.
**Look at:** `Domain/Accounts/AccountType.cs`, `BudgetGrouping.ByDepartment`.

### Q: How does a new year's budget get started?
**A:** From the fiscal year: empty, or from last year's latest adopted version. Last year's adopted
amount becomes the comparative, the request is that amount changed by a percentage (all lines,
appropriations only, or revenue only, optionally rounded to dollars), prior-year actuals start at zero
because an adopted budget is not what was spent (since Phase 22, VIP fills them when it holds that year
closed), and each fund begins with last year's projected ending
balance. Retired codes are listed, never silently dropped.
**Look at:** `BudgetVersion.CreateOriginalFrom`, `BudgetSeedOptions`, `BudgetStartTests`.

### Q: How do you stop two people overwriting each other's budget changes?
**A:** Optimistic concurrency on the aggregate root. `BudgetVersion.Revision` is an EF concurrency
token, and every mutating method calls `Touch()`, so even a line edit updates the version row with
`WHERE Revision = @loaded`. A SQL `rowversion` would miss that case, because it only changes when the
version's own row does. The loser gets "someone else changed this budget; reload", and the tests hold
two contexts open to reproduce an edit racing an adoption and two adoptions racing each other.
**Look at:** `SaveConflicts.TrySaveAsync`, `ConcurrencyTests`.

### Q: Your integration tests use a real SQL Server. Aren't they slow?
**A:** They were: every test migrated a database and ran the full seed, password hashing included. Now
one seeded template is built per run, backed up inside the container, and restored under a unique name
per test. 99 tests went from 1m40s to 40s with the same isolation.
**Look at:** `SqlServerFixture.CreateSeededDatabaseAsync`.


## Phase 19: Documentation and comments

### Q: How do you keep documentation honest in a project this size?
**A:** By treating it like code: it gets reviewed against the code, not against memory. In the
documentation pass I checked every class, method, and file name the docs mention against the
source, reread every code comment beside the code it describes, and fixed what had drifted: a
comment on the budget line index that contradicted the one below it (EF Core adds an `IS NOT NULL`
filter to a unique index over a nullable column, so fund-level lines need their own index), a class
still described as a placeholder long after it stopped being one, and a README that listed
the roles under two different sets of names. Decisions that later changed are amended on the
original ADR rather than rewritten, so the reasoning at each point is still readable.
**Look at:** `docs/DECISIONS.md` (the amended ADRs), `BudgetLineConfiguration.cs`.

### Q: Did anything turn up while you were writing it down?
**A:** A real bug. Explaining the import's preview and commit, I noticed the preview matched codes
loosely (any case, `01000` for `1000`) but commit looked the typed text up again by exact code. A file
whose codes were padded with zeros previewed as clean and then crashed the commit. The fix makes the
analysed rows carry the ids the preview matched, so commit applies exactly what the user approved;
the integration test that reproduces it failed before the fix. Writing an explanation is a good
review, because you have to follow each step instead of skimming it.
**Look at:** `ImportRowDto`, `BudgetImportService.CommitAsync`,
`BudgetImportServiceTests.Codes_written_with_leading_zeros_commit_as_well_as_preview`.


## Phase 22: Actuals from VIP

### Q: How does CivicBudget get real spending from the ERP when you do not have VIP's API?
**A:** The same way the chart arrives: a contract that says everything CivicBudget needs
(`ErpActuals`: monthly activity by account, open encumbrances, fund cash, and how many months are
closed), adapters that fill it, and a service that previews before it applies. One adapter reads an
export file with full account numbers; the other is an API interface that is registered only where a
connection exists. The demo registers a simulated ERP that keeps the demo governments' fictional books.
When Software Solutions hands over the real API or export layout, one adapter changes and nothing above
it moves.
**Look at:** `Application/Erp/ErpActuals.cs`, `ErpActualsFileSource`, `Infrastructure/Erp/SimulatedErpActualsApi.cs`.

### Q: Why refuse the whole file for one unknown account code?
**A:** Because the alternative is a wrong number nobody notices. If one account in a year of actuals is
skipped, every total built on it (the fund's spending, budget against actual, the certificate) is
understated, and the report looks fine. Refusing lists every unknown code once, so the fix is obvious:
sync the chart, or correct the export. The budget import works the same way.
**Look at:** `ActualsMatcher.Match`, `ActualsSyncServiceTests.A_file_with_a_code_the_chart_lacks_changes_nothing`.

### Q: When does a sync change a budget, and how do you keep that safe?
**A:** Only one column, and only from a closed year. A budget for FY2027 compares against FY2025's
actuals; when VIP sends FY2025 with all twelve months, those totals go into every open FY2027 version.
They are written through `BudgetVersion` like any edit, so each change is in the line's history and
checked for concurrency, and the preview lists them before anyone clicks Apply. A year in progress fills
nothing, because eight months of spending in a column called "actual" would mislead every comparison.
Budget amounts themselves are never touched.
**Look at:** `PriorYearActuals`, `ActualsSyncService.PlanAsync`.

### Q: What was the subtle case?
**A:** Revenue budgeted by fund while the ERP records it by department: "1000-4320" in the budget,
"1000-310-4320" (pool fees) in VIP. Exact matching left the budget line at zero. `ActualsByLine` gives a
fund-level line every amount on its fund and account that no department-level line claims, so the money
is counted once and never lost. It is a pure function with its own test.
**Look at:** `ActualsByLine.Sum`, `ErpActualsTests.A_fund_level_line_collects_department_receipts_that_no_department_line_claims`.

### Q: How did you make simulated data believable?
**A:** It is not random. The simulated ERP's books are the seed's own fictional history, spread over the
months the way a village's money moves: real estate taxes in two settlements, biweekly payroll (so two
months have three paydays), capital work in summer, debt service twice a year. A month closes ten days
after it ends. And year-end cash less carried encumbrances equals the next budget's beginning balance,
which is how Ohio defines the unencumbered balance, so the numbers agree on every screen and the
certificate in the next phase will reconcile.
**Look at:** `SimulatedErpActualsApi.Weight`, `ErpActualsTests.A_closed_year_is_twelve_months_that_add_up_and_cash_agrees_with_next_years_balance`.


## Phase 23: Send the budget to VIP

### Q: Why does the journal hold changes instead of the budget's totals?
**A:** Because a budget journal adds to what the ERP already has. Sending full amounts would be right
the first time and double the budget on the first amendment. So each amount is the budget's figure less
what earlier journals for that year posted: the first send is the whole budget, an amendment sends only
what it moved, a removed line sends a decrease, and a budget the ERP already matches sends nothing, which
is also why pressing the button twice does nothing. It is a pure function with its own tests. I wrote down that
assumption (VIP adds rather than replaces) before I could check it, and confirmed it.
**Look at:** `BudgetJournalBuilder`, `BudgetJournalTests`.

### Q: The call to VIP times out. Did it post or not?
**A:** Nobody knows, and the design says so. The send was saved before VIP was called, so it is on record;
a thrown call marks it Failed rather than Rejected; and Try again sends the same journal under the same
id, which VIP recognizes and answers with the journal it already has. A new id on retry is the classic way
to double-post. The integration test uses an API that posts the journal and then throws, which is exactly
the case a naive retry gets wrong.
**Look at:** `BudgetTransmissionService.PostAsync`, `SimulatedErpBudgetApi`, `A_lost_answer_is_retried_under_the_same_id_and_vip_posts_it_once`.

### Q: How do you stop two people sending at the same time?
**A:** Two layers. The service refuses a new send while one for the year is unfinished, and says why. That
check can race, so a filtered unique index on government and fiscal year, covering only the unfinished
statuses, makes the database refuse the second insert, and the save helper turns that into a readable
message. The same technique keeps fund-level budget lines unique.
**Look at:** `BudgetTransmissionConfiguration`, `The_database_refuses_a_second_unfinished_send_for_a_year`.

### Q: Why does a downloaded file not count as sent?
**A:** Because a file on someone's desktop has not changed anything in VIP. If it counted, and nobody
imported it, every later journal would be measured from numbers VIP never received. It waits in
"waiting for import" until someone confirms it was loaded (optionally with VIP's journal number) or
discards it, and it holds the year meanwhile so the same change cannot go by a second route.
**Look at:** `BudgetTransmission.ConfirmImported`, `An_import_file_holds_the_year_until_someone_confirms_it_was_loaded`.

### Q: What if VIP does not have one of the accounts?
**A:** It refuses the journal whole, and CivicBudget shows each refused account with VIP's reason. Nothing
counts as sent and the year is free to try again once the account exists. Posting the good lines and
skipping one would leave the two systems quietly disagreeing on that line.
**Look at:** `BudgetTransmission.MarkRejected`, `Vip_refusing_an_account_posts_nothing_and_leaves_the_year_ready_to_send_again`.

### Q: Why does the app never say "VIP"?
**A:** Because the product is meant for any ERP vendor to take on. VIP is the ERP I know and the one I
checked the rules against, so it appears in these docs as the example, but a buyer's customers would
see their own ERP, not a competitor's name. The button says "Send to ERP", the demo's connection is
"ERP (simulated)", and each connection's name comes from its adapter, so a real one names itself.
**Look at:** `IErpBudgetApi.Name`, `SimulatedErpBudgetApi`.


## Phase 24: The certificate of estimated resources

### Q: What is the certificate, and why build it as its own thing rather than another report?
**A:** It is an Ohio legal document: the county budget commission certifies, fund by fund, the balance
carried in plus the revenue expected, and appropriations may not exceed that total (ORC 5705.36 and
5705.39). It needs things no other report did: balances from the ERP's closed year, a government's own
idea of which accounts are "taxes", headings worded like the county's template, reconciliations, an
amendment number, and a PDF for signatures. The arithmetic lives in one pure builder, and both the issued
five-column form and the detailed schedule render the same row per fund, so they cannot disagree.
**Look at:** `CertificateBuilder`, `CertificateBuilderTests`.

### Q: How does it know which revenue lines are taxes?
**A:** It asks the government. Report settings hold up to four named columns, each a set of revenue
accounts, and everything else is "other sources", so no receipt is left out; an account can only be in
one column, or its money would be counted twice. With nothing saved it defaults to the accounts
categorized as taxes, and says so on the report. A column with no accounts is flagged rather than
printing zeros that look real.
**Look at:** `ReportAccountGroup`, `CertificateService.Problems`, `ReportSettings.razor`.

### Q: Where does the beginning balance come from?
**A:** From the ERP when it has closed the prior year: cash at December 31 less the encumbrances carried
forward, less nonspendable and reserve balances, plus or minus advances not yet repaid. An original
certificate is prepared before the year closes, so until then it uses the budget's estimated beginning
balance and says so. When the ERP figures arrive, a reconciliation compares them with the budget's
beginning balances and names any fund that differs, because the appropriation check should use the
certified number.
**Look at:** `CertificateService.GetAsync`, `Fy2026s_balances_come_from_the_erps_closed_year_and_reconcile_with_the_budget`.

### Q: Why MigraDoc rather than QuestPDF?
**A:** Licensing. QuestPDF has the nicest API, but its free community license ends at $1M in company
revenue, and the whole point of this version is that an ERP vendor could take the product on. PDFsharp
and MigraDoc are MIT. Two things bit me: MigraDoc freezes the document's default page setup, so each
section needs its own copy with the landscape size set outright (the orientation flag alone came out
portrait with the right-hand columns cut off); and a Linux container has no fonts, so the typeface
ships embedded in the assembly. A test checks the page count, the landscape page size, and the font.
**Look at:** `CertificatePdfRenderer`, `EmbeddedFontResolver`, ADR-0036.


## Phase 25: Reports the ERP cannot produce alone

### Q: How do you decide whether a revenue line is "on track" in August?
**A:** Not by the calendar. Eight months is two-thirds of the year, but a village has nearly all its
real estate tax by August (two settlements) and only two-thirds of its income tax. So each account is
compared with the share of last year's total that had arrived by the same month, from the ERP's monthly
history, and it is "behind" only when it trails that by more than ten points. In the demo, real estate
tax at 96% collected reads as normal, because last year it was 95% by now.
**Look at:** `RevenueReceiptRowDto.IsBehind`, `ActualsReportBuilder.RevenueVsReceipts`.

### Q: How is the year-end projection made?
**A:** Line by line: this year to date, scaled by last year's full year over last year at the same month.
A closed year is simply its actual. A line with no history is carried at its budget (never below what
has already happened), and the report says how many lines that is. Encumbered money always counts as
spent. It is one pure function with a test for each case, and the report prints its method, because a
projection nobody can explain is not one a fiscal officer will use.
**Look at:** `ActualsReportBuilder.Project`, `ActualsReportBuilderTests`.

### Q: What is the appropriation measure, and what did you have to decide?
**A:** The ordinance or resolution that appropriates: by fund, then by office and department, with
personal services set out separately (ORC 5705.38(C)); transfers out sit beside the departments as other
financing uses. The decision was what "personal services" includes. Most measures put fringe benefits
with pay, but not all, so it is a report setting with that default, using the same column mechanism the
certificate's taxes use, with the same rules shared.
**Look at:** `AppropriationMeasureBuilder`, `MeasureColumnService`, `ReportColumnRules`.

### Q: Why can a department user run budget against actual but not the projection?
**A:** Budget against actual is a list of lines, and a department user's view of the budget is their
departments' lines, so the report is complete for them. A projection, a trend, or an appropriation
measure is about whole funds; built from part of a fund it would be wrong, not partial. Those return
nothing for department users, the same rule as the certificate.
**Look at:** `ActualsReportService.LoadAsync`, `A_department_user_gets_their_own_lines_and_none_of_the_whole_fund_reports`.

## Phase 26: Personnel budgeting

### Q: How do salary and benefit lines get their amounts?
**A:** From positions. Each position is priced by one pure function: base pay month by month, longevity,
other pay, retirement on pensionable pay, Medicare and workers' compensation on taxable pay, and
insurance by tier less the employee's share. Each piece is split among the position's funds, and the
pieces are summed by fund and account into the department's lines. A line is either typed or calculated,
never both. A calculated line says "from 9 positions", and the domain refuses typing over it, so the
budget can never say something its positions do not.
**Look at:** `PositionCostCalculator.Calculate`, `BudgetVersion.ApplyPersonnel`, `BudgetLine.PositionCount`.

### Q: Why are positions part of the budget version rather than their own thing?
**A:** Because the rules I need already live on the version. An adopted budget cannot change, an
amendment is a copy, the revision token stops two people overwriting each other, and a department user
edits only their own departments until they submit. As children of the version, positions get all of
that for free. As a separate aggregate, the lines and the positions could disagree between two saves.
**Look at:** `BudgetVersion.AddPosition`, `CreateAmendment`, `Position.CarryForward`.

### Q: Why are the settings per fiscal year?
**A:** Because premiums and rates change by year, and the years overlap. In August the fiscal officer
enters next year's health premiums while this year's mid-year amendment is still open; one set of
settings would push next year's premiums into this year's amendment. Per-year settings keep each budget
priced with its own year. Saving them reprices only that year's open budgets, and a change that would
leave a position unpriceable is refused, with the position named.
**Look at:** `PersonnelSettingsService.SaveAsync`, `A_plan_someone_is_on_cannot_be_removed`.

### Q: How do you make longevity configurable without a formula language?
**A:** Every Ohio contract I have seen uses one of three shapes: a step table of flat amounts, a
percentage of pay by years, or an amount per year of service with a cap. So a schedule is one of those
methods plus a list of steps. The page builds it as sentences and reads it back in plain English with a
worked example, both from the code the budget uses. The administrator checks the sentences against the
contract, not the numbers against a formula.
**Look at:** `Longevity.Amount`, `Longevity.Describe`, `LongevityEditor.razor`.

### Q: Tell me about a bug you found in this phase.
**A:** Saving unchanged settings said it had recalculated six lines. The amounts were moving by one
cent, between the General and Street funds. The rounding cent went to "the last fund" in a split, and
the database returns a position's funds in no fixed order. Now the fund with the largest share takes
it, with ties broken by id, and two tests pin it down: a split is the same in either order, and an
unchanged save moves no line.
**Look at:** `PositionCostCalculator.SplitAmongFunds`, `Saving_settings_touches_only_the_lines_the_change_reaches`.

## Phase 27: Employees from the ERP, and personnel reports

### Q: You didn't know the ERP's export format. How did you build the import?
**A:** The same way as the chart and actuals syncs: a contract (`ErpEmployees`) that says what CivicBudget
needs, with adapters that fill it. I wrote down a reasonable layout for a payroll export (one row per
employee, the labor distribution and benefits in a cell each) and built the file reader and the simulated
API against it. Plans travel by name and departments and funds by code, so the only code that will ever
know VIP's own codes is its adapter. When the real layout is known, I write that adapter and nothing
else changes.
**Look at:** `ErpEmployees`, `ErpEmployeeFileSource`, `SimulatedErpEmployeesApi`.

### Q: What happens to a planned raise when the payroll is brought in again?
**A:** It stays. A position has the ERP's facts (name, pay, retirement, insurance, funds) and the budget's
plans (raise, step increase, months, longevity, overtime). A sync refreshes the first and keeps the second.
The matcher does that with one `with` expression, and a test proves the 3% raise from July survives a pay
change in the ERP.
**Look at:** `EmployeeMatcher.Apply`, `A_raise_in_the_erp_updates_the_rate_and_keeps_the_budgets_planned_raise`.

### Q: Why leave a leaver's position vacant rather than delete it?
**A:** Because in a budget a position is a slot, not a person. When someone retires, the department almost
always means to fill the job, and the budget should still carry it. Deleting is one click on the personnel
page, but undoing a wrong delete means re-entering the position. New hires use the same idea from the
other side: they fill a vacancy with the same title before a new position is created.
**Look at:** `SyncAction.Vacate`, `A_new_hire_fills_the_vacancy_with_the_same_title`.

### Q: How do you know the preview shows what Apply will do?
**A:** It runs the same code. The service loads the budget, matches, and applies the plan through the
aggregate's real methods, then compares the lines before and after. A preview throws the context away;
a commit saves it. There is no second "estimate" path that could drift from the real one.
**Look at:** `PersonnelSyncService.RunAsync`.

### Q: How do the personnel reports stay consistent with the budget?
**A:** They add up the calculator's pieces, the same ones the lines are made of. I added a split by fund and
kind to the cost, so "retirement paid by the Street fund" is a sum, not a new formula. An integration test
asserts the roster's total equals the personnel lines, and the cost report's Street fund equals that fund's
lines.
**Look at:** `PositionCost.ByFundAndKind`, `The_personnel_reports_add_up_to_the_personnel_lines`.

## Phase 28: Email, two-step sign-in, and onboarding

### Q: Why an outbox table instead of sending the email when the department submits?
**A:** Two failures I did not want. If I send first and the save fails, someone is told about a
submission that never happened. If I save first and the send fails, or the mail server is slow, the
user's click fails or hangs over an email. With an outbox the email is a row written in the same
save as the submission, so both happen or neither does. A background sender delivers it afterwards
and retries a refusal. The rows also answer "what did we send to whom", which an auditor asks.
**Look at:** `DepartmentRequestService.SubmitAsync`, `OutboxEmail`, `EmailDeliveryService`.

### Q: Why doesn't the sender poll the table?
**A:** The demo runs on a serverless database that pauses when idle, and a poll every minute would
keep it awake and cost money. After a successful save the service writes to an in-memory channel,
which costs nothing, and the sender wakes and drains what is pending. Retries schedule their own
wake-up. No database call happens unless there is mail.
**Look at:** `EmailSignal`, `EmailOutbox.Notify`, `EmailDeliveryService.ExecuteAsync`.

### Q: How is password reset kept safe?
**A:** The link carries Identity's reset token, not a password. The token is tied to the user's
security stamp, so it stops working the moment the password changes: single use. It also expires
in a day. The request page answers the same way whether the address has an account, so nobody can
use it to find out who works for the village. New users get the same kind of link to choose their
first password, so no administrator ever knows it.
**Look at:** `AccountEmailService`, `A_reset_link_sets_a_new_password_once_and_an_unknown_address_gets_nothing`.

### Q: How does "require MFA" reach people who are already signed in?
**A:** Turning it on changes the security stamp of everyone without MFA, which signs them out within
the revalidation interval. At their next sign-in the claims factory adds a claim, and middleware
keeps them on the setup page until they have scanned the code. That is the same pattern as the
temporary-password rule, so there is one way the app says "finish this before anything else".
**Look at:** `SignInSecurityService.SetRequireMfaAsync`, `ApplicationUserClaimsPrincipalFactory`, `RequireMfaMiddleware`.

### Q: Who can create a new government?
**A:** Only the vendor, from the command line with the deployment's credentials, the same way the
nightly reset runs. A sign-up page would let anyone create tenants on a public demo. The first
Administrator is emailed a link and then follows a checklist that is computed from what exists, so it
cannot be out of date.
**Look at:** `ProvisionCommand`, `GovernmentProvisioningService`, `SetupChecklistService`.

## Phase 29: SOC 2 by design

### Q: Is CivicBudget SOC 2 compliant?
**A:** It is designed and built to achieve it, and I say exactly that. A SOC 2 report is an
independent CPA's opinion, and I have not hired one. What I did is build the controls the audit
examines and map each one to its evidence in a control matrix:
- access;
- logging;
- change control;
- encryption;
- backups;
- disposal;
- incident response.

I also wrote the policies an operator would follow. An operator running it that way could get a
Type 1 report straight away and a Type 2 after its first observation period.
**Look at:** `docs/security/README.md`, `docs/security/control-matrix.md`.

### Q: Why a security log when you already have an audit trail?
**A:** They answer different questions. The audit trail is the budget's history: this line went
from 40,000 to 42,500, and who did it. The security log is who got in, who tried to, from where,
and what left the building. Mixing them would bury a budget change under hundreds of sign-ins.
The log is written by overriding Identity's `SignInManager`, so a sign-in path added later is
covered without anyone remembering, and one filter on the export group logs every download.
**Look at:** `AuditingSignInManager`, `AdminExportEndpoints`, `SecurityTests.Every_sign_in_outcome_is_a_security_event`.

### Q: How does an idle timeout work in Blazor Server, where the page never makes a request?
**A:** It can't be done with the cookie alone. Once the page is open, it talks over the circuit's
WebSocket and never sends the cookie again, so the server sees the same silence from a busy tab
and an idle one. The browser keeps the clock instead:
- activity in any tab counts, shared through localStorage;
- a keep-alive request slides the cookie while someone works;
- a banner warns at 28 minutes;
- at 30 it sends the page to an endpoint that signs out and logs "Signed out, idle".

The server still enforces 30 minutes on the cookie for everything else.
**Look at:** `wwwroot/js/session.js`, `SessionPolicy`, `IdentityEndpoints` (`KeepAlive`, `SessionExpired`).

### Q: How does a strict Content Security Policy work with Blazor?
**A:** Scripts may come only from this site. Blazor needs one inline script, its import map, so each
request gets a random nonce that goes on that tag and in the header. I moved my own small inline
helpers into a file. Two catches: Blazor adds its own `frame-ancestors` header, which would have won
over mine, so I switched it off; and the report bars set widths inline, so inline styles stay
allowed. Script is where the risk is.
**Look at:** `SecurityHeadersMiddleware`, `App.razor` (`ImportMap nonce`), `Program.cs` (`ContentSecurityFrameAncestorsPolicy`).

### Q: How do you know the export and the deletion don't miss a table?
**A:** They don't use a list. `GovernmentDataStore` reads the EF model: every table with a
`GovernmentId` column, the government's own row, and tables that point at one of those, like a
user's roles. A test compares that with every table in the model and fails on a new table no
government reaches. Deletion goes in one transaction, and each table is removed only after
everything that points at it, because SQL Server refuses to cascade down more than one path. The
deletion test counts rows from the database's own catalog, not from my code, and checks the other
government is untouched.
**Look at:** `GovernmentDataStore.ScopedTables`, `DeletionOrder`, `SecurityTests.Removing_a_government_deletes_every_row_it_had_and_nothing_else`.

### Q: What stops password spraying?
**A:** Three layers:
- **Lockout**: five wrong passwords lock one account for 15 minutes.
- **Rate limits**: one address trying many accounts gets 20 sign-in posts per 5 minutes, and the
  reset form 5 per 15 minutes. After that it gets a 429, which is logged.
- **Two-step sign-in**: a government can require it.

The limiter's partition key is the client address from `X-Forwarded-For`, trusting only the entry
the load balancer added, so a client cannot choose its own bucket.
**Look at:** `RateLimits.PolicyFor`, `SecurityControlsTests.Limits_only_the_requests_an_attacker_would_repeat`.

## Phase 30: The product site

### Q: Why is the marketing site a static page and not part of the app?
**A:** Three reasons:
- **Availability.** The demo sleeps on free tiers and resets every night, and the page that sells
  it should load instantly whatever the demo is doing.
- **Scope.** A single page needs no framework. It is plain HTML and CSS in my portfolio's
  repository, next to another project's site, and the same Formspree form handles contact.
- **Cost.** Nothing to deploy or pay for beyond what I already run.

**Look at:** `docs/walkthroughs/29-product-site.md`, ADR-0042.

### Q: How did you make the demo clips?
**A:** Playwright records a video of each scripted session against a freshly seeded app. Headless
recordings have no cursor, so the script injects one that follows the mouse. Then ffmpeg trims the
page load, crops the sidebar, and encodes H.264. Each clip is under a megabyte, where a GIF would
be several. On the page they play only while visible, never under reduced motion, and they have a
pause button, because WCAG requires one for motion over five seconds.
**Look at:** `scripts/screenshots/site-clips.mjs`, `public/CivicBudget/assets/js/site.js` in the site repository.

### Q: Why didn't a redirect from the lowercase address work?
**A:** Next matches redirect sources without regard to case, so the redirect from `/civicbudget/`
to `/CivicBudget/` also matched `/CivicBudget/` and looped. Next serves public files
case-sensitively before rewrites run, so a rewrite from any spelling onto the real folder only
catches the misses. I found it by testing against a production build rather than the dev server,
which is where these rules apply.
**Look at:** `next.config.mjs` in the site repository.

## Phase 31: Accessibility self-scan

### Q: How accessible is it, and how do you know?
**A:** I ran axe-core on all 237 page loads, every page for every role plus the portal at phone
width, then had the behavior reviewed by keyboard, because axe only sees a page as it loads. The
report rates every WCAG 2.2 A and AA criterion. It is honest about the limits: no screen reader
testing yet, and two partial passes I can name. After the fixes the sweep is clean, and a script
re-checks each serious keyboard finding.
**Look at:** `docs/accessibility/ACR.md`, `scripts/screenshots/a11y-sweep.mjs`.

### Q: What did the automated scan miss that a person caught?
**A:** Things that happen after a key press:
- a dialog opened from a row menu dropped focus to the page when it closed;
- the phone menu never took focus;
- a refused amount only turned red;
- the idle warning's countdown was in a live region, so a screen reader would be interrupted every
  second.

axe also could not tell that `aria-expanded="@open"` was wrong, because Blazor renders a true bool
as an empty attribute. That is why `Aria.Bool` exists.
**Look at:** `Aria`, `civicbudget.js` (`openModal`, `closeModal`), `session.js`.

### Q: Why fix it in shared components rather than page by page?
**A:** The same defect was on every form and every dialog. `ResultAlert` is on every admin form, so
making it list all the errors, act as an alert, and take focus fixed them all, and any form added
later gets it too. The same goes for the color tokens: most of 109 contrast failures were two CSS
variables, one of them the link color Bootstrap reads from an `-rgb` triple the theme never set.
**Look at:** `ResultAlert.razor`, `app.css` (`--cb-muted`, `--bs-link-color-rgb`).

## Phase 32: Finishing accessibility

### Q: How do you make a grouped table accessible?
**A:** Each group gets its own `<tbody>` with a `<th scope="rowgroup">` as its title, and each row's
naming cell is a `<th scope="row">`. A screen reader then says which fund and program a number
belongs to as you move through it. The worksheet was the tricky one: its groups come from the
current sort order, so sorted by amount the same fund can appear in several runs. The runs are
computed in code, and each is keyed by its position so Blazor's diffing stays stable.
**Look at:** `AccountLineGrid.Runs`, `DepartmentEntryView.razor`.

### Q: Can you claim screen-reader support without testing with one?
**A:** No, and the report does not. What I could do was read the accessibility tree, which is exactly
what a screen reader consumes, for each key screen. That found unnamed tables, symbols read as
symbols, and "$3.70M" with no spoken form, and I fixed them. The VoiceOver pass itself is a written
checklist with expected announcements, and the report's screen-reader line stays open until it is
done.
**Look at:** `docs/accessibility/screen-reader-checklist.md`, `MoneyShort.Speakable`.

## Phase 33: Multi-year plan

### Q: Why store percentages and typed years instead of the projected amounts?
**A:** Projected amounts depend on the budget year. The moment someone changes a line in the
worksheet, every stored projection for that line would be stale. Storing only the inputs (the
percentages per year and the years someone typed) and working the numbers out on every read keeps
the plan consistent by construction, and there is one pure function to test. The snapshot is the
one place projected numbers are stored, because a published budget is frozen on purpose.
**Look at:** `MultiYearPlanCalculator.Project`, `PlannedAmount`, `PublishedBudgetSnapshot.Capture`.

### Q: How do fund balances work across years?
**A:** Each year of each fund is summed into the same `FundBalanceSummary` the budget year uses, by
the same `FundBalanceCalculator`, and its ending balance becomes the next year's beginning balance.
Reusing the calculator means every future year gets the Ohio check that appropriations stay within
estimated resources, so the page can say "Fund 4901 spends more than it has in FY2029".
**Look at:** `MultiYearPlanCalculator`, `FundBalanceCalculator`, `BudgetPlan.razor`.

### Q: What happens to the plan when next year's budget starts?
**A:** It moves on a year. Year 2's percentages become year 1's, typed amounts shift down, and the
new last year repeats the old last year's percentages. The old year 1's typed amounts are dropped,
because that year is now the budget and is started from last year's amounts like any budget. An
amendment copies the plan unchanged. Both are domain methods on `BudgetVersion`, tested without a
database.
**Look at:** `BudgetVersion.CreateOriginalFrom` (`CarryPlanForward`), `BudgetLine.CopyPlannedFrom`,
`MultiYearPlanTests`.

### Q: How did you keep department heads to their own lines?
**A:** The same way as the budget year: `BudgetLinePermissions.CanEdit` with the department's
submitted flag decides whether a future year can be typed, and the service filters the lines to
the user's departments and returns no fund totals. The rule lives in one place, so the plan cannot
drift from the worksheet.
**Look at:** `BudgetPlanService`, `BudgetPlanServiceTests`.

## Phase 34: ERP partner kit

### Q: How would an ERP vendor connect to CivicBudget?
**A:** They build four endpoints to a published OpenAPI description: the chart, a year's actuals,
the payroll roster, and a budget journal post. CivicBudget already has the client, one HTTP
adapter that works with any ERP implementing the API, so the connection is configuration (an
address and a key per government), not code. There is also a file form of every exchange for ERPs
without an API, and a small reference ERP that implements the API so a vendor can see it working.
**Look at:** `docs/partners/README.md`, `HttpErpAdapter`, `samples/CivicBudget.ReferenceErp`.

### Q: How do you keep a hand-written OpenAPI file from drifting from the code?
**A:** A test reads the document and compares each schema with its C# record: the property names,
which are required (a constructor parameter without a default), and which may be null (from the
nullability annotations). It compares each enumeration with the C# enum, checks every `$ref`
resolves, and reads every sample through the real code with unknown fields refused. I proved it
bites by breaking the document four ways; each failed.
**Look at:** `ErpApiContractTests`.

### Q: Why separate wire records instead of serializing your contracts?
**A:** The Application contracts are internal and change with the product; the API is a promise to
vendors. With separate records, renaming a C# property cannot silently rename a JSON field, and a
v2 would be a second set of records beside the first.
**Look at:** `ErpApiContract.cs`.

### Q: What happens when the ERP doesn't answer a budget journal post?
**A:** Nobody knows whether it posted, so the adapter throws instead of guessing. The send service
marks the send failed but open, and a retry sends the same journal id, in the body and in an
`Idempotency-Key` header, which the ERP must recognize and answer with the first answer. A clear
refusal (422 with the refused accounts, or a bad key) is different: nothing posted, so it comes
back as a refusal the Fiscal Officer can act on.
**Look at:** `HttpErpAdapter.PostBudgetJournalAsync`, `BudgetTransmissionService.PostAsync`.

### Q: Where do ERP keys live, and why not in the database?
**A:** In configuration, which in the cloud is the platform's secret store, keyed by government id.
A key opens a government's books and payroll; in the database it would be in every backup and
export and in reach of anything that can read a table. The app validates connections at startup
(HTTPS only, a key present) and never logs call bodies.
**Look at:** `ErpConnectionsOptions`, `ErpConnectionsOptionsValidator`.

### Q: Why is the JSON reading strict about missing fields but relaxed about extra ones?
**A:** A missing amount read as zero would build a budget on a number nobody sent, so missing
required fields and nulls where none are allowed are refused (`RespectRequiredConstructorParameters`,
`RespectNullableAnnotations`). Extra fields are ignored so an ERP can grow its answers without
breaking CivicBudget.
**Look at:** `ErpApiContract.Json`.

## Phase 35: The printable budget book

### Q: How do you make sure the book and the screens never disagree?
**A:** The book computes nothing. `BudgetBookService` asks the same services the report pages use
(fund summary, categories, department detail, certificate, plan, personnel cost, the workspace
lines), and a pure builder only arranges them into pages. If a figure is wrong in the book, it is
wrong on a screen too, and there is one place to fix it. Each service's own permission check comes
along, which is why a department user gets no book.
**Look at:** `BudgetBookService.SourcesAsync`, `BudgetBookBuilder`.

### Q: Why store the published PDF instead of rendering it on demand?
**A:** A resident should download the book council adopted. If the portal rendered on each
request, editing the message or changing the book's layout after publishing would quietly change
the published document. Publishing prints it once and stores it beside the snapshot, in its own
table so the portal's pages never load a PDF. The portal's output cache keeps it with the pages and
drops it on the next publish.
**Look at:** `PublishingService.PublishAsync`, `PublishedBudgetBook`, `SnapshotQueryService.GetBookAsync`.

### Q: How do the demo's published budgets get books if they were never published through the app?
**A:** The seed writes snapshots straight to the database. After seeding, a backfill prints a book
for every published budget without one. It lists the governments (which are not tenant-owned),
then for each one opens a scope that acts for that government, so every query still goes through
the tenant filter; no `IgnoreQueryFilters`. The test template seeds through the same path, so tests
see what the app sees.
**Look at:** `PublishedBookBackfill`, `DatabaseInitializer.SeedAsync`.

### Q: How does the contents page know the page numbers?
**A:** Each heading adds a bookmark, and the contents page, written last but placed second, uses
page reference fields that MigraDoc resolves when it lays out the document. Headings also set an
outline level, so the PDF has a clickable outline. The certificate's landscape pages go in the
middle of the portrait book because MigraDoc sets up each section's page separately.
**Look at:** `BudgetBookPdfRenderer.Heading`, `Contents`, `CertificatePdfRenderer.AddPages`.

### Q: Is the PDF accessible?
**A:** Partly. It has a title, a reading order, and an outline, but PDFsharp cannot tag headings and
tables for screen readers. I said so in the conformance report rather than claim it, and the book
itself points readers to the portal, which holds the same figures in accessible HTML.
**Look at:** `docs/accessibility/ACR.md`, the note in `BudgetBookPdfRenderer.Contents`.

## Phase 36: The assistant

### Q: How does an AI assistant respect each user's permissions?
**A:** It never gets its own access. Every tool is a thin wrapper over the Application service a page
calls, run in the user's own scope with their identity, so the tenant filter, department visibility,
and role checks that protect the pages protect the assistant too. The model sees only what the
tools return: no database, no SQL. The integration tests prove it with a scripted model through the
real tool-calling layer; one compares the fund summary the assistant saw with what the report service
returns to the same user.
**Look at:** `BudgetTools`, `AssistantServiceTests`.

### Q: Why Microsoft.Extensions.AI instead of the Anthropic SDK directly?
**A:** Application depends only on `IChatClient` and `AIFunction`, the way it depends on `ILogger`.
Claude is registered in Infrastructure behind that interface, so a government that needs a model
from its own cloud is a different registration, and the service is tested with a fake `IChatClient`
instead of the network. `UseFunctionInvocation` is the middleware that runs the tools the model asks
for, capped at eight rounds. It paid off at once: adding Ollama as a second provider was one more case
in the registration, because Ollama speaks Anthropic's Messages API.
**Look at:** `AssistantModelRegistration`, `AssistantService`.

### Q: How does it know which pages exist and who may open them?
**A:** The pages tell it. Each carries a `[HelpTopic]` with a title, purpose, and keywords, and a
catalog reads those with the page's routes and `[Authorize]` policies by reflection, so it can't fall
behind the app. Before offering or opening a page the tool asks `IAuthorizationService` the page's
policies for this user. A test fails for any admin page without a topic.
**Look at:** `PageCatalog`, `NavigationTools`, `HelpCatalogTests`.

### Q: What stops a prompt injection hidden in the data?
**A:** Layers. The prompt says typed text in results is data. There is no write tool, so the worst
an injection can do in this phase is word an answer badly. The answer is rendered with raw HTML off
and only local links kept, so it can't send anyone to a phishing page. And the evaluation set plants
"IGNORE ALL PREVIOUS INSTRUCTIONS" in a justification and checks the model reports it rather than
obeying.
**Look at:** `AssistantPrompt`, `AssistantMarkdown`, `AssistantEvaluationTests`.

### Q: How do you test something whose answers vary?
**A:** Two ways. What must never vary (which tools ran, what they were allowed to return, what was
logged) is tested exactly with a scripted model. What the real model says is checked by an evaluation
set that asserts what matters, such as the right figure or the link, not the wording, and runs when a
key is present.
**Look at:** `AssistantServiceTests.ScriptedModel`, `LiveModelFactAttribute`.
