using CivicBudget.Application.Common;
using CivicBudget.Application.Portal;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Publishing;
using CivicBudget.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Infrastructure.Portal;

/// <summary>
/// The portal's reads, over <see cref="PublicPortalDbContext"/> only. A published budget is small
/// (a village has about a hundred lines), so each request loads the snapshot's lines once and
/// computes breakdowns in memory. That keeps the code readable and, with output caching in front,
/// costs one query per cached page. A county with thousands of lines would push the GROUP BYs into
/// SQL; the interface would not change.
/// </summary>
public sealed class SnapshotQueryService(IDbContextFactory<PublicPortalDbContext> dbFactory) : ISnapshotQueryService
{
    public async Task<IReadOnlyList<(string Slug, string Name)>> ListGovernmentsAsync(CancellationToken ct = default)
    {
        await using PublicPortalDbContext db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Snapshots
            .GroupBy(s => new { s.GovernmentSlug, s.GovernmentName })
            .Select(g => new { g.Key.GovernmentSlug, g.Key.GovernmentName })
            .OrderBy(g => g.GovernmentName)
            .ToListAsync(ct);
        return rows.Select(r => (r.GovernmentSlug, r.GovernmentName)).ToList();
    }

    public async Task<PortalBudgetDto?> GetBudgetAsync(string slug, int? fiscalYear, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        if (data is null)
        {
            return null;
        }

        PublishedBudgetSnapshot s = data.Snapshot;
        return new PortalBudgetDto(
            s.Id, s.GovernmentSlug, s.GovernmentName, s.GovernmentDescription,
            s.FiscalYear, s.VersionLabel, s.AmendmentReason, s.ResolutionNumber, s.AdoptedOnUtc, s.PublishedAtUtc,
            data.Years,
            TotalRevenues: Sum(data.Lines, AccountType.Revenue, l => l.Amount),
            TotalExpenditures: Sum(data.Lines, AccountType.Expenditure, l => l.Amount),
            TotalTransfersIn: Sum(data.Lines, AccountType.TransferIn, l => l.Amount),
            TotalTransfersOut: Sum(data.Lines, AccountType.TransferOut, l => l.Amount),
            TotalBeginningBalance: s.Funds.Sum(f => f.BeginningBalance),
            PriorYearRevenues: Sum(data.Lines, AccountType.Revenue, l => l.CurrentYearBudget),
            PriorYearExpenditures: Sum(data.Lines, AccountType.Expenditure, l => l.CurrentYearBudget));
    }

    public async Task<BreakdownDto?> ExpendituresByFundAsync(string slug, int fiscalYear, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        return data is null ? null : Breakdown("Expenditures by fund",
            data.Lines.Where(l => l.AccountType == AccountType.Expenditure),
            l => l.FundCode, l => $"{l.FundCode} {l.FundName}", l => data.Snapshot.Funds.FirstOrDefault(f => f.Code == l.FundCode)?.Description);
    }

    public async Task<BreakdownDto?> RevenuesByCategoryAsync(string slug, int fiscalYear, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        return data is null ? null : Breakdown("Revenues by source",
            data.Lines.Where(l => l.AccountType == AccountType.Revenue),
            l => l.Category.ToString(), l => Labels.Category(l.Category), _ => null);
    }

    public async Task<BreakdownDto?> ExpendituresByCategoryAsync(string slug, int fiscalYear, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        return data is null ? null : Breakdown("Expenditures by category",
            data.Lines.Where(l => l.AccountType == AccountType.Expenditure),
            l => l.Category.ToString(), l => Labels.Category(l.Category), _ => null);
    }

    public async Task<BreakdownDto?> ExpendituresByDepartmentAsync(string slug, int fiscalYear, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        return data is null ? null : Breakdown("Expenditures by department",
            data.Lines.Where(l => l.AccountType == AccountType.Expenditure && l.DepartmentCode != null),
            l => l.DepartmentCode!, l => $"{l.DepartmentName}", l => l.DepartmentDescription);
    }

