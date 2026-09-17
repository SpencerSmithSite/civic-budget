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
- [ ] Draft → Proposed → Adopted transitions with confirmation and role checks
- [ ] Amendments (copy adopted → new draft, reason, resolution number)
- [ ] Publish → `PublishedBudgetSnapshot`; unpublish; republish; history view
- [ ] `PublicPortalDbContext`
- [ ] Walkthrough: snapshot boundary as a security decision

## Phase 4.5 — Admin UI design pass  `phase-4.5-admin-design`
Spencer's review of Phase 3 (2026-09-17): functional, but bare-bones Bootstrap will not impress in an interview. This phase gives the admin app a deliberate visual identity before the public portal reuses it.
- [ ] Design brief: palette (civic, trustworthy; sufficient contrast), typography, spacing scale, iconography (vendored Bootstrap Icons), states (empty, loading, error, success)
- [ ] Mockups of the key screens (login, admin overview, budget workspace, fund balance panel, users) for Spencer's approval before implementation
- [ ] Design tokens as CSS variables layered over Bootstrap (`--bs-*` overrides), one `app.css` theme, no build pipeline
- [ ] Application shell: branded navbar, sidebar with icons and active states, page headers with breadcrumbs and actions, responsive behavior
- [ ] Components: KPI cards on the overview, status badges, toasts instead of inline alerts, confirmation modal instead of `window.confirm`, skeleton loading, consistent form layout and validation styling
- [ ] Budget workspace: worksheet look (sticky header, zebra rows, right-aligned numerics, change columns colored by sign, fund panel as summary cards + table)
- [ ] Login page and home page with the product story (what CivicBudget is, who it is for)
- [ ] Accessibility check of the new theme (contrast, focus states, keyboard navigation)
- [ ] Walkthrough: design tokens over Bootstrap; how the theme is shared with the portal

## Phase 5 — Public transparency portal  `phase-5-portal`
Design-first: this is the screen a citizen (and an interviewer) sees without logging in.
- [ ] Mockups (overview, drill-down, charts, mobile) approved before implementation
- [ ] `/transparency/{slug}/{year?}` overview, drill-down with breadcrumbs
- [ ] Charts with data-table alternatives; year-over-year
- [ ] Search; CSV + XLSX download
- [ ] Output caching with tag eviction on publish
- [ ] Accessibility pass (WCAG 2.1 AA checklist)
- [ ] Walkthrough: static SSR vs. Interactive Server; output caching

## Phase 6 — Import/export & reports  `phase-6-import-reports`
- [ ] CSV/XLSX import with validation preview
- [ ] XLSX export of any grid/report (ClosedXML)
- [ ] Reports: Budget Summary by Fund, Department Budget Detail, Revenue vs. Expenditure by Category; print CSS
- [ ] Walkthrough: server-side Excel without Office

## Phase 7 — AWS deployment (deploy-ready)  `phase-7-aws`
- [ ] Dockerfile (multi-stage), full-stack docker compose
- [ ] Check current AWS docs: ECS Express Mode vs. Elastic Beanstalk, record tradeoffs
- [ ] CDK stack (C#): VPC, ALB, ECS/Fargate, RDS SQL Server Express, Secrets Manager, CloudWatch, GitHub OIDC role
- [ ] CDK assertion tests; `cdk synth` in CI
- [ ] `deploy.yml` (OIDC, ECR push, `cdk deploy`, `workflow_dispatch`)
- [ ] Cost note, teardown command, AWS Budgets alarm
- [ ] Walkthrough: how OIDC removes AWS keys from GitHub

## Phase 8 — Polish  `phase-8-polish`
- [ ] README with screenshots, demo logins, badges, branch-protection guidance
- [ ] Final visual QA across every screen at desktop and phone widths; demo data review; final accessibility check
- [ ] Dependabot (NuGet + Actions)
- [ ] All walkthroughs complete; CHANGELOG finalized
