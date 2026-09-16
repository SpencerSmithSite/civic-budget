# Walkthrough 01 — Foundation

What Phase 1 built, and the concepts behind it, written as interview prep.
Read alongside the code; every section names the file to open.

---

## 1. The dependency rule, enforced by a test

`src/CivicBudget.Domain` has **no package references at all** — open
`CivicBudget.Domain.csproj`; it's three lines. `Application` references
only `Domain`. `Infrastructure` references `Application` (and EF Core).
`Web` references both and is the only project that knows about all of them.

`tests/CivicBudget.Application.Tests/ArchitectureTests.cs` asserts this by
reading each assembly's referenced assemblies. If EF Core ever leaks into
Domain, CI fails.

*Why it matters:* the domain rules (workflow, immutability, appropriation
limit) can be tested with `new BudgetVersion(...)` and no database. 160
domain tests run in 30 ms.

---

## 2. Aggregates: `BudgetVersion` owns its lines

`src/CivicBudget.Domain/Budgets/BudgetVersion.cs`

`BudgetLine` and `FundBeginningBalance` have `internal` constructors and
setters. The *only* way to add or change a line is through `BudgetVersion`
(`AddLine`, `UpdateLineAmount`, `SetBeginningBalance`, …), and every one of
those methods starts with `EnsureEditable()`. That is how "adopted versions
are immutable" is one line of code in one place instead of a check
scattered across services and components.

The collections are exposed as `IReadOnlyCollection<T>` backed by private
`List<T>` fields. EF Core is told to use the fields
(`UsePropertyAccessMode(PropertyAccessMode.Field)` in
`BudgetVersionConfiguration`), so persistence works without a public
mutable list.

`CreateAmendment(reason)` returns a **new** `BudgetVersion` containing copies
of the lines and balances with fresh ids and `VersionNumber + 1`. The
original is untouched — tested in `BudgetAmendmentTests`.

*Interview question:* "Why not just put a status check in the service?"
Because the service isn't the only caller — imports, seeders, and future
features all go through the same entity, and the rule can't be bypassed.

---

## 3. Money: `decimal`, and rounding away from zero

`src/CivicBudget.Domain/Common/Money.cs` + `MoneyTests.cs`

- `decimal` everywhere. `double` can't represent 0.1 exactly; a budget that
  sums to $1,000,000.00000001 is a support ticket.
- `Math.Round` defaults to **banker's rounding** (`ToEven`): 0.125 → 0.12.
  Finance staff live in Excel, which rounds 0.125 → 0.13. We use
  `MidpointRounding.AwayFromZero` explicitly and test it.
- Percent change with a zero baseline is `null`, not infinity or zero — a
  new line item is "new", not "+∞%".
- In SQL, `CivicBudgetDbContext.ConfigureConventions` sets **every**
  `decimal` property to `decimal(18,2)`. An integration test
  (`MigrationTests.Every_decimal_column_is_decimal_18_2`) reads
  `INFORMATION_SCHEMA.COLUMNS` to prove it — including that no `float` or
  `money` column exists.

---

## 4. Tenancy: global query filters + a save interceptor

`src/CivicBudget.Infrastructure/Persistence/CivicBudgetDbContext.cs`
`src/CivicBudget.Infrastructure/Persistence/Interceptors/TenantSaveChangesInterceptor.cs`

Read side — in `OnModelCreating`, `ApplyTenantQueryFilters` loops over
every entity type implementing `ITenantOwned` and calls a generic method
that adds:

```csharp
modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.GovernmentId == CurrentGovernmentId);
```

Two subtleties an interviewer may probe:

1. `CurrentGovernmentId` is an **instance property** on the DbContext that
   reads `ITenantContext`. EF Core sees a member access on the context
   instance and turns it into a SQL **parameter** evaluated per query. If it
   were a local variable captured at model-building time, it would be baked
   into the (cached) model as a constant and every tenant would get the
   first tenant's filter.
2. `Guid == Guid?` — when the tenant is `null` the comparison is false for
   every row, so **no tenant means no rows**, never all rows. Tested in
   `TenantIsolationTests.No_tenant_means_no_rows_not_all_rows`.

Write side — `TenantSaveChangesInterceptor` runs inside `SaveChanges`,
walks the change tracker, and throws `TenantIsolationException` if any
added/modified/deleted `ITenantOwned` entity has a `GovernmentId` different
from the current tenant (or if no tenant is set). Query filters don't apply
to `Add`, so without this a bug could write rows *into* another tenant.

