# Walkthrough 01: Foundation

Phase 1 built the skeleton everything else hangs on: the four projects, the budget
rules, the database with tenant isolation, the seed data, and the test harness.
Each section names the file to open.

---

## 1. The dependency rule, enforced by a test

`src/CivicBudget.Domain` has **no package references at all**: open
`CivicBudget.Domain.csproj` and it is three lines. `Application` references only
`Domain` (plus EF Core's query abstractions and FluentValidation, ADR-0014).
`Infrastructure` references `Application`. `Web` references both and is the only
project that knows about all of them.

`tests/CivicBudget.Application.Tests/ArchitectureTests.cs` checks this by reading each
assembly's references. If EF Core ever leaks into Domain, CI fails.

*Why it matters:* the budget rules (workflow, immutability, the appropriation limit) can
be tested with `BudgetVersion.CreateOriginal(...)` and no database. At the end of this
phase 160 domain tests ran in 30 ms.

---

## 2. Aggregates: `BudgetVersion` owns its lines

`src/CivicBudget.Domain/Budgets/BudgetVersion.cs`

`BudgetLine` and `FundBeginningBalance` have `internal` constructors and setters. The
*only* way to add or change a line is through `BudgetVersion` (`AddLine`,
`UpdateLineAmount`, `SetBeginningBalance`, …), and every one of those methods calls
`EnsureEditable()`. That is how "an adopted budget cannot change" is one line of code
in one place instead of a check scattered across services and components.

The collections are exposed as `IReadOnlyCollection<T>` over private `List<T>` fields.
EF Core is told to use the fields (`UsePropertyAccessMode(PropertyAccessMode.Field)` in
`BudgetVersionConfiguration`), so persistence works without a public mutable list.

`CreateAmendment(reason)` returns a **new** `BudgetVersion` with copies of the lines and
balances, fresh ids, and `VersionNumber + 1`. The original is untouched, which
`BudgetAmendmentTests` checks.

*Interview question:* "Why not put a status check in the service?" Because the service
is not the only caller. Imports, the seeder, and every later feature go through the same
entity, so the rule cannot be bypassed by a new code path.

---

## 3. Money: `decimal`, rounded away from zero

`src/CivicBudget.Domain/Common/Money.cs` and `MoneyTests.cs`

- `decimal` everywhere. `double` cannot represent 0.1 exactly, and a budget that sums to
  $1,000,000.00000001 is a support ticket.
- `Math.Round` defaults to **banker's rounding** (round half to even): 0.125 becomes 0.12.
  Finance staff live in Excel, which gives 0.13. I use `MidpointRounding.AwayFromZero`
  explicitly and test it.
- A percent change from a zero baseline is `null`, not infinity or zero: a new line item
  is "new", not "+∞%".
- In SQL, `CivicBudgetDbContext.ConfigureConventions` maps **every** `decimal` property
  to `decimal(18,2)`. `MigrationTests` reads the database's column metadata to prove it,
  and that no `float` or `money` column exists.

---

## 4. Tenancy: global query filters and a save interceptor

`src/CivicBudget.Infrastructure/Persistence/CivicBudgetDbContext.cs`
`src/CivicBudget.Infrastructure/Persistence/Interceptors/TenantSaveChangesInterceptor.cs`

**Reads.** In `OnModelCreating`, `ApplyTenantQueryFilters` loops over every entity type
that implements `ITenantOwned` and adds:

```csharp
modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.GovernmentId == CurrentGovernmentId);
```

Two subtleties an interviewer may probe:

1. `CurrentGovernmentId` is an **instance property** on the DbContext that reads
   `ITenantContext`. EF Core sees a member of the context instance and turns it into a
   SQL **parameter** evaluated on every query. Had I captured a local variable while
   building the model, its value would be baked into the cached model as a constant, and
   every government would get the first government's filter.
2. `Guid == Guid?`: when there is no tenant the comparison is false for every row, so
   **no tenant means no rows**, never all rows. `TenantIsolationTests` covers it.

**Writes.** Query filters do not apply to `Add`, so without a second check a bug could
write rows *into* another government. `TenantSaveChangesInterceptor` runs inside
`SaveChanges`, walks the change tracker, and throws `TenantIsolationException` if any
added, modified, or deleted `ITenantOwned` entity belongs to a different government, or
if no government is set. It verifies rather than stamps (ADR-0013): entities take their
`GovernmentId` in the constructor, so a fund cannot exist without an owner.

**Where the tenant comes from.** `CurrentUserContext` (scoped) implements both
`ICurrentUser` and `ITenantContext`. In Phase 2 it started being filled from the
signed-in user's claim; the seeder and tests set it directly. The portal never uses it
(it reads through its own context and takes the slug from the URL).

The reflection loop is itself tested: `TenantIsolationTests` inspects the EF model and
fails if any `ITenantOwned` entity has no filter, so a new entity cannot slip through.

---

## 5. `IDbContextFactory`, and why the factory takes `(sp, options)`

`src/CivicBudget.Infrastructure/DependencyInjection.cs`

```csharp
services.AddDbContextFactory<CivicBudgetDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddInterceptors(sp.GetRequiredService<AuditInterceptor>(),
                            sp.GetRequiredService<TenantSaveChangesInterceptor>()),
    ServiceLifetime.Scoped);
```

- **Why a factory at all.** In Blazor Server the DI scope is the **circuit**, which is
  the browser tab. A scoped `DbContext` would live for hours, hold every entity it ever
  loaded, and be shared by overlapping event handlers, and `DbContext` is not
  thread-safe. The factory hands out one short-lived context per unit of work:
  `await using var db = await factory.CreateDbContextAsync(ct);`
- **Why the factory is scoped.** The default factory lifetime is singleton, which cannot
  see scoped services. Making it scoped lets the `(sp, options)` overload resolve the
  *current scope's* interceptors, and so the current scope's tenant. That is the wire
  that connects "who is signed in on this circuit" to "which rows this context may write".

(The audit interceptor arrived in Phase 3; it runs first so the audit rows it adds are
tenant-checked too.)

---

## 6. Migrations and the design-time factory

`src/CivicBudget.Infrastructure/Persistence/CivicBudgetDbContextDesignTimeFactory.cs`

`dotnet ef migrations add` has to build the model, which needs a provider but not a
database. The design-time factory hands it a context with a placeholder connection
string, so a migration is generated with

```bash
dotnet ef migrations add <Name> -p src/CivicBudget.Infrastructure -o Persistence/Migrations --context CivicBudgetDbContext
```

and no startup project, configuration, or secrets are involved.

`MigrationTests` calls `HasPendingModelChanges()`: it fails if an entity changed and
nobody generated a migration. Without it, that check is "hope the reviewer notices".

The generated files are marked `generated_code = true` in `.editorconfig`, so the
analyzers (warnings are errors) do not demand style changes to code I did not write.

---

## 7. Seed data through the front door

`src/CivicBudget.Infrastructure/Seed/`

The seeder builds entities with the same constructors and methods a user's action would
use: `version.AddLine(...)`, `version.Propose()`, `version.Adopt(...)`,
`fy2026.CreateAmendment(...)`. Not `HasData`, not raw SQL. If a seed row breaks a domain
rule, startup fails loudly, which is right for demo data that claims to be realistic.

`SeedLine` records each line once (its FY2025 budget) and derives the other years with a
small deterministic hash, so the demo shows believable dollar and percent changes.
`string.GetHashCode()` is deliberately *not* used: .NET randomizes it per process, which
would make the seed different on every run.

FY2027's Street fund is over its appropriation limit on purpose (`5520 Capital Outlay,
Infrastructure` jumps from $20,000 to $120,000), so the fund balance panel has something
to flag. `SeedTests` proves exactly one fund is over.

---

## 8. Integration tests with Testcontainers

`tests/CivicBudget.IntegrationTests/SqlServerFixture.cs`

One SQL Server 2022 container per test run (an xUnit collection fixture), the same image
as `docker-compose.yml`. On a Mac it runs under Rosetta; on GitHub's Ubuntu runners it
runs natively. Nothing is mocked: the tenancy tests hit real query filters, real
interceptors, and real SQL.

In Phase 1 each test class got its own database. Later phases changed that twice: in
Phase 17 every test that changes data got its own database, so no test depends on the
order xUnit runs them in, and in Phase 18 the fixture started building one seeded
template per run and restoring a copy for each test, which cut the suite from 1m40s to
about 40 seconds (walkthrough 20).

---

## 9. Deliberately not done yet
- No Identity and no UI beyond a placeholder (Phase 2).
- No `Result` type or validators until there was user input to validate (Phase 2).
- No `PublicPortalDbContext` until the snapshot tables existed (Phase 4).
- Enums are stored as `int`. Names would read better in SQL, but they cost index width
  and turn a rename into a migration; with the values documented in Domain, `int` is the
  conventional choice.
