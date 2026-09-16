using CivicBudget.Domain.Common;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Domain.Tests;

public class GovernmentTests
{
    [Theory]
    [InlineData("maple-ridge-oh")]
    [InlineData("pinehollow")]
    [InlineData("city-of-42")]
    public void Accepts_url_safe_slugs(string slug)
    {
        var government = new Government("Test", GovernmentType.City, "OH", 1, slug);
        Assert.Equal(slug, government.PublicSlug);
    }

    [Theory]
    [InlineData("Maple Ridge")]     // space
    [InlineData("Maple-Ridge")]     // uppercase
    [InlineData("maple--ridge")]    // double hyphen
    [InlineData("-maple")]          // leading hyphen
    [InlineData("maple_ridge")]     // underscore
    [InlineData("")]
    public void Rejects_slugs_that_are_not_url_safe(string slug) =>
        Assert.Throws<DomainException>(() => new Government("Test", GovernmentType.City, "OH", 1, slug));

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Rejects_fiscal_year_start_month_outside_1_to_12(int month) =>
        Assert.Throws<DomainException>(() => new Government("Test", GovernmentType.City, "OH", month, "test"));

    [Fact]
    public void Normalizes_state_to_upper_case()
    {
        var government = new Government("Test", GovernmentType.Township, "oh", 1, "test");
        Assert.Equal("OH", government.State);
    }

    [Theory]
    [InlineData("Ohio")]
    [InlineData("O")]
    [InlineData("4H")]
    public void Rejects_states_that_are_not_two_letter_codes(string state) =>
        Assert.Throws<DomainException>(() => new Government("Test", GovernmentType.City, state, 1, "test"));

    [Fact]
    public void Defaults_to_blocking_over_appropriated_funds()
    {
        var government = new Government("Test", GovernmentType.City, "OH", 1, "test");
        Assert.Equal(AppropriationLimitMode.Block, government.AppropriationLimitMode);
    }
}
