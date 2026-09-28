# Walkthrough 30: The accessibility self-scan

Government software is bought against WCAG 2.1 or 2.2 AA, and the buyer asks for a VPAT: a
conformance report that goes through the criteria one by one. I have not paid for an audit. This
phase does what an audit would start with:
1. an automated scan of every page;
2. a review of what a scan cannot see;
3. fixing what both found;
4. an honest report (`docs/accessibility/ACR.md`).

## 1. The automated scan

`scripts/screenshots/a11y-sweep.mjs` runs axe-core on:
- every page each of four roles can reach;
- the sign-in pages;
- every portal page at 1366 and 390 pixels wide.

That is 237 page loads. It groups findings by rule, with the pages and elements for each, so a fix
is one change rather than a hunt.

The first run found five rules:

| Rule | Pages | Cause |
|---|---|---|
| Color contrast | 109 | Three causes: Bootstrap's grey secondary text fails on tinted alerts (3.6:1 on the green one); the green status pills were 4.47:1; and plain links were Bootstrap's own blue, because Bootstrap 5.3 colors links from `--bs-link-color-rgb`, which the theme never set. |
| Scrollable region not keyboard-reachable | 13 | Portal tables that scroll sideways on a phone had nothing focusable inside. |
| Unlabelled form field | 1 | The read-only fields on Government settings had labels without `for`. |
| Duplicate landmark | 10 | The sidebar and other `<aside>`s had no names. |
| Empty table header | 42 | Action columns (the "···" menus) had empty headers. |

Most of it was two lines of CSS: darker muted and success tokens, and the missing `-rgb` triples.
The last run found nothing.

## 2. The reviews

axe checks a page as it loads. It passed pages where:
- a dialog lost focus when it closed;
- a refused amount made no sound;
- the idle warning interrupted a screen reader every second.

For those, two subagents drove the pages by keyboard in Playwright, one on the portal and sign-in
pages and one on the admin application. They read the accessibility tree and focus after every key
press and reported what failed, with the success criterion and a suggested fix. The admin review
found 7 serious issues:

| Found | Fix |
|---|---|
| `aria-expanded="@menuOpen"` renders as an empty attribute when true and disappears when false | `Aria.Bool(menuOpen)` writes `"true"` or `"false"`. Blazor treats a C# bool as an HTML boolean attribute, which is not an ARIA state. |
| The phone menu never took focus | While open it is a named modal dialog. `civicBudget.openModal(sidebar, true)` moves focus in and keeps Tab inside, and closing returns focus to the Menu button. Following a link closes it without pulling focus back, because the new page takes it. |
| Closing a dialog opened from a row menu dropped focus to the page | By the time the dialog renders, Bootstrap has hidden the menu and focus is on the body. `openModal` now remembers the last element that had focus and swaps a dropdown item for its menu's toggle. `closeModal` falls back to the page heading if the opener is gone. |
| A refused amount only turned red | The field is `aria-invalid` and a danger toast (an alert) says what to type. |
| Setup forms did nothing audible on an invalid save | `ResultAlert`, on every admin form, lists every error as an alert and takes focus when a new failure arrives. |
| The sidebar's focus ring was about 1.3:1 | Bootstrap's `nav-link` focus is a faint shadow with no outline; the sidebar now draws a white outline. |
| The idle warning re-announced every second | It is an alert dialog named by a fixed sentence and read once. The countdown is not a live region, and focus goes back where it was when the warning closes. |

The portal and sign-in review's one serious issue was the "$ | %" toggle's focus ring, which the
toggle's rounded, clipped box cut to a sliver; the ring is now drawn inside each segment. Among the
moderate and minor findings:
- **Skip links** in all three layouts.
- **Current section:** `aria-current` on the portal's active section.
- **Sign-in errors** gathered in `FormErrors` and focused on load.
- **The sign-in illustration** stops after two plays (motion that runs over five seconds must stop).
- **The portal's spending/revenue radios** grouped in a fieldset. That moved them away from the
  track they slide, so the CSS reaches it through `:has()`.
- **Headings:** page headings hold the title alone, so the heading focused on each navigation is
  not read with a status and a paragraph of help.
- **Toasts:** failure toasts stay until dismissed.
- **Fund limits:** an edit that takes a fund over its limit says so.

## 3. Checking the fixes

- A keyboard script repeats each serious finding against the running app, and all of them pass.
- The axe sweep, the role sweep, and the phone-width sweep run again clean.
- Seven bUnit tests (`AccessibilityTests`) pin the shared behaviors: the error summary, the refused
  amount, persistent failure toasts, the heading, the grouped radios, and `Aria.Bool`.

## 4. The report

`docs/accessibility/ACR.md` follows the VPAT 2.5 WCAG edition and rates every WCAG 2.2 A and AA
criterion. It says what it did not do, which matters as much as what it did: no screen reader has
been used yet. It lists two partial passes:
- the worksheet's group rows are not header cells;
- editable amount cells have no border until hovered, a design choice.
