using System.Security.Claims;
using CivicBudget.Application.Auditing;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>The audit trail as written by the interceptor during ordinary service calls.</summary>
[Collection(SqlServerTests.Name)]
public class AuditInterceptorTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Guid _mapleRidge;

    public async Task InitializeAsync()
    {
        // One database per test: several tests write audit rows, and one asserts that only the seed has.
        _database = await fixture.CreateDatabaseAsync("CivicBudget_Audit_" + Guid.NewGuid().ToString("N")[..8]);
        await using AsyncServiceScope scope = _database.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AsyncServiceScope AsFinanceDirector(string userId = "user-fd", string displayName = "Dana Whitfield")
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, displayName));
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.FinanceDirector));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    [Fact]
    public async Task Seeding_writes_a_created_entry_per_audited_row_attributed_to_system()
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);

        int lines = await db.BudgetLines.CountAsync();
        int createdLineEntries = await db.AuditEntries.CountAsync(a => a.EntityName == nameof(BudgetLine) && a.Kind == AuditKind.Created);

        Assert.Equal(lines, createdLineEntries);
        Assert.All(await db.AuditEntries.Take(50).ToListAsync(), a => Assert.Equal("system", a.UserId));
    }

    [Fact]
    public async Task Changing_an_amount_records_old_and_new_values_with_the_user_and_utc_time()
    {
        await using AsyncServiceScope scope = AsFinanceDirector();
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        Guid draft = (await _database.CreateContext(_mapleRidge).BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        BudgetLineDto line = (await entry.GetWorkspaceAsync(draft))!.Lines[0];
        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await entry.UpdateLineAmountAsync(draft, line.Id, line.Amount + 250m);

        IReadOnlyList<AuditEntryDto> history = await scope.ServiceProvider.GetRequiredService<IAuditQueryService>().GetHistoryAsync(nameof(BudgetLine), line.Id);
        AuditEntryDto change = history[0]; // newest first
        Assert.Equal(AuditKind.FieldChanged, change.Kind);
        Assert.Equal(nameof(BudgetLine.Amount), change.PropertyName);
        Assert.Equal(line.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), change.OldValue);
        Assert.Equal((line.Amount + 250m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), change.NewValue);
        Assert.Equal("Dana Whitfield", change.UserName);
        Assert.True(change.TimestampUtc >= before && change.TimestampUtc <= DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(AuditKind.Created, history[^1].Kind);
    }

    [Fact]
    public async Task Saving_without_a_real_change_writes_nothing()
    {
        await using AsyncServiceScope scope = AsFinanceDirector();
        IBudgetEntryService entry = scope.ServiceProvider.GetRequiredService<IBudgetEntryService>();
        Guid draft = (await _database.CreateContext(_mapleRidge).BudgetVersions.SingleAsync(v => v.Status == BudgetStatus.Draft)).Id;
        BudgetLineDto line = (await entry.GetWorkspaceAsync(draft))!.Lines[1];
        int before = await _database.CreateContext(_mapleRidge).AuditEntries.CountAsync(a => a.EntityId == line.Id);

        await entry.UpdateLineAmountAsync(draft, line.Id, line.Amount); // same value

        Assert.Equal(before, await _database.CreateContext(_mapleRidge).AuditEntries.CountAsync(a => a.EntityId == line.Id));
    }

    [Fact]
    public async Task Reference_data_edits_are_audited_field_by_field()
    {
        await using AsyncServiceScope scope = AsFinanceDirector(userId: "user-admin", displayName: "Alex Rivera");
        IFundService funds = scope.ServiceProvider.GetRequiredService<IFundService>();
        FundDto water = (await funds.ListAsync(includeInactive: false)).Single(f => f.Code == "5101");

        await funds.SaveAsync(new SaveFundRequest(water.Id, water.Code, "Water Utility Fund", water.Category, water.Description));

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        AuditEntry change = await db.AuditEntries.SingleAsync(a => a.EntityId == water.Id && a.Kind == AuditKind.FieldChanged);
        Assert.Equal(nameof(Fund.Name), change.PropertyName);
        Assert.Equal("Water", change.OldValue);
        Assert.Equal("Water Utility Fund", change.NewValue);
        Assert.Equal("Alex Rivera", change.UserName);
    }

    [Fact]
    public async Task Audit_rows_are_tenant_owned_and_invisible_to_the_other_government()
    {
        await using CivicBudgetDbContext maple = _database.CreateContext(_mapleRidge);
        await using CivicBudgetDbContext anonymous = _database.CreateContext(tenant: null);

        Assert.True(await maple.AuditEntries.AnyAsync());
        Assert.False(await anonymous.AuditEntries.AnyAsync());
    }

    [Fact]
    public async Task Changing_the_account_number_format_is_recorded_field_by_field_on_the_government()
    {
        await using (AsyncServiceScope admin = _database.CreateScopeAs(Roles.Admin, _mapleRidge))
        {
            IGovernmentSettingsService settings = admin.ServiceProvider.GetRequiredService<IGovernmentSettingsService>();
            GovernmentSettingsDto current = await settings.GetAsync();
            Result saved = await settings.UpdateAsync(new UpdateGovernmentSettingsRequest(current.Name, current.PublicSlug, current.AppropriationLimitMode, current.Description,
                3, current.AccountNumberFormat.DepartmentWidth, current.AccountNumberFormat.ObjectWidth, ".", current.AccountNumberFormat.DepartmentLabel));
            Assert.True(saved.IsSuccess);
        }

        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        List<AuditEntry> changes = await db.AuditEntries
            .Where(a => a.EntityName == "Government" && a.PropertyName != null && a.PropertyName.StartsWith("AccountNumberFormat."))
            .ToListAsync();

        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, a => a.PropertyName == "AccountNumberFormat.FundWidth" && a.OldValue == "4" && a.NewValue == "3");
        Assert.Contains(changes, a => a.PropertyName == "AccountNumberFormat.Separator" && a.OldValue == "-" && a.NewValue == ".");
        Assert.All(changes, a => Assert.Equal(_mapleRidge, a.EntityId));
    }
}
