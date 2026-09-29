# CivicBudget: Functional Specification

I wrote this before writing any code (2026-09-16) and have kept it current since. It
describes what the app does and the rules of Ohio municipal budgeting it follows. How
the code is organized is in [ARCHITECTURE.md](ARCHITECTURE.md); why I made each
non-obvious choice is in [DECISIONS.md](DECISIONS.md).

CivicBudget is a multi-tenant web application that lets a local government
(city, village, township, or county) build its annual budget and publish it to
citizens on a public transparency website. It is designed to sit beside the
government's ERP: the chart of accounts can come from the ERP, and budgeting
happens here.

All data in this project is **fictional**. The *Village of Maple Ridge, Ohio*
and *Pine Hollow Township, Ohio* do not exist, and any resemblance to a real
government, person, or figure is coincidental. Real government or client data
never goes into this project.

---

## 1. Audiences

| Audience | App | Sign-in | Render mode | Why |
|---|---|---|---|---|
| Finance staff, department users, administrators | **Admin app** (`/admin/...`) | Required (ASP.NET Core Identity) | Blazor Interactive Server | Stateful editing grids, live fund balances, dialogs |
| Citizens | **Public transparency portal** (`/transparency/{slug}/...`) | None | Blazor static SSR | Fast, cacheable, crawlable, no server connection held per visitor, no JavaScript needed |

Both live in one ASP.NET Core host (`CivicBudget.Web`) so they share the domain
model, but they use **different render modes** and **different data paths**
(see §6).

---

## 2. Tenancy

- A **Government** is the tenant. Every tenant-owned row carries `GovernmentId`.
- Isolation is enforced in the data layer with **EF Core global query filters**
  driven by an `ITenantContext`, not by remembering to add
  `.Where(x => x.GovernmentId == ...)` to every query. A save interceptor refuses
  any write whose `GovernmentId` is not the signed-in user's government.
- A user belongs to **exactly one** government (a claim issued at sign-in).
- The Administrator is a *government's* administrator. New governments are
  created by seed data; there is no cross-tenant "super admin" screen.
- The public portal finds the government from the URL slug, never from a cookie.

---

## 3. Domain model (governmental fund accounting)

Money is always `decimal`, stored as `decimal(18,2)`. Never `double` or `float`.
Rounding is unit-tested (§5.2).

### 3.1 Government (tenant)
| Field | Notes |
|---|---|
| Name | "Village of Maple Ridge" |
| Type | City, Village, Township, County (display only) |
| State | Two-letter code; the seed data is Ohio |
| FiscalYearStartMonth | 1–12. Most Ohio subdivisions run on the calendar year (1); the field exists because some run July to June (7). |
| PublicSlug | URL-safe and unique across all governments, e.g. `maple-ridge-oh`. It can be changed; published snapshots move with it. |
| AppropriationLimitMode | `Warn` or `Block` (§5.1) |
| Description | Plain-language introduction shown on the portal |
| AccountNumberFormat | How this government writes a full account number: segment widths, separator, and the word for the middle segment (§3.4) |
| ChartSource | `Local` (maintained on the setup screens) or `Erp` (received from the ERP by sync, setup screens read-only) |

### 3.2 Fund
A fund is a separate pot of money with its own balance and its own legal limits on
what it can pay for; a street levy can only pay for streets.

| Field | Notes |
|---|---|
| Code | The government's own code. The seed uses Ohio UAN-style codes (1000 General, 2011 SCM&R, 4901 Capital Projects, 5101 Water, 5201 Sewer). |
| Name | |
| Category | GASB fund type, below |
| Description | Plain language for citizens ("What is the Street fund?") |
| IsActive | Funds are retired, never deleted: old budgets and snapshots still point at them |

**Fund categories** (GASB fund types):
- Governmental: `General`, `SpecialRevenue`, `DebtService`, `CapitalProjects`, `Permanent`
- Proprietary: `Enterprise`, `InternalService`
- Fiduciary: `Fiduciary`

### 3.3 Department
An organizational unit: `Code`, `Name`, `Description` (public), `IsActive`.
Departments are **cross-fund**: Streets & Service may have lines in both the
General fund and SCM&R. UAN charts call this segment a "program".

### 3.4 Account (chart of accounts)
| Field | Notes |
|---|---|
| Code | The object code. The seed uses 4xxx revenue and 5xxx expenditure. |
| Name | |
| Type | `Revenue`, `Expenditure`, `TransferIn`, `TransferOut` |
| ReportingCategory | Must be valid for the type (below) |
| IsActive | Retired, never deleted |

**Expenditure categories:** Personal Services, Fringe Benefits, Contractual
Services, Supplies & Materials, Capital Outlay, Debt Service, Other.
**Revenue categories:** Taxes, Intergovernmental, Charges for Services, Fines &
Forfeitures, Licenses & Permits, Investment Income, Miscellaneous.
**Transfer accounts** have the category `Transfers`.

