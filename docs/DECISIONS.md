# Architecture Decision Records

An ADR records one decision that had real alternatives: the situation, what I chose,
what I turned down and why, and what the choice costs. They are numbered in the order I
made them. When a later phase changed an earlier decision, the change is noted on the
original ADR rather than rewriting history, so you can see how the thinking moved.

Every NuGet package in the solution is listed with its reason in [Packages](#packages)
at the end.

Format: **Context**, **Decision**, **Alternatives**, **Consequences**.

---

## ADR-0001: Four projects, plain services, no mediator or CQRS framework
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** I have to be able to explain every part of this project, and the rules
have to be testable without a browser. The most common way Blazor apps rot is EF
queries and business rules written straight into components.

**Decision.** Four projects, `Domain` → `Application` → `Infrastructure` and `Web`, with
plain application services (classes with async methods) that components call.

**Alternatives.** (a) One web project: fastest to start, but the budget rules end up in
`.razor` files. (b) MediatR and a handler per request: well known, but it adds
indirection and a package for no gain at this size, and MediatR moved to a commercial
license in 2025.

**Consequences.** A few more files, and a clear answer to "where does this go?".
Services are easy to test.

---

## ADR-0002: A Blazor Web App with a render mode per area
**Date:** 2026-09-15 · **Status:** Accepted, amended 2026-09-19

**Context.** Two audiences with opposite needs: a few signed-in editors and many
anonymous readers.

**Decision.** The admin app is Interactive Server. The public portal is static
server-side rendering with no interactivity.

**Alternatives.** (a) Everything Interactive Server: simplest, but every citizen would
open a SignalR circuit and hold server memory, and nothing could be cached. (b)
WebAssembly for the portal: a large download for read-only tables. (c) A separate MVC
project for the portal: duplicated layout and hosting.

**Consequences.** Portal pages cannot use `@onclick`; charts need a form that works
without script, which accessibility wants anyway. Output caching becomes possible.

**Amended 2026-09-19: how the modes are applied.** I first put `@rendermode
InteractiveServer` on each admin page. That makes each page an interactive island
while the *layout* renders statically, so the sidebar, top bar, and toast host never
had a circuit: toasts never appeared and the menu could not react. The mode is now set
once in `App.razor`, on `Routes` and `HeadOutlet`, from
`HttpContext.AcceptsInteractiveRouting()`: nothing (plain static rendering, no circuit)
for pages marked `[ExcludeFromInteractiveRouting]` (portal, account, error pages), and
Interactive Server for everything else, layout included. No page carries its own
`@rendermode`.

---

## ADR-0003: IDbContextFactory instead of a scoped DbContext in Blazor Server
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** In Blazor Server the dependency-injection scope is the circuit, not the
request. A scoped `DbContext` would live as long as the browser tab and be shared by
overlapping event handlers, and `DbContext` is not thread-safe.

**Decision.** Register `AddDbContextFactory<CivicBudgetDbContext>()`. Every unit of work
creates and disposes its own context.

**Alternatives.** (a) A scoped context with `OwningComponentBase`: it works, but it is
still one context per component lifetime and easy to misuse. (b) Creating scopes by
hand with `IServiceScopeFactory`: the same effect with more code.

**Consequences.** Nothing is tracked across operations; each service method loads what
it needs. Simpler to reason about, and no stale-entity bugs.

---

## ADR-0004: Tenancy through ITenantContext and EF Core global query filters
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Several governments share one database. Showing one government's draft
budget to another is the worst bug this app could have.

**Decision.** One database and one schema, `GovernmentId` on every tenant-owned row,
global query filters bound to `ITenantContext`, and a save interceptor that verifies
`GovernmentId` on every write (ADR-0013).

**Alternatives.** (a) A database per government: the strongest isolation, but N
migrations and connection routing are heavy for this project. (b) A schema per
government: SQL Server supports it, but EF Core's tooling makes it awkward. (c) SQL
Server row-level security: good as a second lock later, not a substitute for filtering
in the app.

**Consequences.** Every query is filtered without anyone remembering to filter it.
Application code never calls `IgnoreQueryFilters()`; tests use it to check what the
filters hide. The portal finds the government by its URL slug.

---

## ADR-0005: The public portal reads immutable published snapshots only
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Citizens must never see draft or proposed figures, and what was published on
a given day must be reproducible later.

**Decision.** Publishing writes a denormalized `PublishedBudgetSnapshot` with its lines,
funds, and departments. The portal reads only those tables. Unpublishing marks a
snapshot; nothing is deleted.

**Alternatives.** (a) The portal queries the adopted version with a status filter: one
forgotten `.Where` exposes drafts, and renaming an account later rewrites history. (b)
Generating a static site on publish: attractive, but search and downloads become extra
work; it could still be added in front as a cache.

**Consequences.** Some intended duplication. Publishing is an explicit, audited event,
and cache invalidation is simple.

---

## ADR-0006: A separate, read-only PublicPortalDbContext
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** ADR-0005 says the portal reads only snapshots. I wanted that enforced by
structure, not by convention.

**Decision.** A second `DbContext` that maps only the snapshot entities, does not track,
refuses to save, and has no migrations of its own. The admin context owns the schema.

**Alternatives.** One context and discipline: cheaper, but discipline is not a security
control.

**Consequences.** The portal cannot query the live tables at all. In production its
database login could be `SELECT`-only on the snapshot tables. The two contexts must stay
in step when the snapshot tables change, which they do through one shared
`PublishedSnapshotModel.Configure`.

---

## ADR-0007: FluentValidation over DataAnnotations
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Validation belongs in the Application layer and has to handle rules that
involve several fields (a department is required only for expenditure accounts).

**Decision.** FluentValidation validators, one per request, run by the application
service.

**Alternatives.** DataAnnotations: no package, and Blazor's `EditForm` understands them,
but attributes handle cross-field rules poorly and put rule logic in metadata.

**Consequences.** One package. Rules are ordinary classes with unit tests. Pages show the
errors a service returns rather than relying on `DataAnnotationsValidator`.

---

## ADR-0008: Deploy-ready AWS infrastructure without an AWS account
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** I have no AWS account or budget for this project, but much of government software
runs on AWS, so the deployment story matters.

**Decision.** Build everything up to `cdk deploy`: a Dockerfile, a full-stack Docker
Compose file, a CDK stack in C#, `cdk synth` in CI, CDK assertion tests for the
security properties, and an OIDC-based `deploy.yml` that stays idle until a role is
configured. Document cost, teardown, and a budget alarm as they would apply.

**Alternatives.** (a) A free PaaS (Fly.io, Railway): a live URL, but it says nothing
about AWS. (b) LocalStack: RDS and ECS are not in its free tier, so the fidelity is weak.
(c) A new AWS account on the credit-based free tier: possible later; nothing here blocks it.

**Consequences.** Strong, verifiable infrastructure code without a live AWS URL. With an
account, `cdk bootstrap` and `cdk deploy` are the only new steps. (The live demo later
went to Azure's free tiers instead; see ADR-0030.)

---

## ADR-0009: SQL Server 2022 in Docker (amd64 under Rosetta on Apple Silicon)
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** The target stack is SQL Server. My development Mac is arm64, and Microsoft
ships no arm64 SQL Server image.

**Decision.** `mcr.microsoft.com/mssql/server:2022-latest` with `platform: linux/amd64`
in Docker Compose and in Testcontainers, relying on Docker Desktop's Rosetta emulation.

**Alternatives.** PostgreSQL: native on arm64 and cheaper on RDS, but the governments and
ERP vendors this is for run SQL Server. Azure SQL Edge: retired.

**Consequences.** Slower container starts locally (15 to 40 seconds) and the occasional
emulation crash, which Compose now restarts automatically. CI on Ubuntu runs natively.
The EF provider is confined to Infrastructure, so a swap stays possible.

---

## ADR-0010: The repository is public and unlicensed (all rights reserved)
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** I want anyone evaluating CivicBudget to be able to read the code without my granting
anyone the right to reuse it.

**Decision.** No `LICENSE` file; the README says "All rights reserved", public for review
only. Changes reach `main` through pull requests.

**Alternatives.** A private repository (invisible unless each reviewer is invited); a
restrictive license such as CC BY-NC-ND (still grants some rights).

**Consequences.** Anyone can read it, and GitHub cannot stop a fork of a public
repository, but no right to use it is granted.

---

## ADR-0011: QuickGrid for admin grids, no commercial component suite
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Government ERP vendors often use DevExpress or Telerik. I had no license or
trial, and a product sold beside an ERP should not make its buyer license another vendor's suite.

**Decision.** `Microsoft.AspNetCore.Components.QuickGrid` for the grids, with inline
editing built from standard components.

**Consequences.** Less built-in polish than a commercial grid, which the design pass
(ADR-0020) made up for; no licensing risk; every behavior is code I can explain. I
replaced QuickGrid's own `Paginator` with a small `ListPager` so the phone card view
pages together with the grid.

---

## ADR-0012: Central Package Management and analyzers as errors
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** Eight projects at the time (ten now). Package versions drift and analyzer warnings get ignored.

**Decision.** `Directory.Packages.props` holds every package version once.
`Directory.Build.props` sets `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`,
and `EnforceCodeStyleInBuild`; `.editorconfig` switches off the few rules that add ceremony
without value here, with a comment on each. Generated EF migrations are exempt. CI runs
`dotnet format --verify-no-changes`.

**Consequences.** Adding a package takes two edits (the props file and the project),
friction I want, because it pairs with the package table below.

---

## ADR-0013: The tenant interceptor verifies rather than stamps
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** The write side of tenancy can either stamp `GovernmentId` on new rows from
the current tenant, or require entities to carry it and check it on save.

**Decision.** Check it. Every tenant-owned entity takes `governmentId` in its constructor,
and the interceptor throws `TenantIsolationException` on a mismatch or when no tenant is
set.

**Alternatives.** Stamping is convenient, but it hides the tenant from the domain and lets
`new Fund(...)` exist with no owner.

**Consequences.** Constructors are slightly more explicit, and the domain can enforce
cross-entity checks (`BudgetVersion.AddLine` refuses a fund from another government).

---

## ADR-0014: Application reaches the database through ICivicBudgetDbContext
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** Application services need to query and save. The usual choices are a
hand-written repository per entity or an interface over the DbContext.

**Decision.** Application declares `ICivicBudgetDbContext` (the domain `DbSet`s and
`SaveChangesAsync`) and `ICivicBudgetDbContextFactory`, and references EF Core's core
package for LINQ. It does not reference the SQL Server provider, Identity, ASP.NET Core,
or Infrastructure; Domain references nothing. `ArchitectureTests` enforces all of it.

**Alternatives.** Repositories: mostly re-implementing `DbSet`, and awkward for
projections. The specification pattern: heavier than this project needs.

**Consequences.** Queries stay expressive. Application is coupled to EF Core's query
shape, which is fine for a project whose persistence is EF Core.

---

## ADR-0015: Identity tables sit outside the tenant query filter
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** Sign-in must find a user by email before anyone knows which government they
belong to.

**Decision.** `ApplicationUser` carries `GovernmentId` but is not `ITenantOwned`.
`UserAdminService` scopes every query by the current government itself, checks department
assignments against the filtered `Departments`, and has tests that try to cross governments.

**Alternatives.** A separate Identity database or context (more moving parts); working out
the government from the email domain before sign-in (fragile).

**Consequences.** One documented exception to "everything is filtered". The Identity
tables hold no budget data, so a mistake there exposes user details, not finances.

---

## ADR-0016: Bootstrap 5 vendored as static files
**Date:** 2026-09-16 · **Status:** Accepted

**Context.** The admin app needed a usable layout and form styling quickly.

**Decision.** Bootstrap 5.3's CSS and JavaScript bundle, copied from the Blazor template
into `wwwroot/lib`. No CDN, no npm, no NuGet package.

**Alternatives.** A CDN (a runtime dependency on someone else's server, which some
government networks block); hand-written CSS (slower to a decent result).

**Consequences.** About 300 KB in the repository, updated by hand. The portal uses the
same stylesheet: its own section of `app.css` (`.pt-*`) on the same design tokens.

---

## ADR-0017: An audit trail from a SaveChanges interceptor and an opt-in attribute
**Date:** 2026-09-17 · **Status:** Accepted

**Context.** SPEC §7.1: record who changed what and when, field by field, and show the
history of each budget line.

**Decision.** `AuditInterceptor` writes `AuditEntry` rows for entities marked `[Audited]`,
in the same save as the change. The user and time come from `ICurrentUser` and
`TimeProvider`. Audit entries are append-only and belong to a government. Services record
named actions (workflow, publishing, imports) with `AuditEntry.Event`.

**Alternatives.** SQL Server temporal tables (no "who", and whole-row versions); database
triggers (outside the code, with no user context); writing audit rows in each service
(easy to forget).

**Consequences.** Each audited change costs one extra row per changed property. Audit
values are text for people to read, not a replay log. The audit interceptor runs before the
tenant interceptor, so the audit rows it adds are tenant-checked too. Later,
`[NotAudited]` (ADR-0028) let a property whose change is already a named event stay off
the timeline.

---

## ADR-0018: Client-generated keys are declared ValueGeneratedNever
**Date:** 2026-09-17 · **Status:** Accepted

**Context.** Entities assign their own Guid v7 ids in their constructors (time-ordered, so
SQL Server's clustered indexes do not fragment). EF Core's default for a Guid key is
"generated on add", so when a new line was added through the budget version's collection,
EF saw a key already set, assumed the line existed, and issued an UPDATE that touched no
rows and threw a concurrency exception.

**Decision.** `CivicBudgetDbContext.UseClientGeneratedKeys` marks `Id` on every entity
type as `ValueGeneratedNever()`. No schema change.

**Consequences.** Aggregates add children through their own methods and `SaveChanges`
inserts them. Every entity must set its own id, which the base class does.

---

## ADR-0019: Snapshot status lifecycle: Active, Superseded, Unpublished
**Date:** 2026-09-17 · **Status:** Accepted

**Context.** SPEC §6: unpublishing is allowed and audited, and republishing after an
amendment replaces what citizens see while keeping the history.

**Decision.** A snapshot is never deleted, and only its status ever changes. One snapshot
per fiscal year is Active. Publishing a year that already has one marks the old one
Superseded; the Administrator or Fiscal Officer can mark an Active one Unpublished. The
portal context filters to Active.

**Alternatives.** Deleting on unpublish (loses history); a boolean `IsActive` (cannot tell
"replaced" from "withdrawn" in the history).

**Consequences.** Storage grows with each publish, about a hundred rows for a village's
budget. The history can explain every past state. Since Phase 17 a filtered unique index
also enforces "one Active per year", so two publishes racing past the service's check
cannot both win.

---

## ADR-0020: The theme is CSS variables over Bootstrap; Bootstrap Icons vendored
**Date:** 2026-09-18 · **Status:** Accepted

**Context.** Phase 4.5 gave the admin app a visual identity
([design brief](design/DESIGN-BRIEF.md)). I could compile Bootstrap from SCSS with my own
variables, adopt a component library, or override Bootstrap's CSS variables.

**Decision.** Design tokens as CSS custom properties in `app.css`, mapped onto Bootstrap's
`--bs-*` variables in the same `:root` block, plus rules for the shell, grids, pills,
stepper, cards, dialogs, toasts, and empty and loading states. Bootstrap Icons 1.13 is
vendored under `wwwroot/lib/bootstrap-icons`.

**Alternatives.** An SCSS build (brings Node and Sass into a .NET solution for little
gain); a commercial suite (ADR-0011); inline SVG icons (harder to keep consistent).

**Consequences.** One file explains the look. Icon updates are manual. There is no dark
theme yet; the tokens make it a later addition.

---

## ADR-0021: Portal output caching: a base policy, a header rewrite, eviction by tag
**Date:** 2026-09-18 · **Status:** Accepted, amended 2026-09-23

**Context.** The portal is anonymous and statically rendered, which makes it ideal for
ASP.NET Core output caching: render each page once per government until the next publish.
Two things stood in the way. Blazor's static rendering marks every response
`Cache-Control: no-cache, no-store` and sets an antiforgery cookie, and the built-in
default policy refuses to store either. And `[OutputCache]` attributes are not applied to
Razor component endpoints, so I could not declare the policy per page.

**Decision.** Three small pieces in `src/CivicBudget.Web/Caching/`:
1. `PortalOutputCachePolicy`, registered as the *base* policy with
   `excludeDefaultPolicy: true`. It caches only `GET /transparency/**`, tags each entry
   `portal:{slug}` (the portal index gets its own tag), and refuses to store anything that
   is not a 200 or that still sets a cookie. Admin pages, sign-in, and health checks are
   never cached.
2. `PortalResponseMiddleware`, placed *before* `UseOutputCache` so it runs on hits and
   misses alike. In `OnStarting` it rewrites a portal 200 to `Cache-Control: public,
   max-age=600`, drops `Pragma`, and removes the `Set-Cookie` header when the only cookie is
   the antiforgery token (portal pages have no forms that post).
3. `OutputCacheSnapshotInvalidator`, which implements the Application interface
   `IPublishedSnapshotCacheInvalidator` by evicting the government's tag. Publishing,
   unpublishing, a logo change, and a slug change call it.

**Amended 2026-09-23: cache keys.** I first varied the cache by every query string
(`QueryKeys = "*"`). That let anyone skip the cache by adding `?x=1`, `?x=2`, … and make
every request rebuild a page from the database. The policy now varies only by the three
keys the pages read: `q`, `show`, and `view`.

**Alternatives.** The older response-caching middleware (honors `no-store`, and has no tag
eviction); caching in `SnapshotQueryService` with `IMemoryCache` (still renders on every
request, and eviction logic leaks into Infrastructure); a CDN in front (right for
production, but the app should be correct on its own, and a CDN honors the same `public,
max-age` header this emits).

**Consequences.** A miss costs a query and a render; a hit costs nothing past the
middleware. The in-memory store is per process, so a second instance would need the Redis
store, a package swap with no code change. ClosedXML arrived in this phase for the XLSX
download; CSV needs no package (`CsvWriter`).

---

## ADR-0022: Import as preview then commit, with a pure analyser; reports built from the workspace read
**Date:** 2026-09-18 · **Status:** Accepted

**Context.** Phase 6 added CSV and XLSX import of budget lines, and three reports. Import is
the riskiest write in the app (one file can touch every line), and a report must never
disagree with the entry screen.

**Decision.**
- **Import is two calls.** `PreviewAsync` parses the file and classifies every row as Add,
  Update, Unchanged, or Error, with the reason. `CommitAsync` takes the raw rows back,
  re-analyses them against the database *at that moment*, refuses if any row has an error,
  and applies the rest through the `BudgetVersion` aggregate in one save with one audit
  event (the interceptor still records each field). Nothing is written by a preview.
- **The rules are a pure function.** `ImportAnalyzer.Analyze` mirrors
  `BudgetVersion.AddLine`'s guards plus the file-level rules (money that parses, no
  duplicate rows, blank optional columns mean "leave as is"). Every rule has a unit test
  with no database. Analysed rows carry the ids their codes matched, so commit applies
  exactly what the preview showed even when a code was typed as `01000`.
- **The file layout is the export's.** Codes, not ids (Fund, Department, Account, Amount,
  and optional comparatives and justification), exactly what the workspace's "Export
  lines" writes, so export, edit in Excel, import is a round trip. Import never deletes.
- **Reports are shaped from `BudgetWorkspaceDto`.** `ReportBuilder` is pure over the same DTO
  the workspace renders, so the department user's visibility rule and the fund arithmetic
  are applied in one place. `ReportTables` turns each report into an `ExportTable` for XLSX.
- **Print is CSS.** `@media print` hides the shell and leads with the report; the Print
  button calls `window.print`.

**Alternatives.** Committing straight away with a summary (no chance to catch a mistake
first); a staging table (more moving parts than a village needs; the preview lives in the
circuit and is re-checked on commit); a reporting database (premature); a PDF library
(browser print with a stylesheet is enough).

**Consequences.** `ISpreadsheetReader` joins `ISpreadsheetExporter` in Infrastructure;
`CsvReader` sits beside `CsvWriter` in Application. Files over 5 MB or 10,000 rows are
refused up front. A county-sized government would push the report grouping into SQL behind
the same `IReportService`.

---

## ADR-0023: Fargate behind a load balancer with the CDK L2 pattern; secrets by reference
**Date:** 2026-09-18 · **Status:** Accepted, amended 2026-09-24

**Context.** ADR-0008 committed to deploy-ready AWS infrastructure. I had left the compute
choice open between ECS Express Mode and Elastic Beanstalk, to check against current
documentation. Checked 2026-09-18: Express Mode (generally available November 2025) has only
a low-level construct, runs one container in the default VPC's public subnets, and has no
custom domains; Elastic Beanstalk hides the network and database wiring I want to show.

**Decision.**
- **Compute:** `ApplicationLoadBalancedFargateService` from the ECS patterns library: a
  public load balancer, a Fargate task (0.5 vCPU, 1 GB) in private subnets, `/health`
  checks, sticky sessions for Blazor circuits, and a deployment circuit breaker with rollback.
- **Database:** RDS SQL Server Express (`db.t3.micro`, 20 GB, encrypted, seven-day backups)
  in private subnets, reachable only from the service's security group. The same engine as
  local development. Secrets Manager generates and holds the master password.
- **Secrets by reference.** The task definition names Secrets Manager entries (the
  RDS-managed password and a generated demo password), and ECS injects them at start. The
  app composes its connection string from plain settings plus the password
  (`DatabaseOptions`), so no derived connection-string secret can drift when RDS rotates the
  password. A test proves the template contains no password.
- **One app stack.** Network, database, service, logs, alarm, and outputs deploy together,
  with `RemovalPolicy.DESTROY` so `cdk destroy` leaves nothing billing. A production account
  would split the network and database from the service and protect the database from
  deletion; the stack's comments say where.
- **OIDC, not keys.** A one-time stack (`GitHubOidcStack`) creates GitHub's OIDC provider and
  a deploy role that trusts only this repository, on `v*` tags or the `production`
  environment. The role can push images and assume the CDK bootstrap roles, nothing more.
  `deploy.yml` runs only when the `AWS_DEPLOY_ROLE_ARN` variable exists.
- **Data Protection keys in SQL Server.** A container's default key store is its
  filesystem, which vanishes on restart and signs everyone out.
  `PersistKeysToDbContext<CivicBudgetDbContext>` keeps the key ring in a table that restarts
  and a second task share.
- **Migrate and seed on start, opt-in** (`Database:MigrateOnStartup`, `Database:SeedDemoData`),
  for the demo only. A real pipeline would run migrations as a step and never seed.
- **One task, deliberately.** Sticky sessions and shared keys are in place; the missing piece
  for two tasks is a shared output cache store, because an eviction on task A is invisible to
  task B's in-memory cache.

**Amended 2026-09-24: where the image repository lives.** The ECR repository was in the app
stack, but the deploy workflow pushes the image *before* it deploys that stack, so a first
deploy into a fresh account would have failed at the push. The repository now belongs to the
one-time OIDC stack, and the app stack looks it up by name.

**Cost (us-east-2 list prices, September 2026, approximate).** RDS SQL Server Express about
$17 a month plus $2.50 of storage; Fargate about $18; the load balancer about $16; the NAT
gateway about $33 plus data; Secrets Manager $0.80; CloudWatch and ECR under $2. About **$90
a month**, a third of it the NAT gateway. The budget alarm defaults to $60 at 80% so it fires
early. Cheaper variants, by what they give up: put the task in a public subnet and drop the
NAT gateway (saves $33, exposes the task's network interface behind its security group), or
stop RDS outside demo hours.

**Alternatives.** ECS Express Mode and Elastic Beanstalk (above); App Runner (no private
database without a VPC connector, and no WebSockets when I checked); one EC2 instance with
Docker Compose (cheapest, but nothing about it carries over to an ECS estate).

**Consequences.** `dotnet test` needs Node.js for the CDK's JSII runtime, and the infra tests
run one class at a time because JSII is one process per test host. CI gained a `cdk-synth`
job and a Docker build. Nothing here has been deployed; it is synthesized and asserted on
every commit.

---

## ADR-0024: Full account numbers are composed from the three stored codes under a per-government format
**Date:** 2026-09-19 · **Status:** Accepted

**Context.** Staff think in full account numbers (`1000-725-121`, `101-110-5100`), and each
ERP's chart decides how they are written ([research](research/ohio-account-numbers.md)).
The model already stored fund, department, and object codes separately, and every rule (a
department on each expenditure line, revenue at fund level) depends on those separate ids.

**Decision.** Keep the three codes as the source of truth and compose the full number.
- `AccountNumberFormat` is a value object owned by `Government` (five columns on its row):
  segment widths, separator, and the name of the middle segment. It is editable in
  Government settings, and a chart sync sets it when the ERP source provides one.
- `AccountNumber.Compose` and `TryParse` are pure functions in Domain. Composing pads numeric
  codes to the width; parsing accepts any common separator or none and refuses text that is
  not an account number, so a search box can try the number first and fall back to names.
- Every line DTO carries its number, and published snapshot lines store the number as it was
  written on the day they were published.
- The import accepts an `Account Number` column instead of the three code columns, and the
  export writes both.
- The seed's department codes are UAN program numbers (110 Police, 620 Streets, 725
  Finance), and Pine Hollow uses a dotted "Department" layout to show the setting.

**Alternatives.** Storing the full number on each line (duplicates three codes and drifts
when one is renamed); one account entity keyed by the full number (loses fund and department
as things the rules and permissions depend on); a fixed 4-3-4 layout (would not fit a county's
chart, which is the point of plugging in beside an ERP).

**Consequences.** Numbers are computed, so a rename shows everywhere except in snapshots,
which is intended. A fourth segment (cost center) is not modelled; the value object is where
it would go.

---

## ADR-0025: The chart of accounts is received from the ERP through an adapter, never deleted, and owned by a switch
**Date:** 2026-09-19 · **Status:** Accepted, amended 2026-09-27

**Context.** After v1.0 I repositioned CivicBudget as an add-on beside the government's ERP
(VIP or similar), which owns the chart of accounts. Funds, departments, and objects should
come from there, but the app must still work for a government with no feed, and the ERP's
real interface is unknown (an export today, perhaps an API later).

**Decision.**
- **One contract.** `ErpChart` is everything CivicBudget needs from an ERP: three code lists
  with the fields the rules need, and optionally the account number format.
  `IErpChartSource` is the adapter interface. The first adapter, `ErpChartFileSource`, reads a
  one-row-per-code CSV or XLSX (`Kind, Code, Name, Type, Category, Description, Active`) and is
  forgiving about spelling and column order. Its column names are the one place to change when
  a real ERP's layout is known; nothing above it would move.
- **Diff, then apply through the entities.** `ChartDiff.Compute` is pure: Add, Update,
  Deactivate, Reactivate, or Unchanged per code, with before and after text for a person to
  judge. `ChartSyncService.CommitAsync` reads the file again, diffs again, and applies each
  change through the entities' own methods, so the domain rules run and the audit interceptor
  records every field. One sync log row and one audit event per sync.
- **Never delete.** A code the ERP no longer lists is retired: budget lines and snapshots still
  point at it. The preview warns when a file would retire more than a quarter of the chart,
  which almost always means a partial export.
- **Never retype an account in use.** An account whose type the file would change, while budget
  lines use it, is refused: its lines would silently move between revenues and appropriations
  in every budget, adopted ones included.
- **Ownership is explicit.** `Government.ChartSource` is `Local` until the first sync, then
  `Erp`. Under `Erp` the setup services refuse writes (`ChartOwnership`), and the setup screens
  show a banner with the last sync instead of New and Edit. An Administrator can switch back.

**Alternatives.** Calling the ERP from the setup services (couples the app to an interface that
does not exist yet); deleting codes the ERP dropped (breaks history); read-only setup screens
for everyone (a government without an ERP could never start).

**Consequences.** A sync is always the whole chart; there is no partial sync, on purpose. When
an API adapter arrives it implements `IErpChartSource`, and the sync page gains a button beside
the upload.

**Amended 2026-09-27: one contract per kind of data.** Actuals followed the same pattern
(ADR-0034) with their own contract and adapters (`ErpActuals`, `IErpActualsFileSource`,
`IErpActualsApi`) rather than growing `IErpChartSource` into one large connector interface. An
ERP that can send a chart but not actuals, or the other way round, implements only what it has.
The actuals page is the first to have the API button this ADR anticipated.

---

## ADR-0026: The Administrator is a superset; temporary passwords are enforced by a claim; a department assignment bounds every read
**Date:** 2026-09-19 · **Status:** Accepted

**Context.** The v1.1 user story: an administrator with complete access, users who land in
their own department and see nothing else, and logons the administrator creates and resets.

**Decision.**
- **One helper answers "may this user act as the fiscal officer?"**
  `ICurrentUser.IsFiscalAuthority()` (Administrator or Fiscal Officer) replaced every role test
  in the services, and the fiscal policies include the Administrator. `IsDepartmentUser()` is
  its counterpart. The stored role names did not change; the labels became the customer's words.
- **Temporary passwords.** `ApplicationUser.MustChangePassword` is set when an administrator
  creates an account or resets a password. The claims factory turns it into a claim;
  `MustChangePasswordMiddleware` sends any signed-in request outside the account pages to the
  change-password page; that page clears the flag and refreshes the sign-in so the cookie
  loses the claim. A claim, rather than a database check on every request, keeps the middleware
  free of I/O, and the security-stamp change on reset ends any open session.
- **A department assignment bounds every read.** The workspace, reports, exports, search, and
  the audit trail all limit a department user to their own departments.
- **User administration is audited** as named events on the government's trail, because
  Identity's entities are not `[Audited]`.

**Alternatives.** A separate "super admin" role (nothing in the customer's world needs it);
checking `MustChangePassword` in the database on every request (a query per request for a rare
state); enforcing the change only on the sign-in page (a bookmark would bypass it).

**Consequences.** One new column, no data migration of roles. Navigation inside a circuit does
not pass through middleware, but a flagged user never reaches a circuit: their first request
after sign-in is redirected.

---

## ADR-0027: Department requests live on the budget version; submitting locks the department, not the version
**Date:** 2026-09-19 · **Status:** Accepted

**Context.** The last step of v1.1: a police chief signs in, lands in the police department,
enters the request against its accounts, writes a narrative, and hands it to the fiscal
officer, who assembles the whole budget and can send a department's request back. The
existing workflow (Draft, Proposed, Adopted) is the *version's* state; the department round
happens inside Draft.

**Decision.**
- **A `DepartmentRequest` child of `BudgetVersion`**, one per department that has written a
  narrative or submitted: In progress, Submitted (who and when), or Returned (the officer's
  note and when). A department with no row is simply in progress. The aggregate owns the rules:
  submit only while Draft and only with lines; return only what was submitted, with a note;
  submitting again clears the note. Amendments copy narratives (they still describe the year)
  but start a new round.
- **Submitting locks the department for department users, not the version.**
  `BudgetLinePermissions.CanEdit` takes a `departmentSubmitted` flag; the fiscal officer is
  unaffected. `CanSubmitDepartment` and `CanReturnDepartment` sit beside it, so the
  authorization handler, the services, and the DTO flags all come from one place.
- **The workspace DTO carries the round.** `BudgetWorkspaceDto.DepartmentRequests` gives every
  department the user can see with its status, totals, narrative, and what the user may do, so
  the department page, the board, the workspace strip, and the report read one shape.
- **The narrative is published.** The snapshot freezes each department's narrative, and the
  portal's department page shows it.
- **A department user's home is their department.** `/admin` forwards department users to
  `/admin/my-department`, which finds the open version and sends them to their department, or
  to the board when they hold several.

**Alternatives.** A status column on each budget line (one status copied across a dozen lines,
and nowhere for the narrative); a separate department-budget aggregate holding its lines (would
split the appropriation check, which needs every line of the fund); blocking Propose until every
department submits (the officer decides when the round is over; one late department should not
hold up council).

**Consequences.** Two tables, one migration, no change to the version workflow or the
appropriation check.

---

## ADR-0028: Profile pictures are resized by the browser, stored in SQL Server, and served through a versioned URL
**Date:** 2026-09-20 · **Status:** Accepted

**Context.** Users wanted a picture instead of initials. The app has no blob storage, and
resizing on the server would mean an image library with a native dependency and a licensing
decision (ImageSharp's split license, SkiaSharp's size) for one small image per user.

**Decision.**
- **The browser resizes.** `IBrowserFile.RequestImageFileAsync("image/png", 256, 256)` draws
  the chosen file onto a canvas and returns a PNG no larger than 256 px. The server never
  decodes an image; since Phase 17 it checks the size and that the first bytes match a PNG,
  JPEG, or WebP (`UploadedImage`), because a hand-built request can send anything with any label.
- **Bytes live in a `UserAvatars` table**, one row per user, apart from `AspNetUsers` so a user
  list never reads image data. `ApplicationUser.AvatarUpdatedAtUtc` says whether a picture
  exists and doubles as its version.
- **A versioned URL with a long private cache.** `/Account/Avatar/{userId}?v={ticks}` returns
  the bytes with `Cache-Control: private, max-age=31536000, immutable` and `nosniff`; a new
  upload changes the URL, so nothing is ever stale. The endpoint requires sign-in and the
  service only returns pictures from the caller's own government.
- **One `Avatar` component** decides picture or initials everywhere a person appears.
- **Users upload their own; administrators only remove.** Removing is the moderation need.

**Alternatives.** Blob storage (the answer at scale, and a one-class change behind
`IUserAvatarService`); server-side resizing (a package and a native dependency for one
feature); an image in a claim (bloats every request's cookie); Gravatar (sends users' email
addresses to a third party from a government system).

**Consequences.** One migration, no package. A thousand users is under 200 MB. The same phase
added `[NotAudited]` for properties whose change is already a named audit event, because the
department round had started writing ids and timestamps into the activity feed.

---

## ADR-0029: The portal's read-only context also maps the government logo; the overview's panels slide with CSS alone
**Date:** 2026-09-20 · **Status:** Accepted

**Context.** The portal header showed the government's initials in a circle. I wanted the
CivicBudget mark by default and a logo the government's administrator can upload. ADR-0006
says the portal context maps only snapshot tables so nothing live can leak, and a logo is
live data. Separately, the overview's spending and revenue breakdowns should be one sliding
section, and the portal has no JavaScript.

**Decision.**
- **`GovernmentLogos` is the one non-snapshot table the portal context maps.** It holds a
  government id, a content type, bytes, and a timestamp: a public image and nothing else, so
  mapping it read-only cannot expose a draft, a user, or a setting. The portal finds it through
  an active snapshot, so a government with nothing published has no public face, and a changed
  slug cannot orphan it. Uploads are Administrator only, resized by the browser to 512 px, and
  checked for type and size, and they evict the government's cached portal pages.
- **The header falls back to the mark**, not to initials: a default that looks designed.
- **The panels slide with `:checked`.** Two radio buttons styled as tabs and a track two panels
  wide; the checked radio moves the track and hides the other panel so its links leave the tab
  order. Both panels are in the HTML, so search engines, reader mode, and screen readers see
  everything, and the `$ | %` toggle, which reloads the page, keeps the panel through
  `?view=revenue`.

  *Amended 2026-10-01:* the `$ | %` toggle no longer reloads the page. A link reloaded and then
  jumped to the chart's anchor, which sat inside the sliding track, so the reader landed somewhere
  odd. The toggle is now a radio pair like the panels: each bar carries both values and CSS shows
  the one the chart's checked radio picks. Each chart has its own toggle, and `?show=pct` still
  opens a page in percent.

**Alternatives.** Copying the logo into each snapshot (a logo change would need a republish);
serving it through the admin context (breaks the one-door rule for no gain); a JavaScript
carousel (breaks the portal's no-script promise); `<details>` or `:target` for the tabs (no
slide, and `:target` scrolls the page).

**Consequences.** One table, one migration, and one line in the test that lists what the portal
context maps.

---

## ADR-0030: The live demo runs on Azure's free tiers; the database is rebuilt from the seed every night
**Date:** 2026-09-20 · **Status:** Accepted, amended 2026-09-29

**Context.** I wanted the app hosted for free so anyone evaluating it can sign in and use it. It
needs a persistent process (Blazor Server) and SQL Server. The SQL Server container wants 2 GB
of memory, which no free VM tier offers, and the AWS free tier no longer covers RDS or the NAT
gateway the CDK stack uses.

**Decision.**
- **Azure, two always-free offers.** Azure SQL Database's free offer (serverless General
  Purpose under `useFreeLimit`: 100,000 vCore-seconds and 32 GB a month, pausing rather than
  billing when exhausted) is real SQL Server, so the EF provider and every migration run
  unchanged. Azure Container Apps on the consumption plan runs the existing Docker image,
  supports WebSockets, and scales to zero under a monthly free grant. `infra/azure/main.bicep`
  declares both plus a log workspace with a daily cap; `scripts/azure-setup.sh` creates them
  once; `deploy-azure.yml` deploys over OIDC, as the AWS workflow would.
- **A nightly reset, not moderation.** Six shared logins are published in the README. Anything
  a visitor does (change a password, adopt the draft, upload a picture) is undone at 08:00 UTC
  by a Container Apps job that runs the same image with `--reseed`: drop every table, migrate
  from nothing, seed. The database itself is kept, because on Azure it is the free-offer
  resource; recreating it would create a billable one.
- **Not switching to PostgreSQL** to fit a free host. The data layer is SQL Server on purpose,
  and every migration and integration test would need redoing for a hosting convenience.

**Alternatives.** A tunnel from my Mac (free, but only while the Mac is awake, and SQL Server
under Rosetta crashes now and then); Render or Fly with PostgreSQL (the rewrite above); AWS on
the new credit-based free tier (six months, and the NAT gateway alone is about $32 a month); a
"demo mode" that blocks destructive actions (more code, and a worse demo).

**Consequences.** One replica at most, which the in-process output cache already required
(ADR-0021). Every session ends at the nightly reset. CI compiles and lints the Bicep so a
template error cannot wait for a deploy. The first visit after a quiet spell is slow; ADR-0031
is what I did about it.

**Amended 2026-09-29: a page opened before the reset.** The reset drops every table, and that
includes `DataProtectionKeys`, so the next process makes new keys. A static form (sign-in, the
account pages) that was loaded before the reset still carries an antiforgery token sealed with the
old key. Posting it failed the check, and the endpoint answered 400 with an empty body, which the
browser shows as a blank white page. The status-code pages could not help: they re-execute the
request as the same POST, which fails the same check. I kept the keys in the reset's path (keeping
them would mean a reset that is not quite a reset, and a deploy that rotates keys hits the same
thing) and made the failure readable instead. `ExpiredFormMiddleware` runs right after
`UseAntiforgery` and, when the check failed, answers before the endpoint with a small page in the
waiting screen's shell (`PlainPage`): the page expired, nothing changed, and a link back to the same
address as a GET. Sign-in keeps its ReturnUrl only when `LocalUrl.IsLocal` accepts it; a post-only
endpoint (sign-out) links home. The page has no script. The portal's question box opts out of
antiforgery, so it is never checked and never sees this page. I chose to answer before the endpoint
rather than rewrite its 400 afterwards because in Development Blazor writes a line of plain text into
that 400, so an after-the-fact check would work in production and not on my machine.

