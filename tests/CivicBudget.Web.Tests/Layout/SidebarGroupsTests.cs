using CivicBudget.Web.Components.Layout;

namespace CivicBudget.Web.Tests.Layout;

/// <summary>
/// Which sidebar group a page belongs to (so opening a page unfolds its group), and the cookie that
/// remembers the folded groups.
/// </summary>
public class SidebarGroupsTests
{
    [Theory]
    [InlineData("admin", SidebarGroups.Budget)]
    [InlineData("admin/", SidebarGroups.Budget)]
    [InlineData("admin/budgets/0199aaaa-0000-7000-8000-000000000000/plan", SidebarGroups.Budget)]
    [InlineData("admin/budgets#publishing", SidebarGroups.Budget)]
    [InlineData("admin/reports/x/budget-vs-actual?fund=1000", SidebarGroups.Budget)]
    [InlineData("admin/my-department", SidebarGroups.Budget)]
    [InlineData("admin/funds", SidebarGroups.Setup)]
    [InlineData("admin/report-settings", SidebarGroups.Setup)]
    [InlineData("admin/personnel-sync", SidebarGroups.Setup)]
    [InlineData("admin/settings", SidebarGroups.Administration)] // not confused with report-settings
    [InlineData("admin/users/abc", SidebarGroups.Administration)]
    [InlineData("admin/security-log", SidebarGroups.Administration)]
    [InlineData("admin/profile-picture", null)]
    [InlineData("transparency/maple-ridge-oh", null)]
    [InlineData("", null)]
    public void Each_page_belongs_to_the_group_whose_links_lead_to_it(string path, string? group) =>
        Assert.Equal(group, SidebarGroups.For(path));

    [Fact]
    public void The_cookie_round_trips_and_ignores_anything_that_is_not_a_group()
    {
        HashSet<string> folded = SidebarGroups.Parse("setup.nonsense.admin..<script>");

        Assert.Equal([SidebarGroups.Administration, SidebarGroups.Setup], folded.Order());
        Assert.Equal("setup.admin", SidebarGroups.Format(folded));
        Assert.Empty(SidebarGroups.Parse(null));
        Assert.Equal("", SidebarGroups.Format([]));
    }
}
