# Walkthrough 25: Personnel budgeting

Salaries and the benefits that follow them are most of a village's General Fund, and no fiscal
officer types those lines. They come from a spreadsheet of positions: who holds each one, what it
pays, the raise, longevity, overtime, the retirement system, Medicare, workers' compensation, and
health insurance, split across the funds that pay for it. Phase 26 does that spreadsheet's job
inside the budget, so the salary and benefit lines are calculated from positions instead of typed.

## 1. What a position is

A position belongs to a department in one budget version, filled or vacant. It says:

| Part | What it holds |
|---|---|
| Who | Title, employee (or vacant), hire date |
| Pay | A yearly salary, or an hourly rate times hours; typed with a raise from a month, or a pay scale's grade and step with a step increase in a month |
| When | The months it is paid (a vacancy filled in April is paid April to December) |
| Longevity | Which schedule applies, if any |
| Other pay | Overtime and holiday hours, a uniform allowance, a stipend, certification pay, a 457 match |
| Benefits | Retirement system, and whether the employer picks up the employee's share; each insurance plan and tier |
| Paid from | One fund, or shares of several adding to 100% |

The same record (`PositionDetails`) is what the editor fills in, what `Position` stores, and what
the calculator prices.

## 2. Settings belong to a year

The rates and plans live in **personnel settings**, one set per fiscal year (`PersonnelSettings`).
A year matters because the numbers change by year: in August the fiscal officer enters next year's
health premiums while this year's amendment is still open, and that amendment must keep this year's
premiums. An adopted budget keeps the settings it was priced with.

A new year starts from a copy of the last one, or from the Ohio defaults:

| | Employer | Employee |
|---|---|---|
| OPERS | 14% | 10% |
| OPERS law enforcement | 18.1% | 13% |
| OP&F police | 19.5% | 12.25% |
| OP&F fire | 24% | 12.25% |
| Social Security (not in a state system) | 6.2% | 6.2% |

Medicare is 1.45%. Workers' compensation starts at zero, because every public employer has its own
BWC rate. Insurance plans are the government's own: a monthly premium for single, employee and
spouse, and family coverage, less the share employees pay.

## 3. Pricing a position

`PositionCostCalculator` is pure, like the report builders: the same details and settings always give
the same cost.

1. **Base pay, month by month.** Each month paid earns a twelfth of the year's pay at that month's
   rate. A 3% raise in July counts for six months; a step increase in April for nine.
2. **Longevity** from completed years of service (section 4).
3. **Other pay**: an amount, hours at a multiple of the average hourly rate (overtime at 1.5), or a
   percentage of base pay.
4. **Retirement** on *pensionable* pay (what OPERS and OP&F call earnable salary), plus the employee's
   share when the employer picks it up. **Medicare and workers' compensation** on *taxable* pay. Each
   kind of other pay says which it is: a cash uniform allowance is taxable but not pensionable; a
   457 match is neither.
5. **Insurance**: the tier's premium less the employee's share, for each month paid.

Each piece lands on its own account (base pay on salaries, overtime on overtime, OP&F on retirement)
and is rounded to cents. It is then divided among the position's funds. The fund with the largest
share takes the leftover cent, so a 50/50 split comes out the same however the funds happen to be
listed. That last rule came from a bug. Funds come back from the database in no fixed order, so
"the last fund takes the cent" moved a cent between the General and Street funds each time the
year was repriced. A test now saves unchanged settings and asserts that no line moves.

## 4. Longevity, in English

Ohio contracts pay longevity three ways, and the settings support all three:

| Method | Example |
|---|---|
| A step table | $600 after 5 years, $900 after 10, $1,200 after 15 |
| A percentage of pay | 1.5% of base pay after 5 years, 3% after 10 |
| An amount per year of service | $75 for each year once past 5, counting up to 25 |