---

## ADR-0031: The host listens before the database is ready and shows a waiting screen
**Date:** 2026-09-21 · **Status:** Accepted, amended 2026-09-23

**Context.** On the free Azure tier a visitor's first request after an idle hour took about 65
seconds to produce anything: about 15 seconds for the platform to start the container, then
about 48 while serverless SQL resumed. `Program.cs` ran migrations and seeding before the web
server started, so nothing was listening, the startup probe could not pass, and the browser sat
on a blank page. A blank minute reads as "the site is down". Keeping the database awake or a
replica warm would use up the free allowances within days.

**Decision.**
- **Migrate and seed in a hosted service** (`DatabaseStartupService`), not before the host
  starts. The web server listens within seconds of the process starting. A failure stops the
  host so the platform restarts the container.
- **A waiting screen until the database is ready.** `StartupState` is a singleton the service
  flips to ready. Until then `WakingUpMiddleware` answers every page request with a small,
  self-contained page in the app's own look: `503 Service Unavailable` with `Retry-After` and
  `no-store` (so crawlers and monitors do not cache it as the site), a counter that survives
  reloads, and a poll of `/health/startup` every two seconds that reloads the page when the app
  is ready. A `<noscript>` refresh covers browsers without script. Health checks and static
  files pass through.
- **Health endpoints never touch the database.** `/health` is liveness, and it is what the
  platform's startup probe calls, now every two seconds. `/health/startup` answers from memory.
  An earlier `/health/ready` did a real database round trip; I removed it in Phase 17 because
  nothing used it and anyone could have polled it to keep the free database awake.
