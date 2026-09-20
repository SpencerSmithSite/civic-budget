# Changelog

All notable changes to CivicBudget. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/); one section per phase.

## [Unreleased]

## Phase 11 — 2026-09-20 (v1.1)
### Changed
- Front door and sign-in page read like a product, not a project: one centered headline on the navy half, "Sign in to view and enter data" and a link to the public portal on the other. The portfolio, demo, stack, and "fictional data" copy is gone from the app (the README still says it); the redundant "Sign in" link left the public header.

## Phase 10 — 2026-09-20 (v1.1)
### Added
- A CivicBudget logo mark (civic building on a teal tile) as inline SVG and favicon.
- Profile pictures: upload on **Profile picture** (account menu), resized in the browser to 256 px, stored in `UserAvatars`, shown in the top bar, user list, recent activity, and line history; administrators can remove a user's picture. Migration `AddUserAvatars`.
- `[NotAudited]` for entity properties whose change is already a named audit event.
### Changed
- The admin sidebar shows the government's name beside the mark instead of the product name.
- Department request submit/return no longer write id and timestamp field changes to the activity feed.

## Phase 9d — 2026-09-19 (v1.1)
### Added
- Department-first budgeting: sign-in lands in the app, and a department user's home is **My department**, which opens their department for the budget in progress (or a board of their departments).
- Department entry page: every account of the department across its funds as full numbers with prior actual, current budget, request, and change; fund subtotals and a running department total; revenue credited to the department shown apart; a narrative editor; Submit to the fiscal officer.
- Per-department request status on a version (in progress / submitted / returned). Submitting locks the department's lines and narrative for department users; the fiscal officer can return a request with a note. Both are audited.
- Department board (`/admin/budgets/{id}/departments`) showing every department's status and totals, with Return; a chip per department above the workspace grid.
- The department narrative prints on the Department Budget Detail report and on the public portal ("From the department"); snapshots freeze it (`PublishedBudgetSnapshotDepartments`).
### Changed
- Seed: FY2026 narratives reach the portal; FY2027 is mid-round (Police submitted, Parks returned). Reseed with `docker compose down -v && docker compose up -d`.
- Migration `AddDepartmentRequests`.

## Phase 9c — 2026-09-19 (v1.1)
### Changed
- Administrator has complete access: everything the Fiscal Officer can do plus users and settings.
- Role names shown as Administrator, Fiscal Officer, Department User, Viewer.
- A department user's audit trail (recent activity, line history) is limited to their own departments' lines.
### Added
- Temporary passwords: accounts created or reset by an administrator must change their password at the next sign-in (`MustChangePassword`, claim, middleware).
- User administration is audited (create, update, reset, lock, unlock).

## Phase 9b — 2026-09-19 (v1.1)
### Added
- Chart of accounts sync from the parent ERP: upload its export, preview adds/updates/deactivations, apply; sync log with a change drawer; audit event. Never deletes.
- `Government.ChartSource` (Local/Erp): under Erp the setup screens are read-only with a "Managed by the ERP" banner and the services refuse writes; an Administrator can switch back.
- `IErpChartSource` adapter boundary (ADR-0025) with a file-based first implementation.

## Phase 9a — 2026-09-19 (v1.1)
### Added
- Full Ohio-style account numbers (`1000-725-121`, `1000-110` for revenue) composed under a per-government `AccountNumberFormat` (widths, separator, Program/Department), shown in grids, reports, exports, the portal, and searchable everywhere; import accepts an `Account Number` column.
- Government settings: account number format with a live example.
### Changed
- Seed department codes are UAN program numbers; Pine Hollow demonstrates a dotted county-style format. Reseed with `docker compose down -v && docker compose up -d`.
- Published snapshot lines store the composed number (`AddAccountNumberFormat` migration backfills).

## Phase 8 — 2026-09-18
### Changed
- Explanatory subtitles removed across the admin app; the useful ones are info tips beside titles and KPI labels. Portal section leads trimmed.
- Overview: budget versions table no longer overflows into the activity card.
- Accessibility: named account menu and brand links, disambiguated amount input labels, dialogs and drawer take focus on open, styled access-denied page.
- Account grid Export button now downloads the XLSX (was a placeholder).
### Added
- README screenshots and the Playwright script that regenerates them; demo script; spec status table; walkthrough 09.

## Phase 7 — 2026-09-18
### Added
- `Dockerfile` (multi-stage, non-root, healthcheck) and `docker-compose.full.yml` for a one-command containerized demo.
- AWS CDK app in C# (`infra/CivicBudget.Infra`): VPC, ECR, RDS SQL Server Express, Secrets Manager, Fargate service behind an ALB, CloudWatch logs, Budgets alarm; a one-time GitHub OIDC stack; 17 assertion tests; `cdk synth` and Docker build in CI; gated `deploy.yml`.
- `DatabaseOptions`: connection string composed from `Database:*` settings so ECS can inject the RDS-managed password; `MigrateOnStartup` and `SeedDemoData` switches.
- Data Protection keys persisted in SQL Server (`DataProtectionKeys` table) so cookies survive container restarts.
- 404 tests total (+17).

