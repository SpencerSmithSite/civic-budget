using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Identity;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>The demo data must load through the domain rules and match what SPEC §9 promises.</summary>
[Collection(SqlServerTests.Name)]
public class SeedTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync("CivicBudget_Seed");
        await RunSeederAsync();

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task RunSeederAsync()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
    }

    [Fact]
    public async Task Seeds_both_tenants_and_is_idempotent()
    {
        await RunSeederAsync(); // second run must be a no-op

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        List<Government> governments = await db.Governments.OrderBy(g => g.Name).ToListAsync();

        Assert.Equal(["Pine Hollow Township", "Village of Maple Ridge"], governments.Select(g => g.Name));
        Assert.Equal(7, governments[0].FiscalYearStartMonth);
        Assert.Equal(AppropriationLimitMode.Warn, governments[0].AppropriationLimitMode);
        Assert.Equal(AppropriationLimitMode.Block, governments[1].AppropriationLimitMode);
    }

    [Fact]
    public async Task Maple_ridge_has_the_five_funds_from_the_spec()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        List<string> codes = await db.Funds.OrderBy(f => f.Code).Select(f => f.Code).ToListAsync();

        Assert.Equal(["1000", "2011", "4901", "5101", "5201"], codes);
    }

    [Fact]
    public async Task Maple_ridge_has_three_fiscal_years_with_the_expected_versions()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);

        var versions = await db.BudgetVersions
            .Join(db.FiscalYears, v => v.FiscalYearId, fy => fy.Id, (v, fy) => new { fy.Year, v.VersionNumber, v.Status, v.SupersededByVersionId })
            .OrderBy(x => x.Year).ThenBy(x => x.VersionNumber)
            .ToListAsync();

        Assert.Collection(versions,
            v => { Assert.Equal((2025, 1, BudgetStatus.Adopted), (v.Year, v.VersionNumber, v.Status)); Assert.Null(v.SupersededByVersionId); },
            v => { Assert.Equal((2026, 1, BudgetStatus.Adopted), (v.Year, v.VersionNumber, v.Status)); Assert.NotNull(v.SupersededByVersionId); },
            v => { Assert.Equal((2026, 2, BudgetStatus.Adopted), (v.Year, v.VersionNumber, v.Status)); Assert.Null(v.SupersededByVersionId); },
            v => { Assert.Equal((2027, 1, BudgetStatus.Draft), (v.Year, v.VersionNumber, v.Status)); Assert.Null(v.SupersededByVersionId); });
    }

    [Fact]
    public async Task Fy2027_draft_has_exactly_one_fund_over_its_appropriation_limit()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);

        BudgetVersion draft = await db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.BeginningBalances)
            .SingleAsync(v => v.Status == BudgetStatus.Draft);
        Government government = await db.Governments.SingleAsync(g => g.Id == _mapleRidge);

        IReadOnlyList<FundBalanceSummary> summaries = FundBalanceCalculator.CalculateAll(draft);
        IReadOnlyList<AppropriationLimitResult> results = AppropriationLimitCheck.EvaluateAll(summaries, government.AppropriationLimitMode);

        AppropriationLimitResult blocked = Assert.Single(results, r => r.BlocksWorkflow);
        string streetFundId = (await db.Funds.SingleAsync(f => f.Code == "2011")).Id.ToString();
        Assert.Equal(streetFundId, blocked.FundId.ToString());
        Assert.True(blocked.AmountOverLimit > 0);
    }

    [Fact]
    public async Task Pine_hollow_rows_are_invisible_to_maple_ridge()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);

        Assert.False(await db.Funds.AnyAsync(f => f.Code == "2031"));
        Assert.False(await db.Departments.AnyAsync(d => d.Code == "710"));
    }

    [Fact]
    public async Task Seeds_roles_and_one_demo_user_per_role_with_department_claims_for_heads()
    {
        await using AsyncServiceScope scope = _database.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (string role in CivicBudget.Application.Security.Roles.All)
        {
            Assert.True(await roleManager.RoleExistsAsync(role), $"Role {role} was not seeded.");
        }

        ApplicationUser streets = (await userManager.FindByEmailAsync("streets@mapleridge.example"))!;
        Assert.Equal(_mapleRidge, streets.GovernmentId);
        Assert.Contains(CivicBudget.Application.Security.Roles.DepartmentHead, await userManager.GetRolesAsync(streets));
        Assert.True(await userManager.CheckPasswordAsync(streets, TestDatabase.DemoPassword));

        // The claims factory turns department assignments into department_id claims used for authorization.
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        System.Security.Claims.ClaimsPrincipal principal = await claimsFactory.CreateAsync(streets);
        Assert.Equal(_mapleRidge.ToString(), principal.FindFirst(CivicBudget.Application.Security.ClaimNames.GovernmentId)?.Value);
        Assert.Equal(2, principal.FindAll(CivicBudget.Application.Security.ClaimNames.DepartmentId).Count()); // ST and PR
    }

    [Fact]
    public async Task Seeding_again_fills_in_what_is_missing_and_duplicates_nothing()
    {
        // Its own database: this test deletes a seeded user.
        TestDatabase database = await fixture.CreateDatabaseAsync("CivicBudget_Reseed_" + Guid.NewGuid().ToString("N")[..8]);
        await using (AsyncServiceScope first = database.CreateScope())
        {
            await first.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
            UserManager<ApplicationUser> users = first.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("viewer@mapleridge.example"))!)).Succeeded);
        }

        await using AsyncServiceScope second = database.CreateScope();
        await second.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();

        ApplicationUser? viewer = await second.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("viewer@mapleridge.example");
        Assert.NotNull(viewer);
        await using CivicBudgetDbContext db = database.CreateContext(tenant: null);
        Assert.Equal(2, await db.Governments.CountAsync());

        // A government whose address was changed is still recognised as seeded: no second copy appears.
        Government pine = await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh");
        pine.SetPublicSlug("pine-hollow-renamed");
        await db.SaveChangesAsync();
        await using AsyncServiceScope third = database.CreateScope();
        await third.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
        Assert.Equal(2, await db.Governments.CountAsync());
    }
}
