# Walkthrough 21: Actuals from VIP

Phase 22 is the first phase of v1.2, whose goal is a product an ERP vendor could take on.
The reports worth selling beside an ERP are the ones the ERP cannot produce alone, and every
one of them puts real spending next to a budget. So before any new report, CivicBudget needed
the ERP's books. This phase brings them in, shows them where budgets are made, and keeps the
prior-year column honest.

## 1. The problem

The government's ERP (VIP, from Software Solutions, in my case) is the book of record: every
receipt, purchase order, and check is posted there. CivicBudget budgets beside it. Until now the
only actuals it had were a "prior-year actual" per line that someone typed or imported, and
nothing at all about the year in progress.

Two constraints shaped the design:

- **VIP's interface is not mine to see.** It can deliver data by API or by export file,
  whichever the customer prefers, but I have neither its API nor its export layout. The code has
  to work now and change in exactly one place when the real layout is known.
- **The demo has to show it working.** A reviewer should be able to click "Fetch from VIP" on
  the live site and watch numbers arrive, without pretending the demo is connected to real VIP.

## 2. The same shape as the chart sync

The chart of accounts already came from the ERP (walkthrough 11), through a contract, adapters,
a pure diff, and a preview-then-apply service. Actuals follow the same shape, in
`Application/Erp`:

| Piece | Chart (Phase 9b) | Actuals (this phase) |
|---|---|---|
| Contract | `ErpChart` | `ErpActuals`: activity by account and fiscal month, open encumbrances, fund cash, and how many months are closed |
| Adapters | `ErpChartFileSource` | `ErpActualsFileSource` (export file) and `IErpActualsApi` (the ERP directly) |
| Pure step | `ChartDiff` | `ActualsMatcher` (codes to ids) and `ActualsByLine` (amounts per budget line) |
| Service | `ChartSyncService` | `ActualsSyncService` |
| Page | Chart sync | Actuals sync |

I kept them as separate contracts rather than one big "ERP connector" interface. An ERP that
can export a chart but not actuals, or the reverse, implements only what it has, and nothing is
stubbed. ADR-0025 is amended to say so.

The export file is deliberately plain, one row per amount:

```
Fiscal Year,Type,Account,Period,Amount
2025,Actual,1000-110-5110,1,"43,210.55"
2025,Actual,1000-4110,3,"184,500.00"
2025,Encumbrance,1000-110-5310,,"4,000"
2025,Cash,1000,,"655,012.10"
```

The account is the full number the way an ERP report prints it, split into codes with the
government's own number format (walkthrough 10), so "1000.110.5110" works for Pine Hollow's
dotted format too. Amounts are read by `MoneyText`, which the budget import now shares, so both
accept "$1,234.50" and "(125.00)" and both refuse "1,23".

## 3. Rules a sync follows

**It replaces the whole year.** The ERP's books are the truth. If a posting was reversed in
VIP, the old amount must disappear here too, and merging would keep it forever. A sync removes
that fiscal year's rows and writes the new ones in one save.

**One unknown code refuses everything.** A year of actuals with one account missing would
understate every total built on it, and nothing on a report would tell you. `ActualsMatcher`
lists every code the chart does not know, once each, and nothing changes until the file is
fixed or the chart is synced. Codes match ignoring padding ("0110" is "110"), because ERP
reports pad codes to the segment width.

**Actuals live in their own tables.** `ErpActuals`, `ErpEncumbrances`, `ErpFundCash`, and the
`ActualsSync` log. Actuals belong to a fiscal year and an account, not to a budget version: the
original budget and both amendments of a year compare against the same figures. The rows are
not audited one by one, because they are a copy of the ERP's audited books. The sync log and one
audit event record who brought them in, from where, and how far the year ran.

## 4. The prior-year column

A budget for FY2027 compares against FY2025's actuals, the last year whose books are closed while
FY2027 is being prepared. When the ERP sends FY2025 with all twelve months, the sync writes each
line's total into every open FY2027 version. It does that through
`BudgetVersion.UpdateLineComparatives`, so each change is audited in the line's history,
concurrency-checked like any edit, and listed in the preview before anyone clicks Apply.
`PriorYearActuals` holds the rule, and starting a budget or adding a line uses it too.

