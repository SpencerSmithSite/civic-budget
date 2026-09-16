# Interview Prep — CivicBudget

Questions an interviewer is likely to ask about this project, the answer,
and **where to look** to back it up. Grows by one section per phase.
Read `SPEC.md` §10 (glossary) first if the fund-accounting terms are rusty.

How to use: for each question, be able to (1) give the one-sentence answer,
(2) open the file and point at the line, (3) name the alternative you
rejected and why.

---

## The 60-second pitch

CivicBudget is a multi-tenant budgeting tool for local governments — think
a village finance director building next year's appropriations — with a
public transparency portal citizens can browse without logging in. It's
.NET 10 and Blazor end to end: the admin side is Interactive Server because
staff need live grids and validation; the public side is static
server-rendered HTML because it has to be fast, cacheable, accessible, and
cheap at scale. Data is in SQL Server through EF Core, tenancy is enforced
in the data layer with global query filters, and the portal can only read
immutable published snapshots — never live drafts. Infrastructure is AWS
CDK in C#, deployed from GitHub Actions over OIDC.

---

## Phase 0 — Planning & architecture

### Q: Walk me through the architecture.
**A:** Four projects with inward-pointing dependencies: `Domain` (entities
and rules, no framework references), `Application` (use cases, DTOs,
validators, interfaces), `Infrastructure` (EF Core, Identity stores, Excel
I/O — implements the interfaces), and `Web` (Blazor + composition root).
Components call application services; they never touch `DbContext`.
**Look at:** `docs/ARCHITECTURE.md` §1; `src/*/**.csproj` project references
(Phase 1).
**Rejected:** a single web project (rules leak into `.razor`), and a
MediatR/CQRS pipeline (indirection with no payoff at this size; MediatR
also went commercial in 2025). ADR-0001.

### Q: Why two render modes in one app?
**A:** Different audiences, opposite needs. Twenty finance staff editing
grids justify a SignalR circuit each; twenty thousand citizens on
adoption night do not. Static SSR makes every portal hit a cacheable HTTP
response with no per-visitor server state, works without JavaScript, and is
crawlable.
**Look at:** `docs/ARCHITECTURE.md` §3; portal pages will have no
`@rendermode` directive (Phase 5). ADR-0002.

### Q: How do you stop one government from seeing another's budget?
**A:** Every tenant-owned entity carries `GovernmentId`. The `DbContext`
adds a global query filter for each of them bound to an `ITenantContext`
(populated from a claim in the admin app, from the URL slug in the portal).
A `SaveChanges` interceptor stamps new rows and rejects mismatches. If no
tenant is set, the filter returns *nothing* — the safe default. Integration
tests seed two tenants and prove the other's rows are invisible.
**Look at:** `docs/ARCHITECTURE.md` §5; `CivicBudgetDbContext.OnModelCreating`
and `TenantSaveChangesInterceptor` (Phase 1). ADR-0004.
**Rejected:** database-per-tenant (operationally heavier than the project
needs), SQL row-level security (good later as defense-in-depth).

### Q: Why can't the public site just show the adopted budget?
**A:** Three reasons. Security — a forgotten `.Where(status == Adopted)`
would leak drafts, so the portal uses a separate read-only `DbContext` that
doesn't even map the live tables. Immutability — renaming an account next
year shouldn't rewrite what citizens saw last year, so the snapshot carries
its own copies of names and codes. Performance — denormalized rows
aggregate with a `GROUP BY` and invalidate with one cache tag.
**Look at:** `docs/ARCHITECTURE.md` §6; `PublicPortalDbContext` (Phase 4).
ADR-0005, ADR-0006.

### Q: Why `IDbContextFactory` instead of injecting `DbContext`?
**A:** In Blazor Server the DI scope is the *circuit*, not the request. A
scoped `DbContext` would live as long as the browser tab, accumulate tracked
entities, and be shared by concurrent event handlers — and `DbContext` isn't
thread-safe. The factory gives one short-lived context per unit of work.
**Look at:** `docs/ARCHITECTURE.md` §4.1; any application service's
`await using var db = await _dbFactory.CreateDbContextAsync(ct);` (Phase 1+).
ADR-0003.

### Q: Explain the appropriation-limit rule.
**A:** In Ohio a subdivision can't appropriate more than the County Budget
Commission certifies as estimated resources: beginning unencumbered balance
plus estimated revenue. Per fund, `Expenditures + Transfers Out` must be
≤ `Beginning balance + Revenues + Transfers In`. It's configurable per
government as *Warn* or *Block*, enforced at workflow transitions rather
than on every keystroke so staff can save work-in-progress.
**Look at:** `docs/SPEC.md` §5.1; `FundBalanceCalculator` and
`AppropriationLimitCheck` in `Domain` (Phase 1).

### Q: How do you handle money?
**A:** `decimal` everywhere, `decimal(18,2)` in SQL via a model-building
convention so it can't be forgotten on a new property. Percent change uses
`MidpointRounding.AwayFromZero` because that's what finance staff see in
Excel; banker's rounding would produce "wrong" numbers in their eyes. Both
are unit-tested.
**Look at:** `Domain/Common/Money.cs` and its tests (Phase 1).

### Q: Why FluentValidation over DataAnnotations?
**A:** Rules live in the Application layer as plain classes, are trivially
unit-testable, and handle cross-field/async rules (department required only
for expenditure accounts) cleanly. DataAnnotations would be zero packages
but pushes rule logic into attribute metadata.
**Look at:** ADR-0007; `Application/**/Validators` (Phase 3).

### Q: You don't have an AWS account. How is this an AWS project?
**A:** The deployment *engineering* is done and verifiable: multi-stage
Dockerfile, full-stack compose, a CDK stack in C# that `cdk synth`s in CI
with no credentials, CDK assertion tests that fail if RDS becomes public or
IAM gets a wildcard, and an OIDC-based `deploy.yml`. If an account appears,
`cdk bootstrap && cdk deploy` is the only new step.
**Look at:** ADR-0008; `infra/` (Phase 7).

### Q: What's in the seed data and why those funds?
**A:** A fictional Ohio village with the funds every village actually has:
General, Street Construction Maintenance & Repair (state gas tax and license
fees — restricted), Capital Projects, and Water and Sewer enterprise funds.
Fund codes follow the Auditor of State's UAN numbering so they look familiar
to Ohio staff. Three fiscal years give a prior-year actual, a current year
with an amendment, and a draft that is deliberately over its limit in one
fund so the validation demo has something to show.
**Look at:** `docs/SPEC.md` §9; `Infrastructure/Seed/` (Phase 1).

### General information worth having ready
- **Governmental vs. proprietary funds:** governmental funds (General,
  Special Revenue, Capital Projects, Debt Service, Permanent) account for
  tax-supported activities; proprietary funds (Enterprise, Internal Service)
  run like businesses on user charges. Fiduciary funds hold money for others.
- **Appropriation vs. expenditure:** appropriation is legal *authority* to
  spend; expenditure is the actual spend. This app budgets appropriations.
- **Amendment = supplemental appropriation ordinance.** Council passes a
  new ordinance; the app models it as a new version that copies the adopted
  one so the original stays immutable.
- **Why "snapshot" rather than "publish flag":** the budget that was
  public on a date is a record, not a view.