**Full account numbers.** Ohio staff read and type an account as one number:
fund, then department (program), then object, such as `1000-725-121` in a UAN
village or `1000.710.5110` in a county ERP. The app stores the three codes
separately and *composes* the number under the government's
`AccountNumberFormat`, padding numeric codes to the segment widths. Revenue
budgeted at fund level has two segments (`1000-4110`). Search, import, and the
portal all accept a full number with or without separators.

**Where the chart comes from.** A government can keep its chart here, or receive
it from its ERP: an export file is uploaded, the app shows what would be added,
updated, retired, or reactivated, and applies it on confirmation. Codes the ERP no
longer lists are retired, never deleted, and an account's type cannot change once
budget lines use it (that would silently move money between revenues and
appropriations in every budget).

### 3.5 Fiscal Year
`Year` is the label year (2027), with `StartDate` and `EndDate` derived from the
government's start month. For a July start, "FY2027" runs 2026-07-01 to
2027-06-30: fiscal years are named for the year they end in. `IsClosed` stops new
budget versions once the year is done.

### 3.6 Budget Version
| Field | Notes |
|---|---|
| FiscalYearId | |
| VersionNumber | 1 = the Original budget; 2, 3, … = amendments, shown as "Amendment 1", "Amendment 2", … |
| Status | `Draft` → `Proposed` → `Adopted` (§4) |
| AmendmentReason | Required for amendments |
| ResolutionNumber | The ordinance or resolution number, required at adoption |
| AdoptedOnUtc, AdoptedByUserId | Set at adoption |
| SupersededByVersionId | Set on the previous adopted version when an amendment is adopted |
| Revision | Counts every change to the version, for concurrency (below) |

Rules:
- Only **one** open (Draft or Proposed) version per fiscal year at a time.
- An **Adopted** version is immutable: its lines, balances, and department
  requests cannot change. Attempts throw a domain exception.
- An **amendment copies** the latest adopted version (lines, beginning balances,
  and department narratives) into a new Draft with the next version number.
- **Starting a year's budget** (the Original) happens once per fiscal year, from
  the Fiscal years page, while the year is open. It starts empty, or from the prior
  year's latest adopted version:

  | Field | New year's value |
  |---|---|
  | Current year budget (the comparison) | last year's adopted amount |
  | Amount (the starting request) | that amount changed by an optional percentage, applied to every line, appropriations only, or revenue estimates only, and optionally rounded to whole dollars |
  | Prior-year actual | the ERP's figure when it holds that year closed (§3.12); otherwise 0 until typed or imported (an adopted budget is not what was spent) |
  | Fund beginning balance | last year's projected ending balance |

  $100,000 adopted at +3% starts at $103,000. Lines on retired funds,
  departments, or accounts are left out and listed.
- **Two saves based on the same read cannot both succeed.** The version's
  `Revision` is checked on save (optimistic concurrency), so the second person is
  told the budget changed and asked to reload instead of overwriting the first.

### 3.7 Budget Line
| Field | Notes |
|---|---|
| BudgetVersionId, FundId, AccountId | Required |
| DepartmentId | **Required for expenditure accounts; optional for revenue and transfer accounts.** Revenue is usually budgeted by fund alone, but a government may attribute it to a department (water sales to the Water Utility). |
| Amount | The budgeted (for appropriations, appropriated) amount in this version |
| PriorYearActual | What was actually received or spent in the last closed year (FY2025 for an FY2027 budget). The ERP's ledger is the source: once the ERP has sent that year closed, it fills this column (§3.12); before then it is typed or imported. |
| CurrentYearBudget | This year's budget, for comparison |
| Justification | Free text |

**Amounts are never negative.** Revenues and expenditures are both entered as
positive numbers; the account type says which way a line counts. A refund or
correction is a lower amount, not a negative one. The domain enforces this, and
the import refuses a negative (including accounting-style "(500)") in any money
column.

Uniqueness: one line per `(version, fund, department, account)`, including
fund-level lines with no department.

**A department's total** (its request, its appropriation) is the sum of its
**expenditure** lines only. Ohio appropriates by fund, then by office,
department, and division, with personal services shown within each (ORC
5705.38(C)). Transfers out are appropriated too, but as the fund's "other
financing uses", under their own program in the Auditor of State's UAN chart
(910 Transfers), not inside an operating department; Michigan's uniform chart
does the same (activity 965). Revenue a department collects is an estimated
resource of the fund. Revenue and transfer lines coded to a department are shown
beside it, labelled as outside its total.

Derived, not stored: `$ change = Amount − CurrentYearBudget`;
`% change = $ change ÷ CurrentYearBudget`, shown as "new" when the comparison is 0.

