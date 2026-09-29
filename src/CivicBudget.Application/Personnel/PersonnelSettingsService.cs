using System.Collections.ObjectModel;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Personnel;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Personnel;

/// <summary>
/// A fiscal year's personnel settings. Saving them re-prices every position in that year's open
/// budgets, so a new health premium reaches every line it affects at once; adopted budgets keep
/// theirs. A change that would leave any position of the year unpriceable (removing a plan someone
/// is on, a tier someone has) is refused with the positions named.
/// </summary>
public interface IPersonnelSettingsService
{
    /// <summary>The page for a year; with no year, the year of the budget being prepared.</summary>
    Task<PersonnelSettingsPageDto?> GetAsync(int? fiscalYear, CancellationToken ct = default);

    Task<Result> CreateAsync(int fiscalYear, CreatePersonnelSettingsRequest request, CancellationToken ct = default);

    Task<Result<PersonnelSettingsSavedDto>> SaveAsync(int fiscalYear, PersonnelSettingsForm form, CancellationToken ct = default);
}

public sealed class PersonnelSettingsService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : IPersonnelSettingsService
{
    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can change personnel settings.";

    public async Task<PersonnelSettingsPageDto?> GetAsync(int? fiscalYear, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<FiscalYear> years = await db.FiscalYears.OrderByDescending(f => f.Year).ToListAsync(ct);
        if (years.Count == 0)
        {
            return null;
        }

        // By default, the year of the newest budget still being prepared; otherwise the newest year.
        int? preparing = await db.BudgetVersions.Where(v => v.Status != BudgetStatus.Adopted)
            .Join(db.FiscalYears, v => v.FiscalYearId, f => f.Id, (v, f) => (int?)f.Year)
            .OrderByDescending(y => y).FirstOrDefaultAsync(ct);
        FiscalYear year = years.FirstOrDefault(f => f.Year == fiscalYear)
            ?? years.FirstOrDefault(f => f.Year == preparing)
            ?? years[0];

        PersonnelSettings? settings = await PersonnelData.SettingsAsync(db, year.Year, ct);
        bool priorSetUp = await db.PersonnelSettings.AnyAsync(s => s.FiscalYear == year.Year - 1, ct);
        List<Account> accounts = await db.Accounts.OrderBy(a => a.Code).ToListAsync(ct);
        HashSet<Guid> referenced = Referenced(settings);
        IReadOnlyDictionary<Guid, int> usage = settings is null ? new Dictionary<Guid, int>() : await UsageAsync(db, year, ct);

        return new PersonnelSettingsPageDto(
            years.Select(f => f.Year).ToList(), year.Year, year.StartDate, year.EndDate,
            currentUser.IsFiscalAuthority(),
            // The form shows the plans, not what a position costs, so it needs no fund numbers.
            settings is null ? null : PersonnelSettingsForm.From(settings.ToRules(year.StartDate, year.EndDate, ReadOnlyDictionary<Guid, string>.Empty)),
            priorSetUp,
            accounts.Where(a => (a.IsActive && a.Type == AccountType.Expenditure) || referenced.Contains(a.Id))
                .Select(a => new AccountLookupDto(a.Id, a.Code, a.Name, a.Type, a.Category)).ToList(),
            Suggest(accounts.Where(a => a.IsActive && a.Type == AccountType.Expenditure).ToList()),
            usage);
    }

    public async Task<Result> CreateAsync(int fiscalYear, CreatePersonnelSettingsRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.FiscalYears.AnyAsync(f => f.Year == fiscalYear, ct))
        {
            return Result.Failure($"FY{fiscalYear} is not a fiscal year yet. Add it under Fiscal years first.");
        }

        if (await db.PersonnelSettings.AnyAsync(s => s.FiscalYear == fiscalYear, ct))
        {
            return Result.Failure($"FY{fiscalYear} already has personnel settings.");
        }

