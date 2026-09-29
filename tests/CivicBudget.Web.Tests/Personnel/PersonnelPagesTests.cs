using Bunit.TestDoubles;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Personnel;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Personnel;
using CivicBudget.Web.Components.Admin.Budgets;
using CivicBudget.Web.Components.Admin.Personnel;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Personnel;

/// <summary>
/// The personnel pages: positions and the lines they make, the editor pricing a position as it is
/// typed, read-only users, the year that is not set up, and longevity read back in plain English.
/// </summary>
public class PersonnelPagesTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private static readonly Guid Police = Guid.CreateVersion7();
    private static readonly Guid General = Guid.CreateVersion7();
    private static readonly Guid Salaries = Guid.CreateVersion7();
    private static readonly Guid Benefits = Guid.CreateVersion7();
    private readonly FakePersonnel _personnel = new();
    private readonly PayrollRules _rules;

    public PersonnelPagesTests()
    {
        var settings = PersonnelSettings.CreateDefault(Guid.CreateVersion7(), 2027, Salaries, null, Benefits, Benefits, Benefits);
        settings.SaveInsurancePlan(null, "Medical", 650m, 1_300m, 1_760m, 15m, Benefits);
        _rules = settings.ToRules(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), new Dictionary<Guid, string> { [General] = "1000" });

        Services.AddSingleton<IPersonnelService>(_personnel);
        Services.AddSingleton<IPersonnelSettingsService>(new FakeSettings());
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private PositionDetails Officer(string? name, decimal salary) => new()
    {
        Title = "Patrol officer",
        EmployeeName = name,
        Rate = salary,
        RetirementPlanId = _rules.RetirementPlans.Single(p => p.Name == "OP&F police").Id,
        Funds = [new FundShare(General, 100m)],
    };

    private PersonnelPageDto Page(bool canEdit = true, PayrollRules? rules = null, params PositionDetails[] positions)
    {
        PayrollRules? priced = rules ?? _rules;
        List<PositionDto> dtos = positions.Select(p => new PositionDto(Guid.CreateVersion7(), p, PositionCostCalculator.Calculate(p, _rules), null)).ToList();
        return new PersonnelPageDto(VersionId, 2027, "Original", BudgetStatus.Draft, 1,
            new LookupDto(Police, "110", "Police"), [new LookupDto(Police, "110", "Police")],
            canEdit, priced, true, dtos,
            [new PersonnelLineDto(Guid.CreateVersion7(), "1000", "General Fund", "1000-110-5110", "5110", "Salaries & Wages", dtos.Sum(p => p.Cost!.Pay), dtos.Count)],
            [new LookupDto(General, "1000", "General Fund")],
            [new AccountLookupDto(Salaries, "5110", "Salaries & Wages", AccountType.Expenditure, ReportingCategory.PersonalServices),
             new AccountLookupDto(Benefits, "5210", "Retirement", AccountType.Expenditure, ReportingCategory.FringeBenefits)],
            LinesMatchPositions: true);
    }

    private IRenderedComponent<DepartmentPersonnel> RenderPage() =>
        Render<DepartmentPersonnel>(p => p.Add(x => x.VersionId, VersionId).Add(x => x.DepartmentId, Police));

    [Fact]
    public void Positions_show_with_vacancies_totals_and_the_lines_they_make()
    {
        _personnel.Page = Page(positions: [Officer("Riley Chen", 60_000m), Officer(null, 50_000m)]);

        IRenderedComponent<DepartmentPersonnel> page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("Personnel, Police", page.Markup));
        Assert.Contains("1 vacant", page.Markup);
        Assert.Contains("Vacant", page.Find("table[aria-label='Positions'] tbody").TextContent);
        Assert.Contains("1000-110-5110", page.Find("table[aria-label='Budget lines calculated from these positions']").TextContent);
        Assert.Contains("Add position", page.Markup);
    }

    [Fact]
    public void The_editor_prices_the_position_as_it_is_typed_and_saves_it()
    {
        _personnel.Page = Page(positions: [Officer("Riley Chen", 60_000m)]);
        IRenderedComponent<DepartmentPersonnel> page = RenderPage();
        page.WaitForAssertion(() => page.Find("button.cb-link-button"));

        page.Find("button.cb-link-button").Click();
        Assert.Contains("$72,570.00", page.Find(".cb-cost-breakdown tfoot").TextContent);   // 60,000 plus 19.5% OP&F and 1.45% Medicare
        page.Find("#pos-rate").Change("70000");
        Assert.Contains("$84,665.00", page.Find(".cb-cost-breakdown tfoot").TextContent);

        page.FindAll(".cb-drawer-actions button")[0].Click();

        Assert.Equal(70_000m, _personnel.Saved!.Rate);
    }

    [Fact]
    public void Refused_fields_are_marked_where_they_are()
    {
        _personnel.Page = Page(positions: [Officer("Riley Chen", 60_000m)]);
        _personnel.Refusal = [new ValidationError(nameof(PositionDetails.Title), "Enter the position's title.")];
        IRenderedComponent<DepartmentPersonnel> page = RenderPage();
        page.WaitForAssertion(() => page.Find("button.cb-link-button"));
        page.Find("button.cb-link-button").Click();

        page.FindAll(".cb-drawer-actions button")[0].Click();

        Assert.Contains("is-invalid", page.Find("#pos-title").ClassList);
        Assert.Contains("Enter the position's title.", page.Find(".invalid-feedback").TextContent);
    }

    [Fact]
    public void A_read_only_user_sees_the_position_but_cannot_change_it()
    {
        _personnel.Page = Page(canEdit: false, positions: [Officer("Riley Chen", 60_000m)]);
        IRenderedComponent<DepartmentPersonnel> page = RenderPage();
        page.WaitForAssertion(() => page.Find("button.cb-link-button"));

        page.Find("button.cb-link-button").Click();

        Assert.DoesNotContain("Add position", page.Markup);
        Assert.True(page.Find(".cb-position-editor fieldset").HasAttribute("disabled"));
        Assert.DoesNotContain("Save position", page.Markup);
    }

    [Fact]
    public void A_year_without_settings_points_to_setting_it_up()
    {
        _personnel.Page = Page(rules: null) with { Rules = null, Positions = [], Lines = [] };

        IRenderedComponent<DepartmentPersonnel> page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("FY2027 has no personnel settings yet", page.Markup));
        Assert.Equal("admin/personnel-settings?year=2027", page.Find("a.btn-primary").GetAttribute("href"));
    }

    [Fact]
    public void A_calculated_amount_is_read_only_and_links_to_its_positions()
    {
        IRenderedComponent<AmountCell> cell = Render<AmountCell>(p => p
            .Add(x => x.Value, 578_966.80m).Add(x => x.CanEdit, true).Add(x => x.Positions, 9).Add(x => x.PositionsHref, "admin/budgets/x/personnel/y"));

        Assert.Empty(cell.FindAll("input"));
        Assert.Equal("from 9 positions", cell.Find("a.cb-from-positions").TextContent.Trim());
        Assert.Equal("admin/budgets/x/personnel/y", cell.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void Longevity_reads_back_in_plain_english_and_works_an_example()
    {
        var schedule = new LongevityForm
        {
            Name = "FOP",
            Method = LongevityMethod.FlatAmount,
            Steps = [new LongevityStepForm { MinYears = 5, Value = 600m }, new LongevityStepForm { MinYears = 10, Value = 900m }],
        };

        IRenderedComponent<LongevityEditor> editor = Render<LongevityEditor>(p => p.Add(x => x.Schedule, schedule).Add(x => x.Accounts, []).Add(x => x.Prefix, "l-"));

        Assert.Contains("10 or more years of service: $900.00 a year.", editor.Find(".cb-plain-english").TextContent);
        Assert.Contains("$900.00", editor.Find(".cb-plain-english .cb-sentence b").TextContent);   // the example: 12 years
        editor.Find("select[aria-label='How longevity is paid']").Change(LongevityMethod.PercentOfPay.ToString());
        Assert.Contains("10 or more years of service: 900% of base pay.", editor.Find(".cb-plain-english").TextContent);
    }

    private sealed class FakePersonnel : IPersonnelService
    {
        public PersonnelPageDto? Page { get; set; }
        public PositionDetails? Saved { get; private set; }
        public IReadOnlyList<ValidationError>? Refusal { get; set; }

        public Task<PersonnelPageDto?> GetDepartmentAsync(Guid versionId, Guid departmentId, CancellationToken ct = default) => Task.FromResult(Page);

        public Task<Result<PersonnelSavedDto>> AddPositionAsync(Guid versionId, Guid departmentId, PositionDetails details, CancellationToken ct = default) => Save(details);

        public Task<Result<PersonnelSavedDto>> UpdatePositionAsync(Guid versionId, Guid positionId, PositionDetails details, CancellationToken ct = default) => Save(details);

        public Task<Result<PersonnelSavedDto>> RemovePositionAsync(Guid versionId, Guid positionId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new PersonnelSavedDto(positionId, 1)));

        private Task<Result<PersonnelSavedDto>> Save(PositionDetails details)
        {
            Saved = details;
            return Task.FromResult(Refusal is { } errors ? Result.Failure<PersonnelSavedDto>(errors) : Result.Success(new PersonnelSavedDto(Guid.CreateVersion7(), 3)));
        }
    }

    private sealed class FakeSettings : IPersonnelSettingsService
    {
        public Task<PersonnelSettingsPageDto?> GetAsync(int? fiscalYear, CancellationToken ct = default) => Task.FromResult<PersonnelSettingsPageDto?>(null);
        public Task<Result> CreateAsync(int fiscalYear, CreatePersonnelSettingsRequest request, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result<PersonnelSettingsSavedDto>> SaveAsync(int fiscalYear, PersonnelSettingsForm form, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new PersonnelSettingsSavedDto(0, [])));
    }
}