Where does the tenant come from? `AmbientTenantContext` (scoped). Phase 2
sets it from a claim at sign-in; Phase 5 sets it from the portal URL slug;
the seeder and tests set it directly.

The reflection loop is verified by
`TenantIsolationTests.Every_tenant_owned_entity_has_a_query_filter`, which
inspects the EF model — so adding a new `ITenantOwned` entity and somehow
missing the filter is impossible without a failing test.

---

## 5. `IDbContextFactory` and why the factory takes `(sp, options)`

`src/CivicBudget.Infrastructure/DependencyInjection.cs`

```csharp
services.AddDbContextFactory<CivicBudgetDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()),
    ServiceLifetime.Scoped);
```

- Why a factory at all: in Blazor Server the DI scope is the **circuit**
  (the browser tab). A scoped `DbContext` would live for hours, hold every
  entity it ever loaded, and be shared by concurrent event handlers — and
  `DbContext` is not thread-safe. The factory hands out one short-lived
  context per unit of work: `await using var db = await factory.CreateDbContextAsync(ct);`
- Why `ServiceLifetime.Scoped` for the factory: the default factory
  lifetime is singleton, which can't see scoped services. Making it scoped
  lets the `(sp, options)` overload resolve the **current scope's**
  `TenantSaveChangesInterceptor` and therefore the current scope's tenant.
  That is the wire that connects "who is signed in on this circuit" to
  "which rows this context may write".

---

## 6. Migrations and the design-time factory

`src/CivicBudget.Infrastructure/Persistence/CivicBudgetDbContextDesignTimeFactory.cs`

`dotnet ef migrations add` needs to build the model, which needs a provider
but not a database. The design-time factory hands it a context with a
placeholder connection string, so migrations can be generated with

```bash
dotnet ef migrations add <Name> -p src/CivicBudget.Infrastructure -o Persistence/Migrations
```

and no startup project, configuration, or user-secrets are involved.

`MigrationTests.Model_snapshot_matches_the_current_model` calls
`HasPendingModelChanges()` — it fails if an entity changed and nobody
generated a migration. That is the check that used to be "hope the
reviewer notices."

The generated migration files are marked `generated_code = true` in
`.editorconfig` so our analyzers (warnings-as-errors) don't demand
file-scoped namespaces from code we didn't write.

---

## 7. Seed data through the front door

`src/CivicBudget.Infrastructure/Seed/`

The seeder builds entities with the same constructors and methods a user
action would use — `version.AddLine(...)`, `version.Propose()`,
`version.Adopt(...)`, `fy2026.CreateAmendment(...)`. Not `HasData`, not raw
SQL. If a seed row violates a domain rule, startup fails loudly, which is
the correct behavior for demo data that claims to be realistic.

`SeedLine` records each line once (its FY2025 budget) and derives the other
years with a small deterministic hash so the demo shows plausible $ and %
changes. `string.GetHashCode()` is deliberately *not* used — it's
randomized per process in .NET Core, which would make the seed
non-reproducible.

FY2027's Street fund is intentionally over its appropriation limit
(`5520 Capital Outlay – Infrastructure` jumps from $20k to $120k) so the
Phase 3 fund-balance panel has something to flag. `SeedTests` proves it's
exactly one fund.

---

## 8. Integration tests with Testcontainers

`tests/CivicBudget.IntegrationTests/SqlServerFixture.cs`

One SQL Server 2022 container per test run (xUnit collection fixture), one
database per test class. Same image as `docker-compose.yml`. On this Mac it
runs under Rosetta; on GitHub's Ubuntu runners it runs natively. Nothing is
mocked: the tenancy tests hit real query filters, real interceptors, real
SQL. The whole integration suite runs in about two seconds once the
container is up.

---

## 9. Things deliberately *not* done yet
- No Identity, no UI beyond a placeholder (Phase 2).
- No `Result<T>` type or validators (Phase 3, when there's user input).
- No `PublicPortalDbContext` (Phase 4, with the snapshot tables).
- Enums are stored as `int`. Storing names would be more readable in SQL
  but costs index width and makes renames migrations; with the enum values
  documented in Domain, `int` is the conventional choice.
