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

**Amended 2026-09-19 (how the modes are applied).** The first implementation
put `@rendermode InteractiveServer` on each admin page. That makes the page an
interactive island while `RouteView` renders the *layout* statically, so the
sidebar, top bar, and `ToastHost` in `AdminLayout` never had a circuit:
toasts never showed and the menu could not react. The app now applies the
mode once, in `App.razor`, to `Routes` and `HeadOutlet`, computed per request
from `HttpContext.AcceptsInteractiveRouting()`: null (plain static SSR, no
markers, no circuit) for pages marked `[ExcludeFromInteractiveRouting]`
(portal, account, error), Interactive Server for everything else, layout
included. Per-page `@rendermode` attributes are gone (a nested one is an
error). Nothing about the portal's static rendering changed; the static
pages' HTML carries no Blazor markers, which a test could assert.

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
| ClosedXML | Infrastructure | XLSX downloads and (Phase 6) reports without Office or COM | 0021 |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | Infrastructure | Data Protection key ring in SQL Server so cookies survive container restarts and scale-out | 0023 |
| Amazon.CDK.Lib, Constructs | Infra, Infra.Tests | AWS CDK in C#; `Amazon.CDK.Assertions` ships inside Amazon.CDK.Lib | 0008, 0023 |
| dotnet-ef (local tool, `.config/dotnet-tools.json`) | — | Migrations CLI pinned per repo | — |

All planned packages are now in the table.

Not packages: Bootstrap 5.3 CSS/JS is vendored under `src/CivicBudget.Web/wwwroot/lib/bootstrap` (ADR-0016); Bootstrap Icons 1.13 under `wwwroot/lib/bootstrap-icons` (ADR-0020).

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

---

## ADR-0019 — Snapshot status lifecycle: Active, Superseded, Unpublished
**Date:** 2026-09-17 · **Status:** Accepted

**Context.** SPEC section 6: unpublish is allowed and audited; republishing an amendment
replaces what citizens see and keeps history.

**Decision.** A snapshot is never deleted or edited except for its status. Exactly one
snapshot per fiscal year is Active. Publishing a year that already has an Active snapshot
marks the old one Superseded; the Finance Director can mark an Active one Unpublished. The
portal context filters to Active globally.

**Alternatives.** Deleting on unpublish (loses history); a boolean `IsActive` (cannot tell
"replaced" from "withdrawn" in the history view).

**Consequences.** Storage grows with each publish (95 rows per village-sized budget, trivial).
The history view can explain every past state.

---

## ADR-0020 — Theme as CSS variables over Bootstrap; Bootstrap Icons vendored
**Date:** 2026-09-18 · **Status:** Accepted

**Context.** Phase 4.5 gives the admin app a visual identity (docs/design/DESIGN-BRIEF.md).
Options: compile Bootstrap from SCSS with custom variables, adopt a component library, or
override Bootstrap's CSS variables.

**Decision.** Design tokens as CSS custom properties in `app.css`, mapped onto `--bs-*`
variables in the same `:root` block, plus component rules for the shell, grids, pills,
stepper, cards, dialogs, toasts, and states. Bootstrap Icons 1.13 (CSS + two font files) is
vendored under `wwwroot/lib/bootstrap-icons`; no CDN, no NuGet package.

**Alternatives.** SCSS build (adds Node/Sass to a .NET solution for little gain); a
commercial suite (rejected in ADR-0011); inline SVG icons (harder to keep consistent).

**Consequences.** One file to read to understand the look; updates to Bootstrap Icons are
manual; no dark theme yet (tokens make it a later addition).

## ADR-0021 — Portal output caching: base policy, header rewrite, evict by tag
**Date:** 2026-09-18 · **Status:** Accepted

