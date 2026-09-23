using System.Security.Claims;
using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Users;
using CivicBudget.Infrastructure.Identity;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// UserAdminService through real Identity stores. Identity tables are outside the tenant query filter,
/// so the tests that matter most here are the ones proving the service scopes by government anyway.
/// </summary>
[Collection(SqlServerTests.Name)]
public class UserAdminServiceTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string Password = "Another-Strong-Pass-9!";

    private TestDatabase _database = null!;
    private Guid _mapleRidge;
    private Guid _pineHollow;
    private string _mapleAdminId = null!;

    public async Task InitializeAsync()
    {
        // One database per test: tests here add lines or users and others assert exact counts, so a
        // shared database made the result depend on the order xUnit happened to run them in.
        _database = await fixture.CreateDatabaseAsync("CivicBudget_UserAdmin_" + Guid.NewGuid().ToString("N")[..8]);

        await using AsyncServiceScope scope = _database.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync();

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        _mapleRidge = (await db.Governments.SingleAsync(g => g.PublicSlug == "maple-ridge-oh")).Id;
        _pineHollow = (await db.Governments.SingleAsync(g => g.PublicSlug == "pine-hollow-twp-oh")).Id;
        _mapleAdminId = (await db.Users.SingleAsync(u => u.Email == "admin@mapleridge.example")).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A scope acting as the Maple Ridge admin, the way a signed-in circuit would.</summary>
    private AsyncServiceScope AsMapleAdmin()
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, _mapleAdminId));
        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Admin));
        identity.AddClaim(new Claim(ClaimNames.GovernmentId, _mapleRidge.ToString()));
        return _database.CreateScope(user: new ClaimsPrincipal(identity));
    }

    [Fact]
    public async Task Lists_only_users_of_the_current_government()
    {
        await using AsyncServiceScope scope = AsMapleAdmin();
        IReadOnlyList<UserSummaryDto> users = await scope.ServiceProvider.GetRequiredService<IUserAdminService>().ListAsync();

        Assert.Equal(5, users.Count);
        Assert.DoesNotContain(users, u => u.Email.EndsWith("pinehollow.example", StringComparison.Ordinal));
        UserSummaryDto streets = users.Single(u => u.Email == "streets@mapleridge.example");
        Assert.Equal(Roles.DepartmentHead, streets.Role);
        Assert.Equal(["310", "620"], streets.DepartmentCodes);
    }

    [Fact]
    public async Task Cannot_read_or_edit_a_user_from_another_government()
    {
        await using AsyncServiceScope scope = AsMapleAdmin();
        IUserAdminService service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

        await using CivicBudgetDbContext db = _database.CreateContext(tenant: null);
        string pineAdminId = (await db.Users.SingleAsync(u => u.GovernmentId == _pineHollow)).Id;

        Assert.Null(await service.GetAsync(pineAdminId));
        Result update = await service.UpdateAsync(new UpdateUserRequest(pineAdminId, "Hijacked", Roles.Admin, []));
        Assert.True(update.IsFailure);
        Result lockOut = await service.SetLockedOutAsync(pineAdminId, true);
        Assert.True(lockOut.IsFailure);
    }

    [Fact]
    public async Task Creates_a_department_head_with_departments_and_a_role()
    {
        await using AsyncServiceScope scope = AsMapleAdmin();
        IUserAdminService service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        Guid police = await DepartmentIdAsync("110");

        Result<string> created = await service.CreateAsync(new CreateUserRequest(
            "sergeant@mapleridge.example", "Sgt. Casey Nguyen", Password, Roles.DepartmentHead, [police]));

        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(e => e.Message)));
        UserSummaryDto? user = await service.GetAsync(created.Value);
        Assert.NotNull(user);
        Assert.Equal(Roles.DepartmentHead, user.Role);
        Assert.Equal(["110"], user.DepartmentCodes);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser stored = (await userManager.FindByIdAsync(created.Value))!;
        Assert.Equal(_mapleRidge, stored.GovernmentId);
        Assert.True(await userManager.CheckPasswordAsync(stored, Password));
        Assert.True(stored.MustChangePassword); // the administrator's password is temporary

        // Administration is audited under the acting administrator's name.
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityName == "User" && a.Description!.Contains("Created user sergeant@mapleridge.example as Department User")));
    }

    [Fact]
    public async Task Rejects_departments_that_belong_to_another_government()
    {
        await using AsyncServiceScope scope = AsMapleAdmin();
        IUserAdminService service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

        await using CivicBudgetDbContext db = _database.CreateContext(_pineHollow);
        Guid pineRoad = (await db.Departments.SingleAsync(d => d.Code == "610")).Id;

        Result<string> created = await service.CreateAsync(new CreateUserRequest(
            "smuggler@mapleridge.example", "Smuggler", Password, Roles.DepartmentHead, [pineRoad]));

        Assert.True(created.IsFailure);
        Assert.Contains(created.Errors, e => e.PropertyName == nameof(CreateUserRequest.DepartmentIds));
    }

    [Fact]
    public async Task Changing_role_replaces_departments_and_rotates_the_security_stamp()
    {
        await using AsyncServiceScope scope = AsMapleAdmin();
        IUserAdminService service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser chief = (await userManager.FindByEmailAsync("police@mapleridge.example"))!;
        string stampBefore = await userManager.GetSecurityStampAsync(chief);

        Result updated = await service.UpdateAsync(new UpdateUserRequest(chief.Id, "Morgan Hale", Roles.Viewer, []));

        Assert.True(updated.IsSuccess, string.Join("; ", updated.Errors.Select(e => e.Message)));
        UserSummaryDto after = (await service.GetAsync(chief.Id))!;
        Assert.Equal(Roles.Viewer, after.Role);
        Assert.Empty(after.DepartmentCodes);
        Assert.NotEqual(stampBefore, await userManager.GetSecurityStampAsync((await userManager.FindByIdAsync(chief.Id))!));
    }

    [Fact]
    public async Task An_admin_cannot_lock_themselves_out_or_drop_their_own_admin_role()
    {
        await using AsyncServiceScope scope = AsMapleAdmin();
        IUserAdminService service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

        Result lockSelf = await service.SetLockedOutAsync(_mapleAdminId, true);
        Result demoteSelf = await service.UpdateAsync(new UpdateUserRequest(_mapleAdminId, "Alex Rivera", Roles.Viewer, []));

        Assert.True(lockSelf.IsFailure);
        Assert.True(demoteSelf.IsFailure);
        Assert.Contains(demoteSelf.Errors, e => e.PropertyName == nameof(UpdateUserRequest.Role));
    }

    [Fact]
    public async Task Reset_password_and_lockout_work_through_identity()
    {
        await using AsyncServiceScope scope = AsMapleAdmin();
        IUserAdminService service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser viewer = (await userManager.FindByEmailAsync("viewer@mapleridge.example"))!;

        Result reset = await service.ResetPasswordAsync(new ResetPasswordRequest(viewer.Id, Password));
        Assert.True(reset.IsSuccess, string.Join("; ", reset.Errors.Select(e => e.Message)));
        ApplicationUser afterReset = (await userManager.FindByIdAsync(viewer.Id))!;
        Assert.True(await userManager.CheckPasswordAsync(afterReset, Password));
        Assert.True(afterReset.MustChangePassword);                                           // a reset password is temporary too
        ClaimsPrincipal principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(afterReset);
        Assert.True(principal.HasClaim(ClaimNames.MustChangePassword, "1"));                    // and the claim carries it to the middleware

        Assert.True((await service.SetLockedOutAsync(viewer.Id, true)).IsSuccess);
        Assert.True((await service.GetAsync(viewer.Id))!.IsLockedOut);
        Assert.True((await service.SetLockedOutAsync(viewer.Id, false)).IsSuccess);
        Assert.False((await service.GetAsync(viewer.Id))!.IsLockedOut);
    }

    private async Task<Guid> DepartmentIdAsync(string code)
    {
        await using CivicBudgetDbContext db = _database.CreateContext(_mapleRidge);
        return (await db.Departments.SingleAsync(d => d.Code == code)).Id;
    }
}
