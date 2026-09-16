# Changelog

All notable changes to CivicBudget. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/); one section per phase.

## [Unreleased]

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
