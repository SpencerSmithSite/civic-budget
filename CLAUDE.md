# CLAUDE.md — CivicBudget

My portfolio project for a .NET / Blazor role at a government ERP company.
I will be asked to **explain this code**, so: clarity over cleverness,
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
- EF returns a child collection in no fixed order. Anything computed from one (splitting a cent among a position's funds) must order it itself, or a repricing moves cents at random.
- Razor: a field named `page` breaks the same way (`@page.Name` is read as the `@page` directive); call it something else.
- Razor: a variable named `code` inside `@foreach ((..., string code, ...) in ...)` trips the `@code` directive parser; name it something else. A component with a named `RenderFragment` (`<Filters>`) needs the rest wrapped in `<ChildContent>`.
- CDK: `Amazon.CDK.Assertions` lives inside Amazon.CDK.Lib (the separate package is CDK v1). JSII is one Node process per test host, so `CivicBudget.Infra.Tests` disables xUnit parallelization. `Tags.SetTag` on a stack does not write resource tags; use `Tags.Of(this).Add`. `cdk synth` needs the app built first (`--no-build` in cdk.json).
- Docker: never `dotnet publish --no-restore` after a csproj-only restore layer; the static web assets pipeline decides at restore time whether `_framework/blazor.web.js` is needed and a project-files-only restore says no, so the image serves 404 for it and every interactive page is dead. The Dockerfile asserts the file exists; the deploy workflow checks again.
- Docker: the build context must include `.editorconfig` (migration analyzer exemptions) or publish fails on CA1861. `infra/`, `tests/`, `docs/`, `.env` are ignored.
- A per-page `@rendermode` leaves the layout static (no toasts, no sidebar events). Global mode on `Routes` with static opt-outs is the pattern (ADR-0002 amendment).
- The live demo is Azure (ADR-0030): free-offer serverless SQL that auto-pauses, one Container App replica, nightly `--reseed`. The AWS CDK stack is the production-shaped story, not the live one.
- `AddDataProtection` registers an internal hosted service that reads the key ring during host startup; with `PersistKeysToDbContext` that is a database call before Kestrel listens, and on a sleeping database it costs a minute of blank browser. `Program.cs` drops it (`DeferKeyRingLoad`) so the key ring loads on first use. Anything else that touches the database from startup code will do the same damage.
- Startup (ADR-0031): the host listens first; `DatabaseStartupService` migrates and seeds behind it and flips `StartupState`. `WakingUpMiddleware` serves the waiting screen (503) until then, so never put migration or seed calls back in `Program.cs` before `RunAsync`, and keep `/health` free of database checks (it is the platform's startup probe). `/health/startup` is in-memory. No anonymous endpoint may query the database per call (anyone could keep the free database awake); `/health/ready` was removed for that reason. The one exception is a question posted on the portal, and only when a model is connected: `PortalQuestionGate` refuses it first otherwise, and `RateLimits` limits it per address before the page runs (ADR-0049). After `StartupState.QuietSpell` without a page request the middleware re-checks the database (`DatabaseWaker`), because it can pause behind a container that an open tab keeps alive.
- The ~17s before anything shows on a first visit is Azure provisioning a container from zero (our startup is 0.3s). Spencer chose to keep scale-to-zero (2026-09-23); `minReplicas: 1` is ~$4-5/month if that changes.
- GitHub Actions are pinned by commit SHA with the full version in a comment (`# v4.6.0`), and CI's Bicep by version plus sha256. Dependabot keeps that comment form current but mangles any other (a date after it, or `# v4`). To update one by hand, resolve the new tag's commit (`gh api repos/OWNER/REPO/commits/TAG --jq .sha`) and change both. Before taking a major, read its release notes and check the inputs the workflows pass, since the deploy workflows only run on main.
- The product site is `public/CivicBudget` in the spencersmith.site repository (its `docs/civicbudget-site.md`). Its clips come from `scripts/screenshots/site-clips.mjs`, which saves its edits, so reseed before each recording; a reseed also gives records new ids, so look ids up, never hard-code them.
- `scripts/screenshots/role-sweep.mjs` opens every page as every demo user; pages that load cleanly can still be wrong after a click, so exercise actions too.
- Blazor sets its own `Content-Security-Policy: frame-ancestors 'self'`; `Program.cs` turns it off (`ContentSecurityFrameAncestorsPolicy = null`) because `SecurityHeadersMiddleware` keeps a policy that is already set, and OnStarting callbacks run last-registered first.
- Rate limits count per client address; many sign-ins from one machine in a script (more than 20 per 5 minutes) get 429. Restart the app to reset them.
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
- Never call `IgnoreQueryFilters()` in application code (tests may). The operator's cross-government jobs in Infrastructure (email sender, `RetentionService`, `GovernmentDataStore`) are the only exceptions, each scoping every statement by id.
- Identity tables are the one thing outside the tenant filter; `UserAdminService` scopes by government explicitly (ADR-0015).
- Render modes are set once in `App.razor` (`Routes`/`HeadOutlet` get Interactive Server unless the routed page has `[ExcludeFromInteractiveRouting]`). Never put `@rendermode` on a page. Admin pages: `[Authorize(Policy = Policies.X)]`; Account, Portal, and Error pages are static SSR via the attribute (folder `_Imports.razor`).
- Roles: never test `IsInRole(Roles.FinanceDirector)` in a service; use `currentUser.IsFiscalAuthority()` (Admin or Fiscal Officer) or `IsDepartmentUser()`. Display names via `Roles.DisplayName`. Admin-set passwords are temporary (`MustChangePassword` + middleware).
- Department round (9d): `DepartmentRequest` is a child of `BudgetVersion` (`SetDepartmentNarrative`, `SubmitDepartment`, `ReturnDepartment`); never new one up elsewhere. "May they edit" always passes the submitted flag (`BudgetLinePermissions.CanEdit(..., departmentSubmitted)`); pages read `BudgetWorkspaceDto.DepartmentRequests` and its `CanSubmit`/`CanReturn`/`CanEditNarrative` rather than checking roles. A dialog's `OnConfirm` is a `Func`, so call `StateHasChanged()` after reloading inside it.
- ERP chart: `ErpChart` is the contract, `ChartDiff` is pure, `ChartSyncService` applies through entities. Setup services must call `ChartOwnership.RefuseIfErpManagedAsync` before writing. Add an ERP adapter by implementing `IErpChartSource`, never by touching the sync service.
- ERP actuals (ADR-0034): `ErpActuals` is the contract, `ActualsMatcher` and `ActualsByLine` are pure, `ActualsSyncService` replaces a fiscal year. Add a source by implementing `IErpActualsFileSource` or `IErpActualsApi`. Never store actuals on budget lines except the prior-year actual, which only a closed year fills, through `PriorYearActuals` and `BudgetVersion`. The simulated ERP (`AddSimulatedErp`) is registered only where demo data is seeded and no connection is configured (`Program.cs`, and the integration test host).
- Text the app shows says "ERP" or "the ERP", never "VIP" or another vendor's name (a connection names itself through its adapter's `Name`). Docs may use VIP as the example.
- ERP API (ADR-0045): the published API is `docs/partners/openapi.json`; its JSON shapes are the `Api*` records in `ErpApiContract.cs`, never the Application contracts serialized directly. Change a field in both the record and the document, or `ErpApiContractTests` fails; a breaking change is a new version (`/v2`), not an edit. Keep every sample in `docs/partners/samples` readable by the real code. Connections are `Erp:Connections:{government id}` in configuration only, never the database or a page. Every ERP contract has `IsConnected(governmentId)`; services use the adapter only through their `Api` property, which checks it. The journal post throws when the outcome is unknown and returns a refusal only when nothing posted.
- Sending to the ERP (ADR-0035): journals are changes (`BudgetJournalBuilder`), never totals; a `BudgetTransmission` is saved before the ERP is called and its id is the idempotency key; only Accepted or Imported count as sent; one open send per year (service check plus filtered unique index). Add an ERP by implementing `IErpBudgetApi`.
- Certificate (ADR-0036): `CertificateBuilder` is pure and both views render its one row per fund; revenue columns come from `ReportAccountGroup` (report settings), never a hard-coded category; balances from the ERP's closed prior year, else the budget's estimate. PDFs go through `ICertificatePdfRenderer` (MigraDoc, fonts embedded from `Infrastructure/Reports/Fonts`); a MigraDoc section needs its own `PageSetup` copy with the page size set outright.
- Reports on ERP actuals (ADR-0037): `ActualsReportBuilder` is pure; pace and projection come from last year at the same month (`Project`), never a straight line. Whole-fund reports (projection, trends, appropriation measure, certificate) return null for department users. Report columns for any report go through `ReportColumnRules`; the measure's live in `MeasureColumnService`.
- Personnel (ADR-0038): positions are children of `BudgetVersion`; change them only through `AddPosition` / `UpdatePosition` / `RemovePosition`, which reprice the department (`ApplyPersonnel`). A line is typed or calculated (`BudgetLine.PositionCount`), never both; never set a calculated line's amount directly. Settings belong to a fiscal year (`PersonnelSettings`); price only through the pure `PositionCostCalculator` with `PositionDetails` and `PayrollRules`. Load a version with its positions through `PersonnelData.VersionsWithPositions`. Rates and hours are `decimal(9,4)`: add a new one to the list in `MigrationTests`.
- Employees from the ERP (ADR-0039): `ErpEmployees` is the contract; plans are matched by name and departments and funds by code, so an ERP's own codes stay in its adapter (`IErpEmployeesApi` or a file source). `EmployeeMatcher` is pure. A sync refreshes what the ERP owns (name, title, hire date, pay, retirement, insurance, funds) and keeps what the budget owns (raise, step increase, months, longevity, other pay, pay account); keep that split when adding a field. Positions without an `EmployeeId` are never touched by a sync.
- Multi-year plan (ADR-0044): the plan is part of `BudgetVersion` (`SetPlan`, `SetPlannedAmount`); `PlanYears` counts the budget year. Projected amounts are never stored except in the snapshot: work them out with the pure `MultiYearPlanCalculator.Project`, which needs `Lines.Account` and `Lines.PlannedAmounts` loaded. A line's future years follow the same `BudgetLinePermissions.CanEdit` rule as its budget year; a new copy path (like amendments and `CreateOriginalFrom`) must carry the plan.
- Email (ADR-0040): add an `OutboxEmail` through `IEmailOutbox.Add` in the same unit of work as the change it reports, then call `Notify()` after the save. Wording lives in `EmailTemplates` (plain text, never a password). Never send mail inline, and never poll the outbox. Recipients come from `IUserDirectory`; absolute links from `IAppLinks`.
- Account security: sign-in and account events (MFA on or off, recovery codes, resets, links sent) are audit events through `IdentityAudit` or the account services, not a DbContext in a page. Two-step sign-in is Identity's authenticator provider; a gate that must hold a user on one page is a claim plus middleware (`MustChangePasswordMiddleware`, `RequireMfaMiddleware`), exempting `MustChangePasswordMiddleware.IsExempt` paths.
- Security (ADR-0041): who got in and what left goes in the security log (`ISecurityEventLog`), never the audit trail; sign-in outcomes are logged by `AuditingSignInManager` and exports by the `/admin/export` group filter, so new sign-in paths and exports need nothing extra. Session lengths live in `SessionPolicy` only. Rate-limited requests are decided by the pure `RateLimits.PolicyFor`. No inline `<script>` (the CSP allows this site's files plus the import map's nonce); put browser code in `wwwroot/js`. A new table must carry `GovernmentId` or reach one by foreign key, or be added on purpose to the shared list in `SecurityTests`, so the full export and `--offboard` cover it.
- Governments are created only by `--provision` (`GovernmentProvisioningService`), never from a page; removed only by `--offboard` after its export. Retention runs from `--maintenance`, never a timer. The getting-started checklist is computed from data (`SetupChecklistService`); add a step there, not a stored flag.
- Assistant (ADR-0047): a tool wraps an Application service and runs as the signed-in user; never give it a DbContext, SQL, or `IgnoreQueryFilters`, and never check permissions in a tool (the service does). Tools return a compact summary plus the `page` path, and record a readable `AssistantStep`. Application sees only `IChatClient`/`AIFunction`; the provider is registered in Infrastructure (`AddAssistantModel`) only when `Assistant:ApiKey` is set; `Assistant:Provider` is Anthropic or Ollama (Ollama goes through the same Anthropic SDK, key as Bearer, `Assistant:Model` required). Every page (admin, and account pages people ask about) carries `[HelpTopic]`; `HelpCatalogTests` fails without it. Answers render through `AssistantMarkdown` (no raw HTML, local links only). The security log records tool names, never the question's text. Run `AssistantEvaluationTests` (it reads the web project's user-secrets) after changing the prompt, a tool, or the model.
- Assistant actions (ADR-0048): a change is a `propose_` tool in `ActionTools` that checks permission, previews through the page's service, and calls `Propose` with a commit delegate; never add a tool that commits, and never let a tool confirm. Only `IAssistantService.ConfirmAsync` (the card's button) runs a proposal, once. Commits go through the page's own service; line changes pass `Expected` amounts (`UpdateLineAmountsAsync`) so a stale preview is refused whole. Arithmetic lives in pure code (`FundTrim`, `BudgetReview`), never in the model. Evaluation tests read seeded figures from the services rather than hard-coding them.
- Portal questions (ADR-0049): `PortalTools` reads only `ISnapshotQueryService` for the one government fixed at construction; never give a portal tool a government parameter, a DbContext, or anything unpublished, and never a tool that writes. Give the model totals to copy rather than columns to add. `PortalPrompt` holds the public rules (published only, say what is not published, no opinions). The form stays a plain post that works without JavaScript and without cookies. The monthly count changes only through the one `ExecuteUpdateAsync` in `PortalQuestionService`. Run `PortalEvaluationTests` with `AssistantEvaluationTests`.
- Budget book (ADR-0046): the book is assembled from existing report services by `BudgetBookService`, and the pure `BudgetBookBuilder` decides the pages; never compute a figure in the renderer. The message lives on `BudgetVersion` (`SetMessage`) and follows its rules. A new section is a builder change, an option in `BudgetBookOptions` and `BudgetBookSettings`, and a renderer method. The published book is `PublishedBudgetBook`, printed at publish; never re-render it for the portal. Glossary terms live in `Glossary.Terms` for both the portal and the book.
- Amounts typed by people (import, ERP files) go through `MoneyText.Parse`; do not write another money parser.
- Import/reports: `ImportAnalyzer` and `ReportBuilder` are pure; keep rules there and tests in Application.Tests. Exports go through `ExportTable` + `ISpreadsheetExporter`; add endpoints to `AdminExportEndpoints` behind a policy.
- Portal pages call `ISnapshotQueryService` only (the question box also calls `IPortalQuestionService`); shared pieces live in `Components/Portal/Common` (`Breakdown`, `PortalPanels`, `PortalKpi`, `PortalCrumbs`, `PortalNotFound`, `MoneyShort`). Styles are the `.pt-*` section of `app.css`. The portal has no JavaScript: toggles are links or `:checked` CSS. The portal context maps the snapshot tables plus `GovernmentLogos` (a public image) and nothing else.
- Mobile: every page must render at 390px without horizontal overflow (Safari zooms the whole page out otherwise). Grid columns that hold tables are `minmax(0, 1fr)`, never bare `1fr`; wide tables scroll inside `.cb-grid-wrap` with the first column pinned on phones; hidden tooltips are `display:none`, not `visibility:hidden`; long `<select>` option labels widen the page in WebKit. Check with `scripts/screenshots/mobile-sweep.mjs`.
- Phones, list pages: the grid gets `d-none d-md-block` and a `.cb-cards.d-md-none` block renders `<ListCard>` per row from the same filtered data; put the status pill and row menu in `RenderFragment` helpers in `@code` and use them in both. `<Summary>` is the second line (`Meta` is an HTML void element in Razor; avoid it as a fragment name). Working grids mark secondary columns `cb-col-wide` and add a `.cb-row-open` chevron that opens the drawer, which leads with `LineDetail`.
- Budget rules (Phase 18): a department's total is `AccountType.CountsTowardDepartmentTotal()` (expenditures only), never a hand-written type filter; line amounts are never negative (the domain enforces it); only the latest adopted, unreplaced version is publishable or can start next year's budget (`BudgetVersion.CreateOriginalFrom`). Every mutating method on `BudgetVersion` calls `Touch()` (the `Revision` concurrency token), and budget services save with `db.TrySaveAsync(ct)`.
- Accessibility (ADR-0043):
  - ARIA states from a bool go through `Aria.Bool` (a bare bool renders as an empty attribute).
  - Admin forms show a failed `Result` with `<ResultAlert>`, which lists every error and takes focus. Static account forms use `<FormErrors>` inside the `EditForm`.
  - Page `h1`s hold the title only (`PageHeader` puts the pill and tip beside it); card titles are `<h2 class="cb-card-title">`.
  - A table wider than a phone scrolls in a wrapper with `tabindex="0" role="region" aria-label="Table of ..."`.
  - An action column's header is `<span class="visually-hidden">Actions</span>`.
  - Failure and warning toasts stay until dismissed, so do not rely on them vanishing.
  - Run `scripts/screenshots/a11y-sweep.mjs` before merging UI work; the conformance report is `docs/accessibility/ACR.md`.
  - Every table has a name (`aria-label` or a caption).
  - A grouped table puts each group in its own `<tbody>` headed by `<th scope="rowgroup" colspan="…">` in a `tr.cb-group`, and a row's naming cell is `<th scope="row">`. Never a spanning `<td>`.
  - Fields use the `--cb-field-border` token (3:1); editable amounts stay outlined at rest.
  - Short portal amounts go through `MoneyShort.Speakable` (or `PortalKpi Amount=`) so screen readers hear "$3.70 million".
  - `civicbudget.js` makes any overflowing `.cb-grid-wrap` on an interactive page a named, focusable region. Static portal pages set `tabindex="0" role="region"` themselves.
