using System.Reflection;
using CivicBudget.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace CivicBudget.Web.Tests.Security;

/// <summary>
/// Every admin page and the policy it must carry. The services check roles too, but a page that lost
/// its attribute would still show a Viewer a form they cannot use; this table makes dropping or
/// mistyping one a test failure. A new admin page fails until it is added here, on purpose.
/// </summary>
public class AdminPagePolicyTests
{
    private const string SignedIn = "(any signed-in user)";

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["/admin"] = SignedIn,
        ["/admin/profile-picture"] = SignedIn,
        ["/admin/my-department"] = Policies.CanViewBudget,
        ["/admin/budgets"] = Policies.CanViewBudget,
        ["/admin/budgets/{VersionId:guid}"] = Policies.CanViewBudget,
        ["/admin/budgets/{VersionId:guid}/departments"] = Policies.CanViewBudget,
        ["/admin/budgets/{VersionId:guid}/departments/{DepartmentId:guid}"] = Policies.CanViewBudget,
        ["/admin/budgets/{VersionId:guid}/import"] = Policies.CanImport,
        ["/admin/reports"] = Policies.CanViewBudget,
        ["/admin/reports/{VersionId:guid}/fund-summary"] = Policies.CanViewBudget,
        ["/admin/reports/{VersionId:guid}/department-detail"] = Policies.CanViewBudget,
        ["/admin/reports/{VersionId:guid}/category"] = Policies.CanViewBudget,
        ["/admin/funds"] = Policies.CanMaintainSetup,
        ["/admin/funds/new"] = Policies.CanMaintainSetup,
        ["/admin/funds/{Id:guid}"] = Policies.CanMaintainSetup,
        ["/admin/departments"] = Policies.CanMaintainSetup,
        ["/admin/departments/new"] = Policies.CanMaintainSetup,
        ["/admin/departments/{Id:guid}"] = Policies.CanMaintainSetup,
        ["/admin/accounts"] = Policies.CanMaintainSetup,
        ["/admin/accounts/new"] = Policies.CanMaintainSetup,
        ["/admin/accounts/{Id:guid}"] = Policies.CanMaintainSetup,
        ["/admin/fiscal-years"] = Policies.CanMaintainSetup,
        ["/admin/chart-sync"] = Policies.CanMaintainSetup,
        ["/admin/users"] = Policies.CanManageUsers,
        ["/admin/users/new"] = Policies.CanManageUsers,
        ["/admin/users/{Id}"] = Policies.CanManageUsers,
        ["/admin/settings"] = Policies.CanManageUsers,
    };

    public static TheoryData<string, string> AdminPages()
    {
        var data = new TheoryData<string, string>();
        foreach (Type page in typeof(CivicBudget.Web.Components.App).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("CivicBudget.Web.Components.Admin", StringComparison.Ordinal) == true))
        {
            foreach (RouteAttribute route in page.GetCustomAttributes<RouteAttribute>())
            {
                data.Add(page.Name, route.Template);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AdminPages))]
    public void Every_admin_page_requires_sign_in_and_its_policy(string page, string route)
    {
        Type type = typeof(CivicBudget.Web.Components.App).Assembly.GetTypes().Single(t => t.Name == page && t.Namespace?.StartsWith("CivicBudget.Web.Components.Admin", StringComparison.Ordinal) == true);
        List<AuthorizeAttribute> authorize = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();

        Assert.True(Expected.TryGetValue(route, out string? expected), $"{page} ({route}) is a new admin page: add it and its policy to {nameof(AdminPagePolicyTests)}.");
        Assert.NotEmpty(authorize); // at least the folder's [Authorize]: nobody signed out reaches an admin page
        string? policy = authorize.Select(a => a.Policy).SingleOrDefault(p => p is not null);
        Assert.Equal(expected, policy ?? SignedIn);
    }
}
