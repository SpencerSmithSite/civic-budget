# CivicBudget design brief

**Status:** direction approved by Spencer 2026-09-18 with one sharpening: it does not have to
look like VIP, but it must look and feel like it *fits in* next to VIP. Mockups next, then
implementation (ROADMAP Phase 4.5 and Phase 5).
**Inputs:** the VIP screenshot Spencer supplied, and the three research reports in this folder.

## 1. Direction in one sentence

A **modern civic ERP**: instantly familiar to someone who lives in VIP, Munis, or UAN (dark navy
chrome, left module navigation, dense worksheets, toolbars, breadcrumbs, status pills, a workflow
stepper), executed with the restraint of OpenGov and Socrata (one primary, one accent, white cards
on a grey canvas, an 8px rhythm, tabular numerals, one icon set, designed empty and loading states).

Why this and not something flashier: every credible product in the research uses navy/blue and
white; finance directors read density as competence; and the interviewer's own product is a
blue-module-tree ERP. The goal is "this person understands our world and knows what 2026 looks
like", not "this person can imitate a consumer app".

### "Fits in with VIP" means
Same family, one generation newer. Keep: blue module navigation with uppercase group headers,
an icon toolbar above records, a breadcrumb trail, label/value detail cards with small uppercase
labels, tabbed sub-sections over grids, grids that group and filter, blue as the trust color.
Change: one blue instead of several, a dark sidebar instead of saturated bars, whitespace in
headers and cards (not in grids), a single icon set, and designed states. A VIP user should feel
at home in ten seconds; an OpenGov user should not feel they went back in time.

## 2. Design tokens

All colors meet WCAG AA (4.5:1) as text on white unless noted. Defined once as CSS variables in
`app.css` and mapped onto Bootstrap's `--bs-*` variables; no build pipeline.

| Token | Value | Use |
|---|---|---|
| `--cb-navy` | `#1F4E79` | Primary: buttons, active nav, links in chrome (8.7:1) |
| `--cb-action` | `#1565C0` | VIP-adjacent action blue for primary buttons and toolbar icons (4.9:1 with white text) |
| `--cb-navy-hover` | `#2B6CB0` | Hover and focus rings (5.4:1) |
| `--cb-sidebar` | `#0F2A44` | Admin sidebar and portal footer background |
| `--cb-teal` | `#0B7285` | Accent: drill-down links, active rule in the sidebar, chart secondary (5.6:1) |
| `--cb-success` | `#1E7E4A` | Adopted, published, in balance |
| `--cb-warning` | `#9A5B00` (text) / `#FFF4E0` (bg) | Warn-mode limit, proposed |
| `--cb-danger` | `#B42318` (text) / `#FDECEA` (bg) | Over limit, destructive actions, negatives |
| `--cb-info` | `#175CD3` (text) / `#E8F0FC` (bg) | Unsaved value, informational badges |
| `--cb-text` | `#1F2937` | Body text |
| `--cb-muted` | `#5B6B7B` | Secondary text, eyebrow labels (4.6:1) |
| `--cb-canvas` | `#F6F8FA` | Page background |
| `--cb-surface` | `#FFFFFF` | Cards, grids |
| `--cb-border` | `#E3E8EF` | Card and table rules |
| `--cb-row-hover` | `#F1F5F9` | Grid hover |
| `--cb-subtotal` | `#EAF1F8` | Subtotal and group rows |

Chart categorical palette (color-blind safe order, always paired with labels): navy `#1F4E79`,
teal `#0B7285`, amber `#C8873A`, purple `#6B4C9A`, green `#2E8B57`, rose `#B5545C`, slate
`#7A8B99`; "Other" is always slate. Budget vs actual is encoded by hue *and* pattern (solid vs
hatched) or position, never hue alone.

Typography: system stack `"Segoe UI", -apple-system, "Helvetica Neue", Roboto, system-ui, sans-serif`
(Windows users get Segoe, which is what VIP and Munis render in). Base 14px / 1.45. Grid cells 13px.
Page title 20px / 600. Section title 16px / 600. Eyebrow labels 11px uppercase, letter-spacing .06em
(the "GENERAL LEDGER / Budget Entry" pattern). `font-variant-numeric: tabular-nums` on every
amount. Radius 6px. Spacing on an 8px scale. Focus ring 2px `--cb-navy-hover` with 2px offset,
visible on every interactive element.

Icons: Bootstrap Icons, vendored under `wwwroot/lib/bootstrap-icons` (ADR to record). 16px in
navigation and toolbars, 14px inline. No emoji, no mixed icon sets.

## 3. Admin application

**Shell.** Fixed 240px sidebar in `--cb-sidebar`: wordmark at top (a small seal glyph + "CivicBudget"
+ the government name in muted text), then groups with 11px uppercase labels: **Budget** (Versions,
Reports [Phase 6]), **Setup** (Funds, Departments, Chart of accounts, Fiscal years), **Administration**
(Users, Government settings), **Public portal** (Publishing history, Open portal). Items are 36px
tall with a 16px icon; the active item has a 3px teal left rule and a 10% white background. Under
1200px the sidebar collapses to a 56px icon rail with tooltips; under 768px it becomes an offcanvas
sheet behind a hamburger. A 48px white top bar carries the page breadcrumb (Budget > FY2027 >
Original), a fiscal-year selector where relevant, and the user menu (initials avatar, name, role,
Profile, Log out).

