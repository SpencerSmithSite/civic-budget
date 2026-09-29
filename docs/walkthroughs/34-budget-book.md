# Walkthrough 34: The printable budget book

Every Ohio government that adopts a budget ends up with a budget book, the document council votes
on, the auditor files, and residents ask for. It opens with a letter from the mayor, the trustees,
or the fiscal officer, summarizes the year, gives each fund and department its page, and includes
the certificate of estimated resources (the county's statement of what each fund may spend). Most
fiscal officers build it by hand. This phase prints it from the budget.

The decisions are in ADR-0046.

## 1. The budget message

The letter is part of the budget version, like the department narratives:
- **Fields:** `BudgetVersion.SetMessage(heading, body, signedBy, signerTitle)` stores a heading, the
  text, and who signs it. An empty body clears the message.
- **Plain text.** A blank line starts a new paragraph, so the message reads the same in the PDF
  and on the screen, and there is no formatting for either to interpret.
- **The version's rules.** It can be written until adoption, when it is settled with the rest of
  the budget.
- **Carried forward.** An amendment copies it. So does next year's budget, as a draft to rewrite,
  because much of a budget letter carries from year to year.

`BudgetMessageTests` holds these rules.

## 2. The book from reports that already exist

`BudgetBookService` does not compute a single figure. It asks the services the screens already use:
- the fund summary, the categories, and the department detail (`IReportService`);
- the certificate (`ICertificateService`);
- the multi-year plan (`IBudgetPlanService`);
- personnel cost by fund (`IPersonnelReportService`);
- the workspace lines, for the fund pages and the line-item appendix.

Then `BudgetBookBuilder` (pure, tested in `BudgetBookBuilderTests`) decides what each page says:
- **A fund page:** the fund's description, its limit arithmetic (beginning balance, plus revenues and
  transfers in, is estimated resources; less appropriations, is the projected ending balance), its
  money by source, and who spends it by department. Fund-level lines and transfers out each get
  their own row.
- **A department section:** its narrative and its spending by kind, summed across every fund it
  spends from. Only spending counts, the same rule as every department total
  (`CountsTowardDepartmentTotal`). Revenue a department collects appears on its fund's page.
- **The message:** split into paragraphs.
- **Optional sections:** left out when they are not chosen, or when there is nothing to show (a
  one-year plan, no personnel settings).

Because the book is built from those services, it cannot disagree with the screens. Each service's
own permission check applies too: the book covers every fund, so a department user gets none.

## 3. Drawing it

`BudgetBookPdfRenderer` uses MigraDoc, as the certificate does.

The book runs in this order:
1. **Cover:** the logo if it is a PNG or JPEG, the government, the year, and the version. It says
   "Adopted by council on June 15, 2026, resolution 2026-11", or "PROPOSED" in red.
2. **Contents:** page numbers come from bookmarks (`AddPageRefField`), so they stay right whatever
   the book holds.
3. **Message.**
4. **The budget at a glance:** estimated resources, appropriations, and the projected ending
   balance; bars for revenue by source and spending by kind; and every fund in one table, with an
   over-limit fund in red and said in words.
5. **Funds:** one page each.
6. **Departments:** run on one after another; a heading is kept with its narrative and table.
7. **Optional:** the multi-year outlook and personnel cost.
8. **Certificate:** its own landscape pages (`CertificatePdfRenderer.AddPages`), with the book's
   footer.
9. **Optional:** every account line, then the glossary.

Some details that make it a book rather than a report:
- **A draft marks every page.** A budget council has not adopted says "Proposed budget · not
  adopted by council" in the header of every page, not just on the cover. A work-session copy left
  on a table cannot pass for the adopted budget.
- **Every heading is an outline entry,** so a PDF reader's sidebar jumps between funds and
  departments.
- **Long tables repeat their header row** on each page; short tables are kept whole.
- **The bars are drawn in table cells** beside the amount and its share, so the chart is also a
  table.

The certificate renderer shares its colors with the book (`PdfPalette`) and takes a footer and a
bookmark from the caller, so the same certificate code prints the standalone certificate and the
book's.

## 4. The page

**Budget book** (Tools, then Budget book, or its card on Reports) has two parts:
- **Budget message:** the editor for the fiscal authority while the budget is open, and the message
  as text for everyone else.
- **Print the book:**
  - the four optional sections as checkboxes;
  - a note naming the departments that have not written a narrative;
  - **Download PDF**, whose link carries the chosen sections;
  - **Use these for published books**, which saves the choices as the government's defaults.

## 5. Published with the budget

Publishing prints the book with the defaults and stores it in `PublishedBudgetBooks`, one per
snapshot, in its own table so the portal's pages never load a PDF. The portal reads it through its
own context, like every other snapshot table:
- The overview links to it under the lead ("Read the full budget book"), and the footer links to it
  too.
- `/transparency/{slug}/{year}/budget-book.pdf` serves that frozen copy, cached with the pages.

Later edits to the budget, its message, or the book's layout never change what was published.

The seeded demo writes its snapshots straight to the database, so after seeding
`PublishedBookBackfill` prints a book for any published budget that has none. It acts for one
government at a time, so every query stays inside the tenant filter. The test template seeds
through the same path (`DatabaseInitializer.SeedAsync`), so the tests see what the app sees.

## 6. What it does not do

- **The PDF is not tagged** for screen readers; the library cannot do it. The portal's pages hold
  the same figures accessibly, and the book's contents page says so. It is recorded in the
  conformance report.
- **A WebP logo is left off the cover,** because a PDF cannot hold one. The government's name
  stands alone.
