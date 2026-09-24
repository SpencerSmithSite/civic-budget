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
    [Fact]
    public void The_access_card_follows_the_chosen_role()
    {
        Services.AddSingleton<IUserAdminService>(new FakeUserAdmin());
        Services.AddSingleton<IDepartmentService>(new FakeDepartments());
        Services.AddSingleton<IUserAvatarService>(new FakeAvatars());
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();

        IRenderedComponent<UserEdit> page = Render<UserEdit>();

        page.WaitForAssertion(() => Assert.Contains("What the Viewer role allows", page.Markup));
        Assert.Contains("read only", page.Find(".cb-access-list").TextContent);

        page.Find("#role").Change(Roles.DepartmentHead);

        Assert.Contains("What the Department User role allows", page.Markup);
        Assert.Contains("Only the departments ticked here", page.Find(".cb-access-list").TextContent);
        // The departments to tick appear with the role that uses them, labelled as a field, not a heading.
        Assert.Equal("Departments this user may edit", page.Find("fieldset legend.form-label").TextContent);
        Assert.Single(page.FindAll(".cb-access-list .cb-access-no"));
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
