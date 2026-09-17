# Changelog

All notable changes to CivicBudget. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/); one section per phase.

## [Unreleased]

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
