using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Reports;

public sealed class CertificateService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : ICertificateService
{
    /// <summary>Room on a landscape page for the balance, this many revenue columns, other sources, and the total.</summary>
    public const int MaxColumns = ReportColumnRules.MaxColumns;

    private const string NotAllowed = "Only an Administrator or the Fiscal Officer can change the certificate's settings.";
    private const string DefaultColumnLabel = "Taxes";

    public async Task<CertificateReportDto?> GetAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        if (currentUser.IsDepartmentUser())
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await LoadVersionAsync(db, budgetVersionId, ct);
        if (version is null)
        {
            return null;
        }

        FiscalYear year = await db.FiscalYears.SingleAsync(fy => fy.Id == version.FiscalYearId, ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        CertificateSettings settings = await db.CertificateSettings.FirstOrDefaultAsync(ct) ?? new CertificateSettings(government.Id);
        (List<CertificateColumn> columns, bool defaultColumns) = await ColumnsAsync(db, ct);

        // The prior year's closing cash and encumbrances, when the ERP has closed it.
        CertificateCarryover? carryover = null;
        if (await ErpActualsReader.LatestSyncAsync(db, year.Year - 1, ct) is { ThroughPeriod: 12 } closed)
        {
            Dictionary<Guid, decimal> cash = await db.ErpFundCash.Where(c => c.FiscalYear == year.Year - 1).ToDictionaryAsync(c => c.FundId, c => c.Amount, ct);
            Dictionary<Guid, decimal> encumbrances = (await db.ErpEncumbrances.Where(e => e.FiscalYear == year.Year - 1).Select(e => new { e.FundId, e.Amount }).ToListAsync(ct))
                .GroupBy(e => e.FundId).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
            carryover = new CertificateCarryover(closed.AsOf, cash, encumbrances);
        }

        HashSet<Guid> fundIds = version.Lines.Select(l => l.FundId).Concat(version.BeginningBalances.Select(b => b.FundId)).ToHashSet();
        if (carryover is not null)
        {
            fundIds.UnionWith(carryover.Cash.Keys);
        }

        List<Fund> funds = await db.Funds.Where(f => fundIds.Contains(f.Id)).ToListAsync(ct);
        Dictionary<Guid, CertificateAdjustment> adjustments = await db.CertificateFundAdjustments.Where(a => a.FiscalYear == year.Year)
            .ToDictionaryAsync(a => a.FundId, a => new CertificateAdjustment(a.Nonspendable, a.Reserves, a.UnpaidAdvances), ct);

        BudgetVersion? prior = version.IsAmendment
            ? await LoadVersionAsync(db, await db.BudgetVersions.Where(v => v.FiscalYearId == version.FiscalYearId && v.VersionNumber == version.VersionNumber - 1).Select(v => v.Id).SingleAsync(ct), ct)
            : null;

        var header = new CertificateHeaderDto(
            version.Id, government.Name, settings.County, year.Year, version.Label, version.VersionNumber, version.Status, version.ResolutionNumber,
            version.AdoptedOnUtc, settings.FiscalOfficerName, settings.FiscalOfficerTitle, currentUser.DisplayName ?? "", clock.GetUtcNow());

        return CertificateBuilder.Build(new CertificateInputs(
            header, settings.BalanceLabel, settings.OtherSourcesLabel, columns, defaultColumns,
            funds.Select(f => new CertificateFund(f.Id, f.Code, f.Name, f.Category, version.GetBeginningBalance(f.Id),
                version.Lines.Where(l => l.FundId == f.Id && l.Account.Type.IsAppropriation()).Sum(l => l.Amount))).ToList(),
            Resources(version, government),
            year.StartDate,
            carryover,
            adjustments,
            prior is null ? null : $"FY{year.Year} {prior.Label}",
            prior is null ? [] : Resources(prior, government)));
    }

