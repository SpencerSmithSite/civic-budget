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
| Microsoft.EntityFrameworkCore.Design | Web (dev) | `dotnet ef` tooling | — |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | Infrastructure | Identity stores | — |
| Microsoft.AspNetCore.Components.QuickGrid | Web | Admin grids | 0011 |
| FluentValidation | Application | Input validation | 0007 |
| ClosedXML | Infrastructure | XLSX read/write without Office | spec |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | tests | Test framework | spec |
| bunit | Web.Tests | Component tests | spec |
| Testcontainers.MsSql | IntegrationTests | Real SQL Server in tests | spec |
| Amazon.CDK.Lib, Amazon.CDK.Assertions | Infra | Infrastructure as code + tests | 0008 |

(Rows are added as each phase introduces the package; this table is the
plan, and the phase that adds a package confirms the row.)