        PersonnelSettings settings;
        if (request.CopyPriorYear)
        {
            PersonnelSettings? prior = await PersonnelData.SettingsAsync(db, fiscalYear - 1, ct);
            if (prior is null)
            {
                return Result.Failure($"FY{fiscalYear - 1} has no personnel settings to copy.");
            }

            settings = prior.CopyTo(fiscalYear).Copy;
        }
        else
        {
            List<ValidationError> errors = await AccountProblemsAsync(db,
            [
                (nameof(request.PayAccountId), request.PayAccountId, "Base pay"),
                (nameof(request.OvertimeAccountId), request.OvertimeAccountId, "Overtime"),
                (nameof(request.RetirementAccountId), request.RetirementAccountId, "Retirement"),
                (nameof(request.MedicareAccountId), request.MedicareAccountId, "Medicare"),
                (nameof(request.WorkersCompAccountId), request.WorkersCompAccountId, "Workers' compensation"),
            ], ct);
            if (errors.Count > 0)
            {
                return Result.Failure(errors);
            }

            settings = PersonnelSettings.CreateDefault(currentUser.GovernmentId!.Value, fiscalYear, request.PayAccountId, request.OvertimeAccountId,
                request.RetirementAccountId, request.MedicareAccountId, request.WorkersCompAccountId);
        }

