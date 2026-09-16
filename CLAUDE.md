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

## Code conventions
- `Directory.Build.props`: `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`,
  `<ImplicitUsings>enable</ImplicitUsings>`, file-scoped namespaces.
- `async` all the way; never `.Result` / `.Wait()`; pass `CancellationToken`.
- Money is `decimal` (`decimal(18,2)`); never `double`/`float`.
- Components never touch `DbContext`; they call Application services.
- Data access uses `IDbContextFactory<CivicBudgetDbContext>` — one context
  per unit of work (`await using var db = await factory.CreateDbContextAsync(ct);`).
- Never call `IgnoreQueryFilters()` in application code.
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
dotnet ef migrations add <Name> -p src/CivicBudget.Infrastructure -s src/CivicBudget.Web
dotnet user-secrets set "Seed:DemoPassword" "<pw>" --project src/CivicBudget.Web
```

## Git
- Branch per phase: `phase-1-foundation`, `phase-2-identity`, …; PR to `main`.
- Conventional commits: `feat:`, `fix:`, `test:`, `docs:`, `chore:`, `refactor:`.
- PR description: what / why / how to test / screenshots.
- Commit messages end with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

## Docs to keep current
`ROADMAP.md` (check boxes), `CHANGELOG.md` (per phase), `docs/DECISIONS.md`
(new ADR for any non-obvious choice or package), `docs/walkthroughs/NN-*.md`
(one per phase, written as interview prep).
