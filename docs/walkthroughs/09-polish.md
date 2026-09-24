# Walkthrough 09: Polish

Phase 8 was the last pass before v1.0: what my review of the running app
changed, what the accessibility check found, and how the README screenshots are made.

---

## 1. Explanations behind an "i"

Using the app end to end, I found nearly every area had both a title and a
sentence explaining what it was for. That is clutter to anyone who uses the
screen daily. Every `<Subtitle>` that explained a page is gone.
`PageHeader` and `KpiCard` gained a `Tip` parameter that renders
`Components/Common/InfoTip.razor` beside the title or label:

```html
<span class="cb-tip" tabindex="0" role="note" aria-label="...">
    <i class="bi bi-info-circle" aria-hidden="true"></i>
    <span class="cb-tip-text" aria-hidden="true">...</span>
</span>
```

CSS only (`.cb-tip` in `app.css`): the bubble appears on hover or
keyboard focus, the text is the element's accessible name so a screen
reader hears it once, and the visible copy is hidden from assistive tech
so it is not read twice. No Bootstrap tooltip JavaScript to initialise.

The rule for what earns a tip: it must help someone use the screen or
read the data ("Beginning balances plus revenues plus transfers in; Ohio
law caps appropriations at this figure"). Text that only described the
page was deleted. Subtitles now carry data (an amendment reason), and an
empty one collapses (`.cb-sub:empty`).

The portal kept its citizen-facing leads where they define a term or carry
a total, and lost the ones that repeated the heading.

---

## 2. The overlap

The overview's versions table spilled over the activity card. Cause: for
the workspace grid, `.cb-grid-wrap` switches to `overflow: visible` at
1200 px so the sticky header can pin under the top bar, and that rule
applied to every grid. It is now scoped to `.cb-workspace .cb-grid-wrap`;
a grid inside a card scrolls within the card.

---

## 3. What the accessibility check found

Method: read the accessibility tree of each key screen (the built-in
browser exposes it), walk the workflow by keyboard, and re-check the
design system's existing rules. Found and fixed:

- The account menu button in the top bar had no accessible name (its name
  came from a span hidden at small widths). Now `aria-label="Account menu,
  Dana Whitfield"` with `aria-haspopup`.
- Both brand links (admin sidebar, portal header) had no name; explicit
  labels now.
- Amount inputs were labelled "Proposed amount for 5110", and 5110 appears
  once per department. Labels now include the account name and the
  department or fund.
- `ConfirmDialog` and `SideDrawer` did not move focus when they opened,
  so Tab and Escape acted on the page behind the modal. Both now focus
  their panel in `OnAfterRenderAsync` (`ElementReference.FocusAsync`).
- A leftover placeholder: the account grid's Export button still said
  "Arrives in Phase 6". It links to the XLSX endpoint now.
- Identity's `/Account/AccessDenied` was a bare red heading; it matches the
  router's Forbidden page.

Not done, and said so: a session with an actual screen-reader user.

---

## 4. Screenshots that regenerate

`scripts/screenshots/capture.mjs` (Playwright) signs in as the Fiscal
Officer, uploads a sample import file to get a populated preview, and
captures seven screens at 1440 px (portal at 390 px too). `sips` downsizes
them for the repository. When the UI changes, run it again instead of
letting the README drift.

(Phase 20 rebuilt it: it now finds every page by clicking through the seeded app, so no ids are
passed in, and it captures fifteen screens as four people, desktop and phone, including two
ordinary edits first so the overview's activity feed is not empty.)

---

## 5. Things to read

1. `Components/Common/InfoTip.razor` and `.cb-tip` in `app.css`
2. `Components/Common/ConfirmDialog.razor` (the focus handling)
3. `scripts/screenshots/capture.mjs`
4. `docs/DEMO-SCRIPT.md` for how to show the whole thing in ten minutes
