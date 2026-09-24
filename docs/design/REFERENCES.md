# Design references

Visual research for the Phase 4.5 admin design pass and the Phase 5 portal.
Kept as notes so the design brief can point at evidence rather than taste.

## 1. VIP (the target employer's ERP), from a marketing screenshot (2026-09-17)

The screenshot shows the product on a laptop (employee record), a tablet (employee survey), and a phone (pay stub with a deductions donut chart).

Observations:

- **Shell:** left navigation as a stacked module tree. Module headers are solid blue bars with white
  uppercase labels (HOME, GENERAL LEDGER, BUDGETING, CAPITAL IMPROVEMENT, PURCHASING, ACCOUNTS
  PAYABLE, PAYROLL / HR, MISCELLANEOUS RECEIPTS, ACCOUNTS RECEIVABLE). Inside a module: indented
  items and sub-items; the current item has a dark selected background. Items carry a "..." overflow
  menu. This is the classic public-sector ERP tree; users find modules by fund-accounting vocabulary.
- **Page header:** record title in large text ("STRONG, Isaiah M."), an icon toolbar directly under it
  (green plus = add, copy, red trash = delete, pencil = edit, refresh, favorite, save), secondary
  action buttons ("Employee Contacts", "Create Login", "Portal Email", "Import Emails"), then a
  breadcrumb (Workflow Items > Employees > STRONG, Isaiah M.).
- **Detail panel:** two-column label/value list with uppercase small-caps labels (FULL NAME, EMPLOYEE
  NUMBER, ...). Collapsible section bars in dark blue ("MORE INFORMATION", "PERSONAL INFORMATION").
- **Sub-tabs and grid:** a tab strip (Jobs, Accruals, Check Details, Deductions, Direct Deposits,
  Memos, Wage Summary) over a DevExpress-style grid: a "Drag a column header here to group by that
  column" bar, per-column filter funnels, checkbox selection column, a toolbar with the same
  add/delete/edit icons plus filter and export buttons.
- **Palette:** blues throughout (a mid "link" blue ~#2f80c7 for module bars, darker navy for selected
  and section headers), white content, grey borders, green/red only on toolbar icons. No accent color
  beyond blue; charts on mobile use a bright multi-color palette (donut: blue, pink, green, orange).
- **Typography and density:** small system sans-serif, dense rows, minimal padding. Reads as a
  Windows-desktop application rendered in the browser.
- **Mobile:** the phone view is a simplified list (gross wages by period) with a donut chart of
  deductions and labeled values, green header bar.

What to borrow: the module-tree mental model and vocabulary; toolbars above records; breadcrumb
trail; label/value detail panels; dense, groupable grids with filters and export; blue as the
trust color. What to modernize: a single restrained palette with one accent, more whitespace in
headers and cards (not in grids), consistent iconography instead of mixed colored icons, KPI cards
instead of bare numbers, and designed states (empty, loading, success).

## 2. Research reports (2026-09-18)

- [research-ohio-vendors.md](research-ohio-vendors.md): who Ohio governments actually use (UAN,
  VIP, Tyler, BS&A, Springbrook, OpenGov) with named Ohio customers and screenshot sources.
- [research-admin-ui.md](research-admin-ui.md): staff-facing budgeting UI conventions (Questica,
  Workday Adaptive, Tyler Hub/Munis, BS&A, OpenGov, Springbrook) and concrete specs.
- [research-transparency-portals.md](research-transparency-portals.md): Ohio Checkbook, OpenGov,
  ClearGov, Socrata Open Budget, Questica OpenBook, Balancing Act; what works and what to avoid.
- Reference screenshots kept locally: `ref-opengov-budget-builder.png` (the modern budgeting
  benchmark: summary panel, phase stepper, stacked bars, proposals with status pills) and
  `ref-tyler-hub.png` (dark omnibar, icon rail, solid KPI tiles, module tree cards).

The synthesis is [DESIGN-BRIEF.md](DESIGN-BRIEF.md).
