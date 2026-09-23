using CivicBudget.Web.Components.Account;

namespace CivicBudget.Web.Tests.Security;

/// <summary>The sign-in return URL may only point back into the site.</summary>
public class LocalUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    [InlineData("/admin/budgets?view=dept")]
    [InlineData("~/admin")]
    [InlineData("admin")]
    [InlineData("Account/Lockout")]
    public void Accepts_paths_on_this_site(string url) => Assert.True(LocalUrl.IsLocal(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.example")]
    [InlineData("//evil.example/login")]
    [InlineData("/\\evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("~//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/admin\r\nLocation: https://evil.example")]
    public void Refuses_anything_that_leaves_the_site(string? url) => Assert.False(LocalUrl.IsLocal(url));
}
