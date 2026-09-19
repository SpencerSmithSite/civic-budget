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
- `dotnet run` right after `docker compose up` used to crash on the pre-login handshake; DatabaseInitializer now retries for up to a minute.
- Reseed after schema/seed changes: `docker compose down -v && docker compose up -d`, then run the app.
- SQL Server refuses multiple cascade paths; use `DeleteBehavior.Restrict` on the second path (see IdentityConfiguration).
- EF Core cannot `OrderBy` after projecting to a DTO with collection sub-queries; order the entity first.
- Entities set their own Guid v7 ids, so keys are `ValueGeneratedNever` (ADR-0018); otherwise EF tracks children discovered through an aggregate as Modified.
- QuickGrid's default theme wins on CSS specificity; pass `Theme="bootstrap"` and style headers in app.css for dense grids.
- SQL Server under Rosetta occasionally segfaults (container exit 139). Compose now has `restart: unless-stopped` so Docker restarts it within seconds; the volume survives. If the app already gave up (12 retries), run it again.
- Interceptor order: `AuditInterceptor` before `TenantSaveChangesInterceptor` so audit rows are tenant-checked too.
- Output caching: Blazor SSR marks pages `no-store` and the default output cache policy honors it, so the portal policy is the base policy with `excludeDefaultPolicy: true`. Headers are read-only in `ServeResponseAsync`; rewrite them in `PortalResponseMiddleware` (`OnStarting`), which must sit before `UseOutputCache` or it never runs on a hit. `[OutputCache]` does nothing on Razor component endpoints.
- Including two collections triggers EF's cartesian warning; both contexts default to split queries (`UseQuerySplittingBehavior`).
- Razor: a variable named `code` inside `@foreach ((..., string code, ...) in ...)` trips the `@code` directive parser; name it something else. A component with a named `RenderFragment` (`<Filters>`) needs the rest wrapped in `<ChildContent>`.
- CDK: `Amazon.CDK.Assertions` lives inside Amazon.CDK.Lib (the separate package is CDK v1). JSII is one Node process per test host, so `CivicBudget.Infra.Tests` disables xUnit parallelization. `Tags.SetTag` on a stack does not write resource tags; use `Tags.Of(this).Add`. `cdk synth` needs the app built first (`--no-build` in cdk.json).
- Docker: the build context must include `.editorconfig` (migration analyzer exemptions) or publish fails on CA1861. `infra/`, `tests/`, `docs/`, `.env` are ignored.
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
- Admin pages: `@rendermode InteractiveServer` + `[Authorize(Policy = Policies.X)]`; Account and Portal pages are static SSR (`[ExcludeFromInteractiveRouting]` in the folder `_Imports.razor`).
- Import/reports: `ImportAnalyzer` and `ReportBuilder` are pure; keep rules there and tests in Application.Tests. Exports go through `ExportTable` + `ISpreadsheetExporter`; add endpoints to `AdminExportEndpoints` behind a policy.
- Portal pages call `ISnapshotQueryService` only; shared pieces live in `Components/Portal/Common` (`Breakdown`, `PortalKpi`, `PortalCrumbs`, `PortalNotFound`, `MoneyShort`). Styles are the `.pt-*` section of `app.css`.
- UI: use `PageHeader` (sets breadcrumbs), `StatusPill`, `KpiCard`, `ConfirmDialog`, `RowMenu`, `EmptyState`, `SkeletonRows`, `ToastService`; grids use `table.cb-grid` inside `.cb-grid-wrap` (QuickGrid with `Theme="bootstrap"`). Tokens live in `wwwroot/app.css`; see `docs/design/DESIGN-BRIEF.md`. Never `window.confirm`.
- Mark financial entities `[Audited]`; the interceptor does the rest. Use `AuditEntry.Event(...)` for named actions.
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
