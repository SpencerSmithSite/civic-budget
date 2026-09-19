# CivicBudget Roadmap

One phase at a time. Each phase is a branch (`phase-N-name`) merged to
`main` by PR. A phase is done when its checklist is complete, tests pass,
docs are updated, and Spencer has approved.

## Phase 0 — Plan (no code)  `phase-0-plan`
- [x] Clarifying questions answered (name, DevExpress: no, DB: SQL Server, tenancy, Ohio scope, no AWS account)
- [x] `docs/SPEC.md`
- [x] `docs/ARCHITECTURE.md`
- [x] `docs/DECISIONS.md` (ADR-0001…0011)
- [x] `ROADMAP.md`, `CLAUDE.md`, `CHANGELOG.md`, README stub
- [x] `git init`, GitHub repo `civic-budget` (public, unlicensed)
- [x] Spencer approves Phase 0 (2026-09-16)

## Phase 1 — Foundation  `phase-1-foundation`
- [x] Verify Docker Desktop Rosetta; `docker-compose.yml` with SQL Server 2022 (healthy in ~15 s)
- [x] Solution + 4 src projects + 4 test projects, `Directory.Build.props` (nullable, warnings-as-errors, analyzers), `Directory.Packages.props` (central package management), `global.json`, `.editorconfig`
- [x] Domain entities, enums, invariants, `DomainException`, `Guard`, `Money`
- [x] `FundBalanceCalculator`, `AppropriationLimitCheck`, percent-change rounding
- [x] `ITenantOwned`, `ITenantContext`, global query filters, `TenantSaveChangesInterceptor`
- [x] `CivicBudgetDbContext`, configurations, `decimal(18,2)` convention, `InitialCreate` migration, design-time factory
- [x] Seed: Maple Ridge (FY2025 adopted, FY2026 original + Amendment 1, FY2027 draft) and Pine Hollow Township
- [x] `ci.yml`: restore, build, format check, test (Testcontainers), test-results summary; PR template; Dependabot
- [x] Domain unit tests for every rule in SPEC §5 (160); architecture tests (4); bUnit smoke (1); integration tests for migrations, tenancy, seed (14)
- [x] `docs/walkthroughs/01-foundation.md`; `docs/INTERVIEW-PREP.md` Phase 1 section
- [ ] Spencer approves Phase 1

## Phase 2 — Identity & maintenance  `phase-2-identity`
- [x] ASP.NET Core Identity (`AddIdentityCore` + cookies), `ApplicationUser` with `GovernmentId` and `DisplayName`, `UserDepartments`; Account pages ported from the `-au Individual` template (login, logout, profile, change password only)
- [x] Claims factory adds `government_id`, `display_name`, `department_id`; `CurrentUserContext` filled by `CurrentUserMiddleware` (HTTP) and `CurrentUserCircuitHandler` (circuits)
- [x] Roles + policies (`AuthorizationPolicies`) + resource-based `BudgetLineEditHandler` over `BudgetLinePermissions`
- [x] Authorization tests via `IAuthorizationService` (every policy × every role, resource-based cases)
- [x] `Result` type, FluentValidation validators, `ICivicBudgetDbContext` (ADR-0014), setup services (funds, departments, accounts, fiscal years, government settings), `IUserAdminService`
- [x] Admin area with Bootstrap + QuickGrid: overview, funds, departments, chart of accounts, fiscal years, users, government settings
- [x] Demo users seeded (one per role; password in user-secrets via `scripts/dev-setup.sh`)
- [x] Tests: 23 application, 38 web (policies + bUnit), 27 integration (user admin, setup services, seed)
- [x] Walkthrough 02; interview prep Phase 2; ADR-0014/0015/0016
- [ ] Spencer approves Phase 2

## Phase 3 — Budget entry  `phase-3-budget-entry`
- [x] `BudgetEntryService`: workspace DTO (visible lines with `CanEdit`, whole-fund balances with limit results, lookups), update amount/justification, add/remove line, set beginning balance; every mutation re-checks `BudgetLinePermissions`
- [x] By-department entry (department → fund → category, subtotals, inline amounts) via pure `BudgetGrouping`
- [x] By-account grid (QuickGrid, filters, sort, inline amount, note toggle, add/remove) with Bootstrap theming
- [x] Fund balance panel with Warn/Block severity and FD-only beginning balance edits
- [x] `AuditInterceptor` + `[Audited]` + `AuditEntry` (append-only, tenant-owned); `AddAuditTrail` migration; per-line history view
- [x] `ValueGeneratedNever` key convention (ADR-0018)
- [x] Tests: 4 grouping, 7 bUnit (panel, department view), 14 integration (budget entry, audit); 273 total
- [x] Walkthrough 03; interview prep Phase 3; ADR-0017/0018
- [ ] Spencer approves Phase 3

