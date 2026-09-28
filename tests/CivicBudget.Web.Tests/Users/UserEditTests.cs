using CivicBudget.Application.Common;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Application.Users;
using CivicBudget.Web.Components.Admin.Users;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Users;

/// <summary>
/// The user form says what the chosen role grants before anyone saves it, and changes as the role
/// changes, so an administrator never assigns access by guessing what a role name means.
/// </summary>
public class UserEditTests : BunitContext
{
    public UserEditTests()
    {
        Services.AddSingleton<IUserAdminService>(new FakeUserAdmin());
        Services.AddSingleton<IDepartmentService>(new FakeDepartments());
        Services.AddSingleton<IUserAvatarService>(new FakeAvatars());
        Services.AddSingleton<IAccountEmailService>(new FakeAccountEmail());
        Services.AddSingleton<ISignInSecurityService>(new FakeSignInSecurity());
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
    }

    [Fact]
    public void A_new_user_is_emailed_a_link_unless_the_administrator_chooses_a_temporary_password()
    {
        IRenderedComponent<UserEdit> page = Render<UserEdit>();
        page.WaitForAssertion(() => page.Find("#first-link"));

        Assert.True(page.Find("#first-link").HasAttribute("checked"));
        Assert.Empty(page.FindAll("#password"));

        page.Find("#first-temp").Change(true);

        Assert.Single(page.FindAll("#password"));
    }

    [Fact]
    public void The_access_card_follows_the_chosen_role()
    {
        IRenderedComponent<UserEdit> page = Render<UserEdit>();

        page.WaitForAssertion(() => Assert.Contains("What the Viewer role allows", page.Markup));
        Assert.Contains("read only", page.Find(".cb-access-list").TextContent);

        page.Find("#role").Change(Roles.DepartmentHead);

        Assert.Contains("What the Department User role allows", page.Markup);
        Assert.Contains("Only the departments ticked here", page.Find(".cb-access-list").TextContent);
        // The departments to tick appear with the role that uses them, labelled as a field, not a heading.
        Assert.Contains(page.FindAll("fieldset legend.form-label"), l => l.TextContent == "Departments this user may edit");
        Assert.Single(page.FindAll(".cb-access-list .cb-access-no"));
    }

    private sealed class FakeAccountEmail : IAccountEmailService
    {
        public Task RequestPasswordResetAsync(string email, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Result> ResetPasswordAsync(string userId, string code, string newPassword, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> SendWelcomeAsync(string userId, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }

    private sealed class FakeSignInSecurity : ISignInSecurityService
    {
        public Task<SignInSecurityDto> GetAsync(CancellationToken ct = default) => Task.FromResult(new SignInSecurityDto(false, 0, 1));
        public Task<Result> SetRequireMfaAsync(bool require, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> ResetTwoFactorAsync(string userId, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }

    private sealed class FakeDepartments : IDepartmentService
    {
        public Task<IReadOnlyList<DepartmentDto>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DepartmentDto>>([new DepartmentDto(Guid.NewGuid(), "110", "Police", null, true)]);

        public Task<DepartmentDto?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<Guid>> SaveAsync(SaveDepartmentRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeUserAdmin : IUserAdminService
    {
        public Task<IReadOnlyList<UserSummaryDto>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<UserSummaryDto?> GetAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<string>> CreateAsync(CreateUserRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> UpdateAsync(UpdateUserRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> SetLockedOutAsync(string id, bool isLockedOut, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAvatars : IUserAvatarService
    {
        public event Action? Changed { add { } remove { } }

        public Task<UserAvatarDto?> GetAsync(string userId, CancellationToken ct = default) => Task.FromResult<UserAvatarDto?>(null);
        public Task<long?> GetVersionAsync(string userId, CancellationToken ct = default) => Task.FromResult<long?>(null);
        public Task<Result> SetOwnAsync(byte[] data, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> RemoveAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