- Password fields use `<PasswordInput>` (never `InputText type="password"`): it adds the show/hide button, which `js/password-toggle.js` drives on both static and interactive pages.
- Dialogs and drawers call `civicBudget.openModal(element[, focusFirst])` / `closeModal([refocus])` through `FocusJs` (Tab trapped, focus returned to the opener, a dropdown's toggle, or the page heading); a strict bUnit test must `SetupVoid` both.
- Blazor traps (Phase 17): a `Func` callback (`ConfirmDialog.OnConfirm`, `AddLineForm.OnAdd`) does not re-render its owner, so call `StateHasChanged()` after reloading; a page with a route parameter loads in `OnParametersSetAsync` guarded by the id it last loaded; an input bound one way keeps a refused value unless its `@key` changes (`AmountCell`). Paged lists use `ListPager`, not QuickGrid's `Paginator`, so the phone cards follow the page.
- Formatting: times through `Display.Timestamp` / `ShortDate` / `LongDate` (Eastern, zone named), amounts through `Display.Amount` / `Money` (en-US). Never `ToLocalTime()` or a bare `ToString("N2")`.
- Security helpers: return URLs through `LocalUrl.IsLocal`; "is this a static file" through `StaticAssetPath`; uploaded images through `UploadedImage.Validate` and served with `ImageResponse.Harden`. Every service that writes checks the caller's role itself; add each new admin page to `AdminPagePolicyTests`.
- Tests: `CreateScopeAs(role, government)` for a signed-in scope; `fixture.CreateSeededDatabaseAsync(prefix)` gives each test a fresh copy of the seeded template (unique name added for you); only seed and reset tests seed for real; portal tests read changed data through a fresh scope (the query service remembers loads for one request).
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
samples/CivicBudget.ReferenceErp   (the published ERP API on demo data; docs/partners is the kit)
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
dotnet run --project samples/CivicBudget.ReferenceErp -- --urls http://localhost:5090 --ReferenceErp:ApiKey=local-test-key   # the reference ERP
```

## Git
- Branch per phase: `phase-1-foundation`, `phase-2-identity`, …; PR to `main`.
- Conventional commits: `feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`.
- PR description: what / why / how to test / screenshots.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Writing docs and comments
- Docs are in my voice: first person ("I chose", "I kept"), never "Spencer" in the third person,
  never "we" for a team that does not exist. Plain words a newcomer can follow; explain the Ohio
  term the first time it appears. No em dashes in docs or comments.
- Code comments explain why, in the present tense, with no phase numbers or project history
  ("used to", "Phase 9d"); history belongs in walkthroughs, the changelog, and ADR amendments.
- When a later phase changes an earlier decision, amend the original ADR (dated) rather than
  rewriting it, and add a short "since Phase N" note to the walkthrough that described it.
- Check names in docs against the code: every backticked class, method, or file should exist.

## Docs to keep current
`ROADMAP.md` (check boxes), `CHANGELOG.md` (per phase), `docs/DECISIONS.md`
(new ADR for any non-obvious choice or package), `docs/walkthroughs/NN-*.md`
(one per phase), and `docs/INTERVIEW-PREP.md` (Q&A with answers and
code pointers — add a section every phase).
