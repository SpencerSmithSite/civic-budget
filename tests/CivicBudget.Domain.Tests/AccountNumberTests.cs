using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Tests;

/// <summary>The Ohio account number: composed from the three codes under a per-government format, parsed back leniently.</summary>
public class AccountNumberTests
{
    private static readonly AccountNumberFormat Uan = AccountNumberFormat.UanVillage;
    private static readonly AccountNumberFormat Dotted = new(4, 3, 4, ".", "Department");

    [Fact]
    public void Composes_fund_department_object_and_fund_object_for_revenue()
    {
        Assert.Equal("1000-725-5110", AccountNumber.Compose(Uan, "1000", "725", "5110"));
        Assert.Equal("1000-4110", AccountNumber.Compose(Uan, "1000", null, "4110"));
        Assert.Equal("1000.725.5110", AccountNumber.Compose(Dotted, "1000", "725", "5110"));
    }

    [Fact]
    public void Pads_numeric_codes_to_the_segment_width_and_leaves_letter_codes_alone()
    {
        Assert.Equal("0101-001-0121", AccountNumber.Compose(Uan, "101", "1", "121"));
        Assert.Equal("1000-PD-5110", AccountNumber.Compose(Uan, "1000", "PD", "5110"));
    }

    [Theory]
    [InlineData("1000-725-5110", "1000", "725", "5110")]
    [InlineData("1000.725.5110", "1000", "725", "5110")]
    [InlineData("1000 725 5110", "1000", "725", "5110")]
    [InlineData("1000/725/5110", "1000", "725", "5110")]
    [InlineData("  1000-725-5110 ", "1000", "725", "5110")]
    [InlineData("10007255110", "1000", "725", "5110")]
    [InlineData("1000-4110", "1000", null, "4110")]
    [InlineData("10004110", "1000", null, "4110")]
    public void Parses_any_common_separator_or_none(string text, string fund, string? department, string obj)
    {
        Assert.True(AccountNumber.TryParse(Uan, text, out string f, out string? d, out string o));
        Assert.Equal((fund, department, obj), (f, d, o));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1000")]
    [InlineData("1000-725-5110-01")]
    [InlineData("100072551")]
    [InlineData("overtime")]
    [InlineData("street-lights")]
    [InlineData("PD-110-5120")]
    public void Rejects_what_is_not_a_number(string text) =>
        Assert.False(AccountNumber.TryParse(Uan, text, out _, out _, out _));

    [Fact]
    public void Format_validates_widths_separator_and_label()
    {
        Assert.Throws<DomainException>(() => new AccountNumberFormat(0, 3, 4, "-", "Program"));
        Assert.Throws<DomainException>(() => new AccountNumberFormat(4, 3, 9, "-", "Program"));
        Assert.Throws<DomainException>(() => new AccountNumberFormat(4, 3, 4, "--", "Program"));
        Assert.Throws<DomainException>(() => new AccountNumberFormat(4, 3, 4, "1", "Program"));
        Assert.Throws<DomainException>(() => new AccountNumberFormat(4, 3, 4, "-", " "));
        Assert.Equal("Department", AccountNumberFormat.County.DepartmentLabel);
    }
}