- **Data Protection reads its key ring lazily** (`DeferKeyRingLoad`). The first version of this
  ADR shipped and changed nothing: the site still showed a blank browser for about seventy
  seconds. The Azure logs put "Now listening" seventeen milliseconds *after* the migration
  check, fifty-two seconds in, which is the wrong order for a host that is supposed to listen
  first. The cause was upstream of everything I had touched: `AddDataProtection` registers an
  internal hosted service that reads the key ring during startup, the keys live in SQL Server,
  and EF's retry strategy spent most of a minute on that read before the web server was allowed
  to start. Removing that registration leaves the framework's own lazy load, which happens on the
  first request that needs a cookie or an antiforgery token, by which time the database is up.
  The waiting screen uses neither.

**Amended 2026-09-23: a database that pauses behind a live container.** `StartupState` remembers
when it last let a page through. After 55 minutes without one (serverless SQL pauses at 60), the
next page request starts a single shared check (`DatabaseWaker`) and waits up to a second: an
awake database answers in milliseconds and the page is served; a sleeping one gets the waiting
screen. Scale-to-zero usually retires the container long before the database pauses, but an open
admin tab keeps a WebSocket alive and can hold the container up past the hour.

**Amended 2026-09-23: the container is not kept warm.** After a few idle minutes the first visit
still waits about 17 seconds before anything appears. Azure's logs split that into about 15
seconds provisioning, 1 second pulling the image, and 0.3 seconds of the app's own startup, so
nothing in the app can shorten it. `minReplicas: 1` would make every visit instant for about $4
to $5 a month. I chose to keep the demo free; it is a one-line change to `main.bicep`.

