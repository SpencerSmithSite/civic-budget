# CLAUDE.md — CivicBudget

Portfolio project for a .NET / Blazor interview at a government ERP company.
Spencer will be asked to **explain this code**, so: clarity over cleverness,
idiomatic modern C#, small reviewable changes, and every non-obvious choice
written down.

## Read first
- `docs/SPEC.md` — what we're building (Ohio governmental fund accounting)
- `docs/ARCHITECTURE.md` — layers, render modes, tenancy, snapshot boundary
- `docs/DECISIONS.md` — ADRs; **add a row to the Packages table before adding any NuGet package**
- `ROADMAP.md` — phase checklists; update as work completes

## Working agreement
- **One phase at a time.** Finish the phase, run tests, update docs, then
  stop and report (what was built, how to run/test, 3–5 things to read,
  interviewer questions, next phase plan). Do not start the next phase
  without approval.
- **Ask Spencer** about Ohio municipal budgeting / fund accounting rather
  than guessing — he has hands-on experience.
- **All data is fictional.** Tenants: Village of Maple Ridge, OH and Pine
  Hollow Township, OH. Never real governments or client data.
- Tests pass before any commit. Every domain rule and every authorization
  rule gets a test.
- No secrets in the repo (user-secrets locally; Secrets Manager in AWS).
- Never commit license keys or private feed credentials.

