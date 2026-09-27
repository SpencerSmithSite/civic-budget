# Walkthrough 23: The certificate of estimated resources

Phase 24 adds the first report that is also a legal document. The certificate of estimated
resources is how an Ohio county's budget commission tells a village, fund by fund, the most it may
appropriate for the year. It was deliberately out of scope in v1 (only its check was in). It is in
now because it needs the ERP's year-end figures, which CivicBudget has had since walkthrough 21.
The research behind it is in `docs/research/amended-certificate.md`.

## 1. What the certificate says

For each fund, the commission certifies what the government carried into the year plus what it
expects to receive. That total is a ceiling: appropriations from the fund may not exceed it (ORC
5705.36 and 5705.39). The balance carried in is worked out like this:

```
carryover       = cash at 12/31
                − encumbrances carried into the new year
                − nonspendable and reserve balances (ORC 5705.13, 5705.132)
                ± advances between funds not yet repaid
total available = carryover + estimated revenue
```

There are two forms. The **detailed schedule** shows every term above, fund by fund. The **issued
certificate**, the one the commission signs, condenses it to five columns: the fund, the
unencumbered balance on January 1, taxes, other sources, and the total. When estimates change
during the year, an **amended certificate** is issued the same way, numbered.

## 2. One row per fund, two views

`CertificateBuilder` is a pure function from the budget, the ERP's figures, and the settings to a
`CertificateReportDto`: one `CertificateRowDto` per fund, sections by fund type (General, Special
Revenue, Capital Projects, Enterprise...) with subtotals, and a grand total. The issued certificate
and the detailed schedule on the page, in the PDF, and in the spreadsheet are all renderings of those
same rows. The Auditor of State's checklist asks that the certificate's balance equal the detailed
carryover; here they cannot differ, because they are the same number.

The certificate is numbered with the budget version: version 1 prints "Certificate of Estimated
Resources", amendment N prints "Amended Certificate of Estimated Resources No. N".

## 3. Which accounts are taxes is the government's choice

This is the problem that started the phase. The certificate's "Taxes" column pulls real estate
and local taxes, but those revenue lines are not the same in every chart: one village's
local tax is a municipal income tax, a township's is a road levy, and the accounts are numbered
differently. And county templates word the columns differently too ("Gross Taxes", "Rollbacks &
Other Sources").

So the columns are **report settings** (Setup, Report settings):

- Up to four named revenue columns, each a set of revenue accounts (`ReportAccountGroup`). Four is
  what fits on a landscape page beside the balance, other sources, and total.
- Everything not in a column is counted under the last heading, so no receipt is left out,
  transfers in included.
- An account can be in only one column, or its money would be counted twice. The page disables it
  in the other columns and says which one it is in; the service refuses it anyway.
- The county, who prepares the certificate, and the balance and other-sources headings are there
  too (`CertificateSettings`).
- With nothing saved, the certificate uses one "Taxes" column of the accounts categorized as
  taxes, and a note says so.

The settings page previews the column headings exactly as the certificate will print them, so the
fiscal officer can match the county's template before running anything.

## 4. Where the balance comes from

When the ERP has closed the prior year, the carryover is its year-end cash less the encumbrances
it carried forward (both synced since walkthrough 21), less the nonspendable and reserve balances,
plus or minus unpaid advances. Those last three are not in the ERP feed, so they are entered per
fund for the year from the certificate page ("Reserves and advances").

An original certificate is usually prepared before the year closes. Then the carryover is the
budget's own estimated beginning balance, the cash and encumbrance columns are blank, and a note
says the figures will come from the ERP once it closes. In the demo, FY2026's certificate uses the
ERP's closed FY2025 and FY2027's uses the budget's estimate.

The simulated ERP's year-end cash less carried encumbrances equals the next budget's beginning
balance (walkthrough 21, section 6), which is why FY2026's certificate reconciles to the dollar.

## 5. Reconciliations that can fail

A reconciliation that always passes is decoration. These four are on the certificate, and three of
them can fail on real data or settings:

| Check | Fails when |
|---|---|
| Appropriations stay within each fund's total available | A fund appropriates more than it has (FY2027's Street fund does) |
| The budget starts each fund from the certified balance | The ERP's year-end figures, or entered reserves, differ from the budget's beginning balance |
| Every revenue column has accounts | A column was added and nothing put in it |
| The columns add up to estimated revenue | Never, by construction; it proves every revenue dollar is in some column |

The second one matters most in practice. The budget's appropriation check uses the budget's own
beginning balances; once the certificate certifies something different, the fiscal officer should
update them, and the check names each fund and both amounts.

For an amended certificate, a table lists every revenue estimate that moved since the version it
amends, with the justification typed on the line. That is the support a commission asks for when
an estimate changes.

## 6. The PDF

The certificate goes to someone outside the building, so it has a real PDF: landscape Letter, the
issued certificate on page one with signature lines for the budget commission (the County Auditor,
County Treasurer, and Prosecuting Attorney), then the detailed schedule, the reconciliations, the
revenue changes, and the preparer's signature.

I chose PDFsharp and MigraDoc because they are MIT licensed. QuestPDF has the nicer API, but its free
license ends at $1M in company revenue, and the point of this version is that an ERP vendor could
take the product on. Two things surprised me:

- **MigraDoc freezes the document's default page setup**, so each section needs its own copy. And a
  copy carries an explicit page size that the orientation flag no longer swaps: my first PDF came
  out portrait with the right-hand columns cut off. `NewSection` sets 11 by 8.5 inches outright.
- **A Linux container has no fonts.** PDFsharp finds typefaces through a resolver, so Source Sans 3
  (SIL Open Font License) ships embedded in the Infrastructure assembly, and `EmbeddedFontResolver`
  serves it. Every PDF looks the same on a Mac, in CI, and in the Azure container.

A unit test renders a certificate and checks the page count, the landscape page size, and the
embedded font, so a regression in either shows up in CI rather than in a commission's inbox.

## 7. Who sees it

The Administrator, the Fiscal Officer, and Viewers. Not department users: their view of the budget
is limited to their own departments, and a certificate built from part of the budget would be wrong
rather than partial. The page tells them so. Settings and reserves are the Administrator's and
Fiscal Officer's to change.

## 8. Tests

- **Unit** (`CertificateBuilderTests`): the carryover formula with reserves and advances both ways;
  fund-type subtotals and the grand total; a transfer in counted as another source; the checks
  catching a fund over its total and a budget starting from another balance; estimated balances
  before the ERP closes the year, with the adjustments not applied twice; split tax columns and an
  empty one; an amendment's revenue changes with their reasons; settings that would count money
  twice or not at all; and the PDF itself.
- **Integration** (`CertificateServiceTests`): FY2026 reconciling to the ERP to the dollar; FY2027
  estimated with the Street fund over; Pine Hollow on the default column; saved columns and headings
  changing the certificate, with an audit event; reserves and advances lowering the carryover and
  the reconciliation naming the fund; department users and Viewers kept out of what they may not
  see or change.
- **bUnit**: the two views, a failed check standing out, the department user's message, and the
  settings preview with an account locked to one column.

## 9. What I would do next

The certificate is generated, not issued and stored: it always reflects the budget and the ERP as
they are now. A commission signs a particular version of it, so the PDF is the record. If a
customer wanted CivicBudget to keep the certified figures themselves, the next step would be to
freeze a certificate the way publishing freezes a budget (walkthrough 04), and compare later ones
against it.
