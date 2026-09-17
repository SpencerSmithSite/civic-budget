# Architecture Decision Records

Lightweight ADRs. Newest at the bottom. Every NuGet package added to the
solution must be justified here (see "Packages" at the end).

Format: **Context** → **Decision** → **Alternatives considered** → **Consequences**.

---

## ADR-0001 — Four-project clean-ish architecture, no mediator/CQRS framework
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** The project must be explainable in an interview and testable
without a browser. Blazor apps commonly rot by putting EF queries in
components.

**Decision.** `Domain` → `Application` → `Infrastructure`/`Web`, with plain
application services (classes with async methods) called by components.

**Alternatives.** (a) Single web project — fastest, but domain rules end up
in `.razor` files. (b) MediatR/CQRS with handlers per request — well known,
but adds indirection and a package for no gain at this size; also MediatR
moved to a commercial license in 2025.

**Consequences.** Slightly more files; very clear "where does X go" answer;
services are trivially unit-testable.

---

## ADR-0002 — Blazor Web App with per-area render modes
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Two audiences with opposite needs: a few authenticated editors,
many anonymous readers.

**Decision.** Admin area uses `InteractiveServer`; public portal uses static
server-side rendering with no interactivity (progressive enhancement only).

**Alternatives.** (a) Everything Interactive Server — simplest, but every
citizen opens a SignalR circuit and holds server memory; poor SEO/caching.
(b) WebAssembly for the portal — large download, no benefit for read-only
tables. (c) Separate MVC project for the portal — duplicates layout and
hosting.

**Consequences.** Portal pages must be written without `@onclick` state;
charts need a JS-free fallback (which WCAG wants anyway). Output caching
becomes possible.

---

## ADR-0003 — `IDbContextFactory` instead of scoped `DbContext` in Blazor Server
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** In Blazor Server the DI scope is the circuit, not the request.
A scoped `DbContext` lives for the life of the browser tab and is shared by
concurrent event handlers; `DbContext` is not thread-safe.

**Decision.** Register `AddDbContextFactory<CivicBudgetDbContext>()`. Every
unit of work creates and disposes its own context.

**Alternatives.** (a) Scoped context + `OwningComponentBase` — works but
still one context per component lifetime and easy to misuse. (b) Manual
`IServiceScopeFactory` — same effect with more code.

**Consequences.** No change tracking across operations; each service method
loads what it needs. Simpler reasoning, no stale-entity bugs.

---

## ADR-0004 — Tenancy via `ITenantContext` + EF Core global query filters
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Multiple governments in one database; leaking one tenant's
draft budget to another is the worst possible bug.

**Decision.** Single database, shared schema, `GovernmentId` on every
tenant-owned row, global query filters bound to `ITenantContext`, a save
interceptor that stamps and verifies `GovernmentId`.

**Alternatives.** (a) Database-per-tenant — strongest isolation, but
operationally heavier (migrations × N, connection routing) and overkill for
the demo. (b) Schema-per-tenant — SQL Server supports it, EF Core tooling
is awkward. (c) Row-level security in SQL Server — good defense-in-depth,
could be added later; not a substitute for app-level filtering.

**Consequences.** Every query is filtered automatically; `IgnoreQueryFilters`
is treated as a code smell and tested for. Portal resolves tenant by slug.

---

## ADR-0005 — Public portal reads immutable published snapshots only
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Citizens must never see draft or proposed figures; what was
published on a date must be reproducible.

**Decision.** Publishing writes a denormalized `PublishedBudgetSnapshot`
(+ lines). The portal reads only those tables. Unpublish soft-marks; history
is kept.

**Alternatives.** (a) Portal queries the Adopted version with a status
filter — one forgotten `.Where` exposes drafts; renaming an account later
changes history. (b) Static site generation to S3 on publish — attractive,
but search and downloads become extra work; can still be added as a cache.

**Consequences.** Some data duplication (intended); publishing is an
explicit, audited event; cache invalidation is trivial.

---

## ADR-0006 — Separate read-only `PublicPortalDbContext`
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** ADR-0005 says the portal reads only snapshots. Enforce it
structurally rather than by convention.

**Decision.** A second `DbContext` that maps only snapshot entities,
`NoTracking`, no migrations of its own. The main context owns the schema.

**Alternatives.** One context with discipline — cheaper, but "discipline"
is not a security control.

**Consequences.** The portal physically cannot query live tables. In AWS
the portal login can be `SELECT`-only on snapshot tables. Two contexts to
keep in sync when snapshot tables change (rare).

---

## ADR-0007 — FluentValidation over DataAnnotations
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Validation must live in the Application layer and cover
cross-field rules (appropriation vs. resources, department required only
for expenditure accounts).

**Decision.** FluentValidation validators, one per command DTO, run by the
application service.