Each schedule also says whether years are counted on the first or last day of the budget year. The
settings page builds a schedule as sentences ("Pay *a flat amount a year* once an employee
reaches:" and a table of steps) and reads it back beside the form (`Longevity.Describe`):

> Fewer than 5 years of service: no longevity. 5 to 9 years of service: $600.00 a year. ...

An example ("12 years, base pay $50,000, earns $900.00 a year") uses the calculator's own code, so a
contract clause can be checked against the budget before saving.

## 5. Typed or calculated

This is the rule that keeps the rest of the product unchanged. **A line is typed or calculated,
never both.** `BudgetLine.PositionCount` is null for a typed line, and for a calculated one it
holds how many positions it comes from. Every position change prices the department again
(`BudgetVersion.ApplyPersonnel`):

- a position that costs into a fund and account the department has no line for creates the line;
- a typed line that positions now cost into becomes calculated;
- a calculated line that no position reaches any more returns to a typed zero, so the comparison
  columns stay on the page and the line can be removed.

The domain refuses typing over or removing a calculated line, and the import refuses to change its
amount (it may still fill in its comparisons). On the department page the amount shows "from 9
positions" and links to them. Everything downstream reads lines as before: the fund balances, the
appropriation limit, the reports, the certificate, the journal sent to the ERP, and the portal.
None of them had to learn about positions.

Positions are children of `BudgetVersion`, like lines, so the aggregate's guarantees cover them.
An adopted budget's positions cannot change. An amendment copies them. The `Revision` token
catches two people saving the same budget. A department user may change positions exactly when
they may change their department's lines.

## 6. Settings changes reach the budget

Saving a year's settings reprices every position in that year's **open** budgets and reports how
many lines moved ("8 budget lines recalculated in FY2027 Original"). Adopted budgets keep their
lines. Their personnel page says so when the positions no longer price to the adopted amounts.

A change that would leave any position of the year unpriceable is refused, and the positions are
named. Examples are removing the medical plan the chief is on, or blanking a tier someone has. The
same check covers every kind of setting, because it simply asks each position whether it still
prices (`PositionDetails.Problems`).

## 7. Next year

Starting a year's budget from last year's adopted one now carries the positions too
(`Position.CarryForward`). Each starts the new year at the rate it ended the old one: the raised
rate, or one step up after a step increase. No raise is planned yet, and each position is paid all
twelve months. Its plans point at the new year's copies, created from last year's settings if the
year has none. Longevity moves on by itself, because years of service are counted from the hire date.
The personnel lines are then priced with the new year's settings rather than taking the percentage
the rest of the budget was started with.

## 8. The demo

Maple Ridge's FY2027 draft has 16 positions:

- **Police**: a chief whose employee share the village picks up, sergeants and officers on the FOP
  pay scale with anniversary step increases, a vacancy filled in April, and a part-time clerk.
- **Finance**: the fiscal officer and a part-time payroll clerk.
- **Streets & Service**: the director and crew, split between the General Fund and the Street
  fund, and a summer seasonal laborer.

Each longevity method appears once. The Street fund is still over its appropriation limit, so the
Block mode demonstration still works.

## 9. Tests

- **Domain** (`PositionCostCalculatorTests`, `LongevityTests`, `PersonnelSettingsTests`,
  `BudgetVersionPersonnelTests`):
  - every pay and benefit rule;
  - all three longevity methods and their sentences;
  - fund splits that lose no cent and do not depend on order;
  - lines created, converted, and released;
  - calculated lines refusing edits;
  - amendments and carry-forward.
- **Integration** (`PersonnelServiceTests`):
  - the seeded police lines equal their positions;
  - adding and removing positions, audited;
  - field errors;
  - department users limited to their own departments until they submit;
  - viewers read-only;
  - a settings change repricing the open budget and nothing else;
  - a plan in use refused;
  - a new year's settings copied;
  - amendments carrying positions.
- The import refuses to change a calculated amount, and starting a year carries 16 positions.
- **bUnit** (`PersonnelPagesTests`):
  - the positions page;
  - the editor repricing as a rate is typed, and marking refused fields;
  - read-only users;
  - a year without settings;
  - the "from N positions" cell;
  - the longevity sentences.
