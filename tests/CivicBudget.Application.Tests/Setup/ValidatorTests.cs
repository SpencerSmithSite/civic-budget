using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Application.Users;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using FluentValidation.Results;

namespace CivicBudget.Application.Tests.Setup;

/// <summary>Validators are plain classes, so these run with no database and no UI.</summary>
public class ValidatorTests
{
    [Fact]
    public void Fund_request_requires_code_and_name_within_limits()
    {
        var validator = new SaveFundRequestValidator();

        Assert.True(validator.Validate(new SaveFundRequest(null, "1000", "General", FundCategory.General, null)).IsValid);

        ValidationResult result = validator.Validate(new SaveFundRequest(null, "", new string('x', 151), FundCategory.General, null));
        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Account_request_rejects_a_category_that_does_not_match_the_type()
    {
        var validator = new SaveAccountRequestValidator();

        ValidationResult result = validator.Validate(new SaveAccountRequest(null, "5110", "Salaries", AccountType.Revenue, ReportingCategory.PersonalServices));

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal("Category", failure.PropertyName);
        Assert.True(validator.Validate(new SaveAccountRequest(null, "5110", "Salaries", AccountType.Expenditure, ReportingCategory.PersonalServices)).IsValid);
    }

    [Theory]
    [InlineData("maple-ridge-oh", true)]
    [InlineData("Maple Ridge", false)]
    [InlineData("", false)]
    public void Settings_request_checks_the_public_slug_format(string slug, bool expected)
    {
        var validator = new UpdateGovernmentSettingsRequestValidator();
        Assert.Equal(expected, validator.Validate(new UpdateGovernmentSettingsRequest("Village", slug, AppropriationLimitMode.Block, null)).IsValid);
    }

    [Fact]
    public void Department_heads_need_departments_and_nobody_else_may_have_them()
    {
        var validator = new CreateUserRequestValidator();
        const string password = "Long-Enough-Pass1!";

        Assert.False(validator.Validate(new CreateUserRequest("a@b.example", "A", password, Roles.DepartmentHead, [])).IsValid);
        Assert.True(validator.Validate(new CreateUserRequest("a@b.example", "A", password, Roles.DepartmentHead, [Guid.CreateVersion7()])).IsValid);
        Assert.False(validator.Validate(new CreateUserRequest("a@b.example", "A", password, Roles.Viewer, [Guid.CreateVersion7()])).IsValid);
        Assert.True(validator.Validate(new CreateUserRequest("a@b.example", "A", password, Roles.Viewer, [])).IsValid);
    }

    [Fact]
    public void User_requests_reject_unknown_roles_and_short_passwords()
    {
        var validator = new CreateUserRequestValidator();

        ValidationResult result = validator.Validate(new CreateUserRequest("not-an-email", "A", "short", "SuperUser", []));

        Assert.Contains(result.Errors, e => e.PropertyName == "Email");
        Assert.Contains(result.Errors, e => e.PropertyName == "Password");
        Assert.Contains(result.Errors, e => e.PropertyName == "Role");
    }
}
