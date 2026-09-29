# Walkthrough 32: The multi-year plan

An Ohio council appropriates one year at a time, but the questions at a budget hearing are about
the years after it. Will the Street fund still have a balance in 2030? Can the village afford the
Maple Street reconstruction in 2029? Most finance officers answer those in a spreadsheet kept
beside the budget. This phase puts the plan inside the budget: up to ten years, five by default,
on both the revenue and the expenditure side.

The decisions are in ADR-0044.

## 1. What a plan is

A plan belongs to one budget version, the same way its lines do.

- **Length.** `BudgetVersion.PlanYears` counts the budget year. The default of 5 on the FY2027
  budget covers FY2027 to FY2031, and 10 is the most.
- **Percentages.** Each future year has one `PlanAssumption` with two percentages. Revenues and
  transfers in follow the revenue percentage. Expenditures and transfers out follow the expenditure
  percentage.
- **Typed years.** A `PlannedAmount` on a line replaces the calculation for that year. The years
  after it grow from the typed amount, not from what the calculation would have given.
- **Rounding.** `PlanInWholeDollars` rounds the calculated years to the dollar, like the option on
  starting a budget.

Nothing projected is stored. `BudgetVersion.SetPlan` and `SetPlannedAmount` guard the inputs (the
length, a percentage inside the same range as starting a budget, a year inside the plan, an amount
of zero or more), and every reader works the numbers out again.

## 2. The arithmetic

`MultiYearPlanCalculator.Project` is pure, and `MultiYearPlanTests` pins it:

1. Year 0 is each line's budget amount.
2. Each later year is the typed amount if there is one, otherwise the year before times
   (1 + that year's percentage).
3. Each fund's year is summed into a `FundBalanceSummary` by `FundBalanceCalculator`, the same
   code as the budget year. The beginning balance of year 1 is the ending balance of year 0.

Step 3 is why the plan shows the Ohio limit check in every year. A fund whose appropriations exceed
its estimated resources in FY2029 is flagged in FY2029, with the amount over, in words beside the
number.

## 3. The page

`/admin/budgets/{id}/plan` (Tools, then Multi-year plan) has three parts:

- **How each year follows from the one before.** The plan length, rounding, and each year's two
  percentages, with a "same change every year" fill. Saving works out every calculated year again
  and keeps the typed ones. A department head or viewer sees the percentages as a table.
- **Projected ending balance by fund.** One row per fund, one column per year, with overspent
  years in red and said in words underneath.
- **Every line, year by year.** Grouped by fund and program like the worksheet. Future years use
  the worksheet's `AmountCell`; a typed year says "typed · reset", and reset goes back to the
  calculation.

Department heads reach it from their request page, filtered to their department. They see only
their own lines and no fund totals, because a fund spans departments, and they can type a future
year only while they could still change the budget year (`BudgetLinePermissions.CanEdit` with the
submitted flag).

The Export XLSX button writes every line by year, then each fund's ending balance, with a Notes
column naming the typed years and the overspent ones. The Reports page has a card for it too.

## 4. Carrying the plan

- **An amendment** copies the plan as it stands: length, rounding, percentages, and typed years.
- **Next year's budget** moves the plan on a year. FY2027's year 2 becomes FY2028's year 1, typed
  amounts shift down with it, and the new last year repeats the old last year's percentages. A
  typed amount for FY2028 in the FY2027 plan is dropped, because FY2028 is now the budget year and
  is started the usual way.
- **Adoption locks it**, like the rest of the budget.

Both paths load versions through `PersonnelData.VersionsWithPositions`, which now includes the
plan, so nothing is lost on the copy.

## 5. On the portal

Publishing stores one `PublishedBudgetSnapshotPlanYear` per fund per year, with that year's
percentages. The portal's new Outlook page reads those rows through `ISnapshotQueryService` and
shows all funds year by year, with the assumed changes, and each fund's ending balance. It says
plainly which year is adopted and that the rest are not appropriations. A budget published with a
one-year plan (the seeded FY2025) says so instead.

## 6. The seed

- **FY2026** plans 2.5% revenue growth and 3% expenditure growth.
- **FY2027** plans 3.5% expenditure growth in FY2028, then 3%, with revenue slowing from 2.5% to
  2%. The Maple Street reconstruction ($250,000 from Capital Projects) is typed into FY2029.
  The Street fund starts over its limit and stays there, and Capital Projects goes negative from the
  year of the project, which is the kind of thing the plan is for.
- **FY2025** is a one-year plan: budgeted before the plan existed.

## 7. Tests

- `MultiYearPlanTests` (domain): growth by side, rounding, typed years, balance roll-forward, the
  guards, the amendment and next-year carry.
- `BudgetPlanServiceTests` (integration): the seeded plan, saving percentages, who may change what,
  reset and refusals, the amendment carry, the portal outlook, the snapshot rows.
- `WorkflowAndPublishingTests` checks that starting FY2028 carries the typed project into its year 1.
- `BudgetPlanPageTests` (bUnit): the over-limit words, typing and resetting a year, the fill and
  save, and the read-only view.
