# Research: staff-facing budgeting UIs (2026-09-17)

Compiled from public documentation, help centers, and marketing pages. Sources are
listed so every claim can be checked. Used as input to the Phase 4.5 design brief.

## 1. What could be verified (sources and screenshots)

| Vendor | Evidence quality | Primary screenshot sources |
|---|---|---|
| Questica Budget (GTY) | Strong: 30+ real UI screenshots in a customer guide | Colorado State Univ. "Questica Budget Load Guide" PDF: https://www.budgets.colostate.edu/Forms/BudgetConstruction/Questica_Guide.pdf (pp. 1-4, 14-20, 26-33) |
| Workday Adaptive Planning | Strong: full budget-entry manual with sheet/dashboard screenshots | FSU "Adaptive Planning 2024-25 Budget Entry Manual": https://budget.fsu.edu/sites/g/files/upcbnu4406/files/docs/2024-2025%20ADP%20Training%20Manual_Budget%20Entry.pdf (pp. 4-19) |
| Tyler Enterprise ERP (Munis) / Tyler Hub | Strong: Hub shell + Account Inquiry screens | Tyler Hub 2021.1 User Guide (Union County, Ohio): https://www.unioncountyohio.gov/media/Officials/Auditor/Budgetary%20Resources/Munis%20Docs/Tyler%20Hub%202021.1%20User%20Guide.pdf (pp. 4-11); EERP Financial Reports and Inquiry: https://www.cochise.az.gov/DocumentCenter/View/26450/GL--Financial-Reports-and-Inquiry-PDF (pp. 4-5) |
| BS&A Software | Good: one full-resolution Budget Entry screenshot | https://www.bsasoftware.com/wp-content/uploads/Budget-With-Footnotes.png (from https://www.bsasoftware.com/solutions/financial-management/budgeting/) |
| OpenGov Budgeting & Planning | Partial: marketing crops only, no full worksheet | https://opengov.com/wp-content/uploads/2026/07/bp-operating-budget-b-540x303.webp (proposal status legend, approved/pending columns); https://opengov.com/wp-content/uploads/2026/07/Operating-Budget-628x628.webp (Proposal Summary card); https://opengov.com/wp-content/uploads/2026/07/bp-operating-budget-c-540x303.webp (Create Milestone form) |
| Springbrook Cirrus | Good: home/shell screenshot from the help center | https://help.sprbrk.com/Seven_Help/Clean/images/SO_homepage.png (tour: https://help.sprbrk.com/Seven_Help/Clean/Cirrus/SO_General_Tour.html) |
| Caselle Connect | Weak: small GIF crops of a WinForms-style form | https://help.caselle.com/help/cx_help_files/gl0/assets/images/images2020/gl_0159b.gif |
| Syncfusion / DevExpress | Text only; no government-specific demo found | https://github.com/SyncfusionExamples/blazor-financial-dashboard-syncfusion ; https://github.com/DevExpress/demos-dashboard (no public-sector sample; could not verify) |

Could not verify: any OpenGov full budget worksheet or approval-workflow screen; any Springbrook or
Caselle budget grid; Tyler's newer SaaS budget-projection UI.

## 2. Observed UI conventions per product

**Questica Budget.** Top nav (Dashboard / My Tasks / Budgeting / Reports / Administration) with a
mega-menu grouped by module (Operating / Capital / Personnel), each group with its own accent color.
White chrome, single blue primary for Save/Next/Load Data; green for Promote; outline for
Demote/Cancel. Accounts grid: checkbox and lock-icon columns, header filter row ("Type to filter"),
sortable headers, hyperlink titles, Stage and Approved columns. Budget lines grid: toolbar with
labeled icon buttons (Grid View / Display / Forecast Years / Precision / Add / Value Bar / Filter /
Layout / Import / Export), group header rows (Object Code Type: Expense), right-click row menu (Edit,
Lock, Adjust, Clear, Copy Forward, Distribute). Actual Cost Comparison: `2024 Budget | 2024 Actual |
2024 Variance` with bold category subtotal rows on a light-blue band, negatives in parentheses.
Workflow: three-segment stage bar at page top (green done, black current, gray future) and a modal
stepper. Empty state: "Let's Get Started" with a Load Data button.

