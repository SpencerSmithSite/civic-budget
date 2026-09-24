using System.Security.Claims;
using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Users;
using CivicBudget.Infrastructure.Identity;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// Profile pictures: a user sets their own, the list carries the version, the image endpoint's read
/// is scoped to the government, and only an administrator removes someone else's.
/// </summary>
[Collection(SqlServerTests.Name)]
public class UserAvatarServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;
    private string _financeId = null!;
    private string _policeId = null!;
    private string _mapleAdminId = null!;

    public async Task InitializeAsync()
    {
        _database = await fixture.CreateSeededDatabaseAsync("CivicBudget_Avatars");

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;
        _financeId = (await db.Users.SingleAsync(u => u.Email == "finance@mapleridge.example")).Id;
        _policeId = (await db.Users.SingleAsync(u => u.Email == "police@mapleridge.example")).Id;
        _mapleAdminId = (await db.Users.SingleAsync(u => u.Email == "admin@mapleridge.example")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_user_sets_their_own_picture_and_everyone_in_the_government_can_read_it()
    {
        await using AsyncServiceScope finance = As(_financeId, Roles.FinanceDirector, _mapleRidge);
        IUserAvatarService avatars = Avatars(finance);
        Assert.Null(await avatars.GetVersionAsync(_financeId));

        Result set = await avatars.SetOwnAsync(Png, "image/png");
        Assert.True(set.IsSuccess, string.Join("; ", set.Errors.Select(e => e.Message)));

        long? version = await avatars.GetVersionAsync(_financeId);
        Assert.NotNull(version);
        UserAvatarDto stored = (await avatars.GetAsync(_financeId))!;
        Assert.Equal(Png, stored.Data);
        Assert.Equal("image/png", stored.ContentType);
        Assert.Equal(version, stored.UpdatedAtUtc.UtcTicks); // the URL version is the upload time

        // A colleague sees the version on the user list and can fetch the bytes; another government cannot.
        await using AsyncServiceScope admin = As(_mapleAdminId, Roles.Admin, _mapleRidge);
        Assert.Equal(version, (await admin.ServiceProvider.GetRequiredService<IUserAdminService>().GetAsync(_financeId))!.AvatarVersion);
        Assert.NotNull(await Avatars(admin).GetAsync(_financeId));

        await using AsyncServiceScope pine = As("pine-admin", Roles.Admin, _pineHollow);
        Assert.Null(await Avatars(pine).GetAsync(_financeId));
        Assert.Null(await Avatars(pine).GetVersionAsync(_financeId));
    }

    [Fact]
    public async Task Type_and_size_are_checked()
    {
        await using AsyncServiceScope finance = As(_financeId, Roles.FinanceDirector, _mapleRidge);
        IUserAvatarService avatars = Avatars(finance);

        Assert.True((await avatars.SetOwnAsync(Png, "image/gif")).IsFailure);
        Assert.True((await avatars.SetOwnAsync([], "image/png")).IsFailure);
        Assert.True((await avatars.SetOwnAsync(new byte[UserAvatar.MaxBytes + 1], "image/png")).IsFailure);
        Assert.Null(await avatars.GetVersionAsync(_financeId));
    }

    [Fact]
    public async Task Only_an_administrator_removes_someone_elses_picture()
    {
        await using AsyncServiceScope finance = As(_financeId, Roles.FinanceDirector, _mapleRidge);
        Assert.True((await Avatars(finance).SetOwnAsync(Png, "image/png")).IsSuccess);

        await using AsyncServiceScope chief = As(_policeId, Roles.DepartmentHead, _mapleRidge);
        Assert.True((await Avatars(chief).RemoveAsync(_financeId)).IsFailure);

        await using AsyncServiceScope pine = As("pine-admin", Roles.Admin, _pineHollow);
        Assert.True((await Avatars(pine).RemoveAsync(_financeId)).IsFailure); // right role, wrong government

        await using AsyncServiceScope admin = As(_mapleAdminId, Roles.Admin, _mapleRidge);
        Assert.True((await Avatars(admin).RemoveAsync(_financeId)).IsSuccess);
        Assert.Null(await Avatars(admin).GetAsync(_financeId));

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        Assert.Null((await db.Users.SingleAsync(u => u.Id == _financeId)).AvatarUpdatedAtUtc);
        Assert.False(await db.UserAvatars.AnyAsync(a => a.UserId == _financeId));
    }

    [Fact]
    public async Task A_user_removes_their_own_and_the_scope_cache_follows()
    {
        await using AsyncServiceScope finance = As(_financeId, Roles.FinanceDirector, _mapleRidge);
        IUserAvatarService avatars = Avatars(finance);
        int changed = 0;
        avatars.Changed += () => changed++;

        Assert.True((await avatars.SetOwnAsync(Png, "image/png")).IsSuccess);
        Assert.NotNull(await avatars.GetVersionAsync(_financeId));
        Assert.True((await avatars.RemoveAsync(_financeId)).IsSuccess);
        Assert.Null(await avatars.GetVersionAsync(_financeId));
        Assert.Equal(2, changed);
    }

    private static IUserAvatarService Avatars(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IUserAvatarService>();

    private AsyncServiceScope As(string userId, string role, Guid governmentId)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        identity.AddClaim(new Claim(ClaimNames.DisplayName, "Test " + role));
        identity.AddClaim(new Claim(ClaimTypes.Role, role));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, governmentId.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }
}
