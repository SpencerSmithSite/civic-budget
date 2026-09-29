# Walkthrough 31: Finishing accessibility

Phase 31 left the conformance report with two partial passes and no screen-reader testing. This
phase closes both partial passes and gets as close to a screen-reader pass as I can without one.

## 1. Editable amounts are outlined

The worksheet drew editable amounts as plain numbers until you hovered or tabbed to one. The grid
read like a printed budget, but nothing showed which numbers could be changed. Last year's actual,
this year's budget, and lines calculated from positions all looked the same as the ones you could
type over. WCAG 1.4.11 wants the edge of anything you can type into visible at 3:1.

Every editable amount now has a border in `--cb-field-border` (`#848F9B`) on a white fill:
- **Against the row:** 3.3:1 against a white row and 3.0:1 against a hovered one.
- **On hover:** the action blue.
- **Everything else stays plain text,** so a glance down the column tells you which lines are
  yours to change.

The same token gives every form field its edge.

## 2. Groups are row groups

A grouped table, such as the worksheet by fund and program or a report by fund or department, drew
each group title as one wide data cell. A screen reader read it as a cell with no relation to the
rows under it. Now each group is its own `<tbody>`, and its title is a `<th scope="rowgroup">`, so
moving through a row tells you which fund and program it belongs to.

- **The worksheet** is the interesting case. Its groups come from runs of rows in the current sort
  order. Sorted by amount, the same fund can appear several times, so `AccountLineGrid.Runs`
  builds the runs in code, and each run's key includes its position.
- **Seven reports** already had a `tbody` per group and only needed the cell changed.
- **Two tables** (the department page and the department detail report) had one `tbody` around
  everything. The `tbody` moved inside the loop.

The account cell that names each row became a `<th scope="row">` in Phase 31, but the grid's row
styles only targeted `td`, so those cells had lost their divider and padding. The styles now cover
both.

## 3. Reading what a screen reader reads

A real VoiceOver pass needs a macOS setting I am not allowed to change: VoiceOver's AppleScript
control. So I read what VoiceOver reads: the accessibility tree of the sign-in page, the worksheet,
the Add line dialog, a department request, the idle warning, and the portal (Playwright's
`ariaSnapshot`). It found four things:

| Found | Fixed |
|---|---|
| The worksheet table and four others had no name, so a screen reader said only "table" | Every table has an `aria-label` or caption. The loading placeholder stays hidden and says "Loading". |
| "$3.70M" has no spoken form; a screen reader may read "three dollars seventy M" | `MoneyShort.Speakable` shows the short form to the eye and "$3.70 million" to screen readers. |
| The portal's "$" and "%" links were read as symbols | They carry "Dollars" and "Percent". |
| "Expenditure accounts need a department" was not tied to the Program field | `aria-describedby`. |

## 4. Tables that scroll

With the row headers padded properly, the Fund Summary report became wide enough to scroll inside
its card at 1366 pixels, and axe flagged it: a keyboard user could not scroll it. Rather than patch
one table, `civicbudget.js` watches the page. Any table wrapper that overflows becomes a focusable
region named after its table, and stops being one when it fits again. A name that repeats (each
department has a "1000 General Fund" table) takes its section's heading too. The portal has no
script, so its wrappers set this themselves.

## 5. The VoiceOver checklist

`docs/accessibility/screen-reader-checklist.md` is a 15-minute pass. For each screen it lists the
keys to press and what VoiceOver should say. The expected wording comes from the trees above, so a
difference is a finding.
