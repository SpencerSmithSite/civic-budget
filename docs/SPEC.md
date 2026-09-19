# CivicBudget — Functional Specification

**Status:** Approved by Spencer on 2026-09-16 (Phase 0). Living document — updated as phases refine it.
**Audience:** Spencer (author/presenter), future Claude Code sessions, interviewers

CivicBudget is a multi-tenant web application that lets a local government
(city, village, township, or county) build its annual budget and publish it to
citizens through a public transparency website.

All data in this project is **fictional**. The seed municipality, *Village of
Maple Ridge, Ohio*, and the secondary tenant, *Pine Hollow Township, Ohio*, do
not exist. Any resemblance to a real government, person, or figure is
coincidental. Never load real government or client data into this project.

---

## 1. Audiences

| Audience | App | Auth | Render mode | Why |
|---|---|---|---|---|
| Finance staff, department heads, admins | **Admin app** (`/admin/...`) | Required (ASP.NET Core Identity) | Blazor Interactive Server | Rich, stateful editing grids and live validation |
| Citizens | **Public transparency portal** (`/transparency/{slug}/...`) | None (anonymous) | Blazor static SSR | Fast, cacheable, SEO-friendly, no SignalR circuit per visitor, works without JavaScript |

Both live in one ASP.NET Core host (`CivicBudget.Web`) so they share the
domain model and infrastructure, but they use **different render modes** and
**different data paths** (see §6 Publishing).

---

## 2. Tenancy

- A **Government** is the tenant. Every tenant-owned row carries
  `GovernmentId`.
- Isolation is enforced in the data layer with **EF Core global query
  filters** driven by an `ITenantContext`, not by remembering to add
  `.Where(x => x.GovernmentId == ...)` in every query.
- A user belongs to **exactly one** government (stored as a claim at sign-in).
- `Admin` is a *tenant* admin. Creating a new government is done by seed data
  or a CLI command in v1; there is no cross-tenant "super admin" UI.
- The public portal resolves the tenant from the URL slug, never from a
  cookie.

---

## 3. Domain model (governmental fund accounting)

Money is always `decimal`, mapped to `decimal(18,2)` in SQL Server. Never
`double` or `float`. Rounding behavior is unit-tested (see §8).

### 3.1 Government (tenant)
| Field | Notes |
|---|---|
| Name | e.g. "Village of Maple Ridge" |
| GovernmentType | City, Village, Township, County |
| State | Two-letter code; seed data is Ohio |
| FiscalYearStartMonth | 1–12. Most Ohio subdivisions are calendar-year (1); the field exists because some entities run July–June (7). |
| PublicSlug | URL-safe, unique across tenants, e.g. `maple-ridge-oh` |
| AppropriationLimitMode | `Warn` or `Block` — see §5 |
| Description | Plain-language text shown on the public portal |

### 3.2 Fund
| Field | Notes |
|---|---|
| Code | Tenant-defined string. Seed data uses Ohio UAN-style codes (1000 General, 2011 SCM&R, 4901 Capital Projects, 5101 Water, 5201 Sewer). |
| Name | |
| FundCategory | See below |
| Description | Plain-language text for citizens ("What is the Street fund?") |
| IsActive | Soft retirement; inactive funds can't receive new lines |

**Fund categories** (GASB fund types):
- Governmental: `General`, `SpecialRevenue`, `DebtService`, `CapitalProjects`, `Permanent`
- Proprietary: `Enterprise`, `InternalService`
- Fiduciary: `Fiduciary`

A helper on the enum returns the fund *group* (Governmental / Proprietary /
Fiduciary) for reporting.

### 3.3 Department
Organizational unit. `Code`, `Name`, `Description` (plain-language, public),
`IsActive`. Departments are **cross-fund**: the Street department may have
lines in both the General fund and SCM&R.

### 3.4 Account (chart of accounts)
| Field | Notes |
|---|---|
| Code | Tenant-defined string, not parsed into segments. Seed uses 4xxx revenue / 5xxx expenditure. |
| Name | |
| AccountType | `Revenue`, `Expenditure`, `TransferIn`, `TransferOut` |
| ReportingCategory | Depends on type — see below. Domain rule: the category must be valid for the account type. |
| IsActive | |

