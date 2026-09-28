# Walkthrough 26: Employees from the ERP, and personnel reports

Phase 26 put positions in the budget, but someone still had to type them. The ERP's payroll already
knows who works for the government, in which department, at what pay, in which retirement system
and insurance plans, and charged to which funds. Phase 27 brings that in, usually once a year when
next year's budget starts. It also adds the three reports people ask for once personnel is in the
budget.

## 1. The same shape as the other syncs

| Piece | Chart (walkthrough 11) | Actuals (21) | Employees |
|---|---|---|---|
| Contract | `ErpChart` | `ErpActuals` | `ErpEmployees` |
| File adapter | `ErpChartFileSource` | `ErpActualsFileSource` | `ErpEmployeeFileSource` |
| API adapter | `IErpChartSource` | `IErpActualsApi` | `IErpEmployeesApi` |
| Pure match | `ChartDiff` | `ActualsMatcher` | `EmployeeMatcher` |
| Preview, then apply | `ChartSyncService` | `ActualsSyncService` | `PersonnelSyncService` |

Only the adapters know an ERP's layout. The simulated ERP serves the demo payroll through
`SimulatedErpEmployeesApi`. A real VIP connection would implement the same interface, and
nothing above it changes.

## 2. What the export is assumed to hold

VIP's real employee export is not known yet, so the file layout is a reasonable guess at what a
payroll register carries, written down on the page and in the reader. It has one row per employee:

| Column | Example |
|---|---|
| Employee ID | E1033 |
| Name, Title, Department | Casey Lin, Payroll clerk, 725 |
| Pay Type, Rate, Annual Hours | Hourly, 23.60, 1560 |
| Grade, Step (instead of a rate, for a pay scale) | PO, 3 |
| Hire Date | 2019-06-10 |
| Retirement, Pick Up | OPERS, No |
| Funds (the labor distribution) | 1000 50%; 2011 50% |
| Benefits | Medical (PPO): Family; Dental: Family |

Retirement systems and plans are carried **by name**, matched to the year's personnel settings.
Departments and funds are matched **by code**, against the chart. An ERP that writes "OPF-P" has an
adapter that says "OP&F police". Once the real layout is known, a VIP adapter translates it into
`ErpEmployees`.

## 3. What the ERP owns, and what the budget owns

A position has two kinds of facts. The sync refreshes the ERP's facts and keeps the budget's.

| The ERP owns (refreshed) | The budget owns (kept) |
|---|---|
| Name, title, hire date | The planned raise or step increase |
| Pay: rate and hours, or grade and step | The months paid |
| Retirement system and pick-up | Longevity schedule |
| Insurance plans and tiers | Other pay: overtime, holiday, allowances |
| The fund split | The base pay account |

A position keeps the ERP's employee number (`Position.EmployeeId`), which is how next year's sync
finds it again. A name is not enough: two employees can share one, and names change.

## 4. The matching rules

`EmployeeMatcher.Plan` turns the payroll and the budget into a list of steps:

- **Unchanged or Updated**: the employee is budgeted in the same department. The preview lists
  what changed, for example "Pay: $23.10 an hour × 1,560 hours → $23.60 an hour × 1,560 hours".
- **Fill**: a new employee takes a vacant position with the same title in their department. The
  vacancy was budgeted from April. The employee is already on the payroll, so they are paid from
  January (or from their hire date, if that falls within the year).
- **Add**: a new employee with no vacancy to fill. The new position copies the budget's choices
  (raise, longevity, overtime) from a colleague with the same title, because a new patrol officer
  is budgeted like the other patrol officers.
- **Vacate**: someone the payroll no longer lists. The position stays, vacant: the department
  usually means to fill it, and removing it is one click. A transfer vacates the old position and
  adds the employee in the new department.
- Positions entered here **without** an employee number, such as planned vacancies and new
  positions the council has not approved yet, are never touched.

Anything that cannot be matched is an error naming the employee. That covers an unknown
department or fund code, a retirement system or plan the settings do not have, and a grade and
step on no pay scale. **One error refuses the whole sync**, the same rule as the actuals sync. A
roster applied in part would misstate every personnel line and look finished.

## 5. The preview is the real thing

`PersonnelSyncService` runs one path for preview and commit. It:

1. loads the budget;
2. matches;
3. applies the plan through `BudgetVersion.AddPosition` and `UpdatePosition`;
4. compares the lines before and after.

A preview then throws the database context away; a commit saves it with a `PersonnelSync` log row
and an audit event. Because the preview runs the same domain code, "1000-110-5110 goes from
578,966.80 to 592,018.80" is exactly what Apply writes.

The demo's simulated payroll has moved on since the FY2027 budget started, so the first fetch has
something to show:

- Jamie Ortiz was hired into the police vacancy;
- Casey Lin had a merit increase;
- Rowan Ruiz retired from the street crew.

Pine Hollow Township has no personnel in its budget. It sets up FY2027 from the Ohio defaults,
then brings its six employees in instead of typing them.

## 6. The reports

| Report | For | Shows |
|---|---|---|
| Position Roster | everyone (department users: their departments) | each position by department, filled or vacant: hire date, years of service, pay rate, funds, pay, benefits, total |
| Personnel Cost by Fund | Fiscal Officer, Administrator, Viewer | what each fund pays for people, by department: pay, retirement, Medicare, workers' comp, insurance |
| Benefits Summary | Fiscal Officer, Administrator, Viewer | retirement members, pensionable pay, employer share and pick-up; insurance enrollment by plan and tier with employer and employee shares; Medicare and workers' comp on taxable pay |

Every figure is a sum of the calculator's pieces. `PositionCost` now also splits each fund's part
by kind (`ByFundAndKind`) and records the pensionable and taxable pay it charged. So the roster's
total equals the personnel lines to the cent, and the cost report's Street fund equals the Street
fund's personnel lines. An integration test asserts both.

## 7. Tests

- **Unit**:
  - `ErpEmployeeFileSourceTests`: every column, several values to a cell, every bad row at once,
    and the ways payrolls write coverage tiers.
  - `EmployeeMatcherTests`: unchanged, updated with the budget's raise kept, a vacancy filled from
    January, a mid-year hire, a new position copying a colleague, a leaver, a transfer, positions
    entered here left alone, errors naming the employee, and a fund split described by code.
  - `PersonnelReportBuilderTests`: the roster's order, cost split by fund, and the benefits
    arithmetic.
- **Integration** (`PersonnelSyncServiceTests`):
  - the preview writes nothing;
  - apply is logged and audited, and a second run has nothing to do;
  - one unmatched plan refuses the file;
  - only the fiscal authority syncs, and only into an open budget;
  - a new government sets up its year and imports a file;
  - the reports add up to the lines;
  - a department user gets only their roster.
- **bUnit** (`PersonnelSyncAndReportsTests`):
  - the preview's steps and line changes;
  - Apply held back by an error;
  - a year without settings;
  - the roster's vacancies;
  - the benefits split.
