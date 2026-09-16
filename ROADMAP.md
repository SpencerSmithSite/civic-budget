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
- [ ] Verify Docker Desktop Rosetta; `docker-compose.yml` with SQL Server 2022
- [ ] Solution + 4 src projects + 4 test projects, `Directory.Build.props` (nullable, warnings-as-errors, file-scoped namespaces, .NET 10)
- [ ] Domain entities, enums, invariants, `DomainException`
- [ ] `FundBalanceCalculator`, percent-change rounding
- [ ] `ITenantOwned`, `ITenantContext`, global query filters, tenant stamp interceptor
- [ ] `CivicBudgetDbContext`, configurations, `decimal(18,2)` convention, initial migration
- [ ] Seed: Maple Ridge (FY2025 adopted, FY2026 original + Amendment 1, FY2027 draft) and Pine Hollow Township
- [ ] `ci.yml`: restore, build, test, test-results summary
- [ ] Domain unit tests for every rule in SPEC §5; integration test for tenancy filter
- [ ] `docs/walkthroughs/01-foundation.md` (query filters, DbContextFactory, decimal convention)

## Phase 2 — Identity & maintenance  `phase-2-identity`
- [ ] ASP.NET Core Identity, cookie auth, claims factory (`government_id`, `department_id`)
- [ ] Roles + policies + resource-based `BudgetLineEditRequirement`
- [ ] Authorization tests via `IAuthorizationService`
- [ ] CRUD pages: funds, departments, accounts, fiscal years, users (QuickGrid)
- [ ] Friendly error pages, `/health`, JSON logging
- [ ] Walkthrough: policy vs. role vs. resource-based authorization

## Phase 3 — Budget entry  `phase-3-budget-entry`
- [ ] By-department entry (grouped, subtotals)
- [ ] By-account grid with inline editing, filter, sort
- [ ] Fund balance panel with appropriation-limit Warn/Block
- [ ] `AuditInterceptor` + per-line history view
- [ ] bUnit tests for grid and panel; integration tests for audit
- [ ] Walkthrough: SaveChanges interceptors; keeping logic out of components

## Phase 4 — Workflow & publishing  `phase-4-workflow`
- [ ] Draft → Proposed → Adopted transitions with confirmation and role checks
- [ ] Amendments (copy adopted → new draft, reason, resolution number)
- [ ] Publish → `PublishedBudgetSnapshot`; unpublish; republish; history view
- [ ] `PublicPortalDbContext`
- [ ] Walkthrough: snapshot boundary as a security decision

## Phase 5 — Public transparency portal  `phase-5-portal`
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
- [ ] Demo data review, final accessibility check
- [ ] Dependabot (NuGet + Actions)
- [ ] All walkthroughs complete; CHANGELOG finalized