**Workday Adaptive Planning.** Hamburger plus dark-blue left drawer (Home / Sheets / Reports /
Dashboards / Support). Strong blue ribbon carrying context selectors (Time / Level / Currency / Fund)
and right-side icon tools. Dashboards are tab-based with KPI tiles (one big number), bar/pie widgets,
and an embedded sheet. Sheet conventions worth copying: bold = totals, blue text = unsaved, green
text = imported actuals, grey cell = locked, blue background = editable rollup; year columns
`FY2023 | FY2024 | FY2025` with a collapsible account hierarchy; right-click cell menu (Copy
Forward/Downward, Adjust, Add Note, Explore Cell); small confirm dialogs with radio options.

**Tyler Hub / Enterprise ERP.** Dark indigo omnibar with logo, page name, centered search, help,
avatar; thin icon-only left sidebar; content is cards on a light-grey canvas. KPI tiles are
solid-colored blocks (teal Approvals, orange Notifications, green Alerts) with a big count. Data
cards are dense grids with header sort/filter popovers, a "Show as grouped" toggle, rows-per-page
footer. The Account Inquiry program is the classic ERP form: icon toolbar ribbon, label/field form,
tabs (4 Year Comparison / Current Year / History / History Graph) and a matrix of Original Budget /
Transfers / Revised / Actual / Encumbrances / Requisitions / Available / Percent used by fiscal year.

**BS&A.** Page header with a circular module icon and a "GENERAL LEDGER / Budget Entry" eyebrow
title. Tabs (Yearly Budgets / Monthly Allocations). Grid: type column, GL Number, Description, Dept,
then `17-18 Activity | 18-19 Activity | 19-20 Activity | 20-21 Amended Budget | 20-21 Activity |
21-22 Department Request`; blue hyperlinked drillable amounts, zebra rows, selected row light-blue,
footer rows Total Revenues / Total Expenditures / Net with red negatives in parentheses. Right-hand
"Budget Details" panel with Information / Attachments tabs and a Budget Footnotes sub-grid.

**OpenGov.** Contemporary SaaS look: white cards, large rounded corners, Inter-like sans, red/green
amounts for Proposed Expenses/Revenues, columns `Approved | Pending Approval`, proposal status legend
(In Progress / In Review / On Hold / Not Approved / Approved) tied to pie colors, toggle switches and
pill dropdowns in forms.

**Springbrook Cirrus.** Dark-navy top bar with an icon cluster; light left nav with a "Select
Module" switcher, Home / Jobs Viewer / Support / Favorites; content sections (Tasks and Approvals,
General, My Top Reports) with count tiles and shortcut cards bearing two-letter module chips (SS, UB,
AP, GL, CR) in distinct colors.

**Caselle Connect.** Legacy desktop form (yellow required fields, label-left inputs). Dated reference
point only.

## 3. Enterprise-credible vs consumer vs 2008 ERP

- Density: credible ERPs run 32-36px grid rows, 13px cell text, 8-12px cell padding; consumer apps
  use 48px+ rows; 2008 ERPs use 22px rows, 11px text and bevelled borders.
- Color restraint: one saturated primary on white or very light grey; color reserved for status,
  deltas and the nav bar. Solid-colored KPI blocks (Tyler) read slightly dated; white cards with
  colored text (OpenGov, Questica) read current.
- Tables: right-aligned tabular figures, thin horizontal rules, no vertical borders (full gridlines
  are the "old" tell), bold banded subtotal rows, parentheses or red for negatives, hyperlinked
  drillable numbers.
- Chrome: a dark top bar or dark sidebar gives product identity; a plain Bootstrap light navbar
  reads as a demo.
- Icons: one consistent line-icon set with labels under toolbar icons (Questica), not mixed glyphs.
- Workflow visibility: an always-visible stage bar (Questica) or status pill plus Approved/Pending
  columns (OpenGov) is the defining "budgeting" signal.