**Page header.** Eyebrow (module), title, optional status pill, and a right-aligned action area.
Under it, when a page has workflow, the **stepper**: equal segments Draft > Proposed > Adopted >
Published; completed in success green, current in navy, future in border grey; 28px tall; uppercase
11px labels. This replaces the current text-and-badge bar and is the single most "budgeting
software" element on the screen.

**Overview (dashboard).** Four KPI cards (Funds, Departments, Accounts, Fiscal years today; in Phase
4.5 the first row becomes budget-centric: current draft total appropriations, estimated resources,
funds over limit, days since last publish), each with an eyebrow label, a 28px value, and a 12px
delta or context line. Below: "Budget versions" as a compact table with status pills and a
"Continue" action, and "Recent activity" as an audit timeline (actor initials, verb, object,
relative time). Empty state for a brand-new government: one centered block with an icon, a heading,
one sentence, and a primary call to action.

**Worksheet (budget workspace).** Layout: header + stepper; a two-column body at 1200px and up with
the **fund balance panel** as a right rail of compact cards (fund code and name, projected ending
balance as the big number, resources and appropriations as two small figures, a colored top rule:
green in balance, amber warning, red over) and the worksheet on the left; below 1200px the panel
stacks above the grid. Grid: sticky header, sticky Account column, 34px rows, no vertical rules,
zebra off (hover instead), group header rows per fund and department in `--cb-subtotal`, subtotal
rows at 600 weight, negatives in parentheses in danger red, editable cells shown as inputs only on
focus (a pencil affordance on hover), unsaved value text in info blue until the save round-trip
completes, then a brief green check. Toolbar above the grid: left selects (View: By department / By
account line; Fund; Department; Type), right icon+label buttons (Add line, Filter, Export [Phase 6],
Print). The Note column becomes an icon with a count; History opens a right-side drawer instead of
pushing a card to the page bottom.

**Lists (funds, departments, accounts, users).** Same grid style, a 40px toolbar with search and
"Show inactive", status pills, row actions as an overflow kebab menu rather than three buttons.

**Dialogs and feedback.** `ConfirmDialog` everywhere (retire `window.confirm`), 18px title, one
muted subtitle line, right-aligned Cancel then primary, destructive in danger. Success feedback as a
top-right toast that auto-dismisses (4s) with an undo where it is safe; validation stays inline.
Loading is skeleton rows in place, never a page spinner.

**Login and home.** Login: split layout, left a navy panel with the wordmark and one sentence of
what CivicBudget is, right the form on white; no marketing fluff. Home (signed out): a short
product page: the two audiences, three screenshots, a "See a sample transparency portal" link to
Maple Ridge's public page.

## 4. Public transparency portal (Phase 5, design-first)

Principles from the research, in priority order:

1. **Question-led structure.** Tabs or sections titled "Where does the money go?", "Where does it
   come from?", "What changed from last year?", "Fund balances". Not "Expenditures by Object Class".
2. **Lead with three KPI cards**: total revenues, total expenditures, projected ending fund balance
   (all funds), each with the fiscal year, a one-sentence plain-language note, and "Explore".
3. **Bars, not pies.** Sorted horizontal bars with direct value labels and a "$ | %" toggle. A donut
   only for the fund overview, capped at six slices plus Other, with a name + percent legend.
4. **Every chart has a table twin** rendered in the HTML (not toggled in by script) and a CSV/XLSX
   download link on the same card.
5. **Drill-down = breadcrumb rail + "broken down by" selector + Back**: Budget > Fund > Department
   > Category > Account, every level a stable URL.
6. **Trust chrome**: government seal placeholder and name, "Adopted by resolution X on date;
   published date", a data-source line, a glossary ("What is a fund?"), a contact link, and an
   accessibility statement in the footer. Echoes Ohio Checkbook's institutional cues.
7. **Mobile-first**: KPI cards stack, navigation collapses, charts cap their height, tables scroll
   horizontally inside their card.
8. **Fast and static**: no iframes, no third-party embeds, no cookie banner (nothing to consent to).

Palette: the same tokens with `--cb-sidebar` as a header band and white content; the categorical
chart palette above; larger type (16px base) because citizens read, staff scan.

## 5. Mockups to produce before implementation

Admin: login; overview; budget workspace (by account line, with fund panel and stepper); users
list; adopt dialog. Portal: overview; fund drill-down; phone view of the overview. Produced as a
design canvas artifact for review; approval unlocks implementation.

## 6. Implementation plan (Phase 4.5) and acceptance criteria

1. `app.css` rewritten around the tokens; Bootstrap variables overridden; Bootstrap Icons vendored
   (ADR).
2. Shell: sidebar, top bar, breadcrumb, responsive collapse; wordmark and favicon.
3. Components: `KpiCard`, `WorkflowStepper`, `StatusPill`, `Toast` service, `EmptyState`,
   `SkeletonRows`, `SideDrawer` (history), row kebab menu; `ConfirmDialog` restyled.
4. Screens restyled in this order: workspace, overview, version list, lists, users, settings, login,
   home. Each screen reviewed at 1440, 1024, and 375 widths.
5. Accessibility check: contrast (tokens), focus visibility, keyboard through the grid and dialogs,
   landmarks and headings, reduced-motion respected.
6. Walkthrough 05: design tokens over Bootstrap; how the theme is shared with the portal.

Done means: every admin screen uses the shell and tokens; no `window.confirm`; no unstyled
Bootstrap defaults visible; screenshots in the README; Spencer says it looks like a product.