## Phase 6 — 2026-09-18
### Added
- Import of budget lines from CSV or XLSX with a validation preview (per-row Add/Update/Unchanged/Error), committed through the aggregate in one save with an audit event; never deletes.
- XLSX export of the workspace lines (same layout as the import), the three reports, and the setup lists.
- Reports: Budget Summary by Fund, Department Budget Detail (filter by department), Revenue vs. Expenditure by Category; on screen, printable, and as XLSX.
- `CsvReader`, `ISpreadsheetReader` (ClosedXML), `Labels` for plain-language enum names.
- 387 tests total (+40).

## Phase 5 — 2026-09-18
### Added
- Public transparency portal at `/transparency/{slug}/{year?}`: overview with KPIs, where it goes, where it comes from, funds, fund and department drill-downs, year over year, search. Static SSR, no login, no JavaScript.
- `ISnapshotQueryService` read model over the read-only portal context; `Breakdown` bar chart with `$ | %` toggle and table twin.
- CSV and XLSX downloads of every published line (ClosedXML added).
- Output caching for portal pages with eviction by government tag on publish/unpublish; portal responses are `public, max-age=600` with no antiforgery cookie (ADR-0021).
- 347 tests total (+55).
### Changed
- Both DbContexts use split queries for multi-collection includes.

## Phase 4.5 — 2026-09-18
### Added
- Design research (Ohio vendors, admin budgeting UIs, transparency portals), design brief, and eight approved mockups under `docs/design/`.
- Theme: design tokens as CSS variables over Bootstrap; Bootstrap Icons vendored.
- Admin shell with dark module sidebar, breadcrumb top bar, user menu, and off-canvas navigation on small screens.
- Components: page header, status pills, workflow stepper, KPI cards, toasts, restyled confirm dialog, side drawer, row menus, empty and skeleton states.
### Changed
- Every admin screen restyled: workspace with fund rail and grouped worksheet, overview with budget KPIs and activity, all lists, users, settings, login, home, account pages.
- `window.confirm` removed; outcomes reported with toasts.

## Phase 4 — 2026-09-17
### Added
- Workflow: Propose, Return to draft, Adopt with resolution number; Ohio appropriation limit enforced at transitions (Block refuses, Warn requires acknowledgement); audit events per transition.
- Amendments: new draft copied from an adopted version with a reason; adoption supersedes the prior version.
- Publishing: immutable `PublishedBudgetSnapshot` with denormalized lines and funds; publish, unpublish, republish with history; `PublicPortalDbContext` read-only over the three snapshot tables.
- `ConfirmDialog` component and the workflow bar; publishing history on the version list.
- 292 tests total (+19).

## Phase 3 — 2026-09-17
### Added
- Budget entry: version list, workspace with by-department and by-account-line modes, inline editing, add/remove lines, justification notes.
- Fund balance panel with estimated resources, appropriations, projected ending balance, and the appropriation limit in Warn/Block severity; Finance Director edits beginning balances inline.
- Audit trail: `AuditInterceptor` records create/delete/field changes for `[Audited]` entities with user and UTC time; per-line history view; `AddAuditTrail` migration.
- 273 tests total (+25).
### Changed
- Domain entity keys declared `ValueGeneratedNever` so aggregates can add children through their own methods (ADR-0018).
- Fixed ports (5001/5000) and readable console logging in Development; `DatabaseInitializer` waits for SQL Server.
- Dependabot: EF Core 10.0.12, GitHub Actions majors.

## Phase 2 — 2026-09-16
### Added
- ASP.NET Core Identity with cookie sign-in; `ApplicationUser` (government, display name, department assignments); login, logout, profile, and change-password pages; lockout after 5 failed attempts.
- Claims-based tenant resolution: `CurrentUserContext` filled per HTTP request and per circuit.
- Roles (Admin, Finance Director, Department Head, Viewer), nine named policies, resource-based budget line edit rule.
- Application layer: `Result`, FluentValidation validators, `ICivicBudgetDbContext`, setup services, user administration contract.
- Admin area (Bootstrap + QuickGrid): overview, funds, departments, chart of accounts, fiscal years, users, government settings.
- Demo users (one per role) seeded with a password from user-secrets.
- 248 tests total (+69).
### Changed
- Code comments use plain punctuation (no em dashes); seed account names use hyphens.

## Phase 1 — 2026-09-16
### Added
- Solution scaffold: Domain / Application / Infrastructure / Web + four test projects; central package management; analyzers with warnings-as-errors.
- Domain model: Government, Fund, Department, Account, FiscalYear, BudgetVersion (Draft → Proposed → Adopted, amendments), BudgetLine, FundBeginningBalance; `FundBalanceCalculator`, `AppropriationLimitCheck`, `Money` rounding.
- EF Core: `CivicBudgetDbContext`, entity configurations, `decimal(18,2)` convention, `InitialCreate` migration, tenant query filters, `TenantSaveChangesInterceptor`.
- Seed data for Village of Maple Ridge (3 fiscal years, 4 versions, 380 lines) and Pine Hollow Township.
- `docker-compose.yml` (SQL Server 2022), `scripts/dev-setup.sh`, health checks, JSON console logging.
- GitHub Actions `ci.yml`, PR template, Dependabot.
- 179 tests: domain, architecture, bUnit smoke, Testcontainers integration.

## Phase 0 — 2026-09-15
### Added
- Functional specification (`docs/SPEC.md`), architecture (`docs/ARCHITECTURE.md`),
  decision records ADR-0001…0011 (`docs/DECISIONS.md`), roadmap, `CLAUDE.md`.
