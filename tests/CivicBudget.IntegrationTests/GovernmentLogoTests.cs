using System.Security.Claims;
using CivicBudget.Application.Common;
using CivicBudget.Application.Portal;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The portal logo: an administrator sets it under settings, the portal serves it for a government
/// with a published budget and carries its version on the budget header, and nobody else may change it.
/// </summary>
[Collection(SqlServerTests.Name)]
public class GovernmentLogoTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9];

    private TestDatabase _database = null!;
    private Guid _mapleRidge;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateDatabaseAsync("CivicBudget_Logo_" + Guid.NewGuid().ToString("N")[..8]);
        await using AsyncServiceScope scope = _database.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();
        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task An_administrator_sets_the_logo_and_the_portal_serves_it()
    {
        await using AsyncServiceScope anonymous = _database.CreateScope();
        ISnapshotQueryService portal = anonymous.ServiceProvider.GetRequiredService<ISnapshotQueryService>();
        Assert.Null(await portal.GetLogoAsync("maple-ridge-oh"));
        Assert.Null((await portal.GetBudgetAsync("maple-ridge-oh", null))!.LogoVersion);

        await using AsyncServiceScope admin = As(Roles.Admin);
        IGovernmentLogoService logos = admin.ServiceProvider.GetRequiredService<IGovernmentLogoService>();
        Result set = await logos.SetAsync(Png, "image/png");
        Assert.True(set.IsSuccess, string.Join("; ", set.Errors.Select(e => e.Message)));
        Assert.Equal(Png, (await logos.GetAsync())!.Data);

        PortalLogoDto served = (await portal.GetLogoAsync("maple-ridge-oh"))!;
        Assert.Equal(Png, served.Data);
        Assert.Equal(served.UpdatedAtUtc.UtcTicks, (await portal.GetBudgetAsync("maple-ridge-oh", null))!.LogoVersion);
        Assert.Null(await portal.GetLogoAsync("nowhere-oh")); // no published budget, no public face

        Assert.True((await logos.RemoveAsync()).IsSuccess);
        Assert.Null(await portal.GetLogoAsync("maple-ridge-oh"));
    }

    [Fact]
    public async Task Only_an_administrator_changes_it_and_the_file_is_checked()
    {
        await using AsyncServiceScope officer = As(Roles.FinanceDirector);
        Assert.True((await officer.ServiceProvider.GetRequiredService<IGovernmentLogoService>().SetAsync(Png, "image/png")).IsFailure);

        await using AsyncServiceScope admin = As(Roles.Admin);
        IGovernmentLogoService logos = admin.ServiceProvider.GetRequiredService<IGovernmentLogoService>();
        Assert.True((await logos.SetAsync(Png, "image/gif")).IsFailure);
        Assert.True((await logos.SetAsync(new byte[GovernmentLogo.MaxBytes + 1], "image/png")).IsFailure);
        Assert.Null(await logos.GetAsync());
    }

    private AsyncServiceScope As(string role)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-" + role));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }
}