## Phase 4 — Workflow & publishing  `phase-4-workflow`
- [x] `BudgetWorkflowService`: Propose / Return to draft / Adopt (resolution number) with Block/Warn enforcement and acknowledgement, FD-only, audit events per transition
- [x] Amendments: copy adopted version into a new draft with a reason; one open version per year; adoption supersedes the prior adopted version
- [x] `PublishedBudgetSnapshot` (+ lines, funds) captured from adopted versions; Active / Superseded / Unpublished lifecycle (ADR-0019); `AddPublishedSnapshots` migration; seed publishes FY2025, FY2026 Amendment 1, Pine Hollow FY2026
- [x] `PublicPortalDbContext`: three tables, Active-only query filter, read-only, shared mapping with the admin context (ADR-0006)
- [x] `IPublishedSnapshotCacheInvalidator` hook (no-op until Phase 5)
- [x] UI: `ConfirmDialog` (no JS), `WorkflowBar` with six dialogs, publish/unpublish, publishing history on the version list
- [x] Tests: 6 domain, 4 bUnit, 9 integration; 292 total
- [x] Walkthrough 04; interview prep Phase 4; ADR-0019
- [ ] Spencer approves Phase 4

## Phase 4.5 — Admin UI design pass  `phase-4.5-admin-design`
Spencer's review of Phase 3 (2026-09-17): functional, but bare-bones Bootstrap will not impress in an interview. This phase gives the admin app a deliberate visual identity before the public portal reuses it.
- [x] Research: Ohio ERP vendors, admin budgeting UIs, transparency portals (`docs/design/research-*.md`)
- [x] Design brief approved 2026-09-18: "a modern civic ERP that fits in next to VIP" (`docs/design/DESIGN-BRIEF.md`)
- [x] Eight mockups approved before implementation (`docs/design/mockups.html`)
- [x] Design tokens as CSS variables over Bootstrap; Bootstrap Icons vendored (ADR-0020)
- [x] Shell: dark sidebar with module groups and icons, top bar with breadcrumbs (`AdminPageState`) and user menu, off-canvas under 992px
- [x] Components: `PageHeader`, `StatusPill`, `WorkflowStepper`, `KpiCard`, `ToastService`/`ToastHost`, `ConfirmDialog` (Escape), `SideDrawer`, `RowMenu`, `EmptyState`, `SkeletonRows`
- [x] Screens restyled: workspace (stepper, fund rail, dense grid with group rows and row menus, history drawer), overview (budget KPIs, activity timeline), version list, funds, departments, accounts, fiscal years, users, settings, login, home, account pages
- [x] `window.confirm` retired; all outcomes via toasts; every list has empty and loading states
- [x] Accessibility pass: contrast tokens, focus rings, Escape on dialogs, landmarks, `aria-current` stepper, reduced motion
- [x] Checked at 1440 and 390 px; walkthrough 05; interview prep
- [ ] Spencer approves Phase 4.5