**Alternatives.** DataAnnotations — zero packages and Blazor `EditForm`
support, but attributes on DTOs handle cross-field/async rules poorly and
put rule logic in attribute metadata.

**Consequences.** One package (`FluentValidation`). Rules are ordinary,
unit-testable classes. UI shows returned errors rather than relying on
attribute-driven `DataAnnotationsValidator`.

---

## ADR-0008 — Deploy-ready AWS infrastructure without an AWS account
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** No AWS account or budget is available, but the target employer
runs on AWS and the deployment story matters in the interview.

**Decision.** Build everything up to the point of `cdk deploy`: Dockerfile,
full-stack `docker compose`, CDK stack in C#, `cdk synth` in CI, CDK
assertion tests for security invariants, and an OIDC-based `deploy.yml`
that is `workflow_dispatch`-gated. Document cost, teardown, and a Budgets
alarm as they would apply.

**Alternatives.** (a) Deploy to a free PaaS (Fly.io, Railway) — a live URL,
but tells the interviewer nothing about AWS. (b) LocalStack — RDS and ECS
are not in the free tier; weak fidelity. (c) Open an AWS account on the new
credit-based free tier — viable later; nothing in this decision blocks it.

**Consequences.** No live URL by default; strong, verifiable infra code.
If an account appears, `cdk bootstrap && cdk deploy` is the only new step.

---

## ADR-0009 — SQL Server 2022 via Docker (amd64 under Rosetta on Apple Silicon)
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** The employer's stack is SQL Server; the development Mac is
arm64 and Microsoft ships no arm64 SQL Server image.

**Decision.** Use `mcr.microsoft.com/mssql/server:2022-latest` with
`platform: linux/amd64` in `docker-compose.yml` and Testcontainers, relying
on Docker Desktop's Rosetta emulation. Verify on first Phase 1 run.

**Alternatives.** PostgreSQL — native arm64 and cheaper on RDS, but changes
the story the project is meant to tell. Azure SQL Edge — retired.

**Consequences.** Slower container start locally (~20–40 s). CI on Ubuntu is
native. The EF provider is isolated in `Infrastructure` so a swap remains
possible.

---

## ADR-0010 — Repository is public, unlicensed (all rights reserved)
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Spencer wants the repo visible for the interview but not
reusable by others.

**Decision.** No `LICENSE` file; README states "All rights reserved —
portfolio project, not licensed for use, modification, or distribution."
Branch protection on `main` (PRs only, no force-push).

**Alternatives.** Private repo (invisible to interviewers unless invited);
a restrictive license such as CC BY-NC-ND (still grants some rights).

**Consequences.** Anyone can read and technically fork (GitHub cannot
prevent forks of a personal public repo), but no rights to use are granted.

---

## ADR-0011 — QuickGrid for admin grids (no DevExpress)
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** No DevExpress license/trial available.

**Decision.** `Microsoft.AspNetCore.Components.QuickGrid` for the by-account
grid, with inline editing built from standard components.

**Consequences.** Less polish than a commercial grid; zero licensing risk;
all behavior is code we can explain.

---

## Packages

Every NuGet package and why. Add a row when adding a package.

| Package | Project | Why | ADR |
|---|---|---|---|
| Microsoft.EntityFrameworkCore.SqlServer | Infrastructure | Provider for SQL Server | 0009 |
| Microsoft.EntityFrameworkCore.Design | Infrastructure (PrivateAssets) | `dotnet ef` tooling; kept in Infrastructure so no startup project is needed | — |
| Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore | Web | `/health/ready` checks SQL connectivity through the DbContext | — |
| Microsoft.EntityFrameworkCore (abstractions) | Application | LINQ surface for `ICivicBudgetDbContext`; no provider | 0014 |
| FluentValidation | Application | Request validation as testable classes | 0007 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | Infrastructure | Identity stores in the same DbContext | 0015 |
| Microsoft.AspNetCore.Components.QuickGrid | Web | Admin grids | 0011 |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector | tests | Test framework + coverage (template defaults) | spec |
| bunit | Web.Tests | Blazor component tests | spec |
| Testcontainers.MsSql | IntegrationTests | Real SQL Server 2022 in tests | spec |
| dotnet-ef (local tool, `.config/dotnet-tools.json`) | — | Migrations CLI pinned per repo | — |

Planned for later phases (row confirmed when added): ClosedXML (Phase 6), Amazon.CDK.Lib + Amazon.CDK.Assertions (Phase 7, ADR-0008).

Not a package: Bootstrap 5.3 CSS/JS is vendored under `src/CivicBudget.Web/wwwroot/lib/bootstrap` (ADR-0016).

---

## ADR-0012 — Central Package Management and analyzers as errors
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** Eight projects; versions drift and analyzer warnings get ignored.

