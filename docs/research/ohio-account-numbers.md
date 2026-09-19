# Ohio account numbers (research note, 2026-09-19)

How Ohio local governments write a full account number, and how CivicBudget
models it. Source: Ohio Auditor of State, Local Government Services,
*Chart of Accounts for Villages and Libraries* (January 2024) and the
companion township deck; both describe the Uniform Accounting Network (UAN)
chart most villages and townships use.

## The UAN layout

A numeric code has up to four parts: **fund**, then **receipt** (revenue) or
**program** (expenditure), then **object** (expenditure only).

| Kind | Segments | Example | Reads as |
|---|---|---|---|
| Revenue (receipt) | fund - receipt | `1000-110` | General Fund, General Property Tax (real estate) |
| Revenue | fund - receipt | `2031-931` | Cemetery Fund, Transfers In |
| Expenditure (appropriation) | fund - program - object | `1000-725-121` | General Fund, Clerk/Treasurer, Salary |
| Expenditure | fund - program - object | `2011-620-440` | Street fund, Street Maintenance and Repair, Small Tools |
| Transfer out | fund - program - object | `1000-910-910` | General Fund, Transfers Out |

Fund numbers encode the fund type: 1X general, 2X special revenue, 3X debt
service, 4X capital projects, 5X enterprise, 6X internal service, 7X
permanent, 9X fiduciary. Program (function) codes run 100s security of
persons and property, 200s public health, 300s leisure, 400s community
environment, 500s basic utility services, 600s transportation, 700s general
government, 800 capital outlay, 850 debt service, 900s other financing uses.
Object codes run 100s personal services, 200s fringe benefits, 300s
contractual services, 400s supplies and materials, 500s capital outlay,
600s miscellaneous, 700s debt service, 900s other financing uses.

## Counties and vendor charts

County auditors and city ERPs (the product this app plugs in beside) write
the same three ideas with their own widths and words: typically
`XXX-YYY-ZZZZ` with the middle segment called **department**, sometimes a
`.` separator, sometimes a trailing cost-center or project segment. The
object segment is often four digits.

## How CivicBudget models it

The model already stores the three codes on `Fund`, `Department`, and
`Account` (the object). A full number is therefore a *composed* value:

- `AccountNumberFormat` (owned by `Government`): fund width, middle width,
  object width, separator, and the word for the middle segment ("Program"
  or "Department"). Defaults to the UAN village layout, 4-3-4 with "-".
  Comes from the parent ERP's chart in Phase 9b; editable under Government
  settings until then.
- `AccountNumber.Compose` writes `1000-725-121`, or `1000-110` for a line
  with no department (revenue budgeted at fund level, which is why
  `BudgetLine.DepartmentId` is optional).
- `AccountNumber.TryParse` reads `1000-725-121`, `1000.725.121`,
  `1000 725 121`, or `1000725121` (widths decide), and two-segment numbers,
  for search boxes and the import's `Account Number` column.
- Published snapshots store each line's composed number, frozen at publish,
  so the portal shows what the government wrote at the time.

Not modelled: a fourth cost-center segment (rare at village scale; a later
phase could add it as an optional trailing segment).