Two cases are refused on purpose:

- **A year still in progress fills nothing.** Eight months of spending sitting in a column
  labelled "FY2026 actual" would mislead every comparison on the page.
- **A negative total is left alone and reported.** It happens when refunds outrun a year's
  receipts. Budget amounts are never negative (walkthrough 20), and forcing it to zero would
  hide an oddity someone should look at.

There was one subtle case. A government may budget a revenue by fund alone ("1000-4320,
Charges for services") while the ERP records it by department ("1000-310-4320", pool fees). An
exact match would leave the fund-level line at zero. `ActualsByLine` gives a fund-level line
every amount on its fund and account that no department-level line in the same budget claims,
so the money is counted once and never dropped.

## 5. Where people see it

The department page is where a department head decides next year's request, so that is where
this year's spending belongs. Under each line's FY2026 budget there is now a thin bar and
"41,928.92 spent" (or "received", for revenue). The bar turns amber when a line has spent more
than its budget. The budget card at the top adds the department's total spent so far.

My first version added a column, and it pushed the grid past the screen. While measuring why, I
found an older problem: a long fund name in a total row was set not to wrap, which forced the
Street fund's table wider than the page before this phase ever touched it. Putting the
spending inside the budget cell fixed the width, and it reads better too: the bar only means
something next to that budget. The line drawer (the phone view of a line) shows the same figure
and adds open encumbrances.

## 6. The simulated VIP

`SimulatedVipActualsApi` stands in for VIP's API wherever the demo data is seeded. It is honest
about being a stand-in: its name, "VIP (simulated)", appears on the page, in the sync log, and
in the audit trail. What makes it useful rather than a random-number generator:

- **Its books are the seed's own fictional history**, so FY2025's totals equal the prior-year
  actuals the demo budget already shows, and a fetch finds nothing to change.
- **Money moves the way it does in a village.** Real estate taxes arrive in two settlements
  (spring and late summer). Payroll is biweekly, so two months a year have three paydays. Capital
  work happens in summer, utilities peak in winter, and debt service is paid twice a year.
- **A month closes ten days after it ends**, once the bank reconciliation would be done, so on
  September 9 the year runs through July and on September 10 through August.
- **The cash agrees with the budget.** At year end, a fund's cash less its carried encumbrances
  is exactly the next budget's beginning balance, which is how Ohio defines the unencumbered
  balance. The certificate of resources in Phase 24 depends on that being true.

It is registered in `Program.cs` under the same condition as the demo seed. A real deployment
registers nothing, and the page offers the file upload alone, until a real connection implements
`IErpActualsApi`.

## 7. Tests

- **Unit** (`Application.Tests/Erp/ErpActualsTests`): the file (every column, padded and dotted
  numbers, negative months, errors by row, mixed years), the matcher (padding, repeated rows,
  unknown codes listed once, receipts against disbursements), per-line totals (the fund-level
  rule), and the simulated VIP (month closing, a closed year that adds up, real estate taxes only
  in settlement months, cash less encumbrances equal to the next beginning balance).
- **Integration** (`ActualsSyncServiceTests`): the seed's two years; a fetch that restores a
  drifted prior-year actual, with its audit entry and log row; a file that replaces a year and
  appears on the workspace; an unknown code that changes nothing; a line added back that gets its
  prior-year actual from VIP; Department Users and Viewers refused; Pine Hollow's sync leaving
  Maple Ridge's figures alone.
- **bUnit**: the page offers the API only when one is connected and writes nothing before Apply;
  the department grid shows spending under each budget, turns amber when over, and shows nothing
  until the ERP has sent the year.

## 8. What I would change for a large county

A sync removes and re-adds the year's rows through the change tracker. For a village's or a
township's few thousand rows a year that takes well under a second. A county with hundreds of
thousands of rows would want a set-based delete and a bulk insert inside one transaction. The
service would be the only thing to change.
