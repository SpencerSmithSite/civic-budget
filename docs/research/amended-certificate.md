# The certificate of estimated resources (amended certificate)

Research for Phase 24. Collected 2026-09-27 from the Ohio Revised Code and the Auditor of
State's training material and sample certificates.

## What it is

The county budget commission certifies, for each fund, the resources a government can count on
for the year: what it carried in plus what it expects to receive. That total is a legal ceiling:
the government's appropriations from a fund cannot exceed it (ORC 5705.36, and the check in
5705.39). When the year's figures change (actual year-end balances arrive, a revenue estimate
moves), the fiscal officer asks for an **amended** certificate, and the same layout is issued
again with an amendment number and date.

The rows are individual funds, grouped by fund type (General, Special Revenue, Debt Service,
Capital Projects, Enterprise, Trust and the rest), with a subtotal per type and a grand total.

## The detailed certificate

The working version, which shows how each fund's available balance is reached:

| Column | What goes in it |
|---|---|
| Fund type | Governmental, proprietary, or fiduciary, and the type within it |
| Fund name and code | "General (1000)" |
| Cash balance at 12/31 | The fund's ending cash from the cash journal |
| Reserved for encumbrances at 12/31 | Prior-year purchase orders still open, carried into the new year |
| Reserved for nonspendable balance | When it applies |
| Reserve balance accounts | When it applies (ORC 5705.13 and 5705.132) |
| Advances not repaid | Positive for the fund that lent, negative for the fund that borrowed |
| Carryover available for appropriation | The unencumbered beginning balance, calculated |
| Total estimated revenue | Everything the fund expects to receive this year |
| Total available | Carryover available plus estimated revenue: the appropriation ceiling |

```
carryover available = cash balance
                    − carryover encumbrances
                    − nonspendable and reserve balances that apply
                    ± advances not repaid

total available     = carryover available + estimated revenue
```

## The issued certificate

What the commission signs is usually condensed to five columns:

| Fund / fund type | Unencumbered balance 1/1 | Taxes | Other sources | Total |
|---|---|---|---|---|
| Each fund, with subtotals | The carryover available | Estimated property and local taxes | Every other estimated receipt | The sum of the three |

Counties label the revenue columns differently ("Gross Taxes", or "Rollbacks & Other Sources"
split out), so the county auditor's own template decides the headings. That is why the report
needs settings: which revenue accounts are "taxes" differs from one government's chart to the
next, and so do the column names.

## Document controls

Entity name, county, fiscal year, as-of date, amendment number and date, the fiscal officer who
prepared it, the budget commission's certification, and support for any revenue estimate that
changed since the previous certificate.

## Checks the report should show

- Taxes plus other sources equals the fund's total estimated revenue in the budget.
- The issued certificate's unencumbered balance equals the detailed certificate's carryover available.
- The certificate's total is the fund's appropriation ceiling: total appropriations for the fund
  cannot exceed it.

## Where CivicBudget's data comes from

- Cash at 12/31 and carryover encumbrances: the ERP's closed-year actuals (`ErpFundCash`,
  `ErpEncumbrances`), synced since Phase 22.
- Estimated revenue: the budget's revenue and transfer-in lines.
- Taxes versus other sources: the report settings' account groups (Phase 24).
- Nonspendable and reserve balances, unpaid advances: not in the ERP feed yet; entered per fund
  with the certificate until they are.
