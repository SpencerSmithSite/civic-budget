using CivicBudget.Domain.Common;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Domain.Tests.Personnel;

public class PersonnelSettingsTests
{
    [Fact]
    public void Defaults_carry_the_ohio_retirement_rates_and_medicare()
    {
        PayrollRules rules = new PersonnelTestData().Rules;

        Assert.Equal((14m, 10m), Rates(rules, "OPERS"));
        Assert.Equal((19.5m, 12.25m), Rates(rules, "OP&F police"));
        Assert.Equal((24m, 12.25m), Rates(rules, "OP&F fire"));
        Assert.Equal(1.45m, rules.MedicareRate);
        Assert.Contains(rules.ExtraPay, e => e.Name == "Overtime" && e.Kind == ExtraPayKind.Hours && e.Multiplier == 1.5m);
        Assert.Contains(rules.ExtraPay, e => e.Name == "Uniform allowance" && !e.IsPensionable && e.IsTaxable);
    }

    [Fact]
    public void Workers_comp_starts_at_zero_because_every_employer_has_its_own_rate()
    {
        var settings = PersonnelSettings.CreateDefault(TestData.GovernmentId, 2027, Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Equal(0m, settings.WorkersCompRate);
    }

    [Fact]
    public void Names_are_unique_within_a_kind()
    {
        PersonnelSettings settings = new PersonnelTestData().Settings;

        DomainException ex = Assert.Throws<DomainException>(() => settings.SaveRetirementPlan(null, "opers", 14m, 10m, PersonnelTestData.RetirementAccount));
        Assert.Equal("There is already a retirement plan named opers.", ex.Message);
    }

    [Fact]
    public void Rates_are_percentages_from_0_to_100()
    {
        PersonnelSettings settings = new PersonnelTestData().Settings;

        Assert.Throws<DomainException>(() => settings.SaveRetirementPlan(null, "Bad", 140m, 10m, PersonnelTestData.RetirementAccount));
        Assert.Throws<DomainException>(() => settings.SetBasics(2080m, PersonnelTestData.SalariesAccount, -1m, PersonnelTestData.MedicareAccount, 0m, PersonnelTestData.WorkersCompAccount));
    }

    [Fact]
    public void Longevity_steps_are_checked()
    {
        PersonnelSettings settings = new PersonnelTestData().Settings;

        Assert.Throws<DomainException>(() => settings.SaveLongevitySchedule(null, "Twice", LongevityMethod.FlatAmount, ServiceCountedOn.FirstDayOfYear, null, null, [new(5, 100m), new(5, 200m)]));
        Assert.Throws<DomainException>(() => settings.SaveLongevitySchedule(null, "Empty", LongevityMethod.FlatAmount, ServiceCountedOn.FirstDayOfYear, null, null, []));
        Assert.Throws<DomainException>(() => settings.SaveLongevitySchedule(null, "Too much", LongevityMethod.PercentOfPay, ServiceCountedOn.FirstDayOfYear, null, null, [new(5, 120m)]));
    }

    [Fact]
    public void A_pay_scale_names_each_grade_and_step_once()
    {
        PersonnelSettings settings = new PersonnelTestData().Settings;

        Assert.Throws<DomainException>(() => settings.SavePayScale(null, "FOP", PayBasis.Hourly, [new("A", 1, 30m), new("a", 1, 31m)]));
    }

    [Fact]
    public void Saving_an_existing_plan_updates_it_in_place()
    {
        PersonnelTestData data = new();
        Guid opers = data.Plan("OPERS");

        data.Settings.SaveRetirementPlan(opers, "OPERS", 14.5m, 10m, PersonnelTestData.RetirementAccount);

        Assert.Equal(14.5m, data.Rules.RetirementPlan(opers)!.EmployerRate);
    }

    [Fact]
    public void Next_year_is_a_copy_with_new_ids_and_a_map_to_them()
    {
        PersonnelTestData data = new();

        (PersonnelSettings next, IReadOnlyDictionary<Guid, Guid> ids) = data.Settings.CopyTo(2028);
        PayrollRules nextRules = next.ToRules(new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31));

        Assert.Equal(2028, next.FiscalYear);
        Guid oldMedical = data.Insurance("Medical");
        Assert.NotEqual(oldMedical, ids[oldMedical]);
        Assert.Equal("Medical", nextRules.InsurancePlan(ids[oldMedical])!.Name);
        Assert.Equal(data.Rules.Longevity.Count, nextRules.Longevity.Count);
        Assert.Equal(52_000m, nextRules.PayScale(ids[data.Scale("Non-union")])!.Rate("B", 2));
    }

    private static (decimal, decimal) Rates(PayrollRules rules, string name)
    {
        RetirementPlanRule plan = rules.RetirementPlans.Single(p => p.Name == name);
        return (plan.EmployerRate, plan.EmployeeRate);
    }
}