    public async Task<PortalFundDto?> GetFundAsync(string slug, int fiscalYear, string fundCode, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        PublishedBudgetSnapshotFund? fund = data?.Snapshot.Funds.FirstOrDefault(f => f.Code == fundCode);
        if (data is null || fund is null)
        {
            return null;
        }

        List<PublishedBudgetSnapshotLine> lines = data.Lines.Where(l => l.FundCode == fundCode).ToList();
        return new PortalFundDto(
            fund.Code, fund.Name, fund.Category, fund.Description, fund.BeginningBalance,
            Sum(lines, AccountType.Revenue, l => l.Amount),
            Sum(lines, AccountType.TransferIn, l => l.Amount),
            Sum(lines, AccountType.Expenditure, l => l.Amount),
            Sum(lines, AccountType.TransferOut, l => l.Amount),
            Breakdown("Expenditures by department", lines.Where(l => l.AccountType == AccountType.Expenditure && l.DepartmentCode != null),
                l => l.DepartmentCode!, l => l.DepartmentName!, l => l.DepartmentDescription),
            Breakdown("Expenditures by category", lines.Where(l => l.AccountType == AccountType.Expenditure),
                l => l.Category.ToString(), l => Labels.Category(l.Category), _ => null),
            Breakdown("Revenues by source", lines.Where(l => l.AccountType is AccountType.Revenue or AccountType.TransferIn),
                l => l.AccountType == AccountType.TransferIn ? "TransfersIn" : l.Category.ToString(),
                l => l.AccountType == AccountType.TransferIn ? "Transfers in" : Labels.Category(l.Category), _ => null));
    }

    public async Task<PortalDepartmentDto?> GetDepartmentAsync(string slug, int fiscalYear, string fundCode, string departmentCode, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        if (data is null)
        {
            return null;
        }

        List<PublishedBudgetSnapshotLine> lines = data.Lines
            .Where(l => l.FundCode == fundCode && l.DepartmentCode == departmentCode)
            .OrderBy(l => l.AccountType).ThenBy(l => l.Category).ThenBy(l => l.AccountCode)
            .ToList();
        if (lines.Count == 0)
        {
            return null;
        }

        PublishedBudgetSnapshotLine first = lines[0];
        return new PortalDepartmentDto(
            first.DepartmentCode!, first.DepartmentName!, first.DepartmentDescription, first.FundCode, first.FundName,
            Sum(lines, AccountType.Expenditure, l => l.Amount),
            Breakdown("Expenditures by category", lines.Where(l => l.AccountType == AccountType.Expenditure),
                l => l.Category.ToString(), l => Labels.Category(l.Category), _ => null),
            lines.Select(ToLine).ToList());
    }

    public async Task<IReadOnlyList<PortalLineDto>> GetLinesAsync(string slug, int fiscalYear, CancellationToken ct = default)
    {
        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        return data is null
            ? []
            : data.Lines.OrderBy(l => l.FundCode).ThenBy(l => l.DepartmentCode).ThenBy(l => l.AccountCode).Select(ToLine).ToList();
    }

    public async Task<IReadOnlyList<YearTotalsDto>> YearOverYearAsync(string slug, CancellationToken ct = default)
    {
        await using PublicPortalDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<PublishedBudgetSnapshot> snapshots = await db.Snapshots
            .Include(s => s.Lines).Include(s => s.Funds)
            .Where(s => s.GovernmentSlug == slug)
            .OrderBy(s => s.FiscalYear)
            .ToListAsync(ct);

        return snapshots.Select(s =>
        {
            decimal revenues = Sum(s.Lines, AccountType.Revenue, l => l.Amount);
            decimal expenditures = Sum(s.Lines, AccountType.Expenditure, l => l.Amount);
            decimal ending = s.Funds.Sum(f => f.BeginningBalance) + revenues + Sum(s.Lines, AccountType.TransferIn, l => l.Amount)
                - expenditures - Sum(s.Lines, AccountType.TransferOut, l => l.Amount);
            return new YearTotalsDto(s.FiscalYear, s.VersionLabel, revenues, expenditures, ending);
        }).ToList();
    }