**Expenditure categories:** Personal Services, Fringe Benefits, Contractual
Services, Supplies & Materials, Capital Outlay, Debt Service, Other.

**Revenue categories:** Taxes, Intergovernmental, Charges for Services, Fines
& Forfeitures, Licenses & Permits, Investment Income, Miscellaneous.

**Transfer accounts** have the fixed category `Transfers`.

### 3.5 Fiscal Year
`Year` (int — the label year, e.g. 2027), with `StartDate`/`EndDate` derived
from the government's `FiscalYearStartMonth`. For a July start, "FY2027" runs
2026-07-01 to 2027-06-30 (labelled by the ending year, the common convention).
`IsClosed` prevents new versions once the year is done.

### 3.6 Budget Version
| Field | Notes |
|---|---|
| FiscalYearId | |
| VersionNumber | 1 = original budget; 2, 3… = amendments. Display label: "Original", "Amendment 1", "Amendment 2"… |
| Status | `Draft` → `Proposed` → `Adopted` (see §4) |
| AmendmentReason | Required when VersionNumber > 1 |
| ResolutionNumber | Ordinance/resolution number; required at adoption |
| AdoptedOnUtc, AdoptedBy | Set at adoption |
| SupersededByVersionId | Set on the previous adopted version when an amendment is adopted |

Rules:
- Only **one** non-adopted (Draft or Proposed) version may exist per fiscal
  year at a time.
- An **Adopted** version is immutable: its lines and beginning balances cannot
  be edited. Attempts throw a domain exception.
- Creating an amendment **copies** the currently adopted version (all lines
  and beginning balances) into a new Draft with `VersionNumber + 1`.

### 3.7 Budget Line
| Field | Notes |
|---|---|
| BudgetVersionId, FundId, AccountId | Required |
| DepartmentId | **Required for Expenditure accounts; optional for Revenue and Transfer accounts.** Revenue is usually budgeted by fund alone, but a government may attribute revenue to a department (e.g. water sales → Water Utility), so the line allows fund-only *or* fund + department. |
| Amount | The budgeted/appropriated amount for this version |
| PriorYearActual | Entered or imported figure (there is no general ledger in scope) |
| CurrentYearBudget | The comparison figure from the current year, entered or imported |
| Justification | Free text |

Uniqueness: `(BudgetVersionId, FundId, DepartmentId, AccountId)`.

Derived (not stored): `$ change = Amount − CurrentYearBudget`,
`% change = $ change ÷ CurrentYearBudget` (null when CurrentYearBudget is 0).

### 3.8 Fund Beginning Balance
Per `(BudgetVersionId, FundId)`: `BeginningUnencumberedBalance` — the
estimated unencumbered fund balance at the start of the fiscal year, entered by
the Finance Director. Copied into amendments along with the lines.

### 3.9 Fund balance summary (calculated, never stored)
For each fund within a version:

```
Estimated resources   = Beginning unencumbered balance + Revenues + Transfers In
Appropriations        = Expenditures + Transfers Out
Projected ending balance = Estimated resources − Appropriations
```

### 3.10 Deliberately out of scope
- Tax Budget, Certificate of Estimated Resources, and Amended Certificate as
  separate entities (the *check* they enable is in scope — §5).
- Temporary (first-quarter) appropriations.
- Encumbrances, purchase orders, and actual transactions / general ledger.
  "Actual" figures are entered or imported numbers.
- Advances (temporary inter-fund loans). Only permanent transfers are modeled.
- Legal appropriation levels below the fund (e.g. fund + department + object
  level). Validation is at the fund level.

---

## 4. Workflow

```
Draft ──(Finance Director: Propose)──▶ Proposed ──(Finance Director: Adopt, with resolution #)──▶ Adopted
  ▲                                       │
  └────────(Finance Director: Return to Draft)──┘
```

- Department Heads may edit **only while Draft** and only lines in their
  assigned departments.
- The Finance Director may edit any line while Draft or Proposed.
- **Adopted** is terminal and immutable. Changes require an **Amendment**
  (a new version — §3.6), which goes through the same workflow.
- Every transition requires a confirmation dialog and is written to the audit
  trail.

---

## 5. Validation rules