### 3.8 Fund Beginning Balance
Per `(version, fund)`: the estimated unencumbered fund balance at the start of the
fiscal year, entered by the Fiscal Officer (or carried forward when a budget is
started from last year's). Copied into amendments with the lines.

### 3.9 Fund balance summary (calculated, never stored)
For each fund within a version:

```
Estimated resources      = Beginning balance + Revenues + Transfers in
Appropriations           = Expenditures + Transfers out
Projected ending balance = Estimated resources − Appropriations
```

### 3.10 Department request
One per department per version: the department's **narrative** (the budget
message a police chief writes to explain the year's request) and whether it has
been **submitted** to the fiscal officer. Submit and return are recorded as named
events in the audit trail. Narratives travel into amendments; submission status
does not, because an amendment is a new round.

### 3.11 Deliberately out of scope
- The Tax Budget as a document. (The certificate of estimated resources and its
  amendments are in, §3.14.)
- Temporary (first-quarter) appropriations.
- Encumbrances, purchase orders, and the general ledger. The ERP owns the books;
  CivicBudget keeps a read-only copy of each year's actuals, open encumbrances, and
  fund cash (§3.12) and never posts to them.
- Advances (temporary loans between funds). Only permanent transfers are modeled.
- Appropriation limits below the fund. The Ohio check is at the fund level, and
  that is where the app enforces it.

### 3.12 Actuals from the ERP
The ERP sends a fiscal year of its books: receipts and spending by account and
fiscal month, open encumbrances by account, and each fund's cash, as of the end
of its last closed month. It arrives from the ERP's API when a connection is set
up, or from an export file (`Fiscal Year, Type, Account, Period, Amount`, with the
full account number). The live demo connects to a simulated ERP that keeps the
demo governments' fictional books.

- **A sync replaces the year.** Every code must match the chart or nothing
  changes; a year with a missing account would understate every total built on it.
- **A closed year fills prior-year actuals** in every open version of the budget
  two years on. Starting a budget and adding a line take them from there too. A
  year still in progress fills nothing: eight months of spending is not a year's
  actual. A negative total is left alone and reported.
- **An open year shows beside the budget.** Budget screens show the current
  year's receipts or spending so far under each line's current budget, with a bar
  for how much of that budget it is, and the drawer adds open encumbrances.
- A fund-level line (revenue with no department) collects the fund-and-account
  amounts that no department-level line in the budget claims, so money the ERP
  records against a department is neither lost nor counted twice.

### 3.13 Sending the budget to the ERP
An adopted budget goes into the ERP as a **budget journal**, by API or by an
import file, whichever the government uses. Every line carries `Account` (the full
account number), `Amount`, and the journal's one `Description` and one `Date`
(the posting date), as VIP expects.

- **Only the latest adopted version is sent**, the same rule as publishing, and
  only by an Administrator or the Fiscal Officer.
- **A journal holds changes, not totals.** Each amount is the budget's figure less
  what earlier journals for the year already posted. The first send of a year is
  the whole budget; an amendment's is what it changed; a line removed from the
  budget sends its amount back out as a decrease. When the ERP already matches,
  there is nothing to send, so pressing Send twice does nothing.
- **Whole or nothing.** If the ERP refuses any account, nothing posts, and the
  refused accounts are listed with the ERP's reason.
- **One unfinished send per year.** A send that got no answer stays open and is
  retried under the same id, which the ERP recognizes, so it posts once. An import
  file stays open until someone confirms it was loaded (only then does it count)
  or discards it. Nothing new starts for the year until the open one is settled.
- The posting date must fall in the fiscal year; it defaults to the year's first
  day for an original budget and the adoption date for an amendment.


### 3.14 The certificate of estimated resources
The county budget commission certifies, per fund, the resources available for
the year (ORC 5705.36); appropriations from a fund may not exceed its total
(5705.39). CivicBudget produces it for any budget version, numbered with it:
version 1 is the certificate, amendment N is "Amended Certificate No. N".

```
carryover       = cash at 12/31 − carried encumbrances − nonspendable − reserves ± unpaid advances
total available = carryover + revenue columns + other sources
```

- **Two views of one row per fund**, grouped by fund type with subtotals: as
  issued (balance, revenue columns, other sources, total) and the detailed
  schedule (every term above, plus appropriations).
- **Balances** come from the ERP's closed prior year (§3.12). Before it closes,
  the budget's estimated beginning balance is used and the report says so.
  Nonspendable and reserve balances (ORC 5705.13, 5705.132) and unpaid advances
  (positive for the lender, negative for the borrower) are entered per fund for
  the year.
- **Revenue columns are report settings**: up to four named sets of revenue
  accounts ("Taxes"; or "Real estate" and "Local taxes"), each account in at
  most one; every other receipt, transfers in included, is "other sources". With
  nothing saved, one "Taxes" column holds the accounts categorized as taxes. The
  county, the preparer, and the balance and other-sources headings are settings
  too, because county templates word them differently.
- **Reconciliations** shown on the certificate: appropriations within each
  fund's total; the budget's beginning balances equal to the certified
  carryover; every column mapped; the columns adding to the budget's revenue.
  An amended certificate lists every revenue estimate that moved, with its
  justification.
- A PDF (issued certificate with the commission's signature lines, then the
  detailed schedule and reconciliations), an XLSX, and print. Administrators,
  the Fiscal Officer, and Viewers; not department users, whose view of the
  budget is partial.

### 3.15 Reports on the ERP's books, and the appropriation measure
For a budget version of FY Y, compared with the ERP's FY Y actuals (§3.12):

- **Budget vs. actual**: each appropriation's budget, spent, encumbered,
  remaining, the share used with a tick for the share of the year gone, and last
  year's figure at the same month. By fund, then department; transfers out as
  the fund's other financing uses.
- **Revenue vs. receipts**: each estimate, received, still to collect, and the
  share collected against the share of last year's total in by the same month.
  "Behind" means more than ten points behind last year, not behind the calendar.
- **Projected fund balances**: beginning balance, budgeted and projected
  receipts and spending, and budgeted and projected ending balances. Each line
  is projected by scaling its year to date by last year's full year over last
  year at the same month; lines without that history are carried at budget and
  counted; encumbrances count as spent.
- **Multi-year trends**: per fund, each year's budgeted and actual receipts and
  appropriations and actual spending, from each past year's latest adopted
  version and the ERP's actuals, up to this budget.
- **Appropriation measure** (ORC 5705.38): per fund, per department, the
  measure's columns (default "Personal services": the personal services and
  fringe benefit accounts) and "Other", with transfers out beside the
  departments. Columns are report settings, like the certificate's.

