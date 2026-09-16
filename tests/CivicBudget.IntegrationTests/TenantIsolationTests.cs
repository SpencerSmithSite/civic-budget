using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CivicBudget.IntegrationTests;

/// <summary>SPEC §2 / ADR-0004: one tenant can neither read nor write another tenant's rows.</summary>
[Collection(SqlServerTests.Name)]
public class TenantIsolationTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync("CivicBudget_Tenancy");
        string suffix = Guid.NewGuid().ToString("N")[..8];

        // Governments are the tenant, not tenant-owned, so they save without a tenant set.
        await using (CivicBudgetDbContext db = _database.CreateContext(tenant: null))
        {
            var maple = new Government("Maple " + suffix, GovernmentType.Village, "OH", 1, "maple-" + suffix);
            var pine = new Government("Pine " + suffix, GovernmentType.Township, "OH", 7, "pine-" + suffix);
            db.Governments.AddRange(maple, pine);
            await db.SaveChangesAsync();
            _mapleRidge = maple.Id;
            _pineHollow = pine.Id;
        }

        await using (CivicBudgetDbContext db = _database.CreateContext(_mapleRidge))
        {
            db.Funds.Add(new Fund(_mapleRidge, "1000", "General", FundCategory.General));
            db.Funds.Add(new Fund(_mapleRidge, "2011", "SCM&R", FundCategory.SpecialRevenue));
            await db.SaveChangesAsync();
        }

        await using (CivicBudgetDbContext db = _database.CreateContext(_pineHollow))
        {
            db.Funds.Add(new Fund(_pineHollow, "1000", "General", FundCategory.General));
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_tenant_sees_only_its_own_rows()
    {
        await using CivicBudgetDbContext asMaple = _database.CreateContext(_mapleRidge);
        await using CivicBudgetDbContext asPine = _database.CreateContext(_pineHollow);

        List<Fund> mapleFunds = await asMaple.Funds.ToListAsync();
        List<Fund> pineFunds = await asPine.Funds.ToListAsync();

        Assert.Equal(2, mapleFunds.Count);
        Assert.All(mapleFunds, f => Assert.Equal(_mapleRidge, f.GovernmentId));
        Assert.Single(pineFunds);
        Assert.Equal(_pineHollow, pineFunds[0].GovernmentId);
    }

    [Fact]
    public async Task The_filter_applies_to_lookups_by_primary_key_too()
    {
        Guid pineFundId;
        await using (CivicBudgetDbContext asPine = _database.CreateContext(_pineHollow))
        {
            pineFundId = (await asPine.Funds.SingleAsync()).Id;
        }

        await using CivicBudgetDbContext asMaple = _database.CreateContext(_mapleRidge);
        Assert.Null(await asMaple.Funds.FirstOrDefaultAsync(f => f.Id == pineFundId));
    }

    [Fact]
    public async Task No_tenant_means_no_rows_not_all_rows()
    {
        await using CivicBudgetDbContext anonymous = _database.CreateContext(tenant: null);

        Assert.Empty(await anonymous.Funds.ToListAsync());
        Assert.NotEmpty(await anonymous.Governments.ToListAsync()); // governments themselves are not filtered
    }

    [Fact]
    public async Task Writing_another_tenants_row_is_refused()
    {
        await using CivicBudgetDbContext asMaple = _database.CreateContext(_mapleRidge);
        asMaple.Funds.Add(new Fund(_pineHollow, "9999", "Smuggled", FundCategory.General));

        await Assert.ThrowsAsync<TenantIsolationException>(() => asMaple.SaveChangesAsync());
    }

    [Fact]
    public async Task Writing_tenant_owned_rows_with_no_tenant_is_refused()
    {
        await using CivicBudgetDbContext anonymous = _database.CreateContext(tenant: null);
        anonymous.Funds.Add(new Fund(_mapleRidge, "9998", "Orphan", FundCategory.General));

        await Assert.ThrowsAsync<TenantIsolationException>(() => anonymous.SaveChangesAsync());
    }

    [Fact]
    public async Task Every_tenant_owned_entity_has_a_query_filter()
    {
        // The filter is applied by reflection over ITenantOwned; this proves nothing slipped through.
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);

        IEnumerable<IEntityType> tenantOwned = db.Model.GetEntityTypes()
            .Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType));

        Assert.NotEmpty(tenantOwned);
        Assert.All(tenantOwned, e => Assert.True(e.GetDeclaredQueryFilters().Count > 0, $"{e.ClrType.Name} has no tenant query filter."));
    }
}
