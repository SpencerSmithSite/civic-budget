# Walkthrough 20: Budget rules and known gaps

Phase 18 settled the four budgeting questions left open in walkthrough 19 and
closed the five known gaps. Three of the questions I could answer from how Ohio
governments budget; the fourth, what a department's total is, I researched before
deciding, because three screens were already giving three different answers.

## 1. Four budgeting decisions

**Only the latest adopted version is publishable.** After an amendment is
adopted, the earlier version is history. `PublishedBudgetSnapshot.Capture`
refuses a version with `SupersededByVersionId` set, `WorkflowStateDto.CanPublish`
carries the rule to the screen, and the workflow bar says why a replaced
version has no Publish button.

**Amounts are never negative.** Revenues and expenditures are both entered as
positive numbers; the account type says which way they count. The rule moved
into `BudgetLine` itself (construction, amount edits, comparative edits), and
the import refuses a negative in any money column. It used to accept "(10)" as
a prior-year actual.

**A department's total is its expenditure appropriations.** The sources:

- ORC 5705.38(C): appropriation measures are classified by office, department,
  and division, with personal services shown separately within each.
- The Auditor of State's UAN village chart budgets transfers out under their
  own program, "Other Financing Uses" (910 Transfers, 920 Advances, 930
  Contingencies), not inside an operating department.
- Michigan's uniform chart of accounts does the same: activity 965 is
  "Transfers Out and Other Financing Uses".
- Revenue a department collects (court fines, permit fees) is an estimated
  resource of the fund, on the certificate of estimated resources, and never
  part of any appropriation.

`AccountType.CountsTowardDepartmentTotal()` is the one rule. Before it, three
screens disagreed. The workspace's by-department view added revenue in, the
department page added transfers out, and the request total and report used
expenditures only. Now all four agree, and a department's revenue and transfer
lines are shown beside it, labelled as outside its total.

**Starting a year's budget.** Fiscal years page, row menu, "Start the
budget", offered for an open year with no budget yet. Start empty, or start
from last year's latest adopted version:

| Field | New year's value |
|---|---|
| Current year budget (comparative) | last year's adopted amount |
| Amount (the starting request) | that amount changed by the percentage |
| Prior-year actual | 0, until actuals are imported (an adopted budget is not what was spent) |
| Fund beginning balance | last year's projected ending balance |

The percentage applies to every line, appropriations only, or revenue
estimates only, and the result can be rounded to whole dollars. $100,000
adopted at +3% starts at $103,000. Lines whose fund, department, or account
has been retired are left out and listed in a warning. `BudgetVersion.CreateOriginalFrom`
holds the rules; `BudgetWorkflowService.StartBudgetAsync` finds the source and
checks the year.

## 2. Optimistic concurrency

Each budget service loads the version, changes it, and saves. Two of those in
flight at once used to both succeed: an amount edit could land on a budget
adopted a moment earlier, and two adoptions could both win.

`BudgetVersion.Revision` counts every change to the aggregate, and every
mutating method calls `Touch()`. EF maps it as a concurrency token, so the
UPDATE carries `WHERE Revision = <the value loaded>`. Because even a line edit
bumps the version's revision, the version's row is always updated, and always
checked. A SQL `rowversion` would not do: it changes only when the version's
own row changes. Services save through `TrySaveAsync`, which turns the
exception into "Someone else changed this budget at the same moment". The
first save stands. `ConcurrencyTests` holds two contexts open at once to
reproduce all three races.

## 3. The other gaps

- **Focus trap.** `civicBudget.openModal(element)` pushes a dialog on a stack;
  Tab and Shift+Tab cycle inside the topmost one; `closeModal()` pops it and
  returns focus to the opener. A stack, because a confirm dialog can open over
  the line drawer.
- **AWS first deploy.** The workflow pushes the image before deploying the app
  stack, which created the ECR repository, so a first deploy failed at the
  push. The repository now lives in the one-time `GitHubOidcStack`.
- **Test speed.** The fixture builds one migrated, seeded template per run,
  backs it up inside the container, and restores it under a unique name per
  test (`CreateSeededDatabaseAsync`). 99 integration tests went from 1m40s to
  40s. The unique name matters: restoring over a database the previous test's
  pooled connections still held failed with "exclusive access could not be
  obtained".
- **Supply chain.** Every GitHub Action is pinned to a commit SHA with its tag
  in a comment; CI's Bicep is a fixed release checked against its sha256.

## 4. Things to read

1. `Domain/Accounts/AccountType.cs` (`CountsTowardDepartmentTotal` and its sources)
2. `Domain/Budgets/BudgetVersion.cs` (`CreateOriginalFrom`, `Revision`, `Touch`) and `BudgetSeedOptions.cs`
3. `Application/Common/SaveConflicts.cs` and `tests/CivicBudget.IntegrationTests/ConcurrencyTests.cs`
4. `Web/Components/Admin/FiscalYears/FiscalYearList.razor` (the Start the budget dialog)
5. `tests/CivicBudget.IntegrationTests/SqlServerFixture.cs` (the template restore)
