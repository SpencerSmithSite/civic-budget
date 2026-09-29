using System.ComponentModel;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace CivicBudget.Application.Assistant;

/// <summary>
/// The assistant's reading tools: each one calls the service a page calls, as the signed-in user,
/// and returns a compact summary with the path of the page that shows the whole thing. The service
/// decides what the user may see (a department user's department detail holds only their
/// departments; a whole-fund report is null for them), so the tools add no permission logic of their
/// own and cannot show more than the page would. Every optional parameter has a default value:
/// without one the tool layer marks it required, and a model that leaves it out gets an error.
/// </summary>
public sealed class BudgetTools(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IBudgetEntryService entry,
    IReportService reports,
    IActualsReportService actuals,
    IBudgetPlanService plans,
    ICertificateService certificates,
    TimeProvider clock) : IAssistantToolProvider
{
    private const string VersionHelp = "The budget version's id from list_budget_versions. Leave empty for the budget on the user's page, or else the current fiscal year's adopted budget.";

    public IEnumerable<AIFunction> Tools(AssistantTurn turn)
    {
        yield return AIFunctionFactory.Create(
            async (CancellationToken ct) => await ListVersionsAsync(turn, ct),
            "list_budget_versions",
            "Every budget version of this government: id, fiscal year, label (Original or Amendment n), status (Draft, Proposed, Adopted), and resolution number.");

        yield return AIFunctionFactory.Create(
            async ([Description(VersionHelp)] string? versionId = null, CancellationToken ct = default) => await FundSummaryAsync(turn, versionId, ct),
            "fund_summary",
            "Each fund's beginning balance, revenues, transfers, appropriations, projected ending balance, and whether appropriations stay within estimated resources (the Ohio limit).");

        yield return AIFunctionFactory.Create(
            async ([Description(VersionHelp)] string? versionId = null, [Description("A fund number to list that fund's lines, e.g. 1000. Empty for totals by fund and department.")] string? fundCode = null, CancellationToken ct = default) =>
                await BudgetVsActualAsync(turn, versionId, fundCode, ct),
            "budget_vs_actual",
            "Spending so far this year against the budget, from the ERP's books: by fund and department, spent, encumbered (committed, not yet spent), remaining, the share used, and where the same line stood last year at this point. For questions like 'how are we doing against budget'.");

        yield return AIFunctionFactory.Create(
            async ([Description(VersionHelp)] string? versionId = null, CancellationToken ct = default) => await RevenueVsReceiptsAsync(turn, versionId, ct),
            "revenue_vs_receipts",
            "Revenue received so far this year against each estimate, compared with how much had arrived by the same month last year, and which revenues are behind.");

        yield return AIFunctionFactory.Create(
            async ([Description(VersionHelp)] string? versionId = null, CancellationToken ct = default) => await FundProjectionAsync(turn, versionId, ct),
            "fund_projection",
            "Where each fund is projected to end the year from this year's actuals so far, beside where the budget said it would end.");

        yield return AIFunctionFactory.Create(
            async ([Description(VersionHelp)] string? versionId = null, [Description("A department's code (e.g. 110) for its lines and narrative. Empty for every department's totals.")] string? departmentCode = null, CancellationToken ct = default) =>
                await DepartmentsAsync(turn, versionId, departmentCode, ct),
            "department_budget",
            "Each department's spending budget against last year's budget and actual, with the change; or one department's lines and its narrative.");

        yield return AIFunctionFactory.Create(
            async ([Description("Words or a number to find: an account name ('overtime'), code (5120), or full account number (1000-110-5120).")] string text, [Description(VersionHelp)] string? versionId = null, CancellationToken ct = default) =>
                await SearchLinesAsync(turn, text, versionId, ct),
            "search_budget_lines",
            "Finds budget lines by account name, code, number, fund, or department, with the amount, this year's budget, last year's actual, and the justification typed on the line.");

        yield return AIFunctionFactory.Create(
            async ([Description(VersionHelp)] string? versionId = null, CancellationToken ct = default) => await PlanAsync(turn, versionId, ct),
            "multi_year_plan",
            "The multi-year plan: each future year's assumed revenue and spending change, and each fund's projected ending balance year by year.");

        yield return AIFunctionFactory.Create(
            async ([Description(VersionHelp)] string? versionId = null, CancellationToken ct = default) => await CertificateAsync(turn, versionId, ct),
            "certificate",
            "The certificate of estimated resources (or the amended certificate, for an amendment): each fund's total available against its appropriations, and the reconciliation checks.");
    }

    // ---- the tools ----------------------------------------------------------------------------

    private async Task<object> ListVersionsAsync(AssistantTurn turn, CancellationToken ct)
    {
        IReadOnlyList<BudgetVersionSummaryDto> versions = await entry.ListVersionsAsync(ct);
        turn.Steps.Add(new AssistantStep("Listed the budget versions"));
        return versions.OrderByDescending(v => v.Year).ThenByDescending(v => v.VersionNumber)
            .Select(v => new { id = v.Id, fiscalYear = v.Year, label = v.Label, status = v.Status.ToString(), resolution = v.ResolutionNumber, page = $"/admin/budgets/{v.Id}" });
    }

    private async Task<object> FundSummaryAsync(AssistantTurn turn, string? versionId, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct) is not { } v)
        {
            return NoVersion;
        }

        if (await reports.FundSummaryAsync(v.Id, ct) is not { } report)
        {
            return NotShown;
        }

        turn.Steps.Add(new AssistantStep($"Fund summary, {Name(v)}"));
        return new
        {
            version = Name(v),
            funds = report.Funds.Append(report.Total).Select(f => new
            {
                fund = f.FundCode == report.Total.FundCode ? "All funds" : $"{f.FundCode} {f.FundName}",
                f.BeginningBalance,
                f.Revenues,
                f.TransfersIn,
                f.EstimatedResources,
                f.Expenditures,
                f.TransfersOut,
                f.Appropriations,
                f.ProjectedEndingBalance,
                withinLimit = f.IsWithinAppropriationLimit,
            }),
            page = $"/admin/reports/{v.Id}/fund-summary",
        };
    }

    private async Task<object> BudgetVsActualAsync(AssistantTurn turn, string? versionId, string? fundCode, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct, forActuals: true) is not { } v)
        {
            return NoVersion;
        }

        if (await actuals.BudgetVsActualAsync(v.Id, ct) is not { } report)
        {
            return NotShown;
        }

        turn.Steps.Add(new AssistantStep($"Budget against actual, {Name(v)}"));
        if (report.Period is not { } period)
        {
            return new { version = Name(v), noActuals = $"The ERP has sent no actuals for FY{v.Year} yet.", page = $"/admin/reports/{v.Id}/budget-vs-actual" };
        }

        var funds = report.Funds.Where(f => fundCode is null || f.FundCode == fundCode.Trim()).ToList();
        return new
        {
            version = Name(v),
            actualsThroughMonth = period.ThroughPeriod,
            asOf = period.AsOf,
            shareOfYearGone = Percent(period.Pace),
            yearIsClosed = period.IsClosed,
            total = Row(report.Total),
            funds = funds.Select(f => new
            {
                fund = $"{f.FundCode} {f.FundName}",
                total = Row(f.Subtotal),
                departments = f.Departments.Select(d => new { department = d.Label, total = Row(d.Subtotal) }),
                lines = fundCode is null ? null : f.Departments.SelectMany(d => d.Lines).OrderByDescending(l => l.Used ?? 0m).Take(30).Select(Row),
            }),
            overBudget = report.Funds.SelectMany(f => f.Departments.SelectMany(d => d.Lines)).Where(l => l.IsOver).Take(15).Select(Row),
            page = $"/admin/reports/{v.Id}/budget-vs-actual",
        };

        static object Row(BudgetActualRowDto r) => new
        {
            line = $"{r.AccountNumber} {r.Label}".Trim(),
            budget = r.Budget,
            spent = r.Actual,
            encumbered = r.Encumbered,
            remaining = r.Remaining,
            usedPercent = Percent(r.Used),
            lastYearAtThisPointPercent = r.LastYearAtThisPoint is { } l && r.Budget != 0 ? Percent(l / r.Budget) : null,
        };
    }

    private async Task<object> RevenueVsReceiptsAsync(AssistantTurn turn, string? versionId, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct, forActuals: true) is not { } v)
        {
            return NoVersion;
        }

        if (await actuals.RevenueVsReceiptsAsync(v.Id, ct) is not { } report)
        {
            return NotShown;
        }

        turn.Steps.Add(new AssistantStep($"Revenue against receipts, {Name(v)}"));
        if (report.Period is null)
        {
            return new { version = Name(v), noActuals = $"The ERP has sent no actuals for FY{v.Year} yet.", page = $"/admin/reports/{v.Id}/revenue-vs-receipts" };
        }

        return new
        {
            version = Name(v),
            actualsThroughMonth = report.Period.ThroughPeriod,
            asOf = report.Period.AsOf,
            total = Row(report.Total),
            funds = report.Funds.Select(f => new { fund = $"{f.FundCode} {f.FundName}", total = Row(f.Subtotal) }),
            // Named for exactly what it holds, so the model does not read an empty list as "nothing is behind at all".
            linesMoreThanTenPointsBehindLastYear = report.Funds.SelectMany(f => f.Lines).Where(l => l.IsBehind).Take(15).Select(Row),
            page = $"/admin/reports/{v.Id}/revenue-vs-receipts",
        };

        static object Row(RevenueReceiptRowDto r) => new
        {
            line = $"{r.AccountNumber} {r.Label}".Trim(),
            estimate = r.Estimate,
            received = r.Received,
            stillToCollect = r.StillToCollect,
            collectedPercent = Percent(r.Collected),
            normallyByNowPercent = Percent(r.NormallyByNow),
        };
    }

    private async Task<object> FundProjectionAsync(AssistantTurn turn, string? versionId, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct, forActuals: true) is not { } v)
        {
            return NoVersion;
        }

        if (await actuals.FundProjectionAsync(v.Id, ct) is not { } report)
        {
            return NotShown;
        }

        turn.Steps.Add(new AssistantStep($"Projected fund balances, {Name(v)}"));
        return new
        {
            version = Name(v),
            actualsThroughMonth = report.Period?.ThroughPeriod,
            funds = report.Funds.Append(report.Total).Select(f => new
            {
                fund = f == report.Total ? "All funds" : $"{f.FundCode} {f.FundName}",
                f.BeginningBalance,
                budgetedEnding = f.BudgetedEnding,
                projectedEnding = f.ProjectedEnding,
                difference = f.Difference,
                f.ReceivedToDate,
                f.SpentToDate,
                f.Encumbered,
            }),
            page = $"/admin/reports/{v.Id}/fund-projection",
        };
    }

    private async Task<object> DepartmentsAsync(AssistantTurn turn, string? versionId, string? departmentCode, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct) is not { } v)
        {
            return NoVersion;
        }

        if (await reports.DepartmentDetailAsync(v.Id, null, ct) is not { } report)
        {
            return NotShown;
        }

        if (departmentCode is { Length: > 0 } code)
        {
            if (report.Departments.FirstOrDefault(d => d.DepartmentCode == code.Trim()) is not { } d)
            {
                return new { version = Name(v), notFound = $"No department {code} in this budget that this user can see." };
            }

            turn.Steps.Add(new AssistantStep($"{d.DepartmentName} detail, {Name(v)}"));
            return new
            {
                version = Name(v),
                department = $"{d.DepartmentCode} {d.DepartmentName}",
                narrative = d.Narrative,
                total = new { amount = d.Amount, currentYearBudget = d.CurrentYearBudget, priorYearActual = d.PriorYearActual, change = d.DollarChange, changePercent = d.PercentChange },
                lines = d.Lines.Select(l => new
                {
                    account = $"{l.AccountNumber} {l.AccountName}",
                    type = l.AccountType.ToString(),
                    countsTowardTotal = l.AccountType.CountsTowardDepartmentTotal(),
                    amount = l.Amount,
                    currentYearBudget = l.CurrentYearBudget,
                    priorYearActual = l.PriorYearActual,
                    change = l.DollarChange,
                    justification = l.Justification,
                }),
                page = $"/admin/reports/{v.Id}/department-detail?department={d.DepartmentId}",
            };
        }

        turn.Steps.Add(new AssistantStep($"Department totals, {Name(v)}"));
        return new
        {
            version = Name(v),
            departments = report.Departments.Select(d => new
            {
                department = $"{d.DepartmentCode} {d.DepartmentName}",
                amount = d.Amount,
                currentYearBudget = d.CurrentYearBudget,
                priorYearActual = d.PriorYearActual,
                change = d.DollarChange,
                changePercent = d.PercentChange,
                hasNarrative = !string.IsNullOrWhiteSpace(d.Narrative),
            }),
            total = new { amount = report.TotalAmount, currentYearBudget = report.TotalCurrentYearBudget, priorYearActual = report.TotalPriorYearActual },
            page = $"/admin/reports/{v.Id}/department-detail",
        };
    }

    private async Task<object> SearchLinesAsync(AssistantTurn turn, string text, string? versionId, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct) is not { } v)
        {
            return NoVersion;
        }

        if (await entry.GetWorkspaceAsync(v.Id, ct) is not { } workspace)
        {
            return NotShown;
        }

        string q = text.Trim();
        string compact = q.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);
        List<BudgetLineDto> found = [.. workspace.Lines.Where(l =>
            l.AccountName.Contains(q, StringComparison.OrdinalIgnoreCase) || l.AccountCode == q
            || l.AccountNumber.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).Contains(compact, StringComparison.OrdinalIgnoreCase)
            || (l.DepartmentName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || l.FundName.Contains(q, StringComparison.OrdinalIgnoreCase))];
        turn.Steps.Add(new AssistantStep($"Searched the lines of {Name(v)} for \"{q}\""));
        return new
        {
            version = Name(v),
            matches = found.Count,
            lines = found.Take(25).Select(l => new
            {
                account = $"{l.AccountNumber} {l.AccountName}",
                fund = $"{l.FundCode} {l.FundName}",
                department = l.DepartmentName,
                type = l.AccountType.ToString(),
                amount = l.Amount,
                currentYearBudget = l.CurrentYearBudget,
                priorYearActual = l.PriorYearActual,
                change = l.DollarChange,
                justification = l.Justification,
                calculatedFromPositions = l.PositionCount,
            }),
            page = $"/admin/budgets/{v.Id}",
        };
    }

    private async Task<object> PlanAsync(AssistantTurn turn, string? versionId, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct) is not { } v)
        {
            return NoVersion;
        }

        if (await plans.GetAsync(v.Id, ct) is not { } plan)
        {
            return NotShown;
        }

        turn.Steps.Add(new AssistantStep($"Multi-year plan, {Name(v)}"));
        return new
        {
            version = Name(v),
            years = plan.FiscalYears,
            assumptions = plan.Rates.Select(r => new { fiscalYear = r.FiscalYear, revenueChangePercent = r.RevenuePercent, spendingChangePercent = r.ExpenditurePercent }),
            funds = plan.Funds.Select(f => new
            {
                fund = $"{f.FundCode} {f.FundName}",
                endingBalanceByYear = f.Years.Select(y => new { fiscalYear = y.FiscalYear, endingBalance = y.EndingBalance, overLimit = y.OverLimit }),
            }),
            page = $"/admin/budgets/{v.Id}/plan",
        };
    }

    private async Task<object> CertificateAsync(AssistantTurn turn, string? versionId, CancellationToken ct)
    {
        if (await ResolveAsync(turn, versionId, ct) is not { } v)
        {
            return NoVersion;
        }

        if (await certificates.GetAsync(v.Id, ct) is not { } certificate)
        {
            return NotShown;
        }

        turn.Steps.Add(new AssistantStep($"{certificate.Header.Title}, {Name(v)}"));
        return new
        {
            version = Name(v),
            title = certificate.Header.Title,
            balancesFromErp = certificate.CarryoverFromErp,
            funds = certificate.Funds.Append(certificate.Total).Select(f => new
            {
                fund = f == certificate.Total ? "All funds" : $"{f.FundCode} {f.FundName}",
                carryover = f.Carryover,
                estimatedRevenue = f.EstimatedRevenue,
                totalAvailable = f.TotalAvailable,
                appropriations = f.Appropriations,
                withinLimit = f.IsWithinLimit,
            }),
            checks = certificate.Checks.Select(c => new { c.Label, c.Passed, c.Detail }),
            page = $"/admin/reports/{v.Id}/certificate",
        };
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static readonly object NoVersion = new { problem = "No budget version matches. Use list_budget_versions for the ids." };

    // A service returns null when this user may not see the whole of what it shows.
    private static readonly object NotShown = new { problem = "This report covers every fund, so it is not available to this user's account." };

    private static string Name(BudgetVersionSummaryDto v) => $"FY{v.Year} {v.Label} ({v.Status})";

    private static decimal? Percent(decimal? share) => share is { } s ? Math.Round(s * 100m, 1) : null;

    /// <summary>
    /// The version a tool is about: the one named, else the one on the user's page, else the current
    /// fiscal year's latest adopted version, else the newest adopted, else the newest of all. For the
    /// actuals reports the page's version counts only if it is this year's: on next year's draft,
    /// "how are we doing" is about the year under way, which is the only one with books.
    /// </summary>
    private async Task<BudgetVersionSummaryDto?> ResolveAsync(AssistantTurn turn, string? versionId, CancellationToken ct, bool forActuals = false)
    {
        IReadOnlyList<BudgetVersionSummaryDto> versions = await entry.ListVersionsAsync(ct);
        if (versionId is { Length: > 0 })
        {
            return Guid.TryParse(versionId, out Guid id) ? versions.FirstOrDefault(v => v.Id == id) : null;
        }

        int currentYear = await CurrentFiscalYearAsync(ct);
        if (turn.PageVersionId is { } onPage && versions.FirstOrDefault(v => v.Id == onPage) is { } shown && (!forActuals || shown.Year == currentYear))
        {
            return shown;
        }

        IEnumerable<BudgetVersionSummaryDto> adopted = versions.Where(v => v.Status == BudgetStatus.Adopted).OrderByDescending(v => v.Year).ThenByDescending(v => v.VersionNumber);
        return adopted.FirstOrDefault(v => v.Year == currentYear) ?? adopted.FirstOrDefault() ?? versions.OrderByDescending(v => v.Year).ThenByDescending(v => v.VersionNumber).FirstOrDefault();
    }

    private async Task<int> CurrentFiscalYearAsync(CancellationToken ct)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        int startMonth = await db.Governments.Where(g => g.Id == currentUser.GovernmentId).Select(g => g.FiscalYearStartMonth).SingleAsync(ct);
        DateOnly today = Common.OhioTime.DateOf(clock.GetUtcNow());
        return startMonth == 1 || today.Month < startMonth ? today.Year : today.Year + 1;
    }
}