**Amended 2026-09-29: one anonymous exception.** The portal's question box (ADR-0049) reads the
database for each posted question, because the answer comes from the snapshot and the monthly count
lives on the government's row. It does so only when an AI model is connected; without one,
`PortalQuestionGate` refuses the post before anything reads the database, so the free demo, which
has no model, keeps the rule. With a model, 10 questions per address per hour are allowed first.
*Amended again 2026-09-29:* the demo now has a model (see ADR-0047's amendment), so a posted
question can wake its database. Each address gets 10 an hour, so keeping it awake takes many
addresses; if that ever happens, `scripts/azure-assistant.sh --off` restores the rule in a minute.

**Amended 2026-10-01: waking ahead of the visitor.** The product site now starts the demo waking
while someone reads about it: one `POST /health/wake` when its page loads. Any request starts a
container that scaled to zero, and that start wakes the database. The endpoint also covers the case
the request alone cannot, a running container whose database paused: it starts the same check the
next page request would (`StartupState.ClaimWake`) and answers at once. It keeps the rule above. It
opens no connection on the request, and the check runs only when one is due (after `QuietSpell`), so
calling it often keeps the database awake no longer than page views already could. It is a POST so
crawlers and link previews never trigger it, and the site skips it in automated browsers. This is
not the keep-alive ping turned down below: nothing calls it on a timer, only a person opening the
page.

**Alternatives.** A minimum of one replica (costs money, and the database would still pause);
disabling SQL auto-pause (burns the free vCore-seconds in about four days); a keep-alive ping
(the same); a static loading page on a CDN in front (another moving part, and it could not know
when to stop); serving the waiting screen as a 200 (monitors and crawlers would cache it as the site).

**Consequences.** Something appears about 20 seconds into a cold start (platform time only), and
the site continues on its own at about a minute. The warm path is unchanged. The key-ring fix
matches an internal framework type by name, so `DataProtectionStartupTests` fails loudly if a
future .NET renames it, instead of letting the delay creep back. Blazor circuits cannot start
early (`/_blazor` is not exempt), so no page ever renders against a missing database.

---

## ADR-0032: Maintenance pass: services enforce their own roles, times are Eastern, static pages carry no script
**Date:** 2026-09-23 · **Status:** Accepted

**Context.** In Phase 17 I reviewed the whole codebase layer by layer (domain and application,
infrastructure, admin UI, shared UI and portal, tests and CI) and used the running app as every
demo user. Most findings were plain bugs with one right fix; these fixes were choices.

**Decision.**
- **Every service that writes checks the caller itself.** Workflow, publishing, import, and chart
  sync already did; the setup services relied on the page's `[Authorize]`. They now check the
  role too, and `AdminPagePolicyTests` pins every admin page to its policy, so a dropped
  attribute fails the build.
- **Times are shown in Eastern time with the zone named** (`Display.Timestamp`, `ShortDate`,
  `LongDate`). Every government here is in Ohio, which is entirely Eastern, and the server runs
  in UTC, so `ToLocalTime()` showed a 2 PM sync as 6 PM. A multi-state product would store a
  time zone per government. Amounts always format as en-US.
- **Pages that are not interactive load no script.** `App.razor` includes Blazor's script,
  Bootstrap's, and the reconnect dialog only on interactive pages, so the portal's "no
  JavaScript" is literally true.
- **The portal remembers a loaded budget for one request** (`SnapshotQueryService`). The funds
  page went from about 54 queries per cache miss to 6.
- **A government's public address moves its published budgets with it**
  (`PublishedBudgetSnapshot.MoveToSlug`): the slug is where a snapshot is found, not part of what
  was published, and a stale one could be claimed by another government.
- **No anonymous endpoint queries the database per call.** `/health/ready` was removed.
- **Deploys follow CI** (`workflow_run` on success), check the image before it is tagged
  `latest`, and wait until the new revision is the one serving.

**Alternatives.** Optimistic concurrency on `BudgetVersion` (the review found lost-update races
between an edit and an adoption) was larger than a maintenance pass, so I listed it as a known
gap; Phase 18 implemented it (ADR-0033).

**Consequences.** Tests that change settings sign in as the Administrator. Portal tests that
change data read the result through a fresh scope, as the next request would.

**Amended 2026-09-27: one script on the account pages.** Password fields now have a show/hide
button, which needs a few lines of script: a static page has no circuit to handle the click, and CSS
cannot change an input's type. `js/password-toggle.js` loads on interactive pages and on the static
`/Account` pages (sign-in, change password), and nowhere else, so the portal is still script-free.
The button stays hidden until the script runs, so a browser without script sees an ordinary
password field rather than a button that does nothing.

---

## ADR-0033: Department totals, starting a year's budget, and optimistic concurrency
**Date:** 2026-09-24 · **Status:** Accepted

**Context.** Phase 17 ended with four budgeting questions and a list of known gaps. Three
questions I could answer from how Ohio governments work: only the latest adopted version is
publishable; amounts are never negative; and a new year's budget should be able to start from
last year's, optionally raised or cut by a percentage. The fourth, what a department's total
includes, I researched rather than guessed, because three screens were already disagreeing about it.

**Decision.**
- **A department's total is its expenditure appropriations**
  (`AccountType.CountsTowardDepartmentTotal`). Ohio appropriates by fund, then by office,
  department, and division, with personal services within each (ORC 5705.38(C)). The Auditor of
  State's UAN village chart budgets transfers out under their own program, "Other Financing
  Uses" (910 Transfers, 920 Advances), not inside an operating department, and Michigan's uniform
  chart does the same (activity 965). Revenue a department collects is an estimated resource of
  the fund and never part of an appropriation. Screens still show a department's revenue and
  transfer lines, labelled as outside its total.
- **Amounts are never negative**, in the domain and in the import. Revenues and expenditures are
  both entered as positive numbers; the account type says which way a line counts.
- **Starting a year's budget is its own action** (`BudgetVersion.CreateOriginalFrom`), not a copy
  like an amendment: the new year's comparison is last year's adopted amount, the request is that
  amount changed by the chosen percentage, prior-year actuals start at zero (an adopted budget is
  not what was spent; since 2026-09-27 the ERP fills them when it holds that year, ADR-0034), and
  each fund begins with last year's projected ending balance. Only the
  latest adopted, unreplaced version can be the source, the same rule as publishing.
- **Optimistic concurrency on the aggregate root.** `BudgetVersion.Revision` counts every change
  to the version and everything inside it, and EF treats it as a concurrency token, so the
  version's row is updated, and checked, even when only a line changed. Services save through
  `TrySaveAsync`, which turns a stale revision or a unique-index collision into a readable message.

**Alternatives.** A SQL `rowversion` column (changes only when the version's own row changes, so a
line edit would not conflict with an adoption); pessimistic locks (a tab left open would hold
them); counting transfers out in a department's total (contradicts the UAN chart and the way
Ohio's appropriation measure lists other financing uses separately).

**Consequences.** Every mutating method on `BudgetVersion` must call `Touch()`, or its changes will
not conflict. A department's request can be lower than the sum of every line coded to it, and the
screens say why.

---

## ADR-0034: Actuals come from the ERP a fiscal year at a time; a closed year fills prior-year actuals
**Date:** 2026-09-27 · **Status:** Accepted

**Context.** The reports a government cannot get from its ERP alone are the ones that put its
real spending beside its budget: budget against actual, what is left, where a fund will end the
year. Until now CivicBudget had no actuals except a prior-year column people typed or imported.
VIP can deliver data through an API or through export files, whichever the customer uses, and I
have neither its API nor its export layout, so the design has to work before either is known
and change in one place when they are.

**Decision.**
- **A contract, adapters, and a pure matcher, like the chart (ADR-0025).** `ErpActuals` is one
  fiscal year: activity by account and fiscal month, open encumbrances by account, and cash by
  fund, as of the ERP's last closed month. `IErpActualsFileSource` reads an export
  (`Fiscal Year, Type, Account, Period, Amount`, full account numbers split with the government's
  own format); `IErpActualsApi` asks the ERP directly and is registered only where a connection
  exists. `ActualsMatcher` resolves codes (ignoring padding) and adds repeated rows together.
- **A sync replaces the year and refuses unknown codes.** The ERP's books are the truth, so a
  posting it reversed must disappear here too; and a year missing one account would understate
  every total built on it without anyone noticing. The whole file is refused and every unknown
  code listed, as the import does.
- **Separate tables, not columns on budget lines.** `ErpActuals`, `ErpEncumbrances`,
  `ErpFundCash`, and an `ActualsSync` log. Actuals belong to a fiscal year and an account, not to
  a budget version, and a year's figures are shared by every version that compares against it.
  They are not audited row by row: they are a copy of the ERP's audited books, and the sync log
  plus one audit event say who brought them in.
- **A closed year fills the prior-year actual; an open year never does.** A budget for FY Y
  compares with FY Y-2's actuals. When the ERP sends Y-2 with all twelve months, the sync writes
  the totals into every open version of Y through `BudgetVersion.UpdateLineComparatives`, so each
  change is audited, concurrency-checked, and listed in the preview. Starting a budget and adding
  a line take the figure from the same place. A partial year would pass eight months of spending
  off as a year's actual. A negative total is left alone and reported rather than forced to zero.
- **The current year shows beside the budget, not as another column.** Department pages show
  this year's receipts or spending under each line's current budget, with a bar for how much of
  it that is. A seventh column pushed the grid past the screen, and the bar only means something
  next to that budget.
- **A fund-level line collects unclaimed department amounts.** When revenue is budgeted by fund
  but the ERP records it by department, the fund-level line takes every amount on that fund and
  account that no department-level line claims (`ActualsByLine`): counted once, never dropped.
- **The demo connects to a simulated ERP** (`SimulatedErpActualsApi`), registered exactly where
  the demo data is seeded. Its books are the seed's own fictional history spread over the months
  the way municipal money moves (tax settlements, biweekly payroll, summer capital work, debt
  service twice a year), and a month closes ten days after it ends. Year-end cash less carried
  encumbrances equals the next budget's beginning balance, so the numbers agree everywhere.

**Alternatives.** Storing actuals on budget lines (every version would carry its own copy, and a
new version would need re-syncing); merging a sync into what is there (a reversed posting would
live forever); accepting a file with unknown codes and skipping them (silently wrong totals);
filling prior-year actuals from a partial year (misleading); one `IErpConnector` interface with
every capability (forces an adapter to stub what its ERP cannot do).

**Consequences.** Reports can now compare budgets with real figures, which is what Phase 25
builds on, and the certificate of resources (Phase 24) has cash and carried encumbrances to start
from. Removing and re-adding tracked rows is fine for a village's or a township's few thousand
rows a year; a large county's would want a set-based delete inside a transaction. The simulated
ERP is a stand-in, and says so in its name wherever it appears.

---

## ADR-0035: The adopted budget goes to the ERP as a journal of changes, sent once
**Date:** 2026-09-27 · **Status:** Accepted

**Context.** The point of budgeting in CivicBudget is that the adopted numbers end up in the ERP,
which is where purchase orders are checked against appropriations. Today someone retypes them.
VIP takes a budget journal by API or by import file, the customer's choice, with four fields per
line: the full account number, the amount, one description for the whole journal, and one posting
date. Three things can go wrong with a "send" button: the same change posted twice, a change that
never arrived counted as if it had, and a journal half-posted.

**Decision.**
- **A journal of changes.** Each amount is the budget's figure less what earlier journals for the
  fiscal year already posted (`BudgetJournalBuilder`, pure). The first send is the whole budget, an
  amendment's is only what it moved, a removed line sends a decrease, and a budget the ERP already
  matches sends nothing. VIP adds a journal's amounts to the account's budget, as a journal does
  in every fund-accounting system I know, so a decrease is a negative amount (confirmed
  2026-09-27).
- **Only the latest adopted version**, the same rule as publishing, by an Administrator or the
  Fiscal Officer (`CanSendToErp`). The posting date must fall in the fiscal year.
- **Every send is a record** (`BudgetTransmission` and its lines), saved before the ERP is called.
  Only a send the ERP accepted, or a file someone confirmed was imported, counts as "in the ERP".
- **Idempotent by id.** The record's id travels with the journal. A call that gets no answer is
  marked Failed, not Rejected: nobody knows whether it posted. Trying again sends the same journal
  under the same id, and the ERP answers with the journal it already has. The simulated ERP
  behaves this way, and a test loses the first answer on purpose to prove it.
- **Whole or nothing.** When the ERP refuses any account, nothing posts, and each refused account
  is shown with the ERP's reason. A partly posted journal would leave the two systems disagreeing
  in a way that is hard to see and harder to undo.
- **One unfinished send per year.** A failed send or a downloaded file holds the year until it is
  settled (retried, confirmed, or discarded). The service checks first so people get a sentence;
  a filtered unique index on government and year catches two clicks racing each other.
- **The import file is VIP's four columns**, CSV, with the description and date repeated on every
  line as VIP expects: `yyyy-MM-dd` dates, amounts with a period and no separators, and a
  description of at most 100 characters. VIP answers a repeated journal id with its first answer.
  All four confirmed 2026-09-27.
- **The app says "ERP", never a vendor's name.** VIP is the ERP I know and the one I built against,
  but CivicBudget is meant for any ERP vendor to take on, so buttons, messages, and the simulated
  connection ("ERP (simulated)") name the kind of system, not the product.

**Alternatives.** Sending full amounts every time (an amendment would double the budget in an
additive ERP); marking a file "sent" the moment it is downloaded (a file never imported would make
every later journal wrong); posting the accounts VIP accepts and skipping the rest (a half-posted
budget); a new id on every retry (a lost answer followed by a retry would post twice).

**Consequences.** CivicBudget's record of what the ERP holds is only as good as the sends that go
through it; a budget typed straight into VIP would not be known here. A reconciliation against
VIP's budget (reading it back through the actuals connection) is the natural next safeguard. The
simulated ERP's memory of journal ids lasts as long as the process, which is enough for a stand-in.

---

## ADR-0036: The certificate of estimated resources, report columns as settings, and MigraDoc for the PDF
**Date:** 2026-09-27 · **Status:** Accepted

**Context.** The certificate of estimated resources (ORC 5705.36) is the legal ceiling on each
fund's appropriations, certified by the county budget commission and amended during the year. It
was out of scope as a document; only its check was in. Three things made it harder than a report:
which revenue accounts count as "taxes" differs from one government's chart to the next; the
county auditor's template words the columns its own way; and the balances it starts from (cash and
encumbrances at year end) live in the ERP, not the budget. It also leaves the building, so it needs
a real PDF, and the library has to be one a buyer can ship.

**Decision.**
- **One row per fund, two views.** `CertificateBuilder` (pure) computes each fund's carryover
  (cash − carried encumbrances − nonspendable − reserves ± unpaid advances), its revenue by column,
  other sources, total available, and appropriations. "As issued" (balance, revenue columns, other
  sources, total) and the "detailed schedule" are two renderings of the same rows, with fund-type
  subtotals and a grand total, so they cannot disagree.
- **Revenue columns are settings.** `ReportAccountGroup` holds a named set of revenue accounts per
  report; the certificate shows up to four, in order, and everything else is "other sources", so no
  receipt is left out. An account may sit in only one column. With nothing saved, the default is
  one "Taxes" column of the accounts categorized as taxes. `CertificateSettings` holds the county,
  who prepares it, and the two fixed headings.
