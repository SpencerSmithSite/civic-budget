using System.Globalization;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;

namespace CivicBudget.Application.Assistant;

/// <summary>One thing the review found: whether it stops the budget going forward, what it is, and where to fix it.</summary>
public sealed record ReviewFinding(bool MustFix, string Finding, string Page);

/// <summary>
/// "Check my budget before I propose it": everything a fiscal officer would look for before taking a
/// budget to council, from the reports that already say it. Pure, so the rules are tested without a
/// model; the assistant only reads the findings out.
/// </summary>
public static class BudgetReview
{
    /// <summary>A change this large, without a word of justification, is the question council will ask.</summary>
    public const decimal NotableChangePercent = 10m;

    public const decimal NotableChangeDollars = 1_000m;

    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private static string Money(decimal amount) => amount.ToString("C2", Us);

    public static IReadOnlyList<ReviewFinding> Check(BudgetWorkspaceDto workspace, BudgetPlanDto? plan, CertificateReportDto? certificate)
    {
        Guid id = workspace.Version.Id;
        var findings = new List<ReviewFinding>();

        foreach (FundBalanceDto fund in workspace.FundBalances.Where(f => f.Limit.AmountOverLimit > 0m))
        {
            findings.Add(new ReviewFinding(fund.Limit.BlocksWorkflow,
                $"Fund {fund.FundCode} {fund.FundName} appropriates {Money(fund.Limit.AmountOverLimit)} more than its estimated resources (the Ohio limit).",
                $"/admin/budgets/{id}"));
        }

        if (workspace.Version.Status == BudgetStatus.Draft)
        {
            List<string> open = [.. workspace.DepartmentRequests.Where(d => d.Status != DepartmentRequestStatus.Submitted).Select(d => $"{d.DepartmentCode} {d.DepartmentName} ({(d.Status == DepartmentRequestStatus.Returned ? "returned" : "in progress")})")];
            if (open.Count > 0)
            {
                findings.Add(new ReviewFinding(false, $"{open.Count} department request{(open.Count == 1 ? " is" : "s are")} not submitted: {string.Join(", ", open)}.", $"/admin/budgets/{id}/departments"));
            }
        }

        List<string> silent = [.. workspace.DepartmentRequests.Where(d => d.LineCount > 0 && string.IsNullOrWhiteSpace(d.Narrative)).Select(d => $"{d.DepartmentCode} {d.DepartmentName}")];
        if (silent.Count > 0)
        {
            findings.Add(new ReviewFinding(false, $"No narrative from {string.Join(", ", silent)}; the budget book will show their figures alone.", $"/admin/budgets/{id}/departments"));
        }

        List<BudgetLineDto> unexplained = [.. workspace.Lines
            .Where(l => string.IsNullOrWhiteSpace(l.Justification)
                && Math.Abs(l.DollarChange) >= NotableChangeDollars
                && (l.PercentChange is not { } p || Math.Abs(p) >= NotableChangePercent))
            .OrderByDescending(l => Math.Abs(l.DollarChange))];
        if (unexplained.Count > 0)
        {
            findings.Add(new ReviewFinding(false,
                $"{unexplained.Count} line{(unexplained.Count == 1 ? " changes" : "s change")} by {NotableChangePercent}% and {NotableChangeDollars.ToString("C0", Us)} or more with no justification, largest first: "
                + string.Join("; ", unexplained.Take(5).Select(l => $"{l.AccountNumber} {l.AccountName} {(l.DollarChange >= 0 ? "+" : "-")}{Money(Math.Abs(l.DollarChange))}")) + ".",
                $"/admin/budgets/{id}"));
        }

        foreach (CertificateCheckDto check in certificate?.Checks.Where(c => !c.Passed) ?? [])
        {
            findings.Add(new ReviewFinding(true, $"The certificate's check \"{check.Label}\" fails: {check.Detail}", $"/admin/reports/{id}/certificate"));
        }

        foreach (PlanFundDto fund in plan?.Funds ?? [])
        {
            List<int> overYears = [.. fund.Years.Skip(1).Where(y => y.OverLimit).Select(y => y.FiscalYear)];
            if (overYears.Count > 0)
            {
                findings.Add(new ReviewFinding(false, $"In the multi-year plan, fund {fund.FundCode} {fund.FundName} overspends in FY{string.Join(", FY", overYears)}.", $"/admin/budgets/{id}/plan"));
            }
        }

        if (workspace.Lines.Any(l => l.AccountType == AccountType.Expenditure && l.Amount == 0m && l.CurrentYearBudget > 0m))
        {
            int zeroed = workspace.Lines.Count(l => l.AccountType == AccountType.Expenditure && l.Amount == 0m && l.CurrentYearBudget > 0m);
            findings.Add(new ReviewFinding(false, $"{zeroed} expenditure line{(zeroed == 1 ? " is" : "s are")} budgeted at zero after having a budget this year; check none were zeroed by mistake.", $"/admin/budgets/{id}"));
        }

        return findings;
    }
}