Department users run budget vs. actual and revenue vs. receipts for their own
departments; the projection, trends, and measure cover whole funds and are for
the Administrator, Fiscal Officer, and Viewers.

### 3.16 Personnel budgeting
A **position** is budgeted in a department of a budget version: its title, the
employee (or vacant), hire date, pay (a yearly salary, or an hourly rate times
hours; typed with a raise from a chosen month, or from a pay scale's grade and
step with a step increase in a chosen month), the months it is paid, longevity,
extra pay, retirement system (and whether the employer picks up the employee's
share), insurance coverage by plan and tier, the funds that pay for it and their
shares (adding to 100%), and optionally its own base pay account.

A **year's personnel settings** hold: full-time hours; the base pay, Medicare,
and workers' compensation accounts and rates; retirement systems (employer and
employee rates, account); insurance plans (monthly premium per tier, employee
share, account); kinds of extra pay (a yearly amount, hours at a multiple of the
hourly rate, or a percentage of base pay; whether each counts toward retirement
and toward Medicare and workers' compensation; account); longevity schedules
(flat amounts by years, a percentage of pay by years, or an amount per year of
service with a cap; counted on the first or last day of the year); and pay
scales (a rate for each grade and step).

Each position's cost is priced per month paid, split among its funds, and summed
by fund and account into the department's **calculated lines**, which show "from
N positions" and change only with positions or settings. Saving a year's settings
reprices that year's open budgets. Amendments copy positions; a new year's budget
carries them forward at the rate each ends the year. Who may change a position is
who may change the department's lines (§4). Personnel detail is not published to
the portal; the portal shows the lines.

### 3.17 Employees from the ERP, and personnel reports
A budget being prepared takes the ERP's employee list, from its API or a payroll
export with one row per employee: employee number, name, title, department code,
pay type and rate (or grade and step), annual hours, hire date, retirement system,
pick-up, funds with shares, and insurance plans with tiers. Plans are matched to
the year's personnel settings by name; departments and funds by code. The preview
shows, per employee, whether a position is added, a vacancy filled, a position
updated (with what changed), or left vacant, and every budget line that would move;
any employee that cannot be matched refuses the sync. The ERP's side of a position
is refreshed; the budget's planned raise, step increase, months, longevity, and
other pay are kept. Administrator or Fiscal Officer; each sync is logged.

Reports: the **position roster** (every position by department, filled and vacant,
with hire date, years of service, pay rate, funds, pay, benefits, and total), the
**personnel cost by fund** (pay, retirement, Medicare, workers' compensation, and
insurance by fund and department), and the **benefits summary** (retirement members,
pensionable pay, employer share and pick-up; insurance enrollment by plan and tier
with employer and employee shares; Medicare and workers' compensation). The roster
follows a department user's departments; the other two are whole-government.

### 3.18 Email, two-step sign-in, and onboarding
**Email.**
- A department's submission notifies the government's Administrators and Fiscal Officers, except
  whoever submitted it.
- A returned request notifies the department's users, with the note.
- "Forgot your password?" emails a single-use reset link, valid for a day.
- A new user is emailed a link to choose their password; an Administrator can send a fresh one.

Every email is kept in the government's outbox, readable by its Administrators. It is delivered
over SMTP where a mail server is configured, and only kept where one is not (the demo).

**Two-step sign-in.**
- Any user can turn on a six-digit code from an authenticator app, with ten recovery codes.
- An Administrator can require it for everyone, and can reset it for a user who lost their phone.

**Onboarding.**
- The vendor sets up a government and its first Administrator from the command line.
- The Administrator chooses a password from the emailed link, then follows the getting-started
  checklist: the chart, a fiscal year, the first budget, and the team, then personnel, actuals,
  two-step sign-in, and the portal.

### 3.19 Security, sessions, and a government's data
**Sessions.** A session ends after 30 minutes without activity, with a warning two minutes before.
"Remember me" keeps a device signed in for up to 14 days. Deactivating a user, changing a role, or
resetting a password reaches that person's open sessions within 5 minutes.

**The security log.** Every sign-in, wrong password, lockout, second step, recovery code,
sign-out, idle sign-out, password change, export, and refused request is recorded with the time,
the person, and the address it came from. A government's Administrators read its log; attempts
for addresses with no account are the operator's alone.

**Limits.** Sign-in, second-step, and password-reset forms, and exports, are limited per address
(per user for exports). A refusal is logged and says to wait a few minutes.

**A government's data.**
- An Administrator can download everything the government has, one CSV file per table in a ZIP,
  without passwords, two-step keys, or images.
- When a government leaves, the operator exports it one last time and removes every row it has.
- Security events and finished emails are removed after a year.

---

### 3.20 Multi-year plan
Each budget version carries a plan of 1 to 10 years (5 by default), counting the budget year.
- **Percentages:** each future year has a revenue percentage (revenues and transfers in) and an
  expenditure percentage (expenditures and transfers out), within the same range as starting a
  budget. A calculated year is the year before changed by that year's percentage, rounded to the
  cent or, if chosen, the dollar.
- **Typed years:** any line's future year can be set by hand; the years after it follow from it.
  Clearing it goes back to the calculation.
- **Fund balances:** each fund's ending balance is the next year's beginning balance, and each
  year gets the appropriation check of §5.1.
- **Who:** the fiscal authority sets the length and percentages; a line's future years follow the
  same rule as its budget-year amount (§3.10). Adoption locks the plan.
- **Carried:** an amendment copies the plan; starting next year's budget moves it on a year.
- **Published:** the snapshot keeps each fund's plan by year, shown on the portal's Outlook page.

### 3.21 Connecting an ERP
Every exchange with the ERP has a file form and an API form: the chart of accounts, a year's
actuals, and the payroll roster come in; the budget journal goes out.
- **Published API:** version 1, described in `docs/partners/openapi.json`, with an integration guide
  and the file layouts beside it. An ERP that implements it connects by configuration.
- **Connections:** set by the operator per government (address, key, and the ERP's id for the
  government) in the secret store, never the database or a page. HTTPS only.
- **Who sees what:** a government with a connection gets Fetch and Send buttons beside the files;
  one without keeps the files alone.
- **Reference ERP:** a small server implementing the API on demo data, for vendors and for tests.

### 3.22 The budget book
The whole budget as one PDF, printed from any version.
- **Contents:** cover, contents, the budget message, the budget at a glance, a page per fund,
  the departments with their narratives, and the certificate of estimated resources; optionally
  the multi-year outlook, personnel cost, every account line, and the glossary.
- **Message:** typed on the version by the fiscal authority (heading, text, signer), until adoption;
  carried into amendments and next year's budget.
- **Proposed:** a version council has not adopted is marked Proposed on the cover and every page.
- **Who:** anyone who sees the whole budget; not department users.
- **Published:** publishing keeps the book with the snapshot, with the government's default
  sections, and the portal offers that copy.

### 3.23 The assistant
A panel in the admin app that answers questions about the budget and actuals and helps people find
their way around.
- **Acts as the user:** its tools are the Application services, run with the user's identity, so it
  sees what the user's account can see and nothing more.
- **Changes are proposed, never made:** the assistant can set the multi-year plan, change lines in
  bulk, fix a fund over its limit, start next year's budget, bring in actuals, and write the budget
  message or a narrative, but each is a card showing the change before and after, and only the
  user's click makes it. The change goes through the same service a page uses, checked again, and
  the audit trail records that it came through the assistant.
- **Review:** "check my budget" lists what must be fixed before council and what is worth a look.
- **Navigation:** finds and opens pages the user's policies allow, from each page's help topic.
- **Switches:** shown only when the operator has connected a model; each government's Administrator
  turns it on.
- **Records:** each question in the security log with the tools it used, not its text; 40 questions
  an hour per user; nothing about the conversation is stored.

## 4. Workflow

```
Draft ──(Propose)──▶ Proposed ──(Adopt, with resolution #)──▶ Adopted
  ▲                     │
  └──(Return to Draft)──┘
```

- Every transition is the Administrator's or Fiscal Officer's, asks for
  confirmation, and is written to the audit trail.
- **Department round (Draft only).** Each department user enters their own
  department's lines and narrative, then **submits** the request to the fiscal
  officer. A submitted department is locked for department users until the
  fiscal officer **returns** it with a note. The Administrator and Fiscal Officer
  may edit any line, submit on a department's behalf, and return a request at any
  time before adoption.
- The Fiscal Officer may keep adjusting lines while the version is Proposed;
  department users may not.
- **Adopted** is final. Changes need an **amendment** (§3.6), which goes through
  the same workflow. When an amendment is adopted, the version it replaced is
  marked superseded.

---

## 5. Validation rules

### 5.1 Appropriation limit (the Ohio rule)
Per fund: **appropriations may not exceed estimated resources**
(`Expenditures + Transfers out ≤ Beginning balance + Revenues + Transfers in`).
In Ohio the county budget commission certifies estimated resources, and the
appropriation measure cannot exceed them (ORC 5705.39).

Each government chooses how strictly the app applies it:
- `Warn`: the fund balance panel shows a warning; Propose and Adopt proceed after
  an explicit acknowledgement.
- `Block`: the panel shows an error; Propose and Adopt are refused.

Line edits always save, whatever the mode, so staff can work through a budget
that is temporarily out of balance. The rule is enforced at the transition.

### 5.2 Other domain rules (each has a unit test)
- An account's reporting category must be valid for its type.
- Expenditure lines need a department; revenue and transfer lines may have one or none.
- Amounts are never negative and fit `decimal(18,2)`.
- Amounts are kept to the cent; `% change` is rounded to one decimal place
  **away from zero**, the way Excel does. .NET's default banker's rounding would
  surprise finance staff.
- One open version per fiscal year; adopted versions are immutable.
- An amendment needs a reason; adoption needs a resolution number.
- Only the latest adopted version of a year can be published or used to start
  the next year.
- Fund, department, and account codes are unique within a government; the public
  slug is unique across all of them.

### 5.3 Input validation
FluentValidation validators in the Application layer, one per request. Services
return a `Result` with field-level errors; Razor components display them and
contain no rules of their own.

---

## 6. Publishing and the public snapshot

- The Administrator or Fiscal Officer can **publish** the **latest** adopted
  version of a year. Once an amendment is adopted, the earlier version is history
  and cannot be put back on the portal over it.
- Publishing creates an immutable **PublishedBudgetSnapshot**: a header
  (government, fiscal year, version label, resolution, who published and when)
  plus **denormalized rows** for every line, fund, and department, with their own
  copies of codes, names, descriptions, narratives, and full account numbers.
  Rows, not JSON, so SQL can aggregate them and exports are simple.
- The portal reads **only** the snapshot tables, through a separate read-only
  `PublicPortalDbContext` that does not map the live budget tables at all. A bug
  in the portal cannot show draft data because no code path leads to it
  (ARCHITECTURE §6).
- **Unpublishing** marks the snapshot `Unpublished` (it is never deleted) and is
  audited. Citizens then see nothing for that year until it is published again.
- **Republishing** a year (after an amendment is adopted) creates a new snapshot
  and marks the previous one `Superseded`. The portal shows one active snapshot
  per year; the database enforces that.
- Publishing, unpublishing, a logo change, and a slug change evict the portal's
  cached pages for that government.

---

## 7. Features

### 7.1 Admin app
1. **Setup:** government settings (including the account number format and
   who maintains the chart), funds, departments, chart of accounts, fiscal years,
   users and their departments, the ERP chart sync.
2. **Roles** (policy-based authorization, every cell tested):

   | Capability | Administrator | Fiscal Officer | Department User | Viewer |
   |---|:-:|:-:|:-:|:-:|
   | Manage users and government settings | ✓ | | | |
   | Maintain funds, departments, accounts, fiscal years (when the chart is local); sync the chart from the ERP | ✓ | ✓ | | |
   | View budget versions and reports | ✓ | ✓ | own departments | ✓ |
   | Edit budget lines | ✓ (Draft, Proposed) | ✓ (Draft, Proposed) | own departments, Draft, until submitted | |
   | Submit a department's request | ✓ | ✓ | own departments | |
   | Return a submitted request | ✓ | ✓ | | |
   | Edit beginning balances | ✓ | ✓ | | |
   | Workflow, amendments, starting a year's budget | ✓ | ✓ | | |
   | Publish and unpublish | ✓ | ✓ | | |
   | Import lines | ✓ | ✓ | | |
   | Export grids and reports | ✓ | ✓ | own departments | ✓ |
   | View audit history | ✓ | ✓ | own departments' lines | |

   The Administrator can do everything the Fiscal Officer can, plus users and
   settings. A department user's assignment bounds everything they see:
   workspace, reports, exports, search, and the audit trail. Passwords set by an
   administrator (at creation or reset) are temporary and must be changed at the
   next sign-in. "May this user edit *this* line" depends on the line's department
   and the version's status, so it is a resource-based authorization rule, not
   just a role check. The services check roles themselves too; the page
   attributes are the second lock, not the only one.
3. **Budget entry, two ways**, both showing prior-year actual, current budget,
   the new amount, $ change, and % change:
   - *By department:* each department has its own page with its accounts across
     funds (as full account numbers), running totals, its narrative, and Submit.
     The fiscal officer sees every department's status on a board. A department
     user lands on their department after signing in.
   - *By account line:* the fiscal officer works in one filterable, sortable
     grid across every fund and department, with inline editing.
4. **Fund balance panel:** live per-fund summary (§3.9) with the appropriation
   check (§5.1), recomputed from saved data after every edit.
5. **Workflow:** transitions with confirmation; amendments; starting a new
   year's budget from last year's.
6. **Audit trail:** a `SaveChanges` interceptor records entity, field, old value,
   new value, user, and time for every change to an audited entity, plus named
   events ("Adopted by resolution 2027-14"). Each line has a history timeline.
7. **Import and export:** CSV or XLSX import of budget lines with a preview
   (row-level results, nothing written until confirmed); XLSX export of the
   lines, every report, and the setup lists. The lines export uses the import's
   layout, so export, edit in Excel, import is a round trip.
8. **Reports** (on screen, printable, and as XLSX): Budget Summary by Fund,
   Department Budget Detail, Revenue vs. Expenditure by Category.
9. **Multi-year plan** (§3.20): the percentages, each fund's balance by year, and
   every line by year, with an XLSX export.
10. **Budget book** (§3.22): the message, the sections, and the PDF.

### 7.2 Public transparency portal: `/transparency/{slug}/{year?}`
- **Overview:** total revenues and expenditures, where the money comes from and
  where it goes, fund balances, and the government's own description.
- **Drill-down:** fund → department → category → account, with breadcrumbs and a
  stable URL at every level. Department pages carry the department's narrative.
- **Charts:** by category, fund, and department, plus year over year across every
  published year. Every chart has a table twin and does not rely on color.
- **Budget book:** the PDF printed when the budget was published, linked from the overview.
- **Outlook:** the plan published with the budget, all funds by year with the
  assumed changes, and each fund's ending balance.
- **Search** by department, account name, or full account number.
- **Downloads** of the published data as CSV and XLSX.
- **Glossary** of the terms the portal uses.
- **Accessibility:** WCAG 2.1 AA target: semantic HTML, keyboard navigation,
  sufficient contrast, labelled charts. No JavaScript at all.
- **Performance:** ASP.NET Core output caching per page, evicted by government on
  publish. Built for phones first.

### 7.3 Visual design
The app is judged on sight before anyone reads its code, so both halves have a
deliberate visual identity: a civic, trustworthy palette with WCAG AA contrast, a
clear type scale, consistent spacing, icons, and designed empty, loading, error,
and success states. I approved mockups before building each area
([design brief](design/DESIGN-BRIEF.md)). Bootstrap stays as the base, themed
through CSS variables, with no front-end build pipeline. Every page must work at
phone width without sideways scrolling.

---

## 8. Non-functional requirements
- .NET 10 LTS; nullable reference types; warnings as errors; async end to end.
- `IDbContextFactory<T>` for all database access from Blazor.
- Structured JSON logs outside development (the built-in formatter, no extra package).
- Friendly error and not-found pages; `/health` and `/health/startup` endpoints
  that never touch the database.
- The site answers within seconds of starting, even while the database is still
  waking, with a waiting screen instead of a hung request.
- No secrets in the repository: user-secrets locally, the platform's secret store
  in the cloud.
- Tests: xUnit, bUnit for components, Testcontainers (SQL Server) for
  integration tests. Every domain rule and every authorization rule has a test.
- CI on every pull request; deployments sign in with OIDC (no long-lived keys).
- WCAG 2.2 AA: every page scanned with axe-core and reviewed by keyboard; conformance reported in
  `docs/accessibility/ACR.md`.
- Designed and built to achieve SOC 2 compliance (Security, Availability, Confidentiality):
  security headers with a strict script policy, rate limits, code and dependency scanning in CI,
  and the controls, policies, and evidence in `docs/security`.

---

## 9. Seed data

### 9.1 Village of Maple Ridge, Ohio (primary tenant)
- Village, calendar fiscal year, slug `maple-ridge-oh`, `Block` mode, UAN
  account number layout (`1000-725-121`).
- **Funds:** 1000 General · 2011 Street Construction, Maintenance & Repair ·
  4901 Capital Projects · 5101 Water · 5201 Sewer.
- **Departments:** Council & Mayor, Finance, Police, Streets & Service, Parks &
  Recreation, Building & Zoning, Water Utility, Sewer Utility.
- **Accounts:** 12 revenue and transfer-in accounts (real estate tax, municipal
  income tax, Local Government Fund, gasoline tax, motor vehicle license tax,
  mayor's court fines, charges for services, tap-in fees, permits, interest,
  miscellaneous, transfers in) and 16 expenditure and transfer-out accounts
  (salaries, overtime, retirement, Medicare, health insurance, workers'
  compensation, contractual services, utilities, supplies, fuel, two kinds of
  capital outlay, debt principal and interest, other, transfers out).
- **Fiscal years:**
  - **FY2025:** adopted and published, with prior-year actuals.
  - **FY2026:** Original adopted, then **Amendment 1** adopted (a mid-year
    supplemental appropriation with a reason and resolution number) and published.
  - **FY2027:** a draft in progress. The Street fund is deliberately over its
    appropriation limit so the check is demonstrable, and the department round is
    part way: Police has submitted, Parks was returned with a note, the rest are
    still entering.
- **Demo users:** an Administrator, the Fiscal Officer, two department users
  (Police; Streets & Service and Parks & Recreation), and a Viewer. The password
  comes from configuration and is never committed.

### 9.2 Pine Hollow Township, Ohio (secondary tenant)
Small on purpose: a township with a **July fiscal year** (exercises the
configurable start month), slug `pine-hollow-twp-oh`, `Warn` mode, a dotted
account number layout (`1000.710.5110`), two funds, two departments, FY2026
adopted and published, FY2027 in draft, and one Administrator. It exists to
prove tenant isolation in the tests and in the demo.

---

## 10. Glossary (Ohio municipal budgeting)
- **Appropriation:** legal authority to spend, set by ordinance or resolution.
- **Estimated resources:** beginning unencumbered balance plus estimated revenue;
  the ceiling on appropriations. In Ohio the county budget commission certifies
  it as the *Certificate of Estimated Resources*.
- **Unencumbered balance:** cash less money already committed to purchase orders.
- **Fiscal officer:** the official who keeps a village's or township's books and
  assembles its budget (a city's is usually the finance director or auditor).
- **SCM&R:** the Street Construction, Maintenance & Repair fund, a special revenue
  fund paid for by the state gasoline tax and motor vehicle license fees.
- **Supplemental appropriation:** a mid-year change that raises or moves
  appropriations; here, an amendment.
- **UAN:** the Ohio Auditor of State's Uniform Accounting Network, the accounting
  system most small Ohio governments use; the seed follows its numbering.
- **Enterprise fund:** a fund for a business-like activity (water, sewer) paid for
  by user charges.
- **Transfer:** a permanent movement of money between a government's own funds
  (an *advance* is a temporary one, and out of scope).
- **Other financing uses:** transfers out and similar appropriations that are not
  a department's operating spending.

---

## 11. What shipped where

| Section | Phase | Notes |
|---|---|---|
| §2 Tenancy | 1, 2 | Query filters on `ITenantOwned`; the interceptor verifies writes (ADR-0004, 0013); Identity outside the filter (ADR-0015) |
| §3 Domain model | 1, 3 | Guid v7 ids (ADR-0018); revenue by fund or by fund and department |
| §3.4 Account numbers, ERP chart | 9a, 9b | Composed numbers (ADR-0024); chart from the ERP through an adapter (ADR-0025) |
| §3.6 Starting a year, concurrency | 18 | ADR-0033 |
| §3.7 Positive amounts, department totals | 18 | ADR-0033 |
| §3.10 Department requests | 9d | ADR-0027 |
| §3.12 Actuals from the ERP | 22 | ADR-0034; simulated ERP in the demo |
| §3.13 Sending the budget to the ERP | 23 | ADR-0035 |
| §3.14 Certificate of estimated resources | 24 | ADR-0036 |
| §3.15 Reports on the ERP's books, appropriation measure | 25 | ADR-0037 |
| §3.16 Personnel budgeting | 26 | ADR-0038 |
| §3.17 Employees from the ERP, personnel reports | 27 | ADR-0039; simulated payroll in the demo |
| §3.18 Email, two-step sign-in, onboarding | 28 | ADR-0040; the demo keeps email in its outbox |
| §3.19 Security, sessions, a government's data | 29 | ADR-0041; controls mapped in `docs/security` |
| §3.23 The assistant | 36, 37 | ADR-0047, ADR-0048; Claude behind Microsoft.Extensions.AI; off by default; changes only on the user's click |
| §3.22 The budget book | 35 | ADR-0046; frozen with the snapshot |
| §3.21 Connecting an ERP | 34 | ADR-0045; a reference ERP in `samples/` |
| §3.20 Multi-year plan | 33 | ADR-0044; nothing projected is stored except in the snapshot |
| §4 Workflow, §5 Validation | 4 | Block or Warn at the transition |
| §6 Publishing | 4, 5, 18 | Denormalized snapshots (ADR-0005, 0019); read-only portal context (ADR-0006); cache evicted by tag (ADR-0021) |
| §7.1 Admin | 2–4, 6, 9c, 9d, 10 | Two entry modes, live fund panel, audit trail, import, reports, department round, profile pictures |
| §7.2 Portal | 5, 12 | CSS bars with table twins and no JavaScript |
| §7.3 Visual design | 4.5, 8, 11, 14, 15 | Tokens over Bootstrap (ADR-0020); explanations in info tips; the sign-in page; phone layouts |
| §8 Non-functional | 1–7, 16, 17 | JSON logging, OIDC deploys, waiting screen (ADR-0031), 600+ tests |
| AWS (ADR-0008) | 7 | Deploy-ready: Dockerfile, CDK in C#, assertion tests, gated workflow; not deployed |
| Azure (ADR-0030) | 13 | The live demo on free tiers, rebuilt nightly |
