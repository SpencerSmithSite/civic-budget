using CivicBudget.Application.Common;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>The setup services against real data: tenant scoping, uniqueness, and the rules that need the database.</summary>
[Collection(SqlServerTests.Name)]
public class SetupServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync("CivicBudget_Setup");

        await using AsyncServiceScope scope = _database.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        // By name, not slug: one test below changes Pine Hollow's slug, and tests in a class share the database.
        _mapleRidge = (await db.Governments.SingleAsync(g => g.Name == "Village of Maple Ridge")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.Name == "Pine Hollow Township")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Fund_codes_are_unique_per_government_not_globally()
    {
        // Both seeded tenants already have a "1000" General fund; that is allowed. A second "1000" in one tenant is not.
        await using AsyncServiceScope scope = _database.CreateScope(tenant: _mapleRidge);
        IFundService funds = scope.ServiceProvider.GetRequiredService<IFundService>();

        Result<Guid> duplicate = await funds.SaveAsync(new SaveFundRequest(null, "1000", "Another General", FundCategory.General, null));
        Result<Guid> fresh = await funds.SaveAsync(new SaveFundRequest(null, "2021", "State Highway", FundCategory.SpecialRevenue, null));

        Assert.True(duplicate.IsFailure);
        Assert.Equal(nameof(SaveFundRequest.Code), duplicate.Errors.Single().PropertyName);
        Assert.True(fresh.IsSuccess);

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        Assert.Equal(2, await db.Funds.IgnoreQueryFilters().CountAsync(f => f.Code == "1000"));
        Assert.Equal(_mapleRidge, (await db.Funds.IgnoreQueryFilters().SingleAsync(f => f.Id == fresh.Value)).GovernmentId);
    }

    [Fact]
    public async Task Lists_are_scoped_to_the_tenant_of_the_scope()
    {
        await using AsyncServiceScope maple = _database.CreateScope(tenant: _mapleRidge);
        await using AsyncServiceScope pine = _database.CreateScope(tenant: _pineHollow);

        IReadOnlyList<DepartmentDto> mapleDepartments = await maple.ServiceProvider.GetRequiredService<IDepartmentService>().ListAsync(includeInactive: true);
        IReadOnlyList<DepartmentDto> pineDepartments = await pine.ServiceProvider.GetRequiredService<IDepartmentService>().ListAsync(includeInactive: true);

        Assert.Equal(8, mapleDepartments.Count);
        Assert.Equal(2, pineDepartments.Count);
    }

    [Fact]
    public async Task Account_type_cannot_change_once_the_account_has_budget_lines()
    {
        await using AsyncServiceScope scope = _database.CreateScope(tenant: _mapleRidge);
        IAccountService accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        AccountDto salaries = (await accounts.ListAsync(includeInactive: false)).Single(a => a.Code == "5110");

        Result<Guid> renamed = await accounts.SaveAsync(new SaveAccountRequest(salaries.Id, "5110", "Salaries and Wages", AccountType.Expenditure, ReportingCategory.PersonalServices));
        Result<Guid> retyped = await accounts.SaveAsync(new SaveAccountRequest(salaries.Id, "5110", "Salaries", AccountType.Revenue, ReportingCategory.Taxes));

        Assert.True(renamed.IsSuccess);
        Assert.True(retyped.IsFailure);
        Assert.Equal(nameof(SaveAccountRequest.Type), retyped.Errors.Single().PropertyName);
    }

    [Fact]
    public async Task Fiscal_years_follow_the_governments_start_month_and_cannot_repeat()
    {
        await using AsyncServiceScope scope = _database.CreateScope(tenant: _pineHollow); // July start
        IFiscalYearService fiscalYears = scope.ServiceProvider.GetRequiredService<IFiscalYearService>();

        Result<Guid> created = await fiscalYears.CreateAsync(new CreateFiscalYearRequest(2028));
        Result<Guid> repeat = await fiscalYears.CreateAsync(new CreateFiscalYearRequest(2028));

        Assert.True(created.IsSuccess);
        Assert.True(repeat.IsFailure);
        FiscalYearDto fy2028 = (await fiscalYears.ListAsync()).Single(fy => fy.Year == 2028);
        Assert.Equal(new DateOnly(2027, 7, 1), fy2028.StartDate);
        Assert.Equal(new DateOnly(2028, 6, 30), fy2028.EndDate);
        Assert.Equal(0, fy2028.VersionCount);
    }

    [Fact]
    public async Task Public_slug_must_be_unique_across_governments()
    {
        await using AsyncServiceScope scope = _database.CreateScope(tenant: _pineHollow);
        IGovernmentSettingsService settings = scope.ServiceProvider.GetRequiredService<IGovernmentSettingsService>();

        Result taken = await settings.UpdateAsync(new UpdateGovernmentSettingsRequest("Pine Hollow Township", "maple-ridge-oh", AppropriationLimitMode.Warn, null));
        Result ok = await settings.UpdateAsync(new UpdateGovernmentSettingsRequest("Pine Hollow Township", "pine-hollow", AppropriationLimitMode.Block, "A small township."));

        Assert.True(taken.IsFailure);
        Assert.Equal(nameof(UpdateGovernmentSettingsRequest.PublicSlug), taken.Errors.Single().PropertyName);
        Assert.True(ok.IsSuccess);
        GovernmentSettingsDto after = await settings.GetAsync();
        Assert.Equal("pine-hollow", after.PublicSlug);
        Assert.Equal(AppropriationLimitMode.Block, after.AppropriationLimitMode);
    }
}
