# Changelog

All notable changes to CivicBudget. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/); one section per phase.

## [Unreleased]

## Phase 17 — 2026-09-23 (maintenance)
A review of the whole codebase (five parallel reviews by layer, each finding checked against the code) and a walk through the running app as every demo user. About 90 fixes; the headline ones:
### Security
- Sign-in no longer follows a return URL of `//other-host` (`LocalUrl`); a temporary password cannot be "changed" to itself; exports (`*.xlsx`) no longer skip the forced password change; uploaded images must be PNG, JPEG, or WebP whose bytes match (no SVG), capped at the stored size, and are served with `nosniff` and a sandboxing CSP; CSV text that a spreadsheet would run as a formula is written as text.
- Setup services (funds, accounts, departments, fiscal years, settings) check the caller's role themselves; every admin page is pinned to its policy by a test.
- The portal cache varies only on the query keys pages read, so it cannot be bypassed with junk parameters; the anonymous `/health/ready`, which queried the database per call, is removed.
### Fixed
- Admin pages that did not refresh: Add line, deactivate/lock/close on five lists, Start an amendment (the page kept showing the adopted budget), and phone cards that ignored the pager.
- An amount cell keeps showing a refused value; amounts past `decimal(18,2)` crashed the save; the settings preview crashed on a negative width; an admin page error killed the connection with no message (now an error boundary and the error bar).
- ERP chart sync could retype an account budget lines use; closed fiscal years accepted amendments; audit text could exceed its column after the action succeeded; import rejected its own zero-padded export and read `1234,56` as 123,456.
- Changing a government's public address stranded its published budgets on the old one; portal not-found pages were empty; search and year pages answered 200 for unknown years.
- A failed database wake-up check left the site on the waiting screen until restart.
- Owned values (the account number format) were never audited.
### Changed
- Times show in Eastern time with the zone named; amounts format as en-US whatever the server culture.
- Portal and sign-in pages load no JavaScript; the portal loads a budget once per request (the funds page made about 54 queries).
- Indexes: fund-level lines unique per version, one active snapshot per year, recent activity by government and time.
- Deploys wait for CI and check the image before pushing; the image is published for its platform (about 64 MB smaller on disk); NuGet is cached in CI.
- Accessibility: contrast, focus returned from dialogs and drawers, one h1 per page, error toasts that stay, linked charts that screen readers can use.

## Phase 16 — 2026-09-21 (v1.1)
### Fixed
- A database that paused while the container stayed up (an open admin tab can keep it running) no longer hangs the next page load: after 55 minutes without a page request, the next one checks the database first and shows the waiting screen if it is asleep (`DatabaseWaker`).
- The waiting screen now actually appears on a cold start. Data Protection reads its key ring during host startup and our keys live in SQL Server, so on a resuming database EF retried that read for about fifty seconds before Kestrel ever started listening. The key ring is loaded lazily instead (`DataProtectionStartup.DeferKeyRingLoad`); against a database address that hangs, time to the first page went from 31 seconds to 1.
### Changed
- Cold start: the host listens as soon as the process is up and prepares the database behind it (`DatabaseStartupService`). Until it is ready, every page request gets a waiting screen in the app's style (503 with `Retry-After`) that counts the seconds and continues to the requested page on its own; `/health/startup` is the in-memory check it polls. The Azure startup probe checks every two seconds instead of ten. First paint after an idle hour drops from about 65 seconds to about 20; the site itself still appears at about 65 while serverless SQL resumes.

## Phase 15 — 2026-09-21 (v1.1)
### Added
- Phones: list pages (funds, departments, chart of accounts, fiscal years, users, budget versions, publishing history, chart sync history, department board, overview) render as cards (`ListCard`); working grids keep three columns with a chevron that opens a bottom sheet carrying every figure, the editable amount, the note, and history (`LineDetail`); Fund Summary shows a certificate card per fund and Department Detail a card per department. Tables remain on wider screens and in print.

## Phase 14 — 2026-09-21 (v1.1)
### Fixed
- Phones: the budget workspace and department pages no longer force Safari to zoom out. Grid columns can shrink below their content (`minmax(0, 1fr)`), hidden tooltips leave the layout, the settings page's limit-mode options are shorter, and toolbar filters go full width under 576px.
### Added
- Phones: data grids pin their first column while scrolling sideways; `scripts/screenshots/mobile-sweep.mjs` checks every route for horizontal overflow.

## Phase 13 — 2026-09-20 (v1.1)
### Added
- Azure hosting for the live demo: `infra/azure/main.bicep` (serverless Azure SQL under the free offer, Container App on the consumption plan, nightly reset job, capped Log Analytics), `scripts/azure-setup.sh` (one-time create plus GitHub OIDC wiring), `.github/workflows/deploy-azure.yml` (image to GHCR and roll on every push to `main`), and a `bicep-build` CI job.
- `dotnet CivicBudget.Web.dll --reseed`: drops every table, migrates, and seeds (`DatabaseInitializer.ResetAsync`); the demo runs it nightly.
- README "Live demo" section with the demo logins.
### Changed
- The database initializer waits up to two minutes for SQL Server (a paused serverless database resumes in about one).

## Phase 12 — 2026-09-20 (v1.1)
### Added
- Portal header shows the CivicBudget mark, or the government's own logo uploaded under Government settings (`GovernmentLogos`, migration `AddGovernmentLogos`, served at `/transparency/{slug}/logo`).
- Glossary page (`/transparency/{slug}/{year}/glossary`) with the accessibility statement.
- Overview: "Where does the money go?" and "Where does it come from?" are two panels on a sliding track switched by tabs, still without JavaScript.
### Changed
- Overview: the resolution, published date, and version line moved from the top to the foot of the page; "every line of the adopted budget" removed.
- Portal footer is one line: budget, downloads, glossary, accessibility.

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
