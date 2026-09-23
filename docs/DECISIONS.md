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

## ADR-0027 — Department requests live on the budget version; submitting locks the department, not the version
**Date:** 2026-09-19 · **Status:** Accepted

**Context.** v1.1's last step: a fire chief signs in, lands in the fire department, enters the
request against its accounts, writes a narrative, and hands it to the fiscal officer, who
assembles the whole budget and can send a department's request back. The existing workflow
(Draft → Proposed → Adopted) is the *version's* state; the department round happens inside Draft.

**Decision.**
- **A `DepartmentRequest` child of `BudgetVersion`**, one per department that has written a
  narrative or submitted: `InProgress`, `Submitted` (who and when), `Returned` (the officer's
  note and when). Departments with no row are simply in progress. The aggregate owns the rules:
  submit only while Draft and only with lines; return only what was submitted, with a note;
  submitting again clears the note. Amendments copy narratives (they still describe the year)
  but start a new round.
- **Submitting locks the department for department users, not the version.** The one rule in
  `BudgetLinePermissions.CanEdit` gains a `departmentSubmitted` argument; the fiscal authority
  is unaffected. `CanSubmitDepartment` / `CanReturnDepartment` sit beside it, so the
  authorization handler, the services, and the DTO flags (`CanEditNarrative`, `CanSubmit`,
  `CanReturn`) all come from one place.
- **The workspace DTO carries the round.** `BudgetWorkspaceDto.DepartmentRequests` lists every
  department the user can see with status, totals, and narrative, so the department page, the
  board, the workspace strip, and the Department Detail report read one shape and the reports
  builder stays pure.
- **The narrative is published.** `PublishedBudgetSnapshotDepartment` freezes each department's
  narrative with the snapshot, mapped by both contexts like the fund rows; the portal's
  department page shows it as "From the department".
- **A department user's home is their department.** Sign-in lands in the app; `/admin` forwards
  department users to `/admin/my-department`, which resolves the open version and sends them to
  their one department or to the board when they hold several.

**Alternatives.** A per-department status column on `BudgetLine` (one status copied across a
dozen lines, and nowhere to keep the narrative); a separate `DepartmentBudget` aggregate holding
the department's lines (would split the appropriation check, which needs every line of the
fund); blocking Propose until every department submits (the officer decides when the round is
over; a department that never submits should not hold council up); publishing narratives per
line (4,000 characters times every line).