## 4. Recommendations for CivicBudget (Bootstrap 5 + CSS variables, no build)

1. Palette (all at or above 4.5:1 on white): primary navy `#1F4E79` (8.7:1), hover `#2B6CB0`
   (5.4:1), sidebar background `#0F2A44`; accent teal `#0B7285` (5.6:1) for links and drill-downs;
   success `#1E7E4A`, warning text `#9A5B00`, danger `#B42318`, info `#175CD3`; soft badge
   backgrounds `#E6F4EC / #FFF4E0 / #FDECEA / #E8F0FC`; text `#1F2937`, muted `#5B6B7B`, canvas
   `#F6F8FA`, borders `#E3E8EF`.
2. Type scale: system stack `"Segoe UI", Inter, Roboto, system-ui`; base 14px/1.45, grid cells
   13px, page title 20px/600, section 16px/600, eyebrow 11px uppercase with letter-spacing .06em
   (the BS&A "GENERAL LEDGER / Budget Entry" pattern); `font-variant-numeric: tabular-nums` on all
   amount cells.
3. Shell: 240px fixed dark sidebar (`#0F2A44`, white 80% text, active item with a 4px teal left rule
   and 10% white background), grouped by module with small uppercase group labels (Budgeting /
   Reporting / Administration), Bootstrap Icons 16px before each item; 48px white top bar with a
   fiscal-year selector, global search, notification bell, avatar initials. Collapse to an icon rail
   under 1200px.
4. Budget worksheet grid (QuickGrid): columns `Account | Description | FY-2 Actual | FY-1 Actual |
   Current Adopted | Current YTD | Proposed | Change $ | Change %`; sticky header and sticky first two
   columns via CSS `position: sticky`; row height 34px; no vertical borders; hover `#F1F5F9`;
   editable cells outlined `1px solid #2B6CB0` on focus, unsaved value text `#175CD3` (Adaptive
   convention), locked cells `#F3F4F6`; subtotal rows weight 600 on `#EAF1F8`; negatives in
   parentheses colored `#B42318`; group header rows for fund and department.
5. KPI cards: white card, 1px `#E3E8EF` border, 8px radius, 16px padding; 11px uppercase label,
   28px/600 value, 12px delta line with an up/down arrow icon in success or danger; no colored fills.
   Grid of four at 1200px and up.
6. Workflow stepper: Questica-style segmented bar under the page title: equal-width segments,
   completed `#1E7E4A` with white text, current `#1F4E79`, future `#E3E8EF` with grey text, 28px tall,
   uppercase 11px labels; paired with status pill badges using the soft backgrounds above.
7. Dialogs: Bootstrap modal with an 18px/600 title and a one-line muted subtitle, body divider,
   footer right-aligned Cancel (outline) then primary; destructive actions use danger and require a
   typed or checked confirmation; multi-step dialogs show Previous / Next.
8. Toolbars: 40px grid toolbar with left context selects (View / Years / Precision) and right
   icon-with-label buttons (Add, Filter, Layout, Import, Export) using Bootstrap Icons.
9. Empty and loading states: centered 320px block with an outline icon, 16px heading ("No budget
   lines yet"), one sentence, a single primary call to action; loading is three to five skeleton rows
   with `placeholder-glow` inside the grid body, never a full-page spinner.
10. Audit history: left-timelined list (2px rule, 8px dot colored by action) with actor initials,
    action verb at weight 600, before/after values in monospace chips, relative time with an absolute
    tooltip.
11. Icon set: Bootstrap Icons only, 16px in nav and toolbars, 14px inline in badges; two-letter
    colored module chips (GL, BU, RP) for module cards.
12. Quick wins: override `--bs-border-radius: .375rem`, `--bs-body-bg: #F6F8FA`, `--bs-link-color:
    #0B7285`; replace Bootstrap's default blue with the navy; add a favicon and wordmark with a civic
    seal glyph; `@media print` styles for worksheets; a 3px top accent on KPI cards.
