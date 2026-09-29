using System.ComponentModel;
using System.Globalization;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Governments;
using Microsoft.Extensions.AI;

namespace CivicBudget.Application.Assistant;

/// <summary>
/// The assistant's actions. None of them changes anything: each checks that the user may make the
/// change, works it out through the service a page would use (a preview, where the service has one),
/// and leaves a proposal for the user to confirm with a click. Confirming runs the same service call,
/// as the user, which checks everything again (ADR-0047).
/// </summary>
public sealed class ActionTools(
    ICurrentUser currentUser,
    VersionResolver resolver,
    AssistantProposals proposals,
    IBudgetEntryService entry,
    IBudgetPlanService plans,
    IBudgetWorkflowService workflow,
    IFiscalYearService fiscalYears,
    IActualsSyncService actuals,
    IBudgetBookService books,
    IDepartmentRequestService departmentRequests) : IAssistantToolProvider
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private const string VersionHelp = "The budget version's id from list_budget_versions. Leave empty for the open budget on the user's page, or else the open (draft or proposed) budget.";

    public IEnumerable<AIFunction> Tools(AssistantTurn turn)
    {
        yield return AIFunctionFactory.Create(
            async ([Description("How many years the plan covers, counting the budget year: 1 to 10 (5 is a five-year plan).")] int years,
                   [Description("The yearly change in revenues and transfers in, in percent (4 for 4% a year).")] decimal revenuePercent,
                   [Description("The yearly change in spending and transfers out, in percent.")] decimal expenditurePercent,
                   [Description("Round calculated years to whole dollars. Leave empty to keep the budget's current choice.")] bool? wholeDollars = null,
                   [Description(VersionHelp)] string? versionId = null,
                   CancellationToken ct = default) => await PlanAsync(turn, years, revenuePercent, expenditurePercent, wholeDollars, versionId, ct),
            "propose_multi_year_plan",
            "Proposes the multi-year plan's length and yearly percentages, previewing each fund's ending balance. The user confirms it.");

        yield return AIFunctionFactory.Create(
            async ([Description("Words or a code that pick the lines: an account name ('utilities', 'overtime'), object code (5320), or account number. Empty for every line the other filters allow.")] string? account = null,
                   [Description("Only this department's lines, by code (e.g. 620).")] string? departmentCode = null,
                   [Description("Only this fund's lines, by number (e.g. 2011).")] string? fundCode = null,
                   [Description("Change each line by this percent (5 for +5%, -10 for -10%). Give this or newAmount.")] decimal? changePercent = null,
                   [Description("Set each line to this amount in dollars. Give this or changePercent; best for one line.")] decimal? newAmount = null,
                   [Description("Round the new amounts to whole dollars.")] bool roundToWholeDollars = false,
                   [Description(VersionHelp)] string? versionId = null,
                   CancellationToken ct = default) => await LinesAsync(turn, account, departmentCode, fundCode, changePercent, newAmount, roundToWholeDollars, versionId, ct),
            "propose_line_changes",
            "Proposes new amounts for budget lines picked by account, department, or fund, previewing every line before and after. Lines calculated from positions are left out. The user confirms it.");

        yield return AIFunctionFactory.Create(
            async ([Description("The fund's number, e.g. 2011.")] string fundCode,
                   [Description(VersionHelp)] string? versionId = null,
                   CancellationToken ct = default) => await FundFixAsync(turn, fundCode, versionId, ct),
            "propose_fund_fix",
            "For a fund whose appropriations exceed its estimated resources: proposes cutting its spending lines in proportion, by exactly the amount over the limit, previewing every line. The user confirms it.");

        yield return AIFunctionFactory.Create(
            async ([Description("The fiscal year to start, e.g. 2028.")] int fiscalYear,
                   [Description("Change from last year's adopted amounts, in percent (0 to copy as is).")] decimal adjustmentPercent = 0m,
                   [Description("Apply the change to spending only, leaving revenue estimates as they were.")] bool appropriationsOnly = true,
                   [Description("Round the new amounts to whole dollars.")] bool roundToWholeDollars = true,
                   CancellationToken ct = default) => await StartBudgetAsync(turn, fiscalYear, adjustmentPercent, appropriationsOnly, roundToWholeDollars, ct),
            "propose_start_budget",
            "Proposes starting a fiscal year's budget from the prior year's adopted budget. The user confirms it.");

        yield return AIFunctionFactory.Create(
            async ([Description("The fiscal year to bring in. Empty for the current year.")] int? fiscalYear = null, CancellationToken ct = default) => await ActualsAsync(turn, fiscalYear, ct),
            "propose_fetch_actuals",
            "Proposes bringing a fiscal year's actuals in from the ERP, previewing the totals by fund and what the sync would change. The user confirms it.");

        yield return AIFunctionFactory.Create(
            async ([Description("The whole message as the reader will see it, with a blank line between paragraphs.")] string body,
                   [Description("A heading, e.g. 'A budget that keeps our promises'. Empty for 'Budget message'.")] string? heading = null,
                   [Description("Who signs it, e.g. 'Rebecca Lang'.")] string? signedBy = null,
                   [Description("Their office, e.g. 'Mayor'.")] string? signerTitle = null,
                   [Description(VersionHelp)] string? versionId = null,
                   CancellationToken ct = default) => await MessageAsync(turn, body, heading, signedBy, signerTitle, versionId, ct),
            "propose_budget_message",
            "Proposes the budget message that opens the budget book, written from the budget's own figures. The user reads it and confirms it.");

        yield return AIFunctionFactory.Create(
            async ([Description("The department's code, e.g. 110.")] string departmentCode,
                   [Description("The narrative: what the department asks for and why, in plain words.")] string narrative,
                   [Description(VersionHelp)] string? versionId = null,
                   CancellationToken ct = default) => await NarrativeAsync(turn, departmentCode, narrative, versionId, ct),
            "propose_department_narrative",
            "Proposes a department's narrative for its budget request, written from its own lines. The user reads it and confirms it.");
    }

    // ---- the actions --------------------------------------------------------------------------

    private async Task<object> PlanAsync(AssistantTurn turn, int years, decimal revenuePercent, decimal expenditurePercent, bool? wholeDollars, string? versionId, CancellationToken ct)
    {
        if (await resolver.ResolveOpenAsync(turn, versionId, ct) is not { } v)
        {
            return NoOpenVersion;
        }

        if (await plans.GetAsync(v.Id, ct) is not { } current)
        {
            return NotAllowed;
        }

        var request = new SavePlanRequest(v.Id, years, wholeDollars ?? current.WholeDollars,
            [.. Enumerable.Range(1, Math.Max(0, years - 1)).Select(offset => new PlanRateDto(offset, v.Year + offset, revenuePercent, expenditurePercent))]);
        Result<BudgetPlanDto> preview = await plans.PreviewPlanAsync(request, ct);
        if (preview.IsFailure)
        {
            return Problem(preview.Errors);
        }

        int lastYear = v.Year + years - 1;
        List<ProposalRow> rows = [.. preview.Value.Funds.Select(f => new ProposalRow(
            $"{f.FundCode} {f.FundName}, ending FY{lastYear}",
            current.Funds.FirstOrDefault(c => c.FundCode == f.FundCode)?.Years.FirstOrDefault(y => y.FiscalYear == lastYear) is { } before ? Money(before.EndingBalance) : null,
            Money(f.Years[^1].EndingBalance) + (f.Years[^1].OverLimit ? " (overspends)" : "")))];
        string title = $"Plan FY{v.Year} {v.Label} for {years} year{(years == 1 ? "" : "s")}: revenues {Percent(revenuePercent)} and spending {Percent(expenditurePercent)} a year";
        string note = $"Each row is the fund's ending balance in FY{lastYear}: under the plan as saved now, and as proposed. Lines typed by hand in later years keep their amounts.";
        return Propose(turn, title, rows, null, "Save the plan", note,
            async c => (await plans.SavePlanAsync(request, c)).IsSuccess
                ? Result.Success(new ProposalOutcome("The plan is saved.", $"/admin/budgets/{v.Id}/plan"))
                : Result.Failure<ProposalOutcome>("The plan could not be saved; the budget may have changed. Ask again."),
            nameof(BudgetVersion), v.Id);
    }

    private async Task<object> LinesAsync(AssistantTurn turn, string? account, string? departmentCode, string? fundCode, decimal? changePercent, decimal? newAmount,
        bool roundToWholeDollars, string? versionId, CancellationToken ct)
    {
        if (changePercent is null == newAmount is null)
        {
            return new { problem = "Give either changePercent or newAmount." };
        }

        if (await resolver.ResolveOpenAsync(turn, versionId, ct) is not { } v)
        {
            return NoOpenVersion;
        }

        if (await entry.GetWorkspaceAsync(v.Id, ct) is not { } workspace)
        {
            return NotAllowed;
        }

        string? q = account?.Trim();
        string compact = q?.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal) ?? "";
        List<BudgetLineDto> matched = [.. workspace.Lines.Where(l =>
            (string.IsNullOrEmpty(q) || l.AccountName.Contains(q, StringComparison.OrdinalIgnoreCase) || l.AccountCode == q
                || (compact.Length >= 4 && l.AccountNumber.Replace("-", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal).Contains(compact, StringComparison.Ordinal))
                || Labels.Category(l.Category).Contains(q, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(departmentCode) || l.DepartmentCode == departmentCode.Trim())
            && (string.IsNullOrEmpty(fundCode) || l.FundCode == fundCode.Trim()))];
        List<BudgetLineDto> changeable = [.. matched.Where(l => l.CanEditAmount)];
        int calculated = matched.Count(l => l.CanEdit && l.PositionCount is not null);
        int locked = matched.Count(l => !l.CanEdit);
        if (changeable.Count == 0)
        {
            return new { problem = matched.Count == 0 ? "No line matches." : $"{matched.Count} line{(matched.Count == 1 ? " matches" : "s match")}, but none can be changed by this user: {calculated} calculated from positions, {locked} not open to them." };
        }

        List<LineAmountChange> changes = [.. changeable.Select(l => new LineAmountChange(l.Id, l.Amount, NewAmount(l.Amount)))
            .Where(c => c.Amount != c.Expected)];
        if (changes.Count == 0)
        {
            return new { problem = "Those lines already have those amounts." };
        }

        Dictionary<Guid, BudgetLineDto> byId = changeable.ToDictionary(l => l.Id);
        List<ProposalRow> rows = [.. changes.Select(c => new ProposalRow(LineLabel(byId[c.LineId]), Money(c.Expected), Money(c.Amount)))];
        decimal before = changes.Sum(c => c.Expected), after = changes.Sum(c => c.Amount);
        List<string> notes = [$"Total {Money(before)} becomes {Money(after)} ({(after >= before ? "+" : "-")}{Money(Math.Abs(after - before))})."];
        if (calculated > 0)
        {
            notes.Add($"{calculated} matching line{(calculated == 1 ? " is" : "s are")} calculated from positions and left out; change the positions instead.");
        }

        if (locked > 0)
        {
            notes.Add($"{locked} matching line{(locked == 1 ? " is" : "s are")} not open to this account and left out.");
        }

        string how = changePercent is { } p ? Percent(p) : $"set to {Money(newAmount!.Value)}";
        string title = $"Change {changes.Count} line{(changes.Count == 1 ? "" : "s")} in FY{v.Year} {v.Label}: {(string.IsNullOrEmpty(q) ? "" : q + " ")}{how}";
        return Propose(turn, title, rows, null, $"Change {changes.Count} line{(changes.Count == 1 ? "" : "s")}", string.Join(" ", notes),
            async c =>
            {
                Result saved = await entry.UpdateLineAmountsAsync(v.Id, changes, c);
                return saved.IsSuccess
                    ? Result.Success(new ProposalOutcome($"{changes.Count} line{(changes.Count == 1 ? " is" : "s are")} updated.", $"/admin/budgets/{v.Id}"))
                    : Result.Failure<ProposalOutcome>(saved.Errors);
            },
            nameof(BudgetVersion), v.Id);

        decimal NewAmount(decimal amount)
        {
            decimal value = changePercent is { } pct ? amount * (1m + (pct / 100m)) : newAmount!.Value;
            value = Math.Max(0m, value);
            return roundToWholeDollars ? Math.Round(value, 0, MidpointRounding.AwayFromZero) : Domain.Common.Money.Round(value);
        }
    }

    private async Task<object> FundFixAsync(AssistantTurn turn, string fundCode, string? versionId, CancellationToken ct)
    {
        if (await resolver.ResolveOpenAsync(turn, versionId, ct) is not { } v)
        {
            return NoOpenVersion;
        }

        if (await entry.GetWorkspaceAsync(v.Id, ct) is not { } workspace
            || workspace.FundBalances.FirstOrDefault(f => f.FundCode == fundCode.Trim()) is not { } fund)
        {
            return new { problem = $"No fund {fundCode} in this budget that this user can see." };
        }

        decimal over = fund.Limit.AmountOverLimit;
        if (over <= 0m)
        {
            return new { problem = $"Fund {fund.FundCode} {fund.FundName} is within its limit; nothing to fix." };
        }

        // Spending lines only: transfers out are usually set by ordinance, and a calculated line changes through its positions.
        List<BudgetLineDto> spending = [.. workspace.Lines.Where(l => l.FundId == fund.FundId && l.AccountType.CountsTowardDepartmentTotal() && l.CanEditAmount && l.Amount > 0m)];
        if (FundTrim.Trim([.. spending.Select(l => new TrimLine(l.Id, l.Amount))], over) is not { } trimmed)
        {
            return new { problem = $"Fund {fund.FundCode} is {Money(over)} over, more than the spending lines this user can change hold ({Money(spending.Sum(l => l.Amount))}). Its revenue estimates or transfers need a look instead." };
        }

        List<LineAmountChange> changes = [.. spending.Zip(trimmed, (l, t) => new LineAmountChange(l.Id, l.Amount, t.Amount)).Where(c => c.Amount != c.Expected)];
        Dictionary<Guid, BudgetLineDto> byId = spending.ToDictionary(l => l.Id);
        List<ProposalRow> rows = [.. changes.Select(c => new ProposalRow(LineLabel(byId[c.LineId]), Money(c.Expected), Money(c.Amount)))];
        int left = workspace.Lines.Count(l => l.FundId == fund.FundId && l.AccountType.CountsTowardDepartmentTotal() && !l.CanEditAmount);
        string note = $"Takes {Money(over)} off {changes.Count} spending line{(changes.Count == 1 ? "" : "s")} in proportion to their size, which brings the fund to its limit exactly."
            + (left > 0 ? $" {left} line{(left == 1 ? " is" : "s are")} left alone (calculated from positions or not open to this account)." : "")
            + " Raising a revenue estimate is the other way to fix it.";
        return Propose(turn, $"Bring fund {fund.FundCode} {fund.FundName} within its limit: cut {Money(over)}", rows, null, $"Change {changes.Count} line{(changes.Count == 1 ? "" : "s")}", note,
            async c =>
            {
                Result saved = await entry.UpdateLineAmountsAsync(v.Id, changes, c);
                return saved.IsSuccess
                    ? Result.Success(new ProposalOutcome($"Fund {fund.FundCode} is within its limit.", $"/admin/budgets/{v.Id}"))
                    : Result.Failure<ProposalOutcome>(saved.Errors);
            },
            nameof(BudgetVersion), v.Id);
    }

    private async Task<object> StartBudgetAsync(AssistantTurn turn, int fiscalYear, decimal adjustmentPercent, bool appropriationsOnly, bool roundToWholeDollars, CancellationToken ct)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return NotAllowed;
        }

        IReadOnlyList<FiscalYearDto> years = await fiscalYears.ListAsync(ct);
        if (years.FirstOrDefault(y => y.Year == fiscalYear) is not { } year)
        {
            return new { problem = $"FY{fiscalYear} is not set up yet. Add it under Fiscal years first.", page = "/admin/fiscal-years" };
        }

        if (!year.CanStartBudget)
        {
            return new { problem = year.IsClosed ? $"FY{fiscalYear} is closed." : $"FY{fiscalYear} already has a budget." };
        }

        if (year.StartFrom is null)
        {
            return new { problem = $"FY{fiscalYear - 1} has no adopted budget to start from yet. Adopt it first, or start FY{fiscalYear} empty from the Fiscal years page.", page = "/admin/fiscal-years" };
        }

        var request = new StartBudgetRequest(year.Id, StartFromPriorYear: true, adjustmentPercent,
            appropriationsOnly ? SeedAdjustmentScope.AppropriationsOnly : SeedAdjustmentScope.AllLines, roundToWholeDollars);
        List<ProposalRow> rows =
        [
            new("Starts from", null, $"FY{fiscalYear - 1} {year.StartFrom} (adopted)"),
            new("Change", null, adjustmentPercent == 0m ? "copied as is" : $"{Percent(adjustmentPercent)} on {(appropriationsOnly ? "spending only" : "every line")}"),
            new("Rounding", null, roundToWholeDollars ? "whole dollars" : "cents"),
        ];
        return Propose(turn, $"Start the FY{fiscalYear} budget from FY{fiscalYear - 1}", rows, null, $"Start FY{fiscalYear}", "Positions carry over at the rates they end the year; the multi-year plan moves on a year.",
            async c =>
            {
                Result<StartBudgetResultDto> started = await workflow.StartBudgetAsync(request, c);
                return started.IsSuccess
                    ? Result.Success(new ProposalOutcome($"FY{fiscalYear} is started with {started.Value.LineCount} lines.", $"/admin/budgets/{started.Value.VersionId}"))
                    : Result.Failure<ProposalOutcome>(started.Errors);
            },
            nameof(Government), currentUser.GovernmentId!.Value);
    }

    private async Task<object> ActualsAsync(AssistantTurn turn, int? fiscalYear, CancellationToken ct)
    {
        int year = fiscalYear ?? await resolver.CurrentFiscalYearAsync(ct);
        Result<ActualsPreviewDto> preview = await actuals.PreviewFromErpAsync(year, ct);
        if (preview.IsFailure)
        {
            return Problem(preview.Errors);
        }

        ActualsPreviewDto p = preview.Value;
        List<ProposalRow> rows = [.. p.Funds.Select(f => new ProposalRow($"{f.FundCode} {f.FundName}", null, $"received {Money(f.Receipts)}, spent {Money(f.Disbursements)}, encumbered {Money(f.Encumbered)}"))];
        string note = $"Through fiscal month {p.ThroughPeriod} ({p.AsOf.ToString("MMMM d, yyyy", Us)}).{(p.PriorActualChanges.Count > 0 ? $" It also updates {p.PriorActualChanges.Count} prior-year actual{(p.PriorActualChanges.Count == 1 ? "" : "s")} in open budgets." : "")}{(p.Notes.Count > 0 ? " " + string.Join(" ", p.Notes) : "")}";
        return Propose(turn, $"Bring in FY{year} actuals from {p.SourceName}", rows, null, "Bring them in", note,
            async c =>
            {
                Result<ActualsSyncDto> synced = await actuals.CommitFromErpAsync(year, c);
                return synced.IsSuccess
                    ? Result.Success(new ProposalOutcome($"FY{year} actuals are in, through {p.AsOf.ToString("MMMM d", Us)}.", "/admin/actuals-sync"))
                    : Result.Failure<ProposalOutcome>(synced.Errors);
            },
            nameof(Government), currentUser.GovernmentId!.Value);
    }

    private async Task<object> MessageAsync(AssistantTurn turn, string body, string? heading, string? signedBy, string? signerTitle, string? versionId, CancellationToken ct)
    {
        if (await resolver.ResolveOpenAsync(turn, versionId, ct) is not { } v)
        {
            return NoOpenVersion;
        }

        if (await books.GetAsync(v.Id, ct) is not { CanEditMessage: true } page)
        {
            return NotAllowed;
        }

        if (body.Trim().Length > BudgetVersion.MessageMaxLength)
        {
            return new { problem = $"Keep the message under {BudgetVersion.MessageMaxLength:N0} characters." };
        }

        List<ProposalRow> rows =
        [
            new("Heading", page.MessageHeading, string.IsNullOrWhiteSpace(heading) ? BudgetBookBuilder.DefaultMessageHeading : heading.Trim()),
            new("Signed", page.MessageSignedBy is null ? null : $"{page.MessageSignedBy}, {page.MessageSignerTitle}", string.IsNullOrWhiteSpace(signedBy) ? "(no signature)" : $"{signedBy.Trim()}{(string.IsNullOrWhiteSpace(signerTitle) ? "" : ", " + signerTitle.Trim())}"),
        ];
        return Propose(turn, $"Save this as the FY{v.Year} {v.Label} budget message", rows, body.Trim(), "Save the message",
            page.MessageBody is null ? null : "It replaces the message already written.",
            async c =>
            {
                Result saved = await books.SaveMessageAsync(new SaveBudgetMessageRequest(v.Id, heading, body, signedBy, signerTitle), c);
                return saved.IsSuccess
                    ? Result.Success(new ProposalOutcome("The budget message is saved.", $"/admin/budgets/{v.Id}/book"))
                    : Result.Failure<ProposalOutcome>(saved.Errors);
            },
            nameof(BudgetVersion), v.Id);
    }

    private async Task<object> NarrativeAsync(AssistantTurn turn, string departmentCode, string narrative, string? versionId, CancellationToken ct)
    {
        if (await resolver.ResolveOpenAsync(turn, versionId, ct) is not { } v)
        {
            return NoOpenVersion;
        }

        if (await entry.GetWorkspaceAsync(v.Id, ct) is not { } workspace
            || workspace.DepartmentRequests.FirstOrDefault(d => d.DepartmentCode == departmentCode.Trim()) is not { } request)
        {
            return new { problem = $"No department {departmentCode} in this budget that this user can see." };
        }

        if (!request.CanEditNarrative)
        {
            return new { problem = $"{request.DepartmentName}'s narrative is not open to this user right now (a submitted request is locked until it is returned)." };
        }

        List<ProposalRow> rows = [new($"{request.DepartmentCode} {request.DepartmentName}", request.Narrative is null ? null : "the narrative written now", "this narrative")];
        return Propose(turn, $"Save this as {request.DepartmentName}'s narrative for FY{v.Year} {v.Label}", rows, narrative.Trim(), "Save the narrative", null,
            async c =>
            {
                Result saved = await departmentRequests.SaveNarrativeAsync(v.Id, request.DepartmentId, narrative, c);
                return saved.IsSuccess
                    ? Result.Success(new ProposalOutcome($"{request.DepartmentName}'s narrative is saved.", $"/admin/budgets/{v.Id}/departments/{request.DepartmentId}"))
                    : Result.Failure<ProposalOutcome>(saved.Errors);
            },
            nameof(BudgetVersion), v.Id);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private object Propose(AssistantTurn turn, string title, List<ProposalRow> rows, string? text, string confirmLabel, string? note,
        Func<CancellationToken, Task<Result<ProposalOutcome>>> commit, string entityType, Guid entityId)
    {
        AssistantProposalDto proposal = proposals.Add(title, rows, text, confirmLabel, note, commit, title, entityType, entityId);
        turn.Proposals.Add(proposal);
        turn.Steps.Add(new AssistantStep($"Proposed: {title}"));
        return new
        {
            proposed = title,
            preview = rows.Take(12).Select(r => r.Before is null ? $"{r.Label}: {r.After}" : $"{r.Label}: {r.Before} to {r.After}"),
            moreRows = Math.Max(0, rows.Count - 12),
            note,
            status = "Nothing has changed yet. The user sees this as a card with a confirm button below your answer. Say what it will do and ask them to confirm there. Never say it is done.",
        };
    }

    // The department and fund by name as well as by number: a model reading "1000-310-5320" guesses
    // which department 310 is, and says so to the user.
    private static string LineLabel(BudgetLineDto l) => $"{l.AccountNumber} {l.AccountName} ({(l.DepartmentName is null ? "" : l.DepartmentName + ", ")}{l.FundName})";

    private static readonly object NoOpenVersion = new { problem = "There is no open (draft or proposed) budget to change. An adopted budget changes only by amendment, which the user starts from Budget versions." };

    private static readonly object NotAllowed = new { problem = "This user's account cannot make that change." };

    private static object Problem(IReadOnlyList<ValidationError> errors) => new { problem = string.Join(" ", errors.Select(e => e.Message)) };

    private static string Money(decimal amount) => (amount < 0 ? "-" : "") + Math.Abs(amount).ToString("C2", Us);

    private static string Percent(decimal percent) => (percent >= 0 ? "+" : "") + percent.ToString("0.##", Us) + "%";
}