**Consequences.** Two tables, one migration, no change to the version workflow or the
appropriation check. `BudgetLineResource` (the authorization handler's input) gains a flag with
a default, so callers that do not know about submissions keep working. Tests: domain rules
(`DepartmentRequestTests`), the permission matrix (`BudgetLinePermissionsTests`), the services
end to end with the seeded mid-round FY2027 (`DepartmentRequestServiceTests`), the published
narrative (`SnapshotQueryServiceTests`), and the pages (`DepartmentPagesTests`).

## ADR-0028 — Profile pictures are resized by the browser, stored in SQL Server, and served through a versioned URL
**Date:** 2026-09-20 · **Status:** Accepted

**Context.** Users want a picture instead of initials in the account circle. The app has no blob
storage (ADR-0008: deploy-ready, no account), and adding an image library to resize on the
server means a native dependency and a licensing decision (ImageSharp's split license,
SkiaSharp's size) for a feature that handles one small image per user.

**Decision.**
- **The browser resizes.** Blazor's `IBrowserFile.RequestImageFileAsync("image/png", 256, 256)`
  draws the chosen file onto a canvas and hands back a PNG no larger than 256 px. The server
  checks only content type and size (`UserAvatar.MaxBytes`, 512 KB); it never decodes an image.
- **Bytes live in a `UserAvatars` table**, one row per user, separate from `AspNetUsers` so a
  user list never reads image data. `ApplicationUser.AvatarUpdatedAtUtc` says whether a
  picture exists and doubles as the version.
- **A versioned URL with a long private cache.** `/Account/Avatar/{userId}?v={ticks}` returns
  the bytes with `Cache-Control: private, max-age=31536000, immutable`; a new upload changes the
  URL, so nothing is ever stale and nothing is re-fetched. The endpoint requires authentication
  and the service joins to `Users` on the current government, the same scoping rule as every
  Identity read (ADR-0015).
- **One `Avatar` component** decides picture-or-initials. Lists pass the version from their
  DTO; the top bar and timelines ask `IUserAvatarService.GetVersionAsync`, which caches per
  scope (one circuit) and raises `Changed` after an upload so the top bar updates in place.
- **Users upload their own; administrators only remove.** Removing is the moderation need; an
  administrator uploading someone else's face is not.

**Alternatives.** S3 or blob storage (the right answer at scale, and a one-class change behind
`IUserAvatarService` when there is an account); server-side resizing (a package and a native
dependency for one feature); a data URL in a claim (bloats every request's cookie); Gravatar
(sends users' emails to a third party from a government system).

**Consequences.** One migration, no package. A 256 px PNG is 30 to 200 KB; a thousand users
is under 200 MB in the database, acceptable for a village or county and easy to move later.
The same phase adds `[NotAudited]` for properties whose change is already a named audit event,
because the department round (ADR-0027) had started writing ids and timestamps into the
activity feed.

## ADR-0029 — The portal's read-only context also maps the government logo; the overview's panels slide with CSS alone
**Date:** 2026-09-20 · **Status:** Accepted

**Context.** The portal header showed the government's initials in a circle. Spencer wants the
CivicBudget mark by default and a logo the government's administrator uploads. ADR-0006 says
the portal context maps only snapshot tables so nothing live can leak; a logo is live data.
Separately, the overview's two breakdowns should be one sliding section, and the portal has no
JavaScript (ADR-0021's cacheability and the accessibility statement both depend on that).

**Decision.**
- **`GovernmentLogos` is the one non-snapshot table the portal context maps.** It holds a
  government id, a content type, bytes, and a timestamp: a public image and nothing else, so
  mapping it read-only cannot expose a draft, a user, or a setting. The portal looks it up
  through an active snapshot's `GovernmentId`, so a government with nothing published has no
  public face, and a renamed slug cannot orphan it. Uploads go through
  `IGovernmentLogoService` (Administrator only, browser-resized to 512 px, type and size
  checked) and evict the government's portal pages, whose header carries the logo.
- **The header falls back to the mark**, not to initials: a product default that looks
  designed, and no more guessing which words of "Village of Maple Ridge" to abbreviate.
- **Panels slide with `:checked`.** `PortalPanels` renders two radio inputs styled as tabs and a
  track two panels wide; the checked radio moves the track and hides the other panel
  (`visibility`, so its links leave the tab order; `max-height: 0` after the slide, so the page
  is only as tall as the panel on screen). Both panels are in the HTML, so search, reader mode,
  and screen readers see everything, and the $/% toggle, which reloads the page, keeps the panel
  through `?view=revenue`.

**Alternatives.** Copying the logo into each snapshot (a logo change would need a republish);
serving it through the admin context (breaks the one-door rule for no gain); a JavaScript
carousel (the portal's no-script promise); `<details>` or `:target` for the tabs (no slide, and
`:target` scrolls the page).

**Consequences.** One table, one migration, one line in the portal-context test. The trust
line (resolution, published, version) closes the overview instead of opening it; the glossary
and accessibility statement are a page (`/transparency/{slug}/{year}/glossary`) instead of a
footer under every page.

## ADR-0030 — The live demo runs on Azure's free tiers; the database is rebuilt from the seed every night
**Date:** 2026-09-20 · **Status:** Accepted

**Context.** Spencer wants the app hosted for free so hiring managers can sign in and use it.
The app needs a persistent process (Blazor Server) and SQL Server; the SQL Server container
wants 2 GB of memory, which no free VM tier offers, and the AWS free tier no longer covers RDS
or the NAT gateway the CDK stack uses (ADR-0008, ADR-0023).

**Decision.**
- **Azure, two always-free offers.** Azure SQL Database's free offer (serverless General
  Purpose under `useFreeLimit`: 100,000 vCore-seconds and 32 GB a month, pausing rather than
  billing when exhausted) is a real SQL Server, so the EF Core provider and every migration run
  unchanged. Azure Container Apps on the consumption plan runs the existing Docker image, speaks
  WebSockets, and scales to zero under a monthly free grant. `infra/azure/main.bicep` declares
  both plus a Log Analytics workspace with a daily cap; `scripts/azure-setup.sh` creates them
  once; `deploy-azure.yml` rolls the image on every push to `main` over OIDC, mirroring the AWS
  workflow. The AWS stack stays as the production-shaped story.
- **Nightly reset, not moderation.** Five shared logins are published in the README. Anything a
  visitor does (change a password, adopt the draft, upload a picture) is undone at 08:00 UTC by
  a Container Apps job that runs the same image with `--reseed`:
  `DatabaseInitializer.ResetAsync` drops every foreign key and table, migrates from nothing, and
  seeds. The database object is kept because on Azure it is the free-offer resource; dropping
  and recreating it would create a billable one.
- **Not switching to Postgres** to fit a free host: the data layer is SQL Server on purpose (the
  employer's stack) and every migration and Testcontainers test would need redoing for a
  hosting convenience.

**Alternatives.** A tunnel from Spencer's Mac (free, but only while the Mac is awake, and SQL
Server under Rosetta has crashed three times this week); Render or Fly with Postgres (the
rewrite above); AWS on the new credit-based free tier (six months, and the NAT gateway alone is
about $32 a month); a "demo mode" that blocks destructive actions (more code, worse demo).

**Consequences.** The first request after an idle hour takes 30 to 60 seconds while the
database resumes and the container starts; the startup probe allows a few minutes. One replica
at most, which the in-process output cache already required (ADR-0021). Every session ends at
the nightly reset. CI compiles and lints the Bicep so a template error cannot wait for a deploy.

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

## ADR-0026 — Administrator is a superset; temporary passwords are enforced by a claim; department assignment bounds every read
**Date:** 2026-09-19 · **Status:** Accepted

**Context.** v1.1's users story: an administrator with complete access, users who land in their
own department and see nothing else, and logons the administrator can create and reset.

**Decision.**
- **One helper answers "may this user act as the fiscal officer".** `ICurrentUser.IsFiscalAuthority()`
  (Administrator or Fiscal Officer) replaces every `IsInRole(FinanceDirector)` in the services, and
  the four fiscal policies (`CanEditBeginningBalances`, `CanAdvanceWorkflow`, `CanPublish`,
  `CanImport`) include Administrator. `IsDepartmentUser()` is its counterpart. Role *values* in the
  database are unchanged; display names are the customer's words (Administrator, Fiscal Officer,
  Department User, Viewer).
- **Temporary passwords.** `ApplicationUser.MustChangePassword` is set when an administrator
  creates an account or resets its password. The claims factory turns it into a claim;
  `MustChangePasswordMiddleware` redirects any authenticated request outside the account pages
  and static assets to the change-password page; the page clears the flag and refreshes the
  sign-in so the cookie loses the claim. A claim rather than a database check per request keeps
  the middleware free of I/O; the security-stamp change on reset ends any open session.
- **Department assignment bounds every read.** The workspace, reports, exports, and search
  already filtered by the user's departments; the audit trail now does too
  (`AuditQueryService` limits a department user to their own lines' history and activity).
- **User administration is audited** as named events on the government's trail, because Identity
  entities are not `[Audited]`: created, updated (role and departments), password reset, locked,
  unlocked, with the acting administrator's name.

**Alternatives.** A separate "SuperAdmin" role (nothing in the customer's world needs it);
checking `MustChangePassword` in the database on every request (a query per request for a
rare state); enforcing the change in the Login page only (a bookmarked URL would bypass it).

**Consequences.** Five migrations of user data are not needed: one new column. Interactive
navigation inside a circuit does not pass through middleware, but a flagged user never reaches
the circuit: their first request after sign-in is redirected. Tests: policy matrix (Web),
middleware (Web), permissions and audit scoping (integration), user admin flag and audit
(integration).


---

## ADR-0031 — The host listens before the database is ready and shows a waiting screen
**Date:** 2026-09-21 · **Status:** Accepted

**Context.** On the free Azure tier (ADR-0030) a visitor's first request after an idle hour
took about 65 seconds to produce any bytes: 15 seconds for the platform to schedule and start
the container, then 48 seconds while serverless SQL resumed from auto-pause. `Program.cs`
awaited migrations and seeding before `RunAsync`, so Kestrel was not listening and the startup
probe (on `/health`) could not pass; Container Apps held the request the whole time and the
browser showed a blank page. A blank minute reads as "the site is down". Keeping the database
awake or a replica warm would exhaust the free allowances within days.

**Decision.**
- **Migrate and seed in a hosted service** (`DatabaseStartupService`), not inline. The host
  starts listening within seconds of the process; the same options decide what runs
  (Development always, `Database:MigrateOnStartup` and `Database:SeedDemoData` elsewhere). A
  failure logs critical and stops the host so the platform restarts the container, which is
  what an inline throw did before.
- **`StartupState`** is a singleton the service flips to ready. `WakingUpMiddleware`, placed
  before the status-code pages, answers every page request with a self-contained waiting screen
  until then: `503 Service Unavailable` with `Retry-After` and `Cache-Control: no-store`, the
  public header and a card in the app's own tokens, the mark inline, a live counter that
  continues from the process's clock across reloads, and a poll of `/health/startup` every two
  seconds that reloads the original URL once it returns 200. A `noscript` meta refresh covers
  browsers without script. Health endpoints and static assets pass through.
- **Three health endpoints.** `/health` is liveness (the process is up) and is what the
  platform's startup probe hits, now with a two-second delay and period instead of ten.
  `/health/startup` is the in-memory startup check, cheap enough to poll. `/health/ready` is
  startup plus a real database round trip, for anything that must know the database answers.
- **Data Protection reads its key ring lazily** (`DataProtectionStartup.DeferKeyRingLoad`). The
  first attempt at this ADR shipped and changed nothing: the site still showed a blank browser for
  about seventy seconds. The Azure logs put "Now listening" seventeen milliseconds after the
  migration check, fifty-two seconds in, which is the wrong order for a host that is supposed to
  listen first. The cause was upstream of anything this ADR had touched: `AddDataProtection`
  registers an internal hosted service that reads the key ring during startup, our keys live in
  SQL Server, and EF's retry strategy spent the better part of a minute on that read before the
  web host service ever got to start Kestrel. Removing that registration leaves the provider's own
  lazy load, which happens on the first request that protects or unprotects data, by which time
  the database is up; the waiting screen itself uses no cookies or antiforgery tokens.

- **A quiet spell re-checks the database** (amended 2026-09-23). `StartupState` remembers when
  it last let a page through. After 55 minutes without one (serverless SQL pauses at 60), the
  next page request starts a single shared check (`DatabaseWaker`) and waits up to a second
  for it: an awake database answers in milliseconds and the page is served; a sleeping one gets
  the waiting screen, whose counter restarts, until it answers. Scale-to-zero usually retires
  the container long before the database pauses, but an open admin tab holds a WebSocket that
  can keep it up past the hour, and without the check the next visitor waited on a hung request.
- **The container is not kept warm** (Spencer, 2026-09-23). After a few idle minutes the first
  visit still waits about 17 seconds before anything shows. Azure's logs split that into about
  15 seconds provisioning a sandbox, 1 second pulling the image, and 0.3 seconds of our own
  startup, so nothing in the app or image can shorten it. `minReplicas: 1` would make every
  visit instant for roughly $4 to $5 a month (an idle replica bills at a reduced rate after the
  free grant); a weekday business-hours scale rule would fit inside the free grant. Both were
  offered; the free, always-scale-to-zero demo was kept, and either is a one-line change to
  `main.bicep` if that changes.

**Alternatives.** A minimum of one replica (a few dollars a month, and the database would still
pause); disabling SQL auto-pause (burns the free vCore-seconds in about four days); a keep-alive
ping (same); a static "loading" page on a CDN in front (another moving part, and it could not
know when to stop). Serving the waiting screen with 200 (monitors and crawlers would cache it as
the site).

**Consequences.** First paint after a cold start is about 20 seconds (platform time only), and
the site appears on its own at about 65 seconds; nothing about the warm path changes. Against a
database address that hangs, time to the first page went from 31 seconds to 1. The key-ring
removal matches an internal framework type by name, so `DataProtectionStartupTests` asserts the
removal happened and fails loudly if a future .NET renames it rather than letting the delay back
in silently. Local
development gets the same screen for the 15 to 30 seconds SQL Server takes under Rosetta.
Blazor circuits cannot start early (`/_blazor` is not exempt), so no page renders against a
database that is not there. Tests: `WakingUpMiddlewareTests` (Web).