### 5.1 Appropriation limit (Ohio-style)
Per fund: **Appropriations may not exceed Estimated resources**
(`Expenditures + Transfers Out ≤ Beginning balance + Revenues + Transfers In`).

Configurable per government via `AppropriationLimitMode`:
- `Warn` — the fund balance panel shows a warning; workflow transitions are
  allowed after an explicit acknowledgement.
- `Block` — the fund balance panel shows an error; the **Draft → Proposed**
  and **Proposed → Adopted** transitions are refused.

Line edits are always saved regardless of mode, so staff can work through a
budget that is temporarily out of balance. Enforcement happens at the
transition.

### 5.2 Other domain rules (each gets a unit test)
- Account reporting category must match account type.
- Expenditure lines require a department; revenue/transfer lines may have one or none.
- Amounts are stored to two decimal places; `% change` is rounded to one
  decimal place using `MidpointRounding.AwayFromZero` (matches spreadsheet
  behavior finance staff expect; banker's rounding would surprise them).
- Only one open (non-adopted) version per fiscal year.
- Adopted versions are immutable.
- Amendment requires a reason; adoption requires a resolution number.
- Fund/Department/Account codes are unique per government.
- Public slug is unique globally and URL-safe.

### 5.3 Input validation
FluentValidation validators in the Application layer, one per command/DTO.
Razor components display the validation results; they never contain rules.

---

## 6. Publishing & the public snapshot

- A Finance Director can **Publish** any Adopted version.
- Publishing creates an immutable **PublishedBudgetSnapshot**: a header
  (government, fiscal year, version label, `PublishedAtUtc`, `PublishedBy`)
  plus **denormalized snapshot lines** (fund code/name/category/description,
  department code/name/description, account code/name/type/category, amount,
  prior-year actual, current-year budget). Stored as rows, not JSON, so SQL
  can aggregate and exports are trivial.
- The public portal reads **only** snapshot tables through a separate,
  read-only `PublicPortalDbContext` that does not even map the live budget
  tables. A bug in the portal cannot expose draft data because there is no
  code path to it. This is a deliberate security boundary — see
  `ARCHITECTURE.md` §6 and `DECISIONS.md`.
- **Unpublish** sets `UnpublishedAtUtc` on the snapshot (it is never deleted)
  and is audited. Citizens then see the previous active snapshot for that
  year, or nothing.
- **Republishing** (e.g. after an amendment is adopted) creates a new snapshot
  and marks the previous one superseded. The portal shows the newest active
  snapshot per fiscal year; history is retained and visible to admins.
- Publishing evicts the portal's output cache for that government.

---

## 7. Features

### 7.1 Admin app
1. **Setup / maintenance:** government settings, funds, departments, chart of
   accounts, fiscal years, users & role/department assignments.
2. **Roles** (policy-based authorization, all tested):

   | Capability | Administrator | Fiscal Officer | Department User | Viewer |
   |---|:-:|:-:|:-:|:-:|
   | Manage users & government settings | ✓ | | | |
   | Maintain funds / departments / accounts / fiscal years (when the chart is local); sync the chart from the ERP | ✓ | ✓ | | |
   | View budget versions & reports | ✓ | ✓ | own departments | ✓ |
   | Edit budget lines | ✓ (Draft/Proposed) | ✓ (Draft/Proposed) | own departments, Draft only | |
   | Edit beginning balances | ✓ | ✓ | | |
   | Advance / return workflow, adopt, create amendment | ✓ | ✓ | | |
   | Publish / unpublish | ✓ | ✓ | | |
   | Import lines | ✓ | ✓ | | |
   | Export grids & reports | ✓ | ✓ | ✓ | ✓ |
   | View audit history | ✓ | ✓ | own departments' lines | |

   Revised 2026-09-19 (v1.1, Phase 9c): the Administrator is a superset of the
   Fiscal Officer ("complete and total access"); role names are the customer's
   words (the database values `Admin`, `FinanceDirector`, `DepartmentHead`,
   `Viewer` are unchanged). A department user's assignment is the whole of what
   they see: workspace, reports, exports, search, and the audit trail.
   Passwords set by an administrator (at creation or reset) are temporary and
   must be changed at the next sign-in.

   Line-level checks (department ownership + version status) are
   **resource-based** authorization handlers, not just role checks.
3. **Budget entry — two modes**, both showing prior-year actual, current
   budget, proposed amount, $ change, % change:
   - *By department:* a department head sees only their departments' lines,
     grouped by fund then category, with subtotals.
   - *By account line:* the finance director works in a filterable, sortable
     `QuickGrid` across all funds and departments with inline editing.
4. **Fund balance panel:** live per-fund summary (§3.9) with the appropriation
   check (§5.1), updated as lines are saved.
5. **Workflow:** transitions with role checks and confirmation; amendments
   copy the adopted version into a new draft.
6. **Audit trail:** an EF Core `SaveChanges` interceptor records entity, key,
   field, old value, new value, user, and UTC timestamp for every tracked
   change. A per-line history view shows the trail.
7. **Import / export:** CSV/XLSX import of budget lines with a validation
   preview (row-level errors, nothing committed until confirmed); XLSX export
   of any grid or report, generated server-side with ClosedXML.
8. **Reports** (screen + print CSS): Budget Summary by Fund, Department Budget
   Detail, Revenue vs. Expenditure by Category.

### 7.2 Public transparency portal — `/transparency/{slug}/{year?}`
- **Overview:** total revenues, total expenditures, breakdown by fund, and
  the government's plain-language description.
- **Drill-down:** fund → department → category → account, with breadcrumbs
  and stable URLs at every level.
- **Charts:** by category and by fund, plus year-over-year across all
  published years. Every chart has an accessible data-table alternative and
  does not rely on color alone.
- **Search** across departments and accounts.
- **Download** the published data as CSV and XLSX.
- **Plain-language labels** from fund/department descriptions.
- **Accessibility:** WCAG 2.1 AA target — semantic HTML, keyboard navigation,
  sufficient contrast, labelled charts. Core tables work with JavaScript off.
- **Performance:** ASP.NET Core output caching keyed per snapshot, evicted on
  publish/unpublish. Mobile-first layout.

---

## 7.3 Visual design (added 2026-09-17)
The application is a portfolio piece and will be judged on sight before it is judged on
code. Both apps get a deliberate visual identity (ROADMAP Phase 4.5 for the admin app,
Phase 5 for the portal): a civic, trustworthy palette with WCAG AA contrast, a clear type
scale, consistent spacing, icons, and designed states (empty, loading, error, success).
Mockups are approved by Spencer before implementation. Bootstrap stays as the base, themed
through CSS variables, with no front-end build pipeline.

---

## 8. Non-functional requirements
- .NET 10 LTS; nullable enabled; warnings as errors; file-scoped namespaces;
  async end-to-end.
- `IDbContextFactory<T>` for all DbContext use in Blazor components.
- Structured JSON logging (built-in `Microsoft.Extensions.Logging` JSON
  console formatter — CloudWatch-ready without an extra package).
- Friendly error pages; `/health` endpoint.
- No secrets in the repo: user-secrets locally, AWS Secrets Manager in the cloud.
- Tests: xUnit; bUnit for components; Testcontainers (SQL Server) for
  integration tests; every domain rule and every authorization policy has a
  test. Rounding of money and percentages is explicitly tested.
- CI on every PR; deploy workflow is OIDC-based (no long-lived AWS keys).

---

## 9. Seed data

### 9.1 Village of Maple Ridge, Ohio (primary tenant)
- Type: Village · Fiscal year starts January · slug `maple-ridge-oh` ·
  AppropriationLimitMode: `Block`
- **Funds:** 1000 General (General) · 2011 Street Construction, Maintenance &
  Repair (Special Revenue) · 4901 Capital Projects (Capital Projects) ·
  5101 Water (Enterprise) · 5201 Sewer (Enterprise)
- **Departments:** Council & Mayor, Finance, Police, Streets & Service,
  Parks & Recreation, Building & Zoning, Water Utility, Sewer Utility
- **Accounts:** ~12 revenue (real estate taxes, municipal income tax, local
  government fund, gasoline tax, motor vehicle license, water sales, sewer
  charges, mayor's court fines, permits, interest, miscellaneous, transfer in)
  and ~16 expenditure (salaries, overtime, OPERS, Medicare, health insurance,
  workers' comp, contractual services, utilities, supplies, fuel, capital
  outlay – equipment, capital outlay – infrastructure, debt principal, debt
  interest, other, transfer out).
- **Fiscal years:**
  - **FY2025** — Adopted, published. Has prior-year actuals.
  - **FY2026** — Original adopted + **Amendment 1** adopted (a mid-year
    supplemental appropriation with a reason and resolution number);
    Amendment 1 published.
  - **FY2027** — Draft in progress; intentionally has one fund slightly over
    its appropriation limit so the validation is demonstrable.
- Demo users: one per role (admin, finance director, two department heads,
  viewer). Passwords are set by seed configuration, not committed.

### 9.2 Pine Hollow Township, Ohio (secondary tenant)
Small: type Township, **fiscal year starts July** (exercises the configurable
start month), slug `pine-hollow-twp-oh`, mode `Warn`, one General fund, two
departments, a handful of lines, one published year, one admin user. Its
purpose is to prove tenant isolation in integration tests and demos.

---

## 10. Glossary (Ohio municipal budgeting)
- **Appropriation** — legal authority to spend, set by ordinance/resolution.
- **Estimated resources** — beginning unencumbered balance + estimated
  revenue; the ceiling on appropriations (in Ohio, certified by the County
  Budget Commission as the *Certificate of Estimated Resources*).
- **Unencumbered balance** — cash balance less outstanding encumbrances.
- **SCM&R** — Street Construction, Maintenance & Repair fund, a special
  revenue fund funded by state gasoline tax and motor vehicle license fees.
- **Supplemental appropriation** — a mid-year amendment increasing or moving
  appropriations; modeled here as an Amendment version.
- **UAN** — Ohio Auditor of State's Uniform Accounting Network; its fund
  numbering is used for seed data familiarity.
- **Enterprise fund** — a proprietary fund for a business-like activity
  (water, sewer) financed by user charges.
- **Transfer** — a permanent movement of money between funds (vs. an
  *advance*, which is temporary and out of scope).

---

## 11. Open questions
None blocking. Items to confirm as they come up:
- Whether Viewers should see the fund balance panel (assumed yes).
- Whether the FD may edit lines while *Proposed* (assumed yes; department
  heads may not).

---

## 12. Status against this spec (2026-09-18)

| Section | Delivered | Notes |
|---|---|---|
| §2 Tenancy | Phase 1, 2 | Global query filters on `ITenantOwned`; interceptor verifies writes (ADR-0004, 0013); Identity outside the filter (ADR-0015) |
| §3 Domain model | Phase 1, 3 | Guid v7 ids, `ValueGeneratedNever` (ADR-0018); revenue by fund or fund + department (§3.7) |
| §4 Workflow, §5 Validation | Phase 4 | Block/Warn at transitions with acknowledgement; amendments copy lines and balances |
| §6 Publishing | Phase 4, 5 | Denormalized snapshots, Active/Superseded/Unpublished (ADR-0019); read-only portal context (ADR-0006); output cache evicted by tag (ADR-0021) |
| §7.1 Admin 1–6 | Phase 2, 3, 4 | Two entry modes, live fund panel, audit trail with per-line history |
| §7.1 Admin 7 Import/export | Phase 6 | Preview-then-commit import (ADR-0022); XLSX of lines, reports, setup lists |
| §7.1 Admin 8 Reports | Phase 6 | Three reports on screen, print CSS, XLSX |
| §7.2 Portal | Phase 5 | Charts are CSS bars with table twins and no JavaScript at all (a step past "core tables work with JavaScript off") |
| §7.3 Visual design | Phase 4.5, 8 | Tokens over Bootstrap (ADR-0020); Phase 8 moved explanations into info tips |
| §8 Non-functional | Phase 1–7 | JSON logging, health endpoints, no secrets in the repo, OIDC deploy, 400+ tests |
| AWS (ADR-0008) | Phase 7 | Deploy-ready: Dockerfile, CDK in C#, assertion tests, gated `deploy.yml`; not deployed (no account) |

Open questions from §11 were resolved as assumed: Viewers see the fund
balance panel; the Finance Director may edit while Proposed, Department
Heads may not.