**Decision.** `Directory.Packages.props` holds every package version once.
`Directory.Build.props` sets `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`,
and `EnforceCodeStyleInBuild`; `.editorconfig` turns off the handful of rules that add
ceremony without value here (documented inline). EF migrations are marked generated code.
CI runs `dotnet format --verify-no-changes`.

**Consequences.** New packages must be added in two places (props + csproj) — intentional
friction that pairs with the Packages table above. Generated migrations are exempt.

---

## ADR-0013 — Tenant interceptor verifies rather than stamps
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** Two options for the write side of tenancy: stamp `GovernmentId` on new rows from
the ambient tenant, or require entities to carry it and verify on save.

**Decision.** Verify. Every tenant-owned entity takes `governmentId` in its constructor; the
interceptor throws `TenantIsolationException` on mismatch or missing tenant.

**Alternatives.** Stamping is convenient but hides the tenant from the domain and lets
`new Fund(...)` be valid with no owner.

**Consequences.** Slightly more explicit constructors; the domain can enforce cross-entity
tenant checks (e.g. `BudgetVersion.AddLine` rejects a fund from another government).

---

## ADR-0014 — Application depends on EF Core abstractions through `ICivicBudgetDbContext`
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** Application services need to query and save. Options: hand-written repositories
per entity, or an interface over the DbContext.

**Decision.** Application defines `ICivicBudgetDbContext` (domain `DbSet`s + `SaveChangesAsync`)
and `ICivicBudgetDbContextFactory`, and references the `Microsoft.EntityFrameworkCore` package
for the LINQ surface. It does not reference the SQL Server provider, Identity, ASP.NET Core, or
Infrastructure. Domain still references nothing. `ArchitectureTests` enforces all of this.

**Alternatives.** Repositories: more code that mostly re-implements `DbSet`, and awkward for
projections. Specification pattern: heavier than the project needs.

**Consequences.** Queries stay expressive; tests substitute the factory. Application is coupled
to EF Core's query shape, which is acceptable for a project whose persistence story is EF Core.

---

## ADR-0015 — Identity tables sit outside the tenant query filter
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** Sign-in must locate a user by email before any tenant is known.

**Decision.** `ApplicationUser` carries `GovernmentId` but is not `ITenantOwned`.
`UserAdminService` scopes every query by the current government explicitly, validates
department assignments against the filtered `Departments` set, and is covered by tests that
try to cross tenants.

**Alternatives.** A separate Identity database or context (more moving parts); resolving the
tenant from the email domain before login (fragile).

**Consequences.** One documented exception to "everything is filtered". The Identity tables
also hold no budget data, so the blast radius of a mistake is user metadata, not finances.

---

## ADR-0016 — Bootstrap 5 vendored as static files
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** The admin app needs a usable layout and form styling quickly; no DevExpress.

**Decision.** Copy Bootstrap 5.3 CSS and bundle JS from the Blazor template into `wwwroot/lib`.
No CDN, no npm, no NuGet.

**Alternatives.** CDN (external runtime dependency; some government networks block it);
hand-written CSS (slower to a decent result; still an option for the public portal, which has
different needs).

**Consequences.** ~300 KB in the repo; versions are updated by hand. The public portal in
Phase 5 may use its own minimal CSS to stay fast on phones.

---

## ADR-0017 — Audit trail via SaveChanges interceptor and an opt-in attribute
**Date:** 2026-09-17 · **Status:** Accepted

**Context.** SPEC section 7.1 item 6: record who changed what and when, field by field, and show
history per budget line.

**Decision.** `AuditInterceptor : SaveChangesInterceptor` writes `AuditEntry` rows for entities
marked `[Audited]`, in the same context and transaction as the change. User and time come from
`ICurrentUser` and `TimeProvider`. `AuditEntry` is append-only and tenant-owned. `AuditKind.Event`
lets services record named actions (workflow, publishing) explicitly.

**Alternatives.** SQL Server temporal tables (no "who", whole-row versions); database triggers
(outside the code, no user context); writing audit rows in each service (easy to forget).

**Consequences.** Every audited change costs extra insert rows (one per changed property). Audit
values are text for humans; they are not a replay log. The interceptor must be registered before
the tenant interceptor.

---

## ADR-0018 — Client-generated keys are declared `ValueGeneratedNever`
**Date:** 2026-09-17 · **Status:** Accepted

**Context.** Entities assign Guid v7 ids in their constructors. EF Core's default for Guid keys is
"generated on add", which made EF classify a new child discovered through an aggregate's
collection as Modified, producing a zero-row UPDATE and a concurrency exception.

**Decision.** `CivicBudgetDbContext.UseClientGeneratedKeys` marks `Id` on every `Entity` subtype
as `ValueGeneratedNever()`. No schema change.

**Consequences.** Aggregates can add children through their own methods and `SaveChanges` does the
right thing. Any entity must set its own id (the base class does).
