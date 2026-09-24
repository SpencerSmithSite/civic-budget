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

**Context.** I have no AWS account or budget for this project, but the employer I am
interviewing with runs on AWS, so the deployment story matters.

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

**Alternatives.** PostgreSQL: native on arm64 and cheaper on RDS, but it changes the
story this project is meant to tell. Azure SQL Edge: retired.

**Consequences.** Slower container starts locally (15 to 40 seconds) and the occasional
emulation crash, which Compose now restarts automatically. CI on Ubuntu runs natively.
The EF provider is confined to Infrastructure, so a swap stays possible.

---

## ADR-0010: The repository is public and unlicensed (all rights reserved)
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** I want interviewers to be able to read the code without my granting anyone
the right to reuse it.

**Decision.** No `LICENSE` file; the README says "All rights reserved", portfolio review
only. Changes reach `main` through pull requests.

**Alternatives.** A private repository (invisible unless each reviewer is invited); a
restrictive license such as CC BY-NC-ND (still grants some rights).

**Consequences.** Anyone can read it, and GitHub cannot stop a fork of a public
repository, but no right to use it is granted.

---

## ADR-0011: QuickGrid for admin grids, no commercial component suite
**Date:** 2026-09-15 · **Status:** Accepted

**Context.** Government ERP vendors often use DevExpress or Telerik. I had no license or
trial, and a portfolio project should not depend on one.

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
**Date:** 2026-09-19 · **Status:** Accepted

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

**Alternatives.** Copying the logo into each snapshot (a logo change would need a republish);
serving it through the admin context (breaks the one-door rule for no gain); a JavaScript
carousel (breaks the portal's no-script promise); `<details>` or `:target` for the tabs (no
slide, and `:target` scrolls the page).

**Consequences.** One table, one migration, and one line in the test that lists what the portal
context maps.

---

## ADR-0030: The live demo runs on Azure's free tiers; the database is rebuilt from the seed every night
**Date:** 2026-09-20 · **Status:** Accepted

**Context.** I wanted the app hosted for free so hiring managers can sign in and use it. It
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
  not what was spent), and each fund begins with last year's projected ending balance. Only the
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
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector | tests | Test framework and coverage (the template defaults) | |
| bunit | Web.Tests | Blazor component tests | |
| Testcontainers.MsSql | IntegrationTests | A real SQL Server 2022 in tests | 0009 |
| Microsoft.Extensions.TimeProvider.Testing | Web.Tests | `FakeTimeProvider`, so startup and quiet-spell tests control the clock | 0031 |
| Amazon.CDK.Lib, Constructs | Infra, Infra.Tests | AWS CDK in C#; the assertions library ships inside Amazon.CDK.Lib | 0008, 0023 |
| dotnet-ef (local tool, `.config/dotnet-tools.json`) | | The migrations command, pinned per repository | |

Not packages: Bootstrap 5.3 is vendored under `src/CivicBudget.Web/wwwroot/lib/bootstrap`
(ADR-0016), and Bootstrap Icons 1.13 under `wwwroot/lib/bootstrap-icons` (ADR-0020).
