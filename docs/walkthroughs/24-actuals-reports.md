# Walkthrough 24: Reports on the ERP's books

Phase 25 builds the reports an ERP cannot produce alone. The ERP knows what was spent and received;
CivicBudget knows what was budgeted, by whom, and why. Together they answer the questions a fiscal
officer and council actually ask in August: are we on track, what is left, and where will each fund
end the year? The phase also adds the appropriation measure, the second legal document after the
certificate.

## 1. The reports

| Report | For each | Shows |
|---|---|---|
| Budget vs. Actual | appropriation line | budget, spent, encumbered, remaining, share used against the year's pace, last year at the same point |
| Revenue vs. Receipts | revenue line | estimate, received, still to collect, share collected against last year's share by now |
| Projected Fund Balances | fund | budgeted and projected receipts and spending, budgeted and projected ending balances |
| Multi-year Trends | fund and year | budgeted and actual receipts, appropriations, actual spending |
| Appropriation Measure | fund and department | personal services, other, total; transfers out beside |

The reports index now groups them: the budget, legal documents, and reports against the ERP's books.
The last group says to pick the year under way, because next year's budget has no actuals yet, and
each report says so plainly when there are none.

## 2. The problem with "two-thirds of the way through the year"

The obvious test of "on track" in August is the calendar: eight of twelve months, so about 67% should
be spent or collected. It is right for payroll and wrong for most other municipal money:

- Real estate taxes arrive in two settlements, spring and late summer. By August a village has nearly
  all of them.
- Capital work happens in summer; debt service is paid twice a year.
- Some receipts arrive once a year.

A report that called real estate taxes "ahead" every August would be ignored by October. So the
reports pace each account by **where it stood last year at the same month**, from the ERP's monthly
history (walkthrough 21):

- **Revenue vs. Receipts** compares the share collected with the share of last year's total that had
  arrived by the same month, and calls an account "behind" only when it trails that by more than ten
  points. In the demo, real estate tax at 96% collected reads as normal: last year it was 95% by now.
- **Budget vs. Actual** keeps the calendar tick on each bar, because it is the right yardstick for
  evenly spent lines like payroll, and adds a column with last year's figure at the same point for
  the ones that are not.

## 3. The projection

`ActualsReportBuilder.Project` decides where a line ends the year:

| Case | Projection |
|---|---|
| The year is closed | its actual |
| Last year has a full year and something by this month | year to date × last year's full year ÷ last year at this month |
| No such history | its budget, never less than what has already happened (and the report counts these lines) |

Spending is never projected below what is already spent plus what is committed. The fund projection
adds the lines up against the fund's beginning balance, so it shows the budgeted ending balance and
the projected one side by side, with the difference.

The report prints its method in the footer, and how many lines had no history. A projection nobody
can explain is not one a fiscal officer will use, and last year's pattern is an assumption worth
stating: a one-time receipt last year skews this year's projection for that account.

## 4. Trends

Each fund, year by year, up to this budget: what was budgeted (each past year's latest adopted
version, the same budget the portal publishes) and what the ERP says happened (to date for a year
still under way). One small table per fund reads better than one table forty columns wide, on a phone
and on paper.

## 5. The appropriation measure

ORC 5705.38(C) says an appropriation measure is classified by fund, then by office, department, and
division, with the amount for personal services set out separately within each. Transfers out are
appropriated too, at fund level, as other financing uses: the same rule as department totals
(walkthrough 20).

The decision was what counts as "personal services". Most measures put fringe benefits (retirement,
Medicare, health insurance, workers' compensation) with pay, but not every government does. So it is a
report setting, reusing the certificate's columns (walkthrough 23): the default is one "Personal
services" column of the accounts categorized as personal services or fringe benefits, and a government
that appropriates benefits separately makes two columns. The column rules (at most four, each account
once, the right kind of account) moved into one shared place, `ReportColumnRules`, so the two reports
cannot drift apart.

## 6. Who sees what

Budget vs. Actual and Revenue vs. Receipts are lists of lines, and use the same read as the budget
screens: a department user sees their own departments' lines, which is the complete answer for them.
The projection, the trends, and the measure are about whole funds; built from part of a fund they
would be wrong rather than partial, so department users do not get them, the same rule as the
certificate.

## 7. One source of truth

Every figure comes from the same ERP tables and the same per-line totals (`ActualsByLine`) as the
"spent so far" under each budget on the department pages. A number on a report cannot differ from the
number on the screen next to it.

## 8. Tests

- **Unit** (`ActualsReportBuilderTests`): the seasonal projection and its fallbacks; grouping,
  remaining, and "over" in budget against actual; "behind" only against last year's pace; committed
  money in the fund projection and the count of lines without history; the measure's columns, other,
  and transfers out.
- **Integration** (`ActualsReportServiceTests`): FY2026 against the seeded ERP figures through August
  with every line paced by FY2025; FY2027 with no actuals; the trends across FY2025 to FY2027; saved
  measure columns splitting the same money differently; a department user limited to their lines and
  kept out of the whole-fund reports.
- **bUnit**: the pace tick and an over-budget line, the empty state before a year begins, and the
  measure's transfers out and default-columns note.