    public async Task<IReadOnlyList<FundAdjustmentDto>?> GetAdjustmentsAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await YearOfAsync(db, budgetVersionId, ct) is not { } year)
        {
            return null;
        }

        Dictionary<Guid, CertificateFundAdjustment> saved = await db.CertificateFundAdjustments.Where(a => a.FiscalYear == year).ToDictionaryAsync(a => a.FundId, ct);
        List<Fund> funds = await db.Funds.Where(f => f.IsActive || saved.Keys.Contains(f.Id)).OrderBy(f => f.Code).ToListAsync(ct);
        return funds.Select(f => saved.GetValueOrDefault(f.Id) is { } a
                ? new FundAdjustmentDto(f.Id, f.Code, f.Name, a.Nonspendable, a.Reserves, a.UnpaidAdvances)
                : new FundAdjustmentDto(f.Id, f.Code, f.Name, 0m, 0m, 0m))
            .ToList();
    }

    public async Task<Result> SaveAdjustmentsAsync(SaveFundAdjustmentsRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure("Only an Administrator or the Fiscal Officer can change reserves and advances.");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await YearOfAsync(db, request.VersionId, ct) is not { } year)
        {
            return Result.Failure("Budget version was not found.");
        }

        Dictionary<Guid, CertificateFundAdjustment> saved = await db.CertificateFundAdjustments.Where(a => a.FiscalYear == year).ToDictionaryAsync(a => a.FundId, ct);
        HashSet<Guid> known = (await db.Funds.Select(f => f.Id).ToListAsync(ct)).ToHashSet();
        foreach (FundAdjustmentDto fund in request.Funds)
        {
            if (!known.Contains(fund.FundId))
            {
                return Result.Failure("A fund in the list was not found.");
            }

            if (!saved.TryGetValue(fund.FundId, out CertificateFundAdjustment? adjustment))
            {
                if (fund.Nonspendable == 0m && fund.Reserves == 0m && fund.UnpaidAdvances == 0m)
                {
                    continue;
                }

                adjustment = new CertificateFundAdjustment(currentUser.GovernmentId!.Value, year, fund.FundId);
                db.CertificateFundAdjustments.Add(adjustment);
            }

            try
            {
                adjustment.Set(fund.Nonspendable, fund.Reserves, fund.UnpaidAdvances);
            }
            catch (DomainException ex)
            {
                return Result.Failure($"{fund.FundCode}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<CertificateSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        CertificateSettings settings = await db.CertificateSettings.FirstOrDefaultAsync(ct) ?? new CertificateSettings(Guid.Empty);
        (List<CertificateColumn> columns, bool isDefault) = await ColumnsAsync(db, ct);
        List<ReportAccountDto> accounts = await db.Accounts.Where(a => a.Type == AccountType.Revenue).OrderBy(a => a.Code)
            .Select(a => new ReportAccountDto(a.Id, a.Code, a.Name, a.Category, a.IsActive)).ToListAsync(ct);

        return new CertificateSettingsDto(
            settings.County, settings.FiscalOfficerName, settings.FiscalOfficerTitle, settings.BalanceLabel, settings.OtherSourcesLabel,
            columns.Select(c => new ReportColumnDto(c.Label, accounts.Where(a => c.AccountIds.Contains(a.Id)).Select(a => a.Id).ToList())).ToList(),
            isDefault, accounts);
    }

    public async Task<Result> SaveSettingsAsync(SaveCertificateSettingsRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure(NotAllowed);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        HashSet<Guid> revenue = (await db.Accounts.Where(a => a.Type == AccountType.Revenue).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        if (Problems(request, revenue) is { Count: > 0 } problems)
        {
            return Result.Failure(problems.Select(p => new ValidationError(string.Empty, p)));
        }

        Guid governmentId = currentUser.GovernmentId!.Value;
        CertificateSettings? settings = await db.CertificateSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new CertificateSettings(governmentId);
            db.CertificateSettings.Add(settings);
        }

        try
        {
            settings.Update(request.County, request.FiscalOfficerName, request.FiscalOfficerTitle, request.BalanceLabel, request.OtherSourcesLabel);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        // The columns are replaced as a set: their order and membership are one decision.
        db.ReportAccountGroups.RemoveRange(await db.ReportAccountGroups.Where(g => g.Report == ReportKind.Certificate).ToListAsync(ct));
        for (int i = 0; i < request.Columns.Count; i++)
        {
            db.ReportAccountGroups.Add(new ReportAccountGroup(governmentId, ReportKind.Certificate, request.Columns[i].Label, i, request.Columns[i].AccountIds));
        }

        db.AuditEntries.Add(AuditEntry.Event(governmentId, nameof(Government), governmentId,
            $"Updated the certificate settings: {string.Join(", ", request.Columns.Select(c => $"{c.Label.Trim()} ({c.AccountIds.Count} account{(c.AccountIds.Count == 1 ? "" : "s")})").Append(request.OtherSourcesLabel.Trim()))}",
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>What makes the certificate's columns unusable; the rules are shared with every report's columns.</summary>
    public static List<string> Problems(SaveCertificateSettingsRequest request, IReadOnlySet<Guid> revenueAccounts) =>
        ReportColumnRules.Problems(request.Columns, request.OtherSourcesLabel, revenueAccounts, "revenue");

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>The saved columns, or the Ohio default: one "Taxes" column of the revenue accounts categorized as taxes.</summary>
    private static async Task<(List<CertificateColumn> Columns, bool IsDefault)> ColumnsAsync(ICivicBudgetDbContext db, CancellationToken ct)
    {
        List<ReportAccountGroup> groups = await db.ReportAccountGroups.Include(g => g.Accounts)
            .Where(g => g.Report == ReportKind.Certificate).OrderBy(g => g.SortOrder).ToListAsync(ct);
        if (groups.Count > 0)
        {
            return (groups.Select(g => new CertificateColumn(g.Label, g.Accounts.Select(a => a.AccountId).ToHashSet())).ToList(), false);
        }

        // Saved settings with no columns mean the government chose "everything is other sources".
        if (await db.CertificateSettings.AnyAsync(ct))
        {
            return ([], false);
        }

        HashSet<Guid> taxes = (await db.Accounts.Where(a => a.Type == AccountType.Revenue && a.Category == ReportingCategory.Taxes).Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        return ([new CertificateColumn(DefaultColumnLabel, taxes)], true);
    }

    private static List<CertificateResourceLine> Resources(BudgetVersion version, Government government) => version.Lines
        .Where(l => l.Account.Type.IsResource())
        .Select(l => new CertificateResourceLine(l.FundId, l.DepartmentId, l.AccountId,
            AccountNumber.Compose(government.AccountNumberFormat, l.Fund.Code, l.Department?.Code, l.Account.Code),
            l.Account.Name, l.Account.Type, l.Amount, l.Justification))
        .ToList();

    private static Task<BudgetVersion?> LoadVersionAsync(ICivicBudgetDbContext db, Guid versionId, CancellationToken ct) =>
        db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.BeginningBalances)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);

    private static async Task<int?> YearOfAsync(ICivicBudgetDbContext db, Guid versionId, CancellationToken ct) =>
        await db.BudgetVersions.Where(v => v.Id == versionId)
            .Join(db.FiscalYears, v => v.FiscalYearId, fy => fy.Id, (v, fy) => (int?)fy.Year)
            .FirstOrDefaultAsync(ct);
}