## Stack
.NET 10 · Blazor Web App (admin = Interactive Server, portal = static SSR) ·
EF Core + SQL Server 2022 in Docker · ASP.NET Core Identity · QuickGrid ·
FluentValidation · ClosedXML · xUnit / bUnit / Testcontainers · GitHub
Actions · AWS CDK (C#) deploy-ready (no account — see ADR-0008).

## Gotchas learned
- NuGet/`dotnet` network calls hang if Little Snitch blocks `dotnet`; `curl` still works. Allow `dotnet` outbound.
- `dotnet run --no-build` after adding a migration runs stale code ("No migrations were found"). Build first.
- Generated migrations live under `Persistence/Migrations/` and are exempt from analyzers via `.editorconfig`.
- Unsandboxed shell is needed for `dotnet restore`, Docker, and Testcontainers.
- `dotnet run` right after `docker compose up` used to crash on the pre-login handshake; DatabaseInitializer now retries for about two minutes (24 attempts, 5 seconds apart), behind the waiting screen.
- Reseed after schema/seed changes: `docker compose down -v && docker compose up -d`, then run the app.
- SQL Server refuses multiple cascade paths; use `DeleteBehavior.Restrict` on the second path (see IdentityConfiguration).
- EF Core cannot `OrderBy` after projecting to a DTO with collection sub-queries; order the entity first.
- Entities set their own Guid v7 ids, so keys are `ValueGeneratedNever` (ADR-0018); otherwise EF tracks children discovered through an aggregate as Modified.
- QuickGrid's default theme wins on CSS specificity; pass `Theme="bootstrap"` and style headers in app.css for dense grids.
- SQL Server under Rosetta occasionally segfaults (container exit 139). Compose now has `restart: unless-stopped` so Docker restarts it within seconds; the volume survives. If the app already gave up (24 attempts), run it again.
- Interceptor order: `AuditInterceptor` before `TenantSaveChangesInterceptor` so audit rows are tenant-checked too.
- Output caching: Blazor SSR marks pages `no-store` and the default output cache policy honors it, so the portal policy is the base policy with `excludeDefaultPolicy: true`. Headers are read-only in `ServeResponseAsync`; rewrite them in `PortalResponseMiddleware` (`OnStarting`), which must sit before `UseOutputCache` or it never runs on a hit. `[OutputCache]` does nothing on Razor component endpoints.
- Including two collections triggers EF's cartesian warning; both contexts default to split queries (`UseQuerySplittingBehavior`).
- Razor: a variable named `code` inside `@foreach ((..., string code, ...) in ...)` trips the `@code` directive parser; name it something else. A component with a named `RenderFragment` (`<Filters>`) needs the rest wrapped in `<ChildContent>`.
- CDK: `Amazon.CDK.Assertions` lives inside Amazon.CDK.Lib (the separate package is CDK v1). JSII is one Node process per test host, so `CivicBudget.Infra.Tests` disables xUnit parallelization. `Tags.SetTag` on a stack does not write resource tags; use `Tags.Of(this).Add`. `cdk synth` needs the app built first (`--no-build` in cdk.json).
- Docker: never `dotnet publish --no-restore` after a csproj-only restore layer; the static web assets pipeline decides at restore time whether `_framework/blazor.web.js` is needed and a project-files-only restore says no, so the image serves 404 for it and every interactive page is dead. The Dockerfile asserts the file exists; the deploy workflow checks again.
- Docker: the build context must include `.editorconfig` (migration analyzer exemptions) or publish fails on CA1861. `infra/`, `tests/`, `docs/`, `.env` are ignored.
- A per-page `@rendermode` leaves the layout static (no toasts, no sidebar events). Global mode on `Routes` with static opt-outs is the pattern (ADR-0002 amendment).
- The live demo is Azure (ADR-0030): free-offer serverless SQL that auto-pauses, one Container App replica, nightly `--reseed`. The AWS CDK stack is the production-shaped story, not the live one.
- `AddDataProtection` registers an internal hosted service that reads the key ring during host startup; with `PersistKeysToDbContext` that is a database call before Kestrel listens, and on a sleeping database it costs a minute of blank browser. `Program.cs` drops it (`DeferKeyRingLoad`) so the key ring loads on first use. Anything else that touches the database from startup code will do the same damage.
- Startup (ADR-0031): the host listens first; `DatabaseStartupService` migrates and seeds behind it and flips `StartupState`. `WakingUpMiddleware` serves the waiting screen (503) until then, so never put migration or seed calls back in `Program.cs` before `RunAsync`, and keep `/health` free of database checks (it is the platform's startup probe). `/health/startup` is in-memory. No anonymous endpoint may query the database per call (anyone could keep the free database awake); `/health/ready` was removed for that reason. After `StartupState.QuietSpell` without a page request the middleware re-checks the database (`DatabaseWaker`), because it can pause behind a container that an open tab keeps alive.
- The ~17s before anything shows on a first visit is Azure provisioning a container from zero (our startup is 0.3s). Spencer chose to keep scale-to-zero (2026-09-23); `minReplicas: 1` is ~$4-5/month if that changes.
- GitHub Actions are pinned by commit SHA with the tag in a comment, and CI's Bicep by version plus sha256. To update one, resolve the new tag's commit (`gh api repos/OWNER/REPO/commits/TAG --jq .sha`) and change both.
- `scripts/screenshots/role-sweep.mjs` opens every page as every demo user; pages that load cleanly can still be wrong after a click, so exercise actions too.
- Kestrel logs `SslStream ... Bad address` on HTTP/2 when Safari drops an HTTPS connection; harmless macOS noise, use http://localhost:5000 if it bothers you.

## Code conventions
- `Directory.Build.props`: `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`,
  `<ImplicitUsings>enable</ImplicitUsings>`, file-scoped namespaces.
- `async` all the way; never `.Result` / `.Wait()`; pass `CancellationToken`.
- Money is `decimal` (`decimal(18,2)`); never `double`/`float`.
- Components never touch `DbContext`; they call Application service interfaces (`IFundService`, ...). Application services use `ICivicBudgetDbContextFactory` (ADR-0014).
- No em dashes in code comments. Explain *why* in a sentence a reader can follow without the chat history.
- Data access uses `IDbContextFactory<CivicBudgetDbContext>` — one context
  per unit of work (`await using var db = await factory.CreateDbContextAsync(ct);`).
- Never call `IgnoreQueryFilters()` in application code (tests may).
- Identity tables are the one thing outside the tenant filter; `UserAdminService` scopes by government explicitly (ADR-0015).
- Render modes are set once in `App.razor` (`Routes`/`HeadOutlet` get Interactive Server unless the routed page has `[ExcludeFromInteractiveRouting]`). Never put `@rendermode` on a page. Admin pages: `[Authorize(Policy = Policies.X)]`; Account, Portal, and Error pages are static SSR via the attribute (folder `_Imports.razor`).
- Roles: never test `IsInRole(Roles.FinanceDirector)` in a service; use `currentUser.IsFiscalAuthority()` (Admin or Fiscal Officer) or `IsDepartmentUser()`. Display names via `Roles.DisplayName`. Admin-set passwords are temporary (`MustChangePassword` + middleware).
- Department round (9d): `DepartmentRequest` is a child of `BudgetVersion` (`SetDepartmentNarrative`, `SubmitDepartment`, `ReturnDepartment`); never new one up elsewhere. "May they edit" always passes the submitted flag (`BudgetLinePermissions.CanEdit(..., departmentSubmitted)`); pages read `BudgetWorkspaceDto.DepartmentRequests` and its `CanSubmit`/`CanReturn`/`CanEditNarrative` rather than checking roles. A dialog's `OnConfirm` is a `Func`, so call `StateHasChanged()` after reloading inside it.
- ERP chart: `ErpChart` is the contract, `ChartDiff` is pure, `ChartSyncService` applies through entities. Setup services must call `ChartOwnership.RefuseIfErpManagedAsync` before writing. Add an ERP adapter by implementing `IErpChartSource`, never by touching the sync service.
- Import/reports: `ImportAnalyzer` and `ReportBuilder` are pure; keep rules there and tests in Application.Tests. Exports go through `ExportTable` + `ISpreadsheetExporter`; add endpoints to `AdminExportEndpoints` behind a policy.
- Portal pages call `ISnapshotQueryService` only; shared pieces live in `Components/Portal/Common` (`Breakdown`, `PortalPanels`, `PortalKpi`, `PortalCrumbs`, `PortalNotFound`, `MoneyShort`). Styles are the `.pt-*` section of `app.css`. The portal has no JavaScript: toggles are links or `:checked` CSS. The portal context maps the snapshot tables plus `GovernmentLogos` (a public image) and nothing else.
- Mobile: every page must render at 390px without horizontal overflow (Safari zooms the whole page out otherwise). Grid columns that hold tables are `minmax(0, 1fr)`, never bare `1fr`; wide tables scroll inside `.cb-grid-wrap` with the first column pinned on phones; hidden tooltips are `display:none`, not `visibility:hidden`; long `<select>` option labels widen the page in WebKit. Check with `scripts/screenshots/mobile-sweep.mjs`.
- Phones, list pages: the grid gets `d-none d-md-block` and a `.cb-cards.d-md-none` block renders `<ListCard>` per row from the same filtered data; put the status pill and row menu in `RenderFragment` helpers in `@code` and use them in both. `<Summary>` is the second line (`Meta` is an HTML void element in Razor; avoid it as a fragment name). Working grids mark secondary columns `cb-col-wide` and add a `.cb-row-open` chevron that opens the drawer, which leads with `LineDetail`.
- Blazor traps (Phase 17): a `Func` callback (`ConfirmDialog.OnConfirm`, `AddLineForm.OnAdd`) does not re-render its owner, so call `StateHasChanged()` after reloading; a page with a route parameter loads in `OnParametersSetAsync` guarded by the id it last loaded; an input bound one way keeps a refused value unless its `@key` changes (`AmountCell`). Paged lists use `ListPager`, not QuickGrid's `Paginator`, so the phone cards follow the page.
- Formatting: times through `Display.Timestamp` / `ShortDate` / `LongDate` (Eastern, zone named), amounts through `Display.Amount` / `Money` (en-US). Never `ToLocalTime()` or a bare `ToString("N2")`.
- Security helpers: return URLs through `LocalUrl.IsLocal`; "is this a static file" through `StaticAssetPath`; uploaded images through `UploadedImage.Validate` and served with `ImageResponse.Harden`. Every service that writes checks the caller's role itself; add each new admin page to `AdminPagePolicyTests`.
- Tests: `CreateScopeAs(role, government)` for a signed-in scope; a test class that writes gets a database per test (a unique name in `InitializeAsync`); portal tests read changed data through a fresh scope (the query service remembers loads for one request).
- UI: use `PageHeader` (sets breadcrumbs), `StatusPill`, `KpiCard`, `ConfirmDialog`, `RowMenu`, `EmptyState`, `SkeletonRows`, `ToastService`; grids use `table.cb-grid` inside `.cb-grid-wrap` (QuickGrid with `Theme="bootstrap"`). Tokens live in `wwwroot/app.css`; see `docs/design/DESIGN-BRIEF.md`. Never `window.confirm`.
- Mark financial entities `[Audited]`; the interceptor does the rest. Use `AuditEntry.Event(...)` for named actions, and `[NotAudited]` on a property whose change that event already describes.
- People and brand: render a person with `<Avatar UserId Name [Version] [Size]>` (never hand-rolled initials; `Display.Initials` is the one helper) and the mark with `<Logo Size>`; the sidebar brand is the government's name, not the product's.
- Domain invariants throw `DomainException`; user-input problems return a
  `Result` with errors.
- Naming: `*Service` (Application), `*Repository` only if it earns its keep,
  `*Validator`, `*Configuration` (EF), `*Interceptor`, `*Handler` (authz).
- Comments explain *why*, not *what*. Match surrounding density.

## Layout
```
src/CivicBudget.{Domain,Application,Infrastructure,Web}
tests/CivicBudget.{Domain,Application,Web}.Tests, tests/CivicBudget.IntegrationTests
infra/CivicBudget.Infra
docs/  (SPEC, ARCHITECTURE, DECISIONS, walkthroughs/)
```

## Commands
```bash
docker compose up -d                                   # SQL Server (amd64 via Rosetta on Apple Silicon)
dotnet build                                           # warnings are errors
dotnet test                                            # all tests (integration tests need Docker running)
dotnet test tests/CivicBudget.Domain.Tests             # fast domain tests only
dotnet run --project src/CivicBudget.Web               # migrates + seeds in Development
dotnet ef migrations add <Name> -p src/CivicBudget.Infrastructure -o Persistence/Migrations --context CivicBudgetDbContext   # the portal context has no migrations
docker compose -f docker-compose.full.yml up --build   # app + SQL Server in containers on :8080
cd infra/CivicBudget.Infra && npx aws-cdk@2 synth      # CloudFormation from the C# CDK app (no credentials needed)
bicep build infra/azure/main.bicep --stdout >/dev/null  # the Azure template (brew install azure/bicep/bicep); CI does the same
./scripts/azure-setup.sh                               # once: the free Azure demo (needs az login); pushes to main deploy after that
dotnet run --project src/CivicBudget.Web -- --reseed   # drop every table, migrate, seed (what the nightly Azure job runs)
./scripts/dev-setup.sh                                 # once: .env + user-secrets connection string
dotnet format                                          # CI runs --verify-no-changes
```

## Git
- Branch per phase: `phase-1-foundation`, `phase-2-identity`, …; PR to `main`.
- Conventional commits: `feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`.
- PR description: what / why / how to test / screenshots.
- Commit messages end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Docs to keep current
`ROADMAP.md` (check boxes), `CHANGELOG.md` (per phase), `docs/DECISIONS.md`
(new ADR for any non-obvious choice or package), `docs/walkthroughs/NN-*.md`
(one per phase), and `docs/INTERVIEW-PREP.md` (Q&A with answers and
code pointers — add a section every phase).