- **Balances from the ERP when it has closed the year**, from the budget's estimate before then (an
  original certificate is prepared months before year end), and the report says which. Reserves,
  nonspendable balances, and unpaid advances are entered per fund for the year
  (`CertificateFundAdjustment`), because the ERP feed does not carry them.
- **Reconciliations that can fail**: appropriations within total available per fund (ORC 5705.39),
  the budget's beginning balances equal to the certified carryover, every column mapped, and the
  columns adding to the budget's revenue. An amended certificate lists every revenue estimate that
  moved since the version it amends, with the justification typed on the line.
- **Numbered with the budget.** Version 1 prints "Certificate of Estimated Resources"; amendment N
  prints "Amended Certificate of Estimated Resources No. N".
- **Only people who see every fund** get it: the Administrator, the Fiscal Officer, and Viewers.
  A department user's view of the budget is limited to their departments, so a certificate built
  from it would be wrong.
- **MigraDoc and PDFsharp for the PDF**, MIT licensed. Landscape Letter: the issued certificate with
  the commission's signature lines (County Auditor, County Treasurer, Prosecuting Attorney), then
  the detailed schedule, the reconciliations, the revenue changes, and the preparer's signature.
  The typeface (Source Sans 3, SIL Open Font License) is embedded in the assembly, because a Linux
  container has no fonts for PDFsharp to find.

**Alternatives.** QuestPDF (the nicest API, but its free license ends at $1M in company revenue, and
an ERP vendor is over that); a headless browser printing the page (a Chromium in the container for
one report); the browser's own "Save as PDF" (kept as Print, but a commission wants the same file
from everyone); hard-coding "taxes" as the Taxes reporting category (true for many charts, wrong for
a levy recorded elsewhere, and the thing a fiscal officer most needs to be able to change).

**Consequences.** The certificate is generated, not issued and stored: it always reflects the budget
and the ERP's figures as they are now, so a certificate a commission signed should be kept as the
PDF. Report columns are a general mechanism; the reports in Phase 25 can use them too.

---

## ADR-0037: Reports on the ERP's books are paced by last year, and the appropriation measure's columns are settings
**Date:** 2026-09-27 · **Status:** Accepted

**Context.** The reports worth selling beside an ERP put its books next to the budget: how much of
each appropriation is spent, how revenue is coming in, where each fund will end the year, and how
the years compare. Each one needs a sense of "on track", and the obvious measure (eight months gone,
so two-thirds should be spent or collected) is wrong for most municipal money: real estate taxes
arrive in two settlements, capital work happens in summer, debt service is paid twice a year. The
appropriation measure (ORC 5705.38) also needed a decision: which expenditure accounts are
"personal services".

**Decision.**
- **Pace by last year, not the calendar, wherever money is seasonal.** Revenue against receipts
  compares each account's collected share with the share of last year's total that had arrived by
  the same month; an account is "behind" only when it trails that by more than ten points. The fund
  projection scales each line's year to date by last year's full year over last year at the same
  month (`ActualsReportBuilder.Project`). A closed year is its actual; a line with no history is
  carried at its budget, never below what has already happened, and the report counts those lines.
  Committed money (encumbrances) is always projected as spent.
- **Budget against actual shows both paces.** The bar's tick is the calendar share of the year, the
  honest yardstick for evenly spent lines like payroll, and a column beside it gives last year's
  figure at the same point for the lines that are not.
- **Visibility follows the data.** Budget against actual and revenue against receipts use the
  workspace read, so a department user sees their departments. The projection, the trends, and the
  appropriation measure are whole-fund documents, so department users do not get them.
- **Trends use each past year's latest adopted version**, the same budget the portal publishes, and
  the ERP's actuals for each year it has sent (to date for a year under way).
- **The appropriation measure's columns are report settings**, reusing the certificate's mechanism
  (`ReportAccountGroup`, `ReportKind.AppropriationMeasure`). The default is one "Personal services"
  column of the accounts categorized as personal services or fringe benefits; a government that
  appropriates benefits separately makes two columns. Transfers out are appropriated at fund level
  as other financing uses, beside the departments, the same rule as department totals (ADR-0033).
  The column rules (at most four, each account once, the right kind of account) are shared
  (`ReportColumnRules`).

**Alternatives.** A straight-line projection (wrong for property taxes, which by August are almost
all in); last year's monthly shares applied to this year's budget (ignores what has actually happened
this year); hard-coding personal services as the Personal Services category alone (benefits follow
pay in most Ohio appropriation measures, but not all).

**Consequences.** The projections are only as good as last year's pattern; a one-time receipt last
year skews this year's projection for that account, which is why the report shows its method and
the lines without history. Every report here reads the same ERP tables and the same per-line totals
as the budget screens (`ActualsByLine`), so a figure cannot differ between a screen and a report.

---

## ADR-0038: Personnel budgeting: positions live on the budget version, settings belong to a year, and lines are calculated
**Date:** 2026-09-27 · **Status:** Accepted

**Context.** In an Ohio village, salaries and the benefits that follow them are most of the General
Fund, and nobody types those lines: the fiscal officer builds them from a spreadsheet of positions,
each with a salary or hourly rate, a raise, longevity, overtime, a retirement system (OPERS or
OP&F), Medicare, workers' compensation, and insurance by coverage tier, split across the funds that
pay for the position. A budget tool an ERP vendor would buy has to do that spreadsheet's job, and do
it without breaking the rule that the budget lines are the budget: every screen, report, the
certificate, the portal, and the journal sent to the ERP read lines.

**Decision.**
- **Positions belong to the budget version**, as children of `BudgetVersion` like its lines, so
  everything the aggregate already guarantees applies to them: an adopted budget's positions are
  fixed, an amendment copies them, the `Revision` concurrency token covers them, and department
  users may change them exactly when they may change their department's lines.
- **A line is typed or calculated, never both.** `BudgetLine.PositionCount` is null for a typed
  line and says "from 9 positions" for a calculated one. Every position change prices the
  department again (`BudgetVersion.ApplyPersonnel`): a line is created where a position newly
  costs into a fund and account, a typed line becomes calculated when positions cost into it, and a
  calculated line no position reaches any more returns to a typed zero. The domain refuses typing
  over or removing a calculated line, and the import refuses to change one.
- **Settings belong to a fiscal year** (`PersonnelSettings`, one per government and year): next
  year's health premiums are entered while this year's amendment is open and must not reach it,
  and an adopted budget keeps what it was priced with. Saving a year's settings reprices that
  year's open budgets and refuses any change that would leave a position of the year unpriceable
  (a plan someone is on, a tier someone has), naming the positions. A new year starts as a copy of
  the last; starting next year's budget carries each position forward at the rate it ends the year,
  its plans mapped to the new year's copies.
- **One pure calculator** (`PositionCostCalculator`) prices a position from plain values
  (`PositionDetails`, `PayrollRules`), so the editor's live breakdown and the save cannot disagree.
  Base pay is a twelfth of the year's pay per month paid at that month's rate (a raise or step
  increase in month 7 counts for six months, a vacancy filled in month 4 for nine). Retirement is
  charged on pensionable pay (earnable salary), Medicare and workers' compensation on taxable pay,
  and each extra pay item says which it is. Each piece rounds to cents and is divided among the
  funds with the largest share taking the leftover cent, so a split is the same however the funds
  are listed.
- **Longevity supports every common method** (a step table of flat amounts, a percentage of pay,
  an amount per year of service with an optional cap), counted on the first or last day of the
  budget year per schedule, and the settings page reads each schedule back as sentences
  (`Longevity.Describe`) with a worked example beside it.
- **Defaults are Ohio's**: OPERS 14% employer and 10% employee, OPERS law enforcement 18.1%, OP&F
  police 19.5% and fire 24% (12.25% employee), Social Security 6.2% for anyone outside a state
  system, Medicare 1.45%; workers' compensation starts at zero because every public employer has its
  own BWC rate. Insurance is a monthly premium per tier (single, employee and spouse, family) less
  an employee share.

**Alternatives.** Positions as their own aggregate beside the version (the lines and positions could
then disagree between two saves, and amendments would need a second copy step); one set of settings
for all years (a mid-year amendment would pick up next year's premiums); letting a user type over a
calculated line and keeping both numbers (the budget would say one thing and the positions another,
with no rule for which wins); a per-line "personnel worksheet" rather than positions (a position
split across funds and accounts would be entered several times).

**Consequences.** A version now loads its positions where a method needs them (amendments, starting
a year, personnel pages); `PersonnelData.VersionsWithPositions` is the one include list. Changing an
adopted year's settings does not move its lines, so its personnel page says when the positions no
longer price to the adopted amounts. Percentages and hours are the only decimals that are not
money: they keep four places (`decimal(9,4)`), and the migration test names them.

**Amended 2026-09-29: which fund takes the odd cent.** I broke a tie between equal shares by fund
id. Ids are Guid v7, and funds created in the same millisecond (the whole seed) get random low bits,
so a 50/50 position could move its odd cent between the General and Street funds on a reseed, and
the Street fund's over-limit amount in the demo moved between $21,908.63 and $21,908.65. Rounding
every fund's part to the nearest cent also meant the fund "taking the rest" could come out a cent
short: half of $10,000.01 rounds up to $5,000.01 for the other fund. Now every fund but the one with
the largest share is rounded down, that fund takes what is left, and on a tie the lowest fund
number takes it (compared ordinally). The fund numbers come to the calculator in `PayrollRules`
(`FundCodes`), which `PersonnelData.RulesAsync` fills from the government's funds, so the calculator
stays pure and `Position` needs no fund lookup. `ToRules` requires the map, so a caller cannot leave
it out by accident. The demo's Street fund is now over by $21,908.68 on every reseed (the
rounding change moves the uneven splits too, not just the 50/50 one).

---

## ADR-0039: Employees come from the ERP's payroll by name and code; the sync refreshes what the ERP owns and leaves the budget's plans alone
**Date:** 2026-09-27 · **Status:** Accepted

**Context.** Typing a roster of positions every year is the work personnel budgeting was supposed to
remove. The ERP's payroll already knows who is employed, where, at what pay, in which retirement
system and plans, and charged to which funds. Its export layout is not known yet, so the contract has
to be a reasonable guess that a real adapter can translate into, and the sync has to work from a file
as well as an API.

**Decision.**
- **A contract, adapters, a pure matcher, and a preview-then-apply service**, the same shape as the
  chart and actuals syncs. `ErpEmployees` is the contract. `ErpEmployeeFileSource` (a CSV or XLSX
  with one row per employee) and `IErpEmployeesApi` are its adapters. `EmployeeMatcher` is the pure
  match, and `PersonnelSyncService` previews and applies.
- **Plans are matched by name, departments and funds by code.** The contract carries retirement
  systems and insurance plans by the names the year's personnel settings give them, so the only code
  that knows an ERP's own codes ("OPF-P") is its adapter.
- **What the ERP owns versus what the budget owns.**
  - The ERP owns name, title, hire date, pay (rate and hours, or grade and step), retirement and
    pick-up, insurance, and the fund split.
  - The budget owns the planned raise or step increase, the months paid, longevity, other pay, and
    the base pay account.
  - A sync refreshes the ERP's side and keeps the budget's side.
- **Positions keep the ERP's employee number** (`Position.EmployeeId`), so next year's sync finds
  them again.
  - A new employee fills a vacant position with the same title in their department. If there is
    none, they get a new position that copies the budget's choices from a colleague with that title.
  - Someone no longer on the payroll leaves their position vacant rather than removed.
  - A transfer vacates the old position and places the employee in the new department.
  - Positions entered here without a number are never touched.
- **One unmatched employee refuses the whole sync**, the same rule as the actuals sync. The preview
  lists every problem, so they can be fixed in the ERP or in the settings and read again.
- **The preview runs the real apply** on the loaded budget and throws the context away, so its line
  figures are exactly what a commit writes.
- **Three personnel reports:**
  - a position roster, which department users get for their own departments;
  - personnel cost by fund;
  - a benefits summary.

  The last two are whole-government and not shown to department users. All three add up the same
  cost pieces as the lines, split by kind (`PositionCost.ByFundAndKind`).

**Alternatives.**
- Matching by name alone: two employees can share a name, and a name changes on marriage.
- Replacing the budget's positions with the payroll wholesale: it would lose every planned raise,
  vacancy, and longevity choice.
- Skipping employees that do not match: every personnel line would be quietly understated.

**Consequences.** The assumed file layout is written down on the page and in the reader's comment.
When the real VIP export is known, a VIP adapter translates it into `ErpEmployees` and nothing
above it changes.

---

## ADR-0040: Email through a transactional outbox; two-step sign-in with authenticator apps; governments provisioned from the command line
**Date:** 2026-09-27 · **Status:** Accepted

**Context.** A buyer asks three things before anything about budgets.
- **Does it email people?** For example: a department submitted, a request was returned, or
  someone forgot their password.
- **Can we require MFA?**
- **How does a new customer get set up without a developer?**

Those constraints mattered for the answers:
- There is no mail provider yet; spencersmith.site's Formspree only emails its owner.
- Every demo address is fictional.
- The free demo database must be able to sleep (ADR-0031).
- Microsoft sign-in (Entra ID) is out for now.

**Decision.**
- **A transactional outbox.** Every email is an `OutboxEmail` row written in the same save as the
  change that caused it. A submission and its notice are saved together or not at all, and a mail
  server that is down delays a notice rather than failing the submission. The rows double as the
  record of what was sent to whom.
- **Two delivery modes** (`EmailOptions.Mode`). "Outbox", the default and the demo, keeps every
  email for an Administrator to read on the Email outbox page, where its links work. "Smtp" delivers
  through MailKit (any provider that speaks SMTP), with the password in the secret store.
- **No polling.** `EmailDeliveryService` sleeps until a save calls `IEmailOutbox.Notify`, then sends
  everything pending. A refusal is retried after a pause that grows with each attempt, and marked
  failed after five. A timer checking the outbox would keep the serverless database awake.
- **No password is ever emailed.**
  - A forgotten password gets a single-use reset link: Identity's token, tied to the security
    stamp, valid for a day. The page answers the same way whether the address has an account.
  - A new user is emailed a link to choose their own password, instead of an administrator typing
    a temporary one (still possible, for someone without email).