## Phase 5 — Public transparency portal  `phase-5-portal`
Design-first: this is the screen a citizen (and an interviewer) sees without logging in.
- [x] Portal mockup (approved with the Phase 4.5 set in `docs/design/mockups.html`): question-led navigation, bars with table twins, mobile
- [x] `ISnapshotQueryService` (Application) over `PublicPortalDbContext` (Infrastructure): budget header, breakdowns by fund/category/department/source, fund and department pages, lines, year over year, search
- [x] Static SSR pages under `/transparency/{slug}/{year?}`: index, overview, spending, revenue, funds, fund, department, years, search; `PortalLayout` with year pills and section nav; 404 for unknown slugs/years
- [x] `Breakdown` component: CSS bars with values, `$ | %` toggle as links, `<details>` table twin; `PortalKpi`, `PortalCrumbs`, `MoneyShort`
- [x] CSV (`CsvWriter`) and XLSX (`ClosedXmlSpreadsheetExporter`, ClosedXML) downloads via minimal API endpoints
- [x] Output caching: `PortalOutputCachePolicy` (base policy, tag `portal:{slug}`), `PortalResponseMiddleware` (public max-age, no antiforgery cookie), `OutputCacheSnapshotInvalidator` (evict by tag on publish/unpublish); ADR-0021
- [x] Split queries on both contexts (the two-collection includes triggered EF's cartesian warning)
- [x] Accessibility: landmarks, breadcrumb list, `aria-current`, chart text alternatives, works without JavaScript, checked at 1440 and 390 px
- [x] Tests: 9 integration (`SnapshotQueryServiceTests`), 4 unit (CSV, XLSX), 42 Web (cache policy, invalidator, middleware, `Breakdown`, helpers); 347 total
- [x] Walkthrough 06; interview prep Phase 5
- [x] Spencer approves Phase 5 (2026-09-18)

## Phase 6 — Import/export & reports  `phase-6-import-reports`
- [x] `IBudgetImportService`: CSV/XLSX upload, preview with per-row Add/Update/Unchanged/Error, commit re-validates and applies through the aggregate with one audit event (ADR-0022)
- [x] `ImportAnalyzer` (pure rules), `ImportFileParser` (columns by name), `CsvReader`, `ISpreadsheetReader` (ClosedXML)
- [x] Import screen: file picker, four KPIs, preview grid with error rows, confirm dialog; Finance Director only, editable versions only
- [x] XLSX export: workspace lines (in the import's layout), the three reports, funds/departments/accounts; minimal API under `/admin/export` behind the same policies
- [x] `IReportService` + `ReportBuilder`: Budget Summary by Fund, Department Budget Detail (with department filter), Revenue vs. Expenditure by Category
- [x] Report screens with a printable header block, Print (`window.print`) and Export XLSX; `@media print` stylesheet; Reports in the sidebar; Tools menu on the workspace
- [x] Tests: 24 unit (readers, parser, analyser, builders), 8 integration (import service, report service), 8 bUnit; 387 total
- [x] Walkthrough 07; interview prep Phase 6; ADR-0022
- [x] Spencer approves Phase 6 (2026-09-18)

## Phase 7 — AWS deployment (deploy-ready)  `phase-7-aws`
- [x] Multi-stage `Dockerfile` (non-root, healthcheck, forwarded headers), `.dockerignore`, `docker-compose.full.yml` (app + SQL Server, one command)
- [x] App changes for containers: `DatabaseOptions` (compose the connection string from parts), `Database:MigrateOnStartup` / `SeedDemoData` switches, Data Protection keys in SQL Server (`AddDataProtectionKeys` migration)
- [x] Checked current AWS docs: ECS Express Mode (L1 only, public subnets, single container) and Beanstalk rejected for `ApplicationLoadBalancedFargateService`; tradeoffs in ADR-0023
- [x] CDK stack (C#): VPC (2 AZs, 1 NAT), ECR, RDS SQL Server Express (private, encrypted), Secrets Manager (RDS-managed + demo password), Fargate service behind a public ALB (sticky, `/health`, circuit breaker), CloudWatch logs, Budgets alarm, outputs
- [x] `GitHubOidcStack`: OIDC provider + deploy role trusting one repo on `v*` tags / `production`; ECR push + CDK bootstrap roles only
- [x] 17 CDK assertion tests (`tests/CivicBudget.Infra.Tests`); `cdk synth` + Docker build job in CI
- [x] `deploy.yml` (OIDC, ECR push tagged by SHA, `cdk deploy -c imageTag`, `workflow_dispatch`, gated on `AWS_DEPLOY_ROLE_ARN`)
- [x] Cost note (~$90/mo, NAT a third), teardown (`cdk destroy`), Budgets alarm; walkthrough 08; interview prep; ADR-0023
- [x] Spencer approves Phase 7 (2026-09-18)

## Phase 8 — Polish  `phase-8-polish`
Spencer's review (2026-09-18): strip title-plus-explanation clutter into tooltips; fix overlapping elements on the overview.
- [x] `InfoTip` component (CSS only, keyboard focusable, accessible name); `Tip` on `PageHeader` and `KpiCard`; every explanatory subtitle removed or moved into a tip; subtitles carry data only
- [x] Overview overlap fixed: the workspace-only sticky-header `overflow: visible` rule scoped to `.cb-workspace`
- [x] Accessibility check from the accessibility tree and a keyboard walk: account menu and brand links named, amount inputs labelled with account name and department, dialogs and drawer take focus on open, access-denied page styled
- [x] Leftover "Arrives in Phase 6" grid Export placeholder wired to the XLSX endpoint; portal section leads trimmed
- [x] README screenshots (7) via a reproducible Playwright script (`scripts/screenshots`)
- [x] Tests: 406 total (+2 InfoTip)
- [x] Docs sweep: `docs/DEMO-SCRIPT.md`, spec status table (SPEC §12), 60-second pitch refreshed, walkthrough 09, interview prep Phase 8
- [x] Tagged `v1.0.0` (2026-09-19); the deploy workflow ran and skipped as designed (no `AWS_DEPLOY_ROLE_ARN`)
- [x] Spencer approves Phase 8 (2026-09-19)

---

# v1.1 — Plugged into the ERP, department-first (2026-09-19)

Spencer's reframing after v1.0.0: the chart of accounts, funds, and departments come from a parent
ERP (think VIP). CivicBudget plugs in beside it: it takes the chart from the ERP, its own admins
create logons, and each user lands in their own department to enter budget against the accounts
there. Full account numbers read the Ohio way (fund-department-object, e.g. `1000-725-121` for a
UAN village or `101-110-5100` for a county), and permissions follow the department assignment.

## Phase 9a — Ohio account numbers  `phase-9a-account-numbers`
- [x] Research note: UAN structure (fund-program-object for appropriations, fund-receipt for revenues; fund ranges by type), county/VIP-style variants; `docs/research/ohio-account-numbers.md`
- [x] `AccountNumberFormat` per government (segment widths, separator, middle-segment name "Program" or "Department"); `AccountNumber` value object that composes and parses `1000-725-121`, and `1000-110` for revenue lines; editable under Government settings with a live example
- [x] Full account number everywhere a line appears: workspace grids, reports, exports, portal tables and download, portal and workspace search by full or partial number
- [x] Import accepts an `Account Number` column as an alternative to Fund/Department/Account; the export writes both
- [x] Seed: Maple Ridge department codes are UAN program numbers (110 Police, 620 Streets, 725 Finance); Pine Hollow uses a dotted "Department" format
- [x] Snapshot lines store the composed number; migration backfills existing ones
- [x] Tests: compose/parse/validate (Domain), parser column (Application), settings round trip, snapshot and search (integration); walkthrough 10; ADR-0024
- [x] Spencer approves Phase 9a (2026-09-19)

## Phase 9b — The chart comes from the ERP  `phase-9b-erp-chart`
- [x] `ErpChart` contract and `IErpChartSource` adapter in Application; `ErpChartFileSource` reads a one-row-per-code CSV/XLSX export (Kind, Code, Name, Type, Category, Description, Active), forgiving about spelling and order
- [x] `ChartDiff` (pure): Add, Update, Deactivate, Reactivate, Unchanged per code with before/after; `ChartSyncService` preview then commit through the entities (audit interceptor sees every field), `ChartSync` log row, audit event
- [x] Never deletes; the confirm dialog warns when a file would deactivate more than a quarter of the chart (a partial export)
- [x] `Government.ChartSource` Local/Erp: setup services refuse writes under Erp (`ChartOwnership`); Funds/Departments/Accounts show a "Managed by the ERP · last synced" banner and lose New/Edit; Administrator can switch back
- [x] Chart sync page under Setup: upload, preview grid, KPIs, apply, sync history with a change drawer; ADR-0025; walkthrough 11
- [x] Tests: 10 unit (differ, file source incl. XLSX), 4 integration (preview, commit with audit and log, guards, source switch), 1 bUnit (read-only list)
- [x] Spencer approves Phase 9b (2026-09-19)

## Phase 9c — Users, logons, and permissions  `phase-9c-users-permissions`
- [x] Administrator is a superset: `IsFiscalAuthority()` in every service, the four fiscal policies include Admin (may enter lines, set balances, run the workflow, publish, import, sync the chart, and manage users)
- [x] Role names as the customer says them: Administrator, Fiscal Officer, Department User, Viewer (DB values unchanged)
- [x] Temporary passwords: set on create and on admin reset (`MustChangePassword`), carried as a claim, enforced by `MustChangePasswordMiddleware`, cleared by the change-password page which then sends the user into the app
- [x] User administration audited: created, updated, password reset, locked, unlocked, in the acting administrator's name
- [x] Department assignment bounds every read: workspace, reports, exports, search, and now the audit trail (recent activity and line history)
- [x] Tests: policy matrix with Admin, middleware, permissions per role through the services, audit scoping, user admin flag and audit; ADR-0026; walkthrough 12
- [ ] Spencer approves Phase 9c

## Phase 9d — Department-first budgeting  `phase-9d-department-entry`
- [ ] After sign-in a department user lands on "My department" for the open version (a picker when assigned to several)
- [ ] Department entry page: the department's accounts across its funds as full account numbers, prior year actual, current budget, request, change; running totals; a narrative/justification for the department as a whole
- [ ] Department submits to the fiscal officer (per-department Submitted status on the version; fiscal officer can return it); the workspace shows which departments are in
- [ ] Portal and Department Detail report carry the department narrative
- [ ] Tests, walkthrough, interview prep

Order: 9a → 9b → 9c → 9d. Each is a PR with the usual report; approve before the next starts.

