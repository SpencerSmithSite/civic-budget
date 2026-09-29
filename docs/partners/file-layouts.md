# File layouts

The four exchanges as files, for an ERP without the API or a government that prefers files. Three
come from the ERP and are uploaded in CivicBudget; one comes from CivicBudget and is imported into
the ERP. Each has a sample in [`samples/`](samples), and CivicBudget's tests read every sample
through the same code as a real upload (`ErpApiContractTests`).

The files CivicBudget reads:

- are **CSV or Excel (.xlsx)**, with a header row;
- take their **columns in any order**, with names matched regardless of capitals;
- take **account numbers written the way the ERP prints them** (`1000-110-5110`, or `1000.110.5110`
  for a government with dotted numbers), split using the government's own number format;
- are **checked whole before anything is written.** A file with a problem is refused with every
  problem listed by row, and a good file shows a preview the Fiscal Officer confirms.

## Chart of accounts (ERP to CivicBudget)

Uploaded on **Setup, then Chart sync**. One row per code. Sample: [`chart.csv`](samples/chart.csv).

| Column | Required | What it holds |
|---|---|---|
| `Kind` | Yes | `Fund`, `Department` (or `Program`), or `Object` (or `Account`) |
| `Code` | Yes | The code, as it appears in the account number |
| `Name` | Yes | |
| `Type` | Objects | `Revenue`, `Expenditure`, `TransferIn`, or `TransferOut` |
| `Category` | Funds and objects | Funds: `General`, `SpecialRevenue`, `DebtService`, `CapitalProjects`, `Permanent`, `Enterprise`, `InternalService`, `Fiduciary`. Objects: the reporting category, such as `PersonalServices` or `Taxes` (the full list is the `ReportingCategory` enumeration in [`openapi.json`](openapi.json)) |
| `Description` | No | Plain words for the public portal |
| `Active` | No | `Yes` or `No`; blank means active |

Category names are matched loosely (`Special Revenue`, `special_revenue`, and `SpecialRevenue` are
the same), because these files are often typed by people. Send the whole chart; a code missing from
the file is deactivated.

## Actuals (ERP to CivicBudget)

Uploaded on **Setup, then Actuals sync**. One row per amount, one fiscal year per file. Sample:
[`actuals.csv`](samples/actuals.csv).

| Column | Required | What it holds |
|---|---|---|
| `Fiscal Year` | Yes | The year the fiscal year ends in |
| `Type` | Yes | `Actual` (receipts or spending in a month), `Encumbrance` (committed, not yet spent), or `Cash` (a fund's balance) |
| `Account` | Yes | The full account number; for `Cash`, the fund number |
| `Period` | Actual rows | The fiscal month, 1 to 12 |
| `Amount` | Yes | Dollars and cents |

The file runs through the latest month it has activity for; a year with period 12 is closed.

## Employees (ERP to CivicBudget)

Uploaded on **Setup, then Employees from the ERP**. One row per employee. Sample:
[`employees.csv`](samples/employees.csv).

| Column | Required | What it holds |
|---|---|---|
| `Employee ID` | Yes | The ERP's employee number, which must not change from year to year |
| `Name`, `Title` | Yes | |
| `Department` | Yes | The department's code |
| `Pay Type` | Yes, unless on a scale | `Salary` or `Hourly` |
| `Rate` | Yes, unless on a scale | The yearly salary, or the hourly rate |
| `Grade`, `Step` | No | The employee's place on a pay scale in the year's personnel settings; when present, the scale sets the pay type and rate |
| `Annual Hours` | No | Blank means the government's full-time hours |
| `Hire Date` | No | `yyyy-MM-dd` or `M/d/yyyy` |
| `Retirement` | No | The retirement system by the name the personnel settings use (`OPERS`, `OP&F police`); blank for none |
| `Pick Up` | No | `Yes` when the employer also pays the employee's retirement share |
| `Funds` | Yes | How pay is charged: `1000 40%; 2011 60%`, or a single fund alone, `1000` |
| `Benefits` | No | Plans and coverage: `Medical (PPO): Family; Dental: Family; Life ($25,000): Single`. Coverage is `Single`, `Employee + Spouse`, or `Family` |

Several values share one cell (funds, benefits) so that each employee stays on one row, the way
payroll registers print.

## Budget journal (CivicBudget to ERP)

Downloaded from an adopted budget's **Send to the ERP** page, and imported into the ERP. One row per account.
Sample: [`budget-journal.csv`](samples/budget-journal.csv).

| Column | What it holds |
|---|---|
| `Account` | The full account number, in the government's format |
| `Amount` | The change to the account's budget, with a decimal point and no thousands separators; negative for a decrease |
| `Description` | The journal's one description, repeated on every row ("FY2027 Original, resolution 2026-50"), at most 100 characters |
| `Date` | The posting date, `yyyy-MM-dd`, repeated on every row |

The file is CSV with CRLF line endings and a UTF-8 byte order mark, so Excel opens it cleanly. As
with the API, amounts are changes, not totals: an amendment's file lists only the accounts that
moved. Import the whole file or none of it.

A downloaded file is recorded as a send. The Fiscal Officer marks it imported once the ERP has
taken it, or cancels it, and only one send per fiscal year can be open at a time, so the same
change is not imported twice.
