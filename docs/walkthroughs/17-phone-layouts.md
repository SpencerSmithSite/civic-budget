# Walkthrough 17 — Phone layouts

Phases 14 and 15. Phase 14 made every page *fit* a phone; this phase makes
the wide ones *work* on one. Three patterns, chosen by what a table is for.

## 1. Lists become cards

Funds, departments, chart of accounts, fiscal years, users, budget
versions, publishing history, chart sync history, the department board,
and the overview's versions table. Each page keeps its grid inside
`d-none d-md-block` and adds a `.cb-cards.d-md-none` block that renders one
`ListCard` (`Components/Common/ListCard.razor`) per row from the *same*
filtered, paginated data. The pieces a row shares between the two views
(the status pill, the row menu, the summary line) live in `RenderFragment`
helpers in the page's `@code` block, so the card and the grid cell are one
piece of markup and cannot drift.

Two Razor gotchas met on the way: a fragment parameter named `Meta` is
parsed as the HTML `<meta>` void element (it is `Summary`), and a loop
variable named `code` inside a fragment trips the `@code` directive parser.

## 2. Working grids keep three columns

The workspace by account line, the grouped view, and the department entry
page mark prior actual, change $ and change % as `.cb-col-wide`, which
`display: none`s them under 768px, leaving account, current budget, and
the editable amount. The row menu hides too; a `.cb-row-open` chevron takes
its place and opens the drawer. The drawer now leads with `LineDetail`
(every figure, the editable amount, the note, Remove) above the line's
history, and on phones the drawer renders as a bottom sheet. Group rows and
headers wrap on phones because a single long no-wrap label sets a table's
minimum width by itself.

## 3. Reports as cards

Fund Summary renders one certificate card per fund on phones, the
arithmetic stacked the way the Ohio form reads, with the over-limit fund
marked in red; Department Detail renders a card per department with a
two-number list of its lines and the narrative. Both tables stay for wider
screens and for print (`d-print-block`), because reports are paper.

## 4. Things to read

1. `Components/Common/ListCard.razor` and the `.cb-list-card` CSS
2. `Components/Admin/Funds/FundList.razor` (the pattern, smallest instance)
3. `Components/Admin/Budgets/LineDetail.razor` and the drawer in `BudgetWorkspace.razor`
4. `Components/Admin/Reports/FundSummaryReport.razor` (the certificate cards)
5. `scripts/screenshots/mobile-sweep.mjs`
