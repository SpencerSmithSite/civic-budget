using CivicBudget.Application.Notifications;

namespace CivicBudget.Application.Tests;

/// <summary>Every email says why it was sent and where to go, and none carries a password.</summary>
public class EmailTemplateTests
{
    [Fact]
    public void A_submission_names_the_department_the_budget_and_the_link()
    {
        EmailContent email = EmailTemplates.DepartmentSubmitted("Village of Maple Ridge", "Police", "FY2027 Original", "Chief Morgan Hale", "https://x.test/admin/budgets/1/departments/2");

        Assert.Equal("Police submitted its FY2027 Original request", email.Subject);
        Assert.Contains("Chief Morgan Hale submitted Police's request for the FY2027 Original budget of Village of Maple Ridge.", email.Body, StringComparison.Ordinal);
        Assert.Contains("https://x.test/admin/budgets/1/departments/2", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_return_quotes_the_note_indented_line_by_line()
    {
        EmailContent email = EmailTemplates.DepartmentReturned("Village of Maple Ridge", "Parks", "FY2027 Original", "Move the playground.\nThen resubmit.", "https://x.test/p");

        Assert.Contains("  Move the playground.\n  Then resubmit.", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_and_welcome_links_say_how_long_they_last_and_what_to_do_otherwise()
    {
        EmailContent reset = EmailTemplates.PasswordReset("Village of Maple Ridge", "https://x.test/r", TimeSpan.FromDays(1));
        EmailContent welcome = EmailTemplates.Welcome("Village of Maple Ridge", "Viewer", "Alex Rivera", "https://x.test/w", TimeSpan.FromDays(1));

        Assert.Contains("for 24 hours", reset.Body, StringComparison.Ordinal);
        Assert.Contains("If you did not ask, ignore this email", reset.Body, StringComparison.Ordinal);
        Assert.Contains("Alex Rivera added you to Village of Maple Ridge's budget in CivicBudget as Viewer.", welcome.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("password:", welcome.Body, StringComparison.OrdinalIgnoreCase);
    }
}