**Context.** The portal is static SSR and anonymous, so its pages are ideal for ASP.NET
Core output caching: one render per URL per government until the next publish. Two things
stood in the way. Blazor's static SSR endpoint marks every response `Cache-Control:
no-cache, no-store` and issues an antiforgery cookie, and the built-in default output cache
policy honors both by refusing to store the response. `[OutputCache]` attributes are also
not applied to Razor component endpoints, so the policy could not be declared per page.

**Decision.** Three small pieces in `src/CivicBudget.Web/Caching/`:
1. `PortalOutputCachePolicy`, registered as the *base* policy with
   `excludeDefaultPolicy: true`. It enables caching only for `GET /transparency/**`, keys by
   the full URL (`QueryKeys = "*"`, because `?show=pct` and `?q=` change the page), tags the
   entry `portal:{slug}`, and refuses to store anything that is not a 200 or that still
   sets a cookie. Everything else (admin, sign-in, health) is untouched.
2. `PortalResponseMiddleware`, placed *before* `UseOutputCache` so it runs on hits and
   misses. In `OnStarting` it rewrites a 200 portal response to `Cache-Control: public,
   max-age=600`, drops `Pragma`, and removes the `Set-Cookie` header when the only cookie
   is the antiforgery token (portal pages have no POST forms; search is a GET form).
3. `OutputCacheSnapshotInvalidator` implements the Application hook
   `IPublishedSnapshotCacheInvalidator` with `IOutputCacheStore.EvictByTagAsync("portal:{slug}")`,
   so a publish or unpublish drops exactly that government's pages. Registered in Web after
   `AddInfrastructure`, replacing the no-op.

**Alternatives.** `ResponseCaching` middleware (honors `no-store`, no tag eviction);
caching inside `SnapshotQueryService` with `IMemoryCache` (saves the query but still renders
every request, and eviction logic would leak into Infrastructure); a reverse proxy or CDN
(right for production, but the app should be correct on its own and the CDN respects the
same `public, max-age` header this emits).

**Consequences.** A cache miss costs one query and one render; a hit costs nothing past the
middleware. The in-memory store is per instance; in AWS with more than one task the Redis
`IOutputCacheStore` package drops in without code changes. The ClosedXML package
(`ISpreadsheetExporter`) lands in this phase for the XLSX download and is reused by Phase 6
reports; CSV needs no package (`CsvWriter`).

## ADR-0022 — Import as preview-then-commit with a pure analyser; reports built from the workspace read
**Date:** 2026-09-18 · **Status:** Accepted

**Context.** Phase 6 adds CSV/XLSX import of budget lines and three reports. Import is the
riskiest write in the app (one file can touch every line), and reports must never disagree with
the entry screen.

**Decision.**
- **Import is two calls.** `PreviewAsync` parses the file and returns every row classified as
  Add, Update, Unchanged, or Error with the reason; `CommitAsync` takes the raw rows back,
  re-analyses them against the database *at that moment*, refuses if any row errs, and applies
  the rest through the `BudgetVersion` aggregate in one `SaveChanges` with a single audit event
  (the interceptor still records each field change). Nothing is written from a preview.
- **The rules live in a pure function.** `ImportAnalyzer.Analyze(rows, funds, departments,
  accounts, existingLines)` mirrors `BudgetVersion.AddLine`'s guards plus the file-level ones
  (parseable money, no duplicate keys, blank optional columns mean "leave as is"). Every rule
  has a unit test with no database; the service test proves the same rules over SQL Server.
- **The file contract is the export.** Columns are codes (Fund, Department, Account, Amount,
  Prior Year Actual, Current Year Budget, Justification), the exact layout the workspace's
  "Export lines" writes, so export, edit in Excel, import is the round trip. Import never
  deletes; a line absent from the file is left alone.
- **Reports are shaped from `BudgetWorkspaceDto`.** `ReportBuilder` is pure over the same DTO
  the workspace renders, so the Department Head visibility rule and the fund arithmetic are
  applied once. `ReportTables` turns each report into an `ExportTable` for XLSX, and the
  minimal API endpoints under `/admin/export` reuse `ISpreadsheetExporter` from Phase 5.
- **Print is CSS.** `@media print` hides the shell and leads with the report block; the Print
  button is the admin app's one JavaScript call (`window.print`).

**Alternatives.** Import committing directly with a summary (no chance to see a mistake before
it lands); a staging table for imports (more moving parts than a village needs; the preview is
held in the circuit and re-validated on commit); SQL views or a reporting database for reports
(premature; the workspace read is already one query per version); a PDF library for print
(browser print with a stylesheet is enough and needs no package).

**Consequences.** `ISpreadsheetReader` (ClosedXML) joins `ISpreadsheetExporter` in
Infrastructure; `CsvReader` sits beside `CsvWriter` in Application. A file larger than 5 MB or
10,000 rows is refused up front. Reports cost the workspace read plus one lookup; a county-scale
tenant would cache or push grouping into SQL behind the same `IReportService`.

## ADR-0023 — Fargate behind an ALB via the CDK L2 pattern; one stack; secrets by reference
**Date:** 2026-09-18 · **Status:** Accepted

**Context.** ADR-0008 committed to deploy-ready AWS infrastructure without an account. Phase 0
left the compute choice open between ECS Express Mode and Elastic Beanstalk, to be checked
against current docs. Checked 2026-09-18: Express Mode (GA November 2025) has only an L1
construct (`CfnExpressGatewayService`), runs a single container in the default VPC's public
subnets, and has no custom-domain support; Elastic Beanstalk is a platform abstraction that
hides the network and database wiring an interviewer wants to see.

**Decision.**
- **Compute:** `ApplicationLoadBalancedFargateService` (the ECS Patterns L2) in
  `infra/CivicBudget.Infra/CivicBudgetStack.cs`: a public ALB, a Fargate task (0.5 vCPU / 1 GB)
  in private subnets, a target group with `/health` checks and sticky sessions (Blazor Server
  circuits), a deployment circuit breaker with rollback. Express Mode is the right answer for a
  public API with no database; not for this.
- **Database:** RDS SQL Server Express (`db.t3.micro`, 20 GB gp3, encrypted, 7-day backups) in
  private subnets, reachable only from the service's security group. Same engine as local
  development. The master password is generated and held by Secrets Manager.
- **Secrets by reference, not by value.** The task definition names Secrets Manager entries
  (`Database__Password` from the RDS-managed secret, `Seed__DemoPassword` from a generated one);
  ECS injects them at start. The app composes its connection string from `Database:*` settings
  plus the password (`DatabaseOptions`), so no derived connection-string secret exists to drift
  when RDS rotates the password. The template never contains a password; a test proves it.
- **One stack for the demo.** VPC, ECR, RDS, ECS, ALB, logs, alarm, and outputs in one
  `cdk deploy`, with `RemovalPolicy.DESTROY` everywhere so `cdk destroy` leaves nothing billing.
  A production account would split network + database from the service and set deletion
  protection and snapshot-on-delete on RDS; the comments say so where it applies.
- **OIDC, not keys.** A second, one-time stack (`GitHubOidcStack`) creates the GitHub OIDC
  provider and a deploy role trusting only `repo:SpencerSmithSite/civic-budget` on `v*` tags or
  the `production` environment. The role can push to one ECR repository and assume the CDK
  bootstrap roles; CloudFormation permissions live in those, so the GitHub role is narrow.
  `deploy.yml` runs only when the repository variable `AWS_DEPLOY_ROLE_ARN` exists.
- **Data Protection keys in SQL Server.** The container's default key store is its filesystem,
  which is gone on every restart (every user signed out, every antiforgery token invalid).
  `PersistKeysToDbContext<CivicBudgetDbContext>` keeps the key ring in a `DataProtectionKeys`
  table: restarts and a second task share it. Migration `AddDataProtectionKeys`.
- **Migrate and seed on startup, opt-in.** `Database:MigrateOnStartup` and
  `Database:SeedDemoData` are true for the containerized demo and the AWS deploy (one task, EF's
  migration lock). A real pipeline would run migrations as a step and never seed.
- **Scaling is deliberately one task.** Sticky sessions and shared keys are in place; the one
  missing piece for two tasks is a shared output cache store for the portal
  (`Microsoft.AspNetCore.OutputCaching.StackExchangeRedis`), because an in-memory eviction on
  task A is invisible to task B. Written in the stack where the autoscaling would go.

**Cost (us-east-2, list prices, September 2026, approximate).** RDS SQL Server Express
`db.t3.micro` ≈ $17/mo + 20 GB gp3 ≈ $2.50; Fargate 0.5 vCPU / 1 GB ≈ $18/mo; ALB ≈ $16/mo +
LCU; NAT gateway ≈ $33/mo + data; Secrets Manager 2 × $0.40; CloudWatch and ECR under $2.
About **$90/mo** running, of which the NAT gateway is a third; the budget alarm defaults to $60 at
80% so it fires early. Teardown: `cdk destroy CivicBudget-App` (the OIDC stack costs nothing).
Cheaper variants, in order of what they give up: drop the NAT gateway by putting the task in a
public subnet with a public IP (saves $33, exposes the task's ENI behind its security group);
stop the RDS instance outside demo hours (RDS restarts it after seven days).

**Alternatives.** ECS Express Mode (above); Elastic Beanstalk (above); App Runner (no VPC-private
database without a VPC connector, no WebSockets at the time of checking); a single EC2 instance
with docker compose (cheapest, but nothing about it transfers to the employer's ECS estate).

**Consequences.** `dotnet test` now needs Node.js for the JSII runtime (the Infra.Tests assembly
runs sequentially because JSII is one process per test host); CI gained a `cdk-synth` job and a
Docker build. The Dockerfile must copy `.editorconfig` for the migration analyzer exemptions.
Nothing here has been deployed; it has been synthesized and asserted on every commit.

## ADR-0024 — Full account numbers are composed from the three stored codes under a per-government format
**Date:** 2026-09-19 · **Status:** Accepted

**Context.** v1.1 reframes CivicBudget as a plug-in beside the government's ERP. Staff think in
full account numbers (`1000-725-121`, `101-110-5100`), and the ERP's chart decides how those are
written (see `docs/research/ohio-account-numbers.md`). The model already stores fund, department,
and object codes separately, and every rule (a department per expenditure line, revenue at fund
level) hangs off those separate ids.

**Decision.** Keep the three codes as the source of truth and compose the full number:
- `AccountNumberFormat` is a value object owned by `Government` (five columns on its row):
  segment widths, separator, and the middle segment's name. Editable in Government settings;
  Phase 9b will populate it from the ERP chart.
- `AccountNumber.Compose` and `TryParse` are pure functions in Domain. Composition pads numeric
  codes to the width; parsing accepts any common separator or none and refuses text that is not
  a number (fund must be numeric), so a search box can try the number first and fall back to names.
- Every line DTO carries `AccountNumber`; the workspace DTO carries the format so screens use the
  government's word ("Program" or "Department"). Published snapshot lines store the composed
  number at publish time, backfilled by the migration for existing snapshots.
- The import accepts an `Account Number` column as an alternative to the three code columns and
  the export writes both, so the round trip works either way.
- Seed department codes became UAN program numbers (110 Police, 620 Streets, 725 Finance) so the
  demo reads like a real chart; Pine Hollow uses a dotted "Department" format to show the setting.

**Alternatives.** Storing the full number on `BudgetLine` (duplicates three codes and drifts when
a code is renamed); a single `Account` entity keyed by the full number (loses the fund and
department as first-class things the rules and permissions depend on); a fixed 4-3-4 layout
(would not fit a county ERP's chart, which is the point of the plug-in).

**Consequences.** Numbers are computed, so a chart rename is reflected everywhere except in
snapshots, which is intended. A fourth (cost-center) segment is not modelled; the value object is
the place to add it.

## ADR-0025 — The chart of accounts is received from the ERP through an adapter, never deleted, and owned by a switch
**Date:** 2026-09-19 · **Status:** Accepted

**Context.** v1.1 positions CivicBudget beside the government's ERP (VIP or similar), which owns
the chart of accounts. Funds, departments, and objects must come from there, but the app must
still work for a government with no feed, and the ERP's real interface is unknown (an export
today, perhaps an API later).

**Decision.**
- **One contract.** `ErpChart` (Application/Erp) is everything CivicBudget wants from an ERP:
  three code lists with the fields the domain rules need, plus optionally the account number
  format. `IErpChartSource` is the adapter interface; `IErpChartFileSource` is the file flavour
  and `ErpChartFileSource` reads a one-row-per-code CSV/XLSX (`Kind, Code, Name, Type, Category,
  Description, Active`), forgiving about spelling and order. Its column names are the seam to
  change when the ERP's real layout is known; nothing above it would move.
- **Diff, then apply through the entities.** `ChartDiff.Compute` is pure: Add, Update,
  Deactivate, Reactivate, Unchanged per code, with before and after text for a person to judge.
  `ChartSyncService.CommitAsync` re-reads the file, re-diffs, and applies each change through
  `Fund.Update`, `Department.Deactivate`, and so on, so the domain rules run and the audit
  interceptor records every field. One `ChartSync` log row and one audit event per sync.
- **Never delete.** A code the ERP no longer lists is deactivated: budget lines and snapshots
  still point at it and history keeps its name. The preview warns when a file would deactivate
  more than a quarter of the chart, the signature of a partial export.
- **Ownership is explicit.** `Government.ChartSource` is `Local` until the first sync, then
  `Erp`. Under `Erp` the setup services refuse writes (`ChartOwnership.RefuseIfErpManagedAsync`)
  and the setup screens show a banner with the last sync instead of New/Edit. An Administrator
  can switch back to `Local` for a government that maintains its own chart.

**Alternatives.** Calling the ERP directly from the setup services (couples the app to an
interface that does not exist yet); deleting codes the ERP dropped (breaks history); making the
setup screens read-only unconditionally (a government without an ERP could not start).

**Consequences.** A sync is a whole-chart operation; there is no partial sync by design. The
sync log stores the change list as JSON for the drill-down. When an API adapter arrives, it
implements `IErpChartSource` and the sync page gains a "Sync from VIP" button beside the upload.