- **Two-step sign-in with authenticator apps** (TOTP, Identity's built-in provider).
  - Users set it up under Your account, from a QR code drawn as SVG on the server (QRCoder), so
    the sign-in pages carry no third-party script.
  - They get ten single-use recovery codes and can choose to remember a browser for 14 days.
  - An Administrator can require it for the whole government: everyone without it is signed out,
    and a claim sends them to set it up at the next sign-in (`RequireMfaMiddleware`, the same shape
    as the temporary-password rule).
  - An Administrator can also reset it for a user who lost their phone.
  - Every change is an audit event.
- **Provisioning is the vendor's operation.** `--provision` (like `--reseed`) creates a government
  and its first Administrator, emails that person a link to choose a password, and prints the link
  when there is no mail server. No page a government's users can reach creates governments.
- **The getting-started checklist** (`SetupChecklistService`) tells the new Administrator what
  remains:
  - the chart (from the ERP or its file);
  - a fiscal year;
  - the first budget;
  - their team;
  - then the optional pieces.

  Each step is worked out from what exists, so it is done the moment the thing exists.
- **Forwarded headers.** Links and redirects use the scheme the load balancer saw
  (`X-Forwarded-Proto`), and `App:PublicUrl` can fix the address outright.
  *Amended 2026-09-28 (ADR-0041):* the app also takes the client address from `X-Forwarded-For`
  (only the one the load balancer added), for the security log and the rate limits.

**Alternatives.**
- Sending mail inline in the request: a slow or down server fails the user's action.
- A hosted email API client: it ties the product to one provider, when SMTP works with all of them.
- Formspree: it can only email its owner.
- SMS codes: they need a paid provider, and SIM swapping makes them the weaker second factor.
- A web sign-up page for new governments: anyone could create tenants on a public demo.

**Consequences.** The outbox holds sign-in links, so only Administrators read it. A deployment with
SMTP configured delivers pending mail on the next save after a restart, not at startup, which keeps
startup free of database calls. `IAppLinks` gives services an absolute address; outside a page the
command line needs `App:PublicUrl`.

---

## ADR-0041: SOC 2 by design: a security log apart from the audit trail, sessions that end, a government's data found from the model, and scanning in CI
**Date:** 2026-09-28 · **Status:** Accepted

**Context.** A government or an ERP vendor buying CivicBudget will ask for a SOC 2 report, and a
state buyer may ask about GovRAMP. I decided not to pay for an audit yet, but
to build what an auditor would examine, and to say plainly "designed and built to achieve SOC 2
compliance". Most of an audit tests five things:
- who can get in;
- what they did;
- how changes reach production;
- how data is kept and disposed of;
- how problems are found and handled.

Several pieces were missing:
- sign-ins were not recorded;
- a session lasted 8 hours whatever the person did;
- there were no rate limits, security headers, or code scanning;
- a government could not take its data with it, and leaving had no procedure.

**Decision.**
- **A security log, separate from the audit trail.** `SecurityEvent` rows record every sign-in
  outcome, sign-out, idle sign-out, password change, export, and refused request, with the client
  address.
  - The audit trail answers "what changed in the budget"; the security log answers "who got in,
    who tried to, and what left the building". Mixing them would bury one in the other.
  - The log is written by overriding Identity's `SignInManager` (`AuditingSignInManager`), so a
    sign-in path added later is logged without anyone remembering to.
  - Every export is logged by one endpoint filter on the export group.
  - Events for an unknown address belong to no government and are not tenant-filtered; the
    Administrators' page scopes by government explicitly, like the Identity services.
  - Writing a row never fails the thing it records.
- **Sessions that end.** 30 idle minutes, or 14 days for "Remember me" (`SessionPolicy`).
  - An open Blazor page talks over its circuit rather than making requests, so the cookie's
    sliding expiry alone would sign out someone typing into the worksheet. `js/session.js` keeps
    an idle clock in the browser, shared across tabs through localStorage. It renews the cookie
    while someone works, warns two minutes before the end, and signs out at the limit.
  - The security stamp is re-checked every 5 minutes by the cookie and by each open page, so a
    deactivated user loses access that quickly.
- **Rate limits** (ASP.NET Core's built-in limiter, no package), partitioned by client address:
  - sign-in forms: 20 per 5 minutes;
  - password reset: 5 per 15 minutes;
  - exports: 60 per minute per user.

  Everything else is unlimited. `RateLimits.PolicyFor` is a pure function, so the rules are
  tested without a server. The real client address comes from `X-Forwarded-For`, taking only the
  address the load balancer itself added.
- **Security headers on every response** (`SecurityHeadersMiddleware`): a Content-Security-Policy
  that allows scripts from this site only, plus a per-request nonce for Blazor's one inline import
  map. Also no framing, nosniff, a strict referrer policy, and a permissions policy. The inline
  `window.civicBudget` helpers moved to `js/civicbudget.js` so no other inline script is needed.
  Blazor's own `frame-ancestors` header is switched off in favor of the full policy.
- **A government's data is found from the EF model** (`GovernmentDataStore`), not from a list.
  Its scope:
  - every table with a `GovernmentId` column;
  - the Governments row;
  - tables that reach one of those through a foreign key (a user's roles and departments).

  Two jobs use it:
  - **"Download everything"**: one CSV per table in a ZIP. It withholds password hashes, stamps,
    and two-step keys, and guards text against spreadsheet formulas.
  - **`--offboard`**: after writing that export to a file, it deletes every row in one transaction,
    ordered so each table goes after everything that points at it.

  A test fails if a new table is reached from no government, so the export and the removal
  cannot silently miss it.
- **Retention by a scheduled command** (`--maintenance`): security events and finished emails are
  removed after a year. A timer inside the app would keep the serverless demo database awake.
- **Scanning in CI.**
  - NuGet audit on every restore (all packages, any severity, an error because warnings are).
  - A `security.yml` workflow with CodeQL (`security-extended`, C# and JavaScript) and the
    package check, on every change and weekly.
  - Dependabot for npm too.
- **Policies and a control matrix** in `docs/security`, written for the operator: each SOC 2
  criterion, what meets it, where it lives, the evidence, and the NIST CSF 2.0 and 800-53
  (GovRAMP) references.

**Alternatives.**
- One log for everything: auditors and administrators ask different questions of each.
- The session cookie alone: it cannot see activity on an open Blazor page, so it either signs out
  active people or never signs out idle ones.
- A web application firewall for rate limits: it costs money on both clouds, and the app's own
  limiter goes wherever the app goes.
- A hand-kept list of tables for export and removal: a table added later would be missed. The
  model, and a test, are harder to forget.
- A tenant `IsDeleted` flag instead of deletion: it keeps data the customer asked to remove.
- Paying for an audit now: there is no operator and no customer yet.

**Consequences.**
- The CSP allows inline styles (`style-src 'unsafe-inline'`), because the report bars and a few
  layouts set widths inline. Scripts, where the risk is, stay strict.
- The security log survives `--offboard` for the rest of its year, as the operator's evidence.
- Backups still hold a removed government's data until they age out, and the retention policy
  says so.
- Two gaps are named in `docs/security/README.md`: breached-password checks and single sign-on.

---

## ADR-0042: The product site is one static page on my existing site, with clips recorded from the app
**Date:** 2026-09-28 · **Status:** Accepted

**Context.** An ERP vendor or a government deciding whether to look further needs one page that
says what CivicBudget is, shows it working, and offers the demo and a way to get in touch. I already
run spencersmith.site (Next.js on Vercel), which hosts Council's static site and has a contact form
through Formspree.

**Decision.**
- **One static page** at spencersmith.site/CivicBudget, in that repository's `public/CivicBudget`,
  following the Council pattern: plain HTML and CSS, a little JavaScript, no build step. The
  security story and the demo logins are sections of the page, not pages of their own.
- **The app's own design tokens**, so the site and the product look like one thing:
  - the sidebar's navy;
  - the logo's teal;
  - the over-limit red, used once, on the thing Ohio law cares about.

  Public Sans (the U.S. government's typeface) and IBM Plex Mono are **self-hosted**: a page
  selling security should not send its visitors to a font host.
- **Clips, not GIFs.** Three silent H.264 clips (200 to 800 KB each), recorded by Playwright from a
  freshly seeded app (`scripts/screenshots/site-clips.mjs`):
  - a department moving a line and submitting;
  - the fiscal officer bringing the Street fund within its limit;
  - the portal on a phone.

  They play only on screen, never under reduced motion, and each has a pause button (WCAG 2.2.2)
  and a caption.
- **The contact form** posts to the site's Formspree endpoint with a hidden subject of
  "CivicBudget inquiry", and works without JavaScript.
- **The address keeps its capitals** (`/CivicBudget/`), and any other spelling is rewritten onto
  it, because a redirect loops (Next matches redirect sources without regard to case).

**Alternatives.**
- A page inside the app: it would share the demo's cold start, and the demo resets nightly.
- A separate domain: another thing to pay for and renew, when spencersmith.site already exists.
- Separate security and demo pages: the page is short enough that one scroll answers everything.
- Animated GIFs: ten times the size and worse to look at.
- Google Fonts by link: a third-party request on every visit.

**Consequences.**
- The screenshots and clips go stale when the UI changes; the script regenerates them.
- The hero's fund panel repeats its screenshot's figures by hand, and the site's doc says to
  update both together.
- The site lives in another repository, so it merges and deploys separately.

---

## ADR-0043: Accessibility by self-scan: axe on every page, reviews of behavior, fixes in the shared components, and a conformance report
**Date:** 2026-09-28 · **Status:** Accepted

**Context.** Public agencies buy software that meets WCAG 2.1 or 2.2 AA (Section 508 for federal
money, and more and more state and local policy), and procurement asks for a VPAT. I decided not to
pay for an audit yet, and to do my own:
- an automated scan of every page;
- reviews of what a scan cannot see;
- fixes;
- an honest conformance report.

**Decision.**
- **axe-core on every page** each role can reach, the sign-in pages, and every portal page at
  desktop and phone width (`scripts/screenshots/a11y-sweep.mjs`, 237 page loads), grouped by rule
  so each fix is one change.
- **Two reviews of behavior**, one for the portal and sign-in pages and one for the admin
  application. They were done by subagents driving the pages by keyboard in Playwright: focus,
  dialogs, live regions, errors, zoom, reflow. Their findings were fixed, then checked again with a
  keyboard script.
- **Fixes go into the shared pieces**, so pages written later get them for free:
  - `ResultAlert` lists every error of a failed result as an alert and takes focus. It is on every
    admin form.
  - `FormErrors` does the same for the static sign-in pages, focused by `password-toggle.js` on load.
  - `Aria.Bool` writes ARIA states as `"true"`/`"false"`, because Blazor renders a C# bool as an
    empty or missing attribute.
  - `civicBudget.openModal` / `closeModal` remember a dropdown's toggle (or the last focused
    element) as the opener, fall back to the page heading, and can move focus in (the phone menu).
  - Failure and warning toasts stay until dismissed.
  - Page `h1`s hold the title alone.
  - Skip links are in all three layouts.
  - The color tokens pass 4.5:1 on every tint; form fields get a 3:1 border.
- **The report** (`docs/accessibility/ACR.md`) follows the VPAT 2.5 WCAG edition. It rates every
  WCAG 2.2 A and AA criterion, says plainly that no screen reader was used, and lists the known
  partial passes.

**Alternatives.**
- A paid audit now: there is no customer yet, and a self-scan finds most of what an audit would.
- axe alone: it passed pages whose dialogs lost focus and whose errors were silent; behavior needs
  a person, or a person's checklist, pressing keys.
- Fixing each page separately: the same defects would come back on the next page.
- Bootstrap's tooltip and modal JavaScript: the app's own small helpers already handle focus, and
  the portal has no script at all.

**Consequences.**
- Portal panels now use CSS `:has()`, so browsers older than 2023 show the first panel only.
- Editable amount cells keep their borderless look at rest, a known partial pass on 1.4.11.
  *Amended 2026-09-28 (Phase 32):* I chose to outline them at rest, and the worksheet's
  group rows became row-group headers, closing both partial passes.
- Any new page should pass the sweep before it merges.

## ADR-0044: A multi-year plan on each budget version: percentages per year, typed years, balances rolled forward
**Date:** 2026-09-28 · **Status:** Accepted

**Context.** An Ohio council adopts one year's appropriations, but councils and auditors ask where
the funds are heading: will the Street fund run dry in three years, can the village afford the
Maple Street project in 2029? Finance officers answer that today with a spreadsheet beside the
budget. I wanted the plan in the budget itself: up to ten years (five by default) on both the
revenue and expenditure side, each year worked out from the year before the way starting a new
budget works from last year's amounts.

**Decision.**
- **The plan belongs to a budget version.** `BudgetVersion` holds `PlanYears` (1 to 10, counting
  the budget year, so the default 5 is FY2027 to FY2031), `PlanInWholeDollars`, and one
  `PlanAssumption` per future year with a revenue and an expenditure percentage. Revenue and
  transfers in follow the revenue percentage; expenditures and transfers out follow the other.
- **A typed year replaces the calculation.** `PlannedAmount` is a child of `BudgetLine` keyed by
  year offset; the years after it follow from it. Clearing it goes back to the calculation. Only
  future years can be typed; the budget year is the line's amount, typed in the worksheet.
- **The arithmetic is pure.** `MultiYearPlanCalculator.Project` takes the lines, the rates, and the
  beginning balances and returns every line's amounts and every fund's `FundBalanceSummary` per
  year, rolling each ending balance into the next year's beginning balance. It reuses
  `FundBalanceCalculator`, so each future year gets the same over-limit check as the budget year.
  Nothing projected is stored; the page, the export, and the snapshot all call the calculator.
- **Who may change what.** The percentages and length follow the fiscal authority rule and lock at
  adoption, like the rest of the budget. A line's future years follow the same rule as its budget
  year (`BudgetLinePermissions.CanEdit` with the department's submitted flag), so a department head
  plans their own lines while their request is open and sees no fund totals.
- **It carries forward.** An amendment copies the plan as it stands. Starting next year's budget
  moves it on a year: old year 2 becomes year 1, typed amounts shift down, the old year 1's typed
  amounts drop (that year is now the budget), and the new last year repeats the old last year's
  percentages.
- **Published with the budget.** `PublishedBudgetSnapshot.Capture` stores one
  `PublishedBudgetSnapshotPlanYear` row per fund per year, with the year's percentages, so the
  portal's Outlook page reads frozen numbers like every other portal page.

**Alternatives.**
- A separate "plan" record beside the budget: two things to keep in step, and no answer to which
  plan went with which amendment.
- One growth rate for everything: the user asked for revenue and expenditure separately, and a
  single rate hides the structural gap the plan exists to show.
- Storing every projected amount: thousands of rows that go stale the moment a budget-year amount
  changes. Storing only the percentages and the typed years keeps the plan consistent by
  construction.
- Rates per account or per fund: more control, and much more to explain. Typed years cover the
  exceptions (a project, a grant that ends).

**Consequences.**
- A version created before this change gets a five-year plan with no change from year to year until
  someone sets the percentages. The seeded FY2025 budget is set to one year to show a budget
  published without a plan.
- The portal gains an Outlook page, and the portal context maps one more snapshot table.
- Personnel lines are projected by percentage like any other line; positions are not repriced for
  future years.

## ADR-0045: An ERP partner kit with a published API, one HTTP adapter, and connections from the operator's secret store
**Date:** 2026-09-28 · **Status:** Accepted

**Context.** CivicBudget exchanges four things with a government's ERP: the chart of accounts,
actuals, the payroll roster, and the budget journal (ADR-0025, 0034, 0035, 0039). Each already had
a contract in Application and a file form, and three had a simulated API for the demo. What was
missing is what an ERP vendor would need to connect a real system: a written API to build to, and a
client on CivicBudget's side that works with any ERP that builds it. Without that, each ERP means
new code inside CivicBudget.

**Decision.**
- **One published API, version 1** (`docs/partners/openapi.json`): four endpoints under
  `/v1/entities/{entityId}`, JSON with camelCase names and enumerations by name, errors in the
  problem format (RFC 9457), a Bearer key per government. The chart gained an API form
  (`IErpChartApi`) so all four exchanges have one.
- **The wire shapes are their own records** (`ApiChart`, `ApiActuals`, ... in
  `Infrastructure/Erp/Http/ErpApiContract.cs`), translated to and from the Application contracts.
  The Application contracts can change with CivicBudget; the wire shapes are a promise to vendors
  and change only by adding a version.
- **Strict about what is missing, relaxed about what is extra.** A required field left out, a null
  where none is allowed, or an enumeration sent as a number is refused
  (`RespectRequiredConstructorParameters`, `RespectNullableAnnotations`), because a budget built
  on a silently zeroed amount is worse than an error. Unknown fields are ignored, so an ERP can add
  to its answers.
- **One adapter for every ERP** (`HttpErpAdapter`) implements all four contracts. Reads turn every
  failure into a sentence for the Fiscal Officer. The journal post refuses only when the ERP clearly
  said no (a 422 with the refused accounts, or a 400, 401, 403, or 404), and throws when the answer
  is lost or the ERP fails on its side, so the send stays open and is retried under the same id,
  which the ERP must recognize (the `Idempotency-Key` header repeats it).
- **Connections come from configuration, keyed by government id** (`Erp:Connections:{id}`: base
  address, key, and the ERP's own entity id), which in the cloud means the platform's secret store.
  Never the database or a page: an ERP key opens a government's books and payroll, and keeping it
  out of the database keeps it out of backups, exports, and the reach of a SQL injection. Keyed by
  id because an Administrator can change a government's slug. Validated at startup: HTTPS only
  (plain HTTP to localhost for testing), a key present, the key a real government id.
- **One adapter serves many governments.** Each ERP contract gained `IsConnected(governmentId)`, and
  each service uses the adapter only for a connected government. A configured connection replaces
  the simulated ERP; without any, the simulated one stands in where the demo data is seeded
  (`AddErpConnections`, `AddSimulatedErp`).
- **A reference ERP** (`samples/CivicBudget.ReferenceErp`) implements the API over the demo data,
  borrowing the simulated ERP's behavior. Vendors read it; the tests run the adapter against it over
  real HTTP.
- **The document is tested against the code.** `ErpApiContractTests` compares every schema's
  properties, required fields, and nullability with its record, every enumeration with the C# enum,
  and reads every sample (JSON and CSV) through the real code, refusing sample fields the API does
  not define.

**Alternatives.**
- **Generate the OpenAPI file from code** (Microsoft.AspNetCore.OpenApi on the reference ERP): it
  would need a package and annotations for every rule, and the descriptions (the part vendors
  actually read) would live in attributes. A hand-written document held to the code by a test gives
  the same guarantee and reads better.
- **Serialize the Application contracts directly:** less code, but renaming a C# property would
  silently change the public API.
- **Connections on a settings page, encrypted in the database:** self-service, but it puts a key
  to the government's books in the database, its backups, and its exports, and adds rotation and a
  page to secure. I chose operator configuration (2026-09-28), which matches how governments
  are provisioned.
- **A per-vendor adapter** for each ERP: the old path; it scales with vendors instead of staying
  at one.
- **Webhooks from the ERP:** nothing in the budget cycle needs a push, and every exchange already
  has a preview the Fiscal Officer confirms.

**Consequences.**
- An ERP connects by building four endpoints, with no release of CivicBudget.
- The demo now fetches the chart too (the simulated ERP holds the seeded chart, so a fetch finds
  nothing to change until someone edits the chart).
- The solution has a `samples/` folder; the Docker image leaves it out.
- A second API version would be a second set of wire records and a second base path, with the
  adapter choosing by configuration.

## ADR-0046: The budget book is assembled from the existing reports, printed with MigraDoc, and frozen at publish
**Date:** 2026-09-29 · **Status:** Accepted

**Context.** Councils, auditors, and residents still expect a budget book: one document with a
letter from the mayor or fiscal officer, a summary, every fund and department, and the certificate
of estimated resources. Fiscal officers assemble it by hand from spreadsheets and word processors,
and it drifts from the numbers the moment one of them changes. CivicBudget had every figure on a
screen or in a report; what was missing was the letter and one document to hold it all.

**Decision.**
- **The message is part of the budget version** (`MessageHeading`, `MessageBody`, `MessageSignedBy`,
  `MessageSignerTitle` on `BudgetVersion`, through `SetMessage`). It follows the version's rules:
  written until adoption, copied into an amendment and into next year's budget as a draft. Plain
  text with blank lines between paragraphs, so it reads the same in the book and on a screen.
- **The book is assembled from the reports that already exist.** `BudgetBookService` calls the
  fund summary, category, department detail, certificate, plan, and personnel cost services and
  the workspace lines, and the pure `BudgetBookBuilder` decides what each page says. The book
  cannot disagree with the screens, and each report's own permission check applies to it.
- **One book for the whole government.** Department users get none, like the other whole-fund
  reports: a book of part of the budget would read as the whole.
- **Any version prints; a draft says so.** A budget council has not adopted prints "Proposed" on the
  cover and at the top of every page, so a work-session copy cannot pass for the adopted budget.
- **Four optional sections** (multi-year outlook, personnel, every account line, glossary) are
  chosen when printing. The government's defaults (`BudgetBookSettings`) pre-fill the choices and
  decide the published book.
- **Frozen at publish.** Publishing prints the book and stores it beside the snapshot
  (`PublishedBudgetBook`), in its own table so the portal's pages never load a PDF. The portal
  serves that copy, cached with the pages and evicted on the next publish. Budgets published
  without a book (the seeded demo, or before this change) get one from `PublishedBookBackfill`
  after seeding, acting for one government at a time so the tenant filter stays on.
- **MigraDoc, as for the certificate** (ADR-0036). The certificate's landscape pages go inside the
  portrait book unchanged, with the book's footer; the contents page gets real page numbers from
  bookmarks, and every heading is a PDF outline entry.

**Alternatives.**
- **Generate the book from the snapshot alone:** the snapshot has no certificate, personnel, or
  prior-year figures, and would need to grow to hold them. Printing at publish from the live
  reports, then freezing the PDF, gives the same guarantee.
- **HTML to PDF with a headless browser:** better typography for less code, but a browser in the
  container, a much larger image, and a process to supervise. MigraDoc is already here.
- **A rich-text message editor:** formatting that the PDF and the portal would each have to
  interpret; plain paragraphs are what budget messages are.
- **Render the book on every portal download:** a resident could see a book that differs from what
  was published if the budget or its message changed afterwards.

**Consequences.**
- The book is not a tagged PDF (the library cannot tag it); the portal is the accessible form of the
  same figures, and the book's contents page says so. Recorded in the conformance report.
- Publishing takes a little longer, because it prints the book first.
- A new report section is a builder change and a renderer method; the report services stay as
  they are.

## ADR-0047: An assistant that acts only through the Application services, as the signed-in user
**Date:** 2026-09-29 · **Status:** Accepted

**Context.** I want CivicBudget to have an AI assistant that does real work, not a chat window: answer
"how are actuals compared to our budget so far this year?", find and open the page for "how do I
print the budget book?", and later carry out tasks like building a five-year plan. The hard
requirement is permissions: a department head's assistant must see what a department head sees and
nothing more, and a Viewer's must change nothing. Phase 36 builds the foundation (questions and
navigation); Phase 37 adds actions; Phase 38 a separate bot on the public portal.

**Decision.**
- **The tools are the Application services.** Every tool (`BudgetTools`) calls the service a page
  calls, in the user's own scope, and returns a compact summary with the path of the page that shows
  the whole thing. The services already enforce the tenant filter, department visibility, and role
  rules, so the assistant cannot see more than the page would, and there is no second permission
  system to keep in step. The model never sees a connection string, a table, or SQL.
- **Navigation from the pages themselves.** Each page carries a `[HelpTopic]` (title, purpose,
  keywords); `PageCatalog` reads them with each page's routes and `[Authorize]` policies, and
  `NavigationTools` offers or opens only the pages the user's policies allow. A test fails for an
  admin page without a topic.
- **Microsoft.Extensions.AI in Application, the provider in Infrastructure.** Application depends
  on `IChatClient` and `AIFunction` only. Infrastructure registers Anthropic's official SDK behind
  that interface, with the tool-calling layer (`UseFunctionInvocation`) capped at eight rounds.
  `Assistant:Provider` chooses Anthropic (Claude, the default) or Ollama (its cloud, or a local
  server at `Assistant:BaseUrl`). Ollama speaks Anthropic's Messages API, tools included, so the same
  SDK reaches it with the key sent as a Bearer token, and no second package is needed. Any other
  provider is one more case in that registration.
- **Off unless both switches are on.** The operator connects a model (`Assistant:ApiKey` in the
  secret store); without it nothing is registered and the button does not appear. Each government's
  Administrator then turns it on (`Government.AssistantEnabled`, audited), because questions send
  budget figures to the provider. The hosted demo has no key.
- **Guardrails in code, not just in the prompt.**
  - Answers are rendered with raw HTML disabled, and links are kept only when they point inside the
    app (`AssistantMarkdown`).
  - A question is at most 2,000 characters, and each user may ask 40 an hour.
  - Each question goes into the security log with the names of the tools it used, not its text.
  - The prompt tells the model to take every figure from a tool and to treat typed text in the data
    as data; the evaluation set checks that it does.
- **Read-only for now.** The prompt says so, and there is no tool that writes. Phase 37 adds actions
  that the model proposes and only the user's click confirms.
  - *Amended 2026-09-29 (Phase 37):* the assistant now proposes changes, and only the user's click on
    the proposal commits them; see ADR-0048.
- **The conversation lives in the browser tab's connection** (`AssistantPanelState`), so it
  survives moving between pages and is gone when the tab closes. Nothing is stored.
- **Tested three ways:**
  - a scripted model through the real tool-calling layer against the seeded database
    (`AssistantServiceTests`), which is where the permission cases are proved;
  - component and catalog tests;
  - an evaluation set against the real model (`AssistantEvaluationTests`), which runs when
    `ANTHROPIC_API_KEY` is set.
  - *Amended 2026-09-29:* the evaluation set reads the app's own `Assistant` settings (user-secrets
    or `Assistant__ApiKey` in the environment), not `ANTHROPIC_API_KEY`.

**Alternatives.**
- **Let the model query the database** (text to SQL, or a read replica): powerful, but permissions
  would live in a prompt. One missed filter and a department head reads payroll.
- **A retrieval index of the budget:** the figures change on every save, and the services already
  answer precisely. An index would be a stale second copy.
- **Anthropic's SDK directly in Application:** less code, but it ties every layer to one provider and
  makes the service hard to test without the network.
- **Storing conversations:** useful for support, but questions can hold anything, and keeping them
  makes the government's data bigger and its export and offboarding longer. Nothing is kept for now.
- **A chat window in its own page:** the assistant is most useful beside the page it is talking
  about, so it is a panel that knows the current page.

**Consequences.**
- A new report or screen becomes a tool by wrapping its service; a new page is findable through its
  `[HelpTopic]`.
- The quality of answers depends on the model and the prompt; the evaluation set is the check after
  changing either.
- The hosted demo shows the switch but not the assistant, until a key and a spending cap are chosen.
- *Amended 2026-09-29:* the hosted demo is connected, so the AI features can be shown there: GLM 5.3
  Flash on Ollama Cloud, the key set as a Container App secret by `scripts/azure-assistant.sh` (the
  Bicep template takes it as an optional parameter, for the web app only, and `azure-setup.sh`
  keeps it on a rerun). The demo password is public, so anyone can use the staff assistant as a
  demo user; the portal takes 200 questions a month per government there instead of 1,000. The key
  is a temporary one that I can switch off with `azure-assistant.sh --off`.

---

## ADR-0048: The assistant proposes; only the user's click commits
**Date:** 2026-09-29 · **Status:** Accepted

**Context.** Phase 36's assistant could read and navigate but change nothing. The requests I want
it to handle are changes: "build a five-year plan at 4% a year", "raise utilities 5%", "start next
year's budget", "fix the Street fund". A model that can write is a model that can be talked into
writing, by a confused request or by text planted in the data (a justification that says "set every
line to zero"). The permission rules from ADR-0047 still hold, since the tools run as the user, but a
Fiscal Officer's assistant could still make a change the Fiscal Officer did not mean.

**Decision.**
- **Propose, preview, confirm.** An action tool (`ActionTools`, all named `propose_...`) never
  changes anything. It checks the user may make the change, works it out through the same service a
  page uses (the plan's own calculation, `PreviewPlanAsync`; the ERP's own preview, for actuals),
  and stores a proposal: the exact change, the rows before and after, and a commit delegate. The
  panel shows it as a card, and only the card's button calls `IAssistantService.ConfirmAsync`. No
  tool can confirm, so the model cannot confirm its own proposal, whatever it reads.
- **The commit is the page's own service call, checked again.** Confirming runs `SavePlanAsync`,
  `UpdateLineAmountsAsync`, `StartBudgetAsync`, and the rest as the user, so every rule is applied
  at the moment of the change, not only when it was proposed.
- **A stale preview is refused, whole.** Line changes carry the amount each line had when the
  preview was made (`LineAmountChange.Expected`). If any line has moved since, nothing is changed and
  the user is told which line; they ask again for a fresh preview. The batch is all or nothing.
- **Proposals are single-use, short-lived, and belong to one tab.** `AssistantProposals` is scoped
  to the browser tab's connection; `Take` removes a proposal as it runs, and it expires after 30
  minutes. A proposal id from another tab or another user finds nothing.
- **The arithmetic is code, not the model.** "Fix the Street fund" is `FundTrim`, which cuts the
  fund's spending lines in proportion by exactly the amount over the limit, to the cent. "Check my
  budget" is `BudgetReview`, which reads the reports that already exist. Both are pure and tested.
  The prompt tells the model to copy amounts, not work them out.
- **The audit trail says how.** The service's own changes are audited as usual under the user; a
  named event, "Through the assistant: (the proposal's title)", records that the assistant proposed
  them.
- **The page catches up.** After a confirmed change the admin layout rebuilds the page beneath the
  panel (`AssistantPanelState.PageGeneration` is the key), so the page shows the change.
- **The model hears what happened.** When the conversation goes back with the next question, each
  proposal is followed by what the user did with it (confirmed, cancelled, failed, still waiting), so
  "now do the same for Police" starts from the truth.

**Alternatives.**
- **Let the model act, and offer undo.** Faster for the user, but undo is not free in a budget: a
  change can be published or sent to the ERP before anyone looks, and a reverted change still went
  through the audit trail as a change.
- **Ask "are you sure?" in the chat, and act on "yes".** The model reads the "yes" too, and so can
  a planted instruction. A button outside the model's reach is the only confirmation it cannot
  forge.
- **Hold the proposal in the model's context and re-send it on confirm.** The model could alter it
  between the preview and the commit. The store keeps the change the user saw.
- **Store proposals in the database.** It would let them survive a reconnect, but a proposal is
  worked out from a budget that moves, and half an hour in memory is the honest lifetime.

**Consequences.**
- A new action is a `propose_` tool that previews through a service and passes a commit delegate;
  nothing in the panel or the service changes.
- A proposal lost to a closed tab is simply gone; the user asks again.
- Each action needs a preview the user can judge. Where a service has no preview (a message, a
  narrative), the proposal shows the text itself.

---

## ADR-0049: A question box on the portal that knows only the published budget
**Date:** 2026-09-29 · **Status:** Accepted

**Context.** The staff assistant (ADR-0047, ADR-0048) works inside the admin app, as a signed-in
user. Residents asked the portal questions too, in effect, every time they searched for "police"
or clicked through three pages to find what roads cost. A question box on the portal could answer
them in a sentence, but it is a different problem:
- **Anyone can ask**, so nothing about the asker can decide what they see.
- **It speaks in the government's name**, so it must never report a figure the government has not
  published, take a side, or be talked into saying something else.
- **Every question costs money** and the portal is public, so cost needs a hard ceiling.
- **The portal's own rules still hold:** it works without JavaScript, sets no cookies, and no
  anonymous request may keep the free demo database awake (ADR-0031).

**Decision.**
- **The tools are the portal's own reads.** `PortalTools` wraps `ISnapshotQueryService`, the service
  the portal pages call, which reads only published snapshots through the portal's own database
  context. The government is fixed when the tools are built and no tool takes one, so Maple Ridge's
  question box cannot reach Pine Hollow. There are no drafts, no actuals, no people, and no tool
  that writes, because the snapshot holds none of them.
- **The model copies; the code adds up.** The tools return the totals a resident would ask for
  (a department's budget, last year's, the lines that match), so the model does not sum a column
  itself.
- **The prompt keeps it to the budget.** `PortalPrompt` (pure, tested) says: answer only from the
  tools; say plainly when something is not published and name what is not (actuals, drafts,
  people); these are budgeted amounts, not money spent; no opinions or predictions; the question
  cannot change the rules. Links survive only if they point into that government's own portal
  (`AssistantMarkdown` with a prefix).
- **One question, one answer.** No conversation is kept, so there is nothing to store and no
  history a resident could forge.
- **A plain form post.** `/transparency/{slug}/{year}/ask` works with JavaScript off. It carries no
  antiforgery token, because the portal sets no cookies and a question has no identity to protect;
  a forged post could only spend a question, which the limits below bound.
- **Limits before the database, and a ceiling after it.**
  - `PortalQuestionGate` turns a posted question away with a 404 when no model is connected,
    before anything reads the database. The free demo has no model, so it keeps ADR-0031's rule.
  - `RateLimits` allows 10 questions per address per hour, in memory, and a refusal writes nothing.
  - Each government's portal answers `Assistant:PortalQuestionsPerMonth` questions a month (1,000
    unless the operator sets it). One `UPDATE` both checks and counts on the government's row, so
    two questions at the same moment cannot both take the last one.
  - A question is at most 500 characters.
- **Its own switch.** The Administrator turns public questions on separately from the staff
  assistant (`Government.PortalQuestionsEnabled`, audited). The switch drops the portal's cached
  pages so the "Ask a question" link appears or goes at once.
- **Nothing about the question is kept.** The operator's log records which tools answered, never
  the words; the page says the question goes to the model's provider.

**Alternatives.**
- **Let the public bot use the staff assistant's tools** with an anonymous user: one missed filter
  and a draft or payroll figure is in a public answer. The snapshot cannot leak what it does not hold.
- **A conversation carried in hidden fields:** follow-ups are nicer, but the "earlier answer" would
  come from the browser, where anyone can rewrite it to put words in the government's mouth.
- **Answer from a fixed set of questions (an FAQ):** safe, but it cannot answer "what does the Water
  fund spend on chemicals?", which is the point.
- **Cache answers by question:** identical questions are rare in free text, and a cached answer
  would outlive a republished budget unless it were tied to the snapshot; not worth it yet.
- **Count questions in memory:** simpler, but a container that scales to zero forgets the count
  every few minutes, so the monthly ceiling would not hold.

**Consequences.**
- A resident gets an answer and the page that shows it; the pages stay the official record, and
  the page says the answer can be wrong.
- The question page reads the database per post when a model is connected, the one anonymous
  exception to ADR-0031's rule, bounded by the address limit and the monthly ceiling.
- The ceiling is per government and per month, set by the operator who pays for the model.

---

## ADR-0050: The portal takes the product site's look: navy openings, white cards, icons as signposts
**Date:** 2026-10-01 · **Status:** Accepted

**Context.** The portal worked and read correctly, but every page was the same white column: a
small heading, a gray paragraph, then charts and tables of equal weight, and nothing that said where
to start. The product site (spencersmith.site/CivicBudget) had just been reworked to be skimmed:
navy bands where the app is navy, light sections, white cards, icons, short leads, and overlap only
where it means something. Residents skim a portal even more than buyers skim a product page.

**Decision.**
- **Every page opens on a navy band** (`PortalHero`) that continues the header: the breadcrumbs, an
  optional eyebrow (the fund type, the department's fund), the title, one short lead, and actions
  (the budget book, the download, the search box). One component, so every page opens the same way.
- **Drawn edge to edge without leaving the column.** The portal's `<main>` is a centered column, and
  a band inside it could not reach the window's edges. The band paints its sides with a spread
  `box-shadow` and clips that to its own height with `clip-path`, so it needs no change to the layout
  and adds no sideways scroll (which on a phone would make Safari zoom the page out).
- **Overlap in one place only.** The headline figures (revenues, expenditures, the projected ending
  balance) sit as cards over the band's bottom edge on the overview and on each fund. Nothing else
  overlaps or tilts; the product site showed that layering everything is noise.
- **White cards on a light page.** Each chart and table is a card (`.pt-card`), so a section reads as
  one thing. Table headings may wrap, so a wide table fits its card instead of scrolling for the sake
  of one heading.
- **Icons as signposts** (`PortalIcon`, inline SVG, always `aria-hidden`): on the headline cards, the
  overview's "Explore the budget" cards, and the adoption details. The text beside each icon says
  what it means.
- **A navy footer**, closing the page the way the header opens it. Always light: there is no dark
  scheme, as on the product site.
- **Still no JavaScript**, and the question box is offered on the overview only when the portal takes
  questions, which the layout already works out (`PortalAskable`, a cascading value) rather than each
  page asking again.

**Alternatives.**
- **Restructure the layout so each page owns full-width sections:** cleaner HTML, but every page
  would carry its own column wrapper, and a page that forgot one would break the layout.
- **A dark mode:** turned every section dark on a device set to dark mode on the product site, which
  buried the contrast between bands; not worth it for a public page.
- **Tilted, layered screenshots-style cards everywhere:** tried on the product site and pulled back.

**Consequences.**
- A new portal page starts with `<PortalHero>` and puts its sections in `.pt-card`.
- `.pt-h1`, `.pt-head`, `.pt-lead`, `.pt-book`, and `.pt-govlist` are gone.
- The README's portal screenshots and the product site's portal image show the new look.

---

## ADR-0051: The assistant is called Civic Buddy, and on the portal it floats in the corner of every page
**Date:** 2026-10-01 · **Status:** Accepted

**Context.** The staff assistant (ADR-0047) was a button labeled "Assistant", and the portal's
question box (ADR-0049) was "Ask a question", the last item in a crowded menu in the top right
corner. Residents had to find it, and nothing said the answers came from an AI. Both are the same
feature to the people using them, so they should share a name.

**Decision.**
- **One name, Civic Buddy, in everything people see:** the top bar's button, the panel's heading,
  the portal launcher and its page, the settings switches, the security log ("Asked Civic Buddy"),
  the audit trail ("Through Civic Buddy: ..."), toasts and messages. The code keeps its names
  (`AssistantService`, `PortalQuestionService`, `Assistant:*` settings), because they describe what
  the pieces do and renaming them would touch configuration on the live demo for nothing.
- **It says it is an AI, everywhere.** An "AI" badge beside the name, a mark built from sparkles
  (the sign people read as AI) on the CivicBudget tile (`BuddyMark`), and both prompts tell the
  model its name and to say it is an AI, not a person or an official, if asked.
- **Not a help desk.** No headset, no "How can we help?", no person's face. The launcher reads
  "Ask Civic Buddy / AI answers from this budget", and the panel leads with what it answers from.
- **On the portal it floats on every page** of a government that takes questions, bottom right,
  and the menu item is gone. The ask page still exists: it is where every answer is written.
- **Still no JavaScript.** The launcher is a `<details>` element: the summary is the button, it
  opens and closes on its own, and a screen reader hears whether it is open. The panel's form is a
  plain post to the ask page carrying `_handler` set to `PortalAsk.FormName`, so the ask page
  treats it as its own form. Each suggested question is its own small form for the same reason:
  links would be GETs that crawlers follow, and every question spends the monthly allowance.

**Alternatives.**
- **Answer inside the floating panel without leaving the page:** needs JavaScript or every portal
  page handling the post, and an answer page is easier to share and to read on a phone.
- **A dialog or a JavaScript chat widget:** the portal has no script by design (static SSR per
  ADR-0002, and ADR-0049's form that works without one).
- **Rename the code too:** churn in settings, secrets, and tests for a word only developers see.

**Consequences.**
- On phones the panel opens as a sheet above the launcher; the footer leaves room so the launcher
  never covers its links. The launcher is hidden when printing.
- Copy that names the assistant says "Civic Buddy"; comments and docs about the code may still
  say "the assistant".

---

## Packages

Every NuGet package and why it is here. A package is added to this table in the same change that
adds it.

| Package | Project | Why | ADR |
|---|---|---|---|
| Microsoft.EntityFrameworkCore.SqlServer | Infrastructure | The SQL Server provider | 0009 |
| Microsoft.EntityFrameworkCore.Design | Infrastructure (private) | `dotnet ef` tooling, kept in Infrastructure so no separate startup project is needed | |
| Microsoft.EntityFrameworkCore | Application | The LINQ surface behind `ICivicBudgetDbContext`, with no provider | 0014 |
| FluentValidation | Application | Request validation as testable classes | 0007 |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | Infrastructure | Identity's stores in the same DbContext | 0015 |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | Infrastructure | The Data Protection key ring in SQL Server, so cookies survive restarts | 0023 |
| Microsoft.AspNetCore.Components.QuickGrid | Web | Admin grids | 0011 |
| ClosedXML | Infrastructure | Reading and writing XLSX without Office or COM | 0021, 0022 |
| MailKit | Infrastructure | Sends the outbox's email over SMTP; Microsoft's own documentation points to it instead of the old `SmtpClient`; MIT licensed | 0040 |
| QRCoder | Web | Draws the QR code for setting up an authenticator app, as SVG on the server, so the sign-in pages stay free of third-party script; MIT licensed | 0040 |
| PDFsharp-MigraDoc | Infrastructure | The certificate's PDF: MigraDoc lays out pages and tables, PDFsharp writes the file; MIT licensed, cross-platform | 0036 |
| Microsoft.Extensions.AI.Abstractions | Application | The assistant's model interface (`IChatClient`) and tools (`AIFunction`): interfaces only, so Application names no provider; MIT | 0047 |
| Microsoft.Extensions.AI | Infrastructure | Runs the tool calls the model asks for (`UseFunctionInvocation`); MIT | 0047 |
| Anthropic | Infrastructure | The official Claude SDK, which implements `IChatClient`; the first model behind the assistant; MIT | 0047 |
| Markdig | Web | Turns the assistant's Markdown answers into HTML with raw HTML disabled; BSD-2-Clause | 0047 |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector | tests | Test framework and coverage (the template defaults) | |
| bunit | Web.Tests | Blazor component tests | |
| Testcontainers.MsSql | IntegrationTests | A real SQL Server 2022 in tests | 0009 |
| Microsoft.Extensions.TimeProvider.Testing | Web.Tests | `FakeTimeProvider`, so startup and quiet-spell tests control the clock | 0031 |
| Amazon.CDK.Lib, Constructs | Infra, Infra.Tests | AWS CDK in C#; the assertions library ships inside Amazon.CDK.Lib | 0008, 0023 |
| dotnet-ef (local tool, `.config/dotnet-tools.json`) | | The migrations command, pinned per repository | |

Not packages: the PDF typeface, Source Sans 3 (SIL Open Font License, license beside the files), is
embedded from `src/CivicBudget.Infrastructure/Reports/Fonts` (ADR-0036). Bootstrap 5.3 is vendored under `src/CivicBudget.Web/wwwroot/lib/bootstrap`
(ADR-0016), and Bootstrap Icons 1.13 under `wwwroot/lib/bootstrap-icons` (ADR-0020).
