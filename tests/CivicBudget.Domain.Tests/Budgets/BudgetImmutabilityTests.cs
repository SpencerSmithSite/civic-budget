using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Tests.Budgets;

/// <summary>SPEC §3.6: an adopted version is immutable. Every mutating member must refuse.</summary>
public class BudgetImmutabilityTests
{
    private static BudgetVersion AdoptedWithOneLine(out Guid lineId)
    {
        BudgetVersion version = TestData.DraftVersion();
        lineId = version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(), 100_000m).Id;
        version.SetBeginningBalance(TestData.GeneralFund(), 50_000m);
        version.Propose();
        version.Adopt("2026-14", "user-fd", DateTimeOffset.UtcNow);
        return version;
    }

    [Fact]
    public void Adopted_version_rejects_adding_lines()
    {
        BudgetVersion version = AdoptedWithOneLine(out _);
        Assert.Throws<DomainException>(() => version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Supplies(), 1m));
    }

    [Fact]
    public void Adopted_version_rejects_amount_changes()
    {
        BudgetVersion version = AdoptedWithOneLine(out Guid lineId);
        Assert.Throws<DomainException>(() => version.UpdateLineAmount(lineId, 1m));
        Assert.Equal(100_000m, version.Lines.Single().Amount);
    }

    [Fact]
    public void Adopted_version_rejects_comparative_and_justification_changes()
    {
        BudgetVersion version = AdoptedWithOneLine(out Guid lineId);
        Assert.Throws<DomainException>(() => version.UpdateLineComparatives(lineId, 1m, 1m));
        Assert.Throws<DomainException>(() => version.UpdateLineJustification(lineId, "x"));
    }

    [Fact]
    public void Adopted_version_rejects_removing_lines()
    {
        BudgetVersion version = AdoptedWithOneLine(out Guid lineId);
        Assert.Throws<DomainException>(() => version.RemoveLine(lineId));
        Assert.Single(version.Lines);
    }

    [Fact]
    public void Adopted_version_rejects_beginning_balance_changes()
    {
        BudgetVersion version = AdoptedWithOneLine(out _);
        Assert.Throws<DomainException>(() => version.SetBeginningBalance(TestData.GeneralFund(), 1m));
    }

    [Fact]
    public void Proposed_version_is_still_editable_by_design()
    {
        // The finance director may adjust a proposed budget before council adopts it (SPEC §4).
        BudgetVersion version = TestData.DraftVersion();
        Guid lineId = version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(), 100m).Id;
        version.Propose();

        version.UpdateLineAmount(lineId, 200m);

        Assert.Equal(200m, version.Lines.Single().Amount);
    }
}