    public async Task<IReadOnlyList<PortalSearchHitDto>> SearchAsync(string slug, int fiscalYear, string query, CancellationToken ct = default)
    {
        string q = query.Trim();
        if (q.Length < 2)
        {
            return [];
        }

        Loaded? data = await LoadAsync(slug, fiscalYear, ct);
        if (data is null)
        {
            return [];
        }

        string root = $"/transparency/{slug}/{fiscalYear}";
        var hits = new List<PortalSearchHitDto>();

        foreach (PublishedBudgetSnapshotFund fund in data.Snapshot.Funds.Where(f => Matches(f.Code, q) || Matches(f.Name, q)))
        {
            hits.Add(new PortalSearchHitDto("Fund", $"{fund.Code} {fund.Name}", $"{root}/funds/{fund.Code}",
                Sum(data.Lines.Where(l => l.FundCode == fund.Code), AccountType.Expenditure, l => l.Amount)));
        }

        foreach (var dept in data.Lines.Where(l => l.DepartmentCode != null && (Matches(l.DepartmentCode!, q) || Matches(l.DepartmentName!, q)))
                     .GroupBy(l => new { l.FundCode, l.FundName, l.DepartmentCode, l.DepartmentName }))
        {
            hits.Add(new PortalSearchHitDto("Department", $"{dept.Key.DepartmentName} ({dept.Key.FundCode} {dept.Key.FundName})",
                $"{root}/funds/{dept.Key.FundCode}/departments/{dept.Key.DepartmentCode}",
                Sum(dept, AccountType.Expenditure, l => l.Amount)));
        }

        foreach (PublishedBudgetSnapshotLine line in data.Lines.Where(l => Matches(l.AccountCode, q) || Matches(l.AccountName, q)).Take(25))
        {
            string where = line.DepartmentCode is null ? $"{line.FundCode} {line.FundName}" : $"{line.DepartmentName}, {line.FundCode} {line.FundName}";
            string url = line.DepartmentCode is null ? $"{root}/funds/{line.FundCode}" : $"{root}/funds/{line.FundCode}/departments/{line.DepartmentCode}";
            hits.Add(new PortalSearchHitDto("Account", $"{line.AccountCode} {line.AccountName} ({where})", url, line.Amount));
        }

        return hits;
    }

    // ---- helpers ------------------------------------------------------------------------------

    private sealed record Loaded(PublishedBudgetSnapshot Snapshot, IReadOnlyList<PublishedBudgetSnapshotLine> Lines, IReadOnlyList<PortalYearDto> Years);

    private async Task<Loaded?> LoadAsync(string slug, int? fiscalYear, CancellationToken ct)
    {
        await using PublicPortalDbContext db = await dbFactory.CreateDbContextAsync(ct);

        List<PortalYearDto> years = await db.Snapshots
            .Where(s => s.GovernmentSlug == slug)
            .OrderByDescending(s => s.FiscalYear)
            .Select(s => new PortalYearDto(s.FiscalYear, s.VersionLabel, s.PublishedAtUtc))
            .ToListAsync(ct);
        if (years.Count == 0)
        {
            return null;
        }

        int year = fiscalYear ?? years[0].FiscalYear;
        PublishedBudgetSnapshot? snapshot = await db.Snapshots
            .Include(s => s.Lines).Include(s => s.Funds)
            .FirstOrDefaultAsync(s => s.GovernmentSlug == slug && s.FiscalYear == year, ct);
        return snapshot is null ? null : new Loaded(snapshot, snapshot.Lines.ToList(), years);
    }

    private static decimal Sum(IEnumerable<PublishedBudgetSnapshotLine> lines, AccountType type, Func<PublishedBudgetSnapshotLine, decimal> amount) =>
        lines.Where(l => l.AccountType == type).Sum(amount);

    private static BreakdownDto Breakdown(
        string title,
        IEnumerable<PublishedBudgetSnapshotLine> lines,
        Func<PublishedBudgetSnapshotLine, string> key,
        Func<PublishedBudgetSnapshotLine, string> label,
        Func<PublishedBudgetSnapshotLine, string?> description)
    {
        List<BreakdownItemDto> items = lines
            .GroupBy(key)
            .Select(g => new BreakdownItemDto(g.Key, label(g.First()), description(g.First()), g.Sum(l => l.Amount), g.Sum(l => l.CurrentYearBudget)))
            .OrderByDescending(i => i.Amount)
            .ToList();
        return new BreakdownDto(title, items.Sum(i => i.Amount), items);
    }

    private static bool Matches(string value, string query) => value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static PortalLineDto ToLine(PublishedBudgetSnapshotLine l) => new(
        l.FundCode, l.FundName, l.DepartmentCode, l.DepartmentName, l.AccountCode, l.AccountName,
        l.AccountType, l.Category, l.Amount, l.PriorYearActual, l.CurrentYearBudget);
}