        db.PersonnelSettings.Add(settings);
        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return conflict;
        }

        return Result.Success();
    }

    public async Task<Result<PersonnelSettingsSavedDto>> SaveAsync(int fiscalYear, PersonnelSettingsForm form, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<PersonnelSettingsSavedDto>(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        PersonnelSettings? settings = await PersonnelData.SettingsAsync(db, fiscalYear, ct);
        FiscalYear? year = await db.FiscalYears.FirstOrDefaultAsync(f => f.Year == fiscalYear, ct);
        if (settings is null || year is null)
        {
            return Result.Failure<PersonnelSettingsSavedDto>($"FY{fiscalYear} has no personnel settings to change.");
        }

        List<ValidationError> accountErrors = await AccountProblemsAsync(db,
        [
            (nameof(form.PayAccountId), form.PayAccountId, "Base pay"),
            (nameof(form.MedicareAccountId), form.MedicareAccountId, "Medicare"),
            (nameof(form.WorkersCompAccountId), form.WorkersCompAccountId, "Workers' compensation"),
            .. form.RetirementPlans.Select(p => (nameof(form.RetirementPlans), (Guid?)p.AccountId, p.Name)),
            .. form.InsurancePlans.Select(p => (nameof(form.InsurancePlans), (Guid?)p.AccountId, p.Name)),
            .. form.ExtraPay.Select(p => (nameof(form.ExtraPay), p.AccountId, p.Name)),
            .. form.Longevity.Select(p => (nameof(form.Longevity), p.AccountId, p.Name)),
        ], ct);
        if (accountErrors.Count > 0)
        {
            return Result.Failure<PersonnelSettingsSavedDto>(accountErrors);
        }

        try
        {
            Apply(settings, form);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PersonnelSettingsSavedDto>(ex.Message);
        }

        PayrollRules rules = await PersonnelData.RulesAsync(db, settings, year, ct);

        // Every position of the year must still price, adopted budgets included: their pages show what
        // each position costs, and the database would refuse to drop a plan they point at anyway.
        List<BudgetVersion> versions = await PersonnelData.VersionsWithPositions(db).Where(v => v.FiscalYearId == year.Id).ToListAsync(ct);
        List<string> broken = versions
            .SelectMany(v => v.Positions.Select(p => (Version: v, Details: p.ToDetails())))
            .Select(x => (x.Version, x.Details, Problems: x.Details.Problems(rules)))
            .Where(x => x.Problems.Count > 0)
            .Select(x => $"{PersonnelData.Describe(x.Details)} ({x.Version.Label}): {x.Problems[0].Message}")
            .ToList();
        if (broken.Count > 0)
        {
            string more = broken.Count > 3 ? $" And {broken.Count - 3} more." : "";
            return Result.Failure<PersonnelSettingsSavedDto>(
                $"That would leave positions that cannot be priced. {string.Join(" ", broken.Take(3))}{more} Change those positions first.");
        }

        int changed = 0;
        var recalculated = new List<string>();
        foreach (BudgetVersion version in versions.Where(v => v.IsEditable))
        {
            int lines;
            try
            {
                lines = await PersonnelData.ApplyAllAsync(db, version, rules, ct);
            }
            catch (DomainException ex)
            {
                return Result.Failure<PersonnelSettingsSavedDto>($"FY{fiscalYear} {version.Label}: {ex.Message}");
            }

            if (lines > 0)
            {
                changed += lines;
                recalculated.Add($"FY{fiscalYear} {version.Label}");
                db.AuditEntries.Add(AuditEntry.Event(version.GovernmentId, nameof(BudgetVersion), version.Id,
                    $"FY{fiscalYear} personnel settings changed; {lines} budget line{(lines == 1 ? "" : "s")} recalculated",
                    currentUser.UserId ?? "system", currentUser.DisplayName ?? "system", clock.GetUtcNow()));
            }
        }

        if (await db.TrySaveAsync(ct) is { } conflict)
        {
            return Result.Failure<PersonnelSettingsSavedDto>(conflict.Errors);
        }

        return Result.Success(new PersonnelSettingsSavedDto(changed, recalculated));
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// Brings the settings in line with the form: removes what the form dropped, then saves each row in
    /// the form's order (a row with an id updates that plan, one without adds a plan). The form itself
    /// is not changed, so a refused save leaves the page's form as the user typed it.
    /// </summary>
    private static void Apply(PersonnelSettings settings, PersonnelSettingsForm form)
    {
        settings.SetBasics(form.StandardHours, form.PayAccountId, form.MedicareRate, form.MedicareAccountId, form.WorkersCompRate, form.WorkersCompAccountId);

        RemoveMissing(settings.RetirementPlans.Select(p => p.Id), form.RetirementPlans.Select(p => p.Id), settings.RemoveRetirementPlan);
        RemoveMissing(settings.InsurancePlans.Select(p => p.Id), form.InsurancePlans.Select(p => p.Id), settings.RemoveInsurancePlan);
        RemoveMissing(settings.ExtraPay.Select(p => p.Id), form.ExtraPay.Select(p => p.Id), settings.RemoveExtraPay);
        RemoveMissing(settings.LongevitySchedules.Select(p => p.Id), form.Longevity.Select(p => p.Id), settings.RemoveLongevitySchedule);
        RemoveMissing(settings.PayScales.Select(p => p.Id), form.PayScales.Select(p => p.Id), settings.RemovePayScale);

        foreach (RetirementPlanForm p in form.RetirementPlans)
        {
            settings.SaveRetirementPlan(p.Id, p.Name, p.EmployerRate, p.EmployeeRate, p.AccountId);
        }

        foreach (InsurancePlanForm p in form.InsurancePlans)
        {
            settings.SaveInsurancePlan(p.Id, p.Name, p.SinglePremium, p.EmployeeSpousePremium, p.FamilyPremium, p.EmployeeSharePercent, p.AccountId);
        }

        foreach (ExtraPayForm p in form.ExtraPay)
        {
            settings.SaveExtraPay(p.Id, p.Name, p.Kind, p.Multiplier, p.IsPensionable, p.IsTaxable, p.AccountId);
        }

        foreach (LongevityForm p in form.Longevity)
        {
            settings.SaveLongevitySchedule(p.Id, p.Name, p.Method, p.CountedOn, p.MaxYears, p.AccountId, p.ToRule().Steps);
        }

        foreach (PayScaleForm p in form.PayScales)
        {
            settings.SavePayScale(p.Id, p.Name, p.Basis, p.ToRates());
        }
    }

    private static void RemoveMissing(IEnumerable<Guid> existing, IEnumerable<Guid?> kept, Action<Guid> remove)
    {
        HashSet<Guid> keep = kept.OfType<Guid>().ToHashSet();
        foreach (Guid id in existing.Where(id => !keep.Contains(id)).ToList())
        {
            remove(id);
        }
    }

    /// <summary>Every account a cost lands on must be an active expenditure account; a null optional account is fine.</summary>
    private static async Task<List<ValidationError>> AccountProblemsAsync(ICivicBudgetDbContext db, IReadOnlyList<(string Field, Guid? AccountId, string What)> uses, CancellationToken ct)
    {
        List<Guid> ids = uses.Where(u => u.AccountId is not null).Select(u => u.AccountId!.Value).Distinct().ToList();
        Dictionary<Guid, Account> accounts = await db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        return uses
            .Where(u => u.AccountId is { } id && accounts.GetValueOrDefault(id) is not { IsActive: true, Type: AccountType.Expenditure })
            .Select(u => new ValidationError(u.Field, $"{u.What}: choose an active expenditure account."))
            .ToList();
    }

    /// <summary>Every account the settings point at, so a retired one still shows in its picker.</summary>
    private static HashSet<Guid> Referenced(PersonnelSettings? settings) => settings is null ? [] :
    [
        settings.PayAccountId, settings.MedicareAccountId, settings.WorkersCompAccountId,
        .. settings.RetirementPlans.Select(p => p.AccountId),
        .. settings.InsurancePlans.Select(p => p.AccountId),
        .. settings.ExtraPay.Select(p => p.AccountId).OfType<Guid>(),
        .. settings.LongevitySchedules.Select(p => p.AccountId).OfType<Guid>(),
    ];

    /// <summary>How many positions in the year's budgets use each plan, schedule, item, and scale.</summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> UsageAsync(ICivicBudgetDbContext db, FiscalYear year, CancellationToken ct)
    {
        List<Position> positions = await db.Positions
            .Include(p => p.Coverages).Include(p => p.ExtraPay)
            .Where(p => db.BudgetVersions.Any(v => v.Id == p.BudgetVersionId && v.FiscalYearId == year.Id))
            .ToListAsync(ct);
        return positions
            .SelectMany(p => new[] { p.RetirementPlanId, p.LongevityScheduleId, p.PayScaleId }.OfType<Guid>()
                .Concat(p.Coverages.Select(c => c.InsurancePlanId))
                .Concat(p.ExtraPay.Select(e => e.ExtraPayId)))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Guesses the accounts for a new year's defaults from the chart: personal services for pay (the one
    /// named for overtime, if any, for overtime) and fringe benefits by name for the rest. Only a guess;
    /// the form shows it and the administrator confirms.
    /// </summary>
    private static SuggestedAccounts Suggest(List<Account> expenditure)
    {
        Guid? Named(ReportingCategory category, params string[] words) =>
            expenditure.FirstOrDefault(a => a.Category == category && words.Any(w => a.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))?.Id;
        Guid? First(ReportingCategory category) => expenditure.FirstOrDefault(a => a.Category == category)?.Id;

        Guid? overtime = Named(ReportingCategory.PersonalServices, "overtime");
        Guid? pay = Named(ReportingCategory.PersonalServices, "salar", "wage") ?? expenditure.FirstOrDefault(a => a.Category == ReportingCategory.PersonalServices && a.Id != overtime)?.Id;
        Guid? benefits = First(ReportingCategory.FringeBenefits);
        return new SuggestedAccounts(
            pay,
            overtime,
            Named(ReportingCategory.FringeBenefits, "retire", "pension", "opers") ?? benefits,
            Named(ReportingCategory.FringeBenefits, "medicare") ?? benefits,
            Named(ReportingCategory.FringeBenefits, "workers") ?? benefits,
            Named(ReportingCategory.FringeBenefits, "insurance", "health", "medical") ?? benefits);
    }
}
