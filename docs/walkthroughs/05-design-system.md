# Walkthrough 05: Design tokens over Bootstrap

Phase 4.5 was a design pass. After Phase 3 the app worked but looked like a
Bootstrap tutorial, and a government buying budgeting software judges it on sight,
so I gave the admin app a deliberate visual identity before the portal reused it.

---

## 1. One file is the theme

`src/CivicBudget.Web/wwwroot/app.css` starts with the design tokens from
`docs/design/DESIGN-BRIEF.md` as CSS custom properties on `:root`
(`--cb-navy`, `--cb-action`, `--cb-sidebar`, `--cb-teal`, semantic colors,
neutrals, the chart palette). Immediately after, the same block sets
Bootstrap's own variables from them: `--bs-body-bg`, `--bs-primary`,
`--bs-link-color`, `--bs-border-radius`, and so on. Bootstrap components then
pick up the theme without any Bootstrap SCSS build.

Why not compile Bootstrap with custom SCSS? Because the project has no
front-end build pipeline and adding one (Node, Sass, a watcher) to a .NET
solution is a real cost with no benefit an interviewer would care about.
Bootstrap 5 exposes enough as CSS variables that `--bs-*` overrides plus a
few component rules (`.btn-primary`, `.card`, `.modal-content`) get the
whole surface.

Every color in the brief was checked for WCAG AA contrast on white; the
soft `-bg` variants exist so status pills read at 4.5:1 too.

---

## 2. The shell

`Components/Layout/AdminLayout.razor` is the whole admin shell: a dark
`cb-sidebar` of module groups (Budget, Setup, Administration, Public
portal), a white `cb-topbar` with breadcrumbs and the user menu, and
`cb-main`. Links are inside `AuthorizeView` per policy, so a Viewer never
sees Users. Under 992px the sidebar becomes an off-canvas panel behind a
hamburger; the state is a Blazor boolean and a CSS class, no JavaScript.

Breadcrumbs are set by the page, not the layout. `AdminPageState` is a
scoped service: `PageHeader` calls `SetCrumbs(...)` and the layout
re-renders on its `Changed` event. Blazor parameters flow downward, so a
child cannot hand data to its layout directly; a small scoped service is
the idiomatic bridge.

The public shell (`MainLayout`) is a navy header bar for the home page and
the Account pages. The login page uses the same navy as the sidebar on its
left panel, so signing in feels continuous.

---

## 3. Components that replaced ad-hoc markup

| Component | Replaces | Why |
|---|---|---|
| `PageHeader` | h1 + ad-hoc buttons | Eyebrow, title, pill, subtitle, actions in one place; sets breadcrumbs |
| `StatusPill` | `StatusBadge` and inline badges | Same word, same color, everywhere |
| `WorkflowStepper` | the status bar | Draft, Proposed, Adopted, Published as a stage bar, the most "budgeting software" element on the screen |
| `KpiCard` | inline cards | Eyebrow, big value, delta line; tone colors only the top rule |
| `ToastService` + `ToastHost` | `SuccessAlert` | Outcomes appear top-right and dismiss themselves; validation stays inline |
| `ConfirmDialog` (restyled) | `window.confirm` | Every destructive action now has a titled dialog with a real explanation; Escape closes |
| `SideDrawer` | a card appended to the page bottom | Line history opens beside the worksheet instead of pushing it around |
| `RowMenu` | three buttons per row | Grid rows stay narrow; actions live behind a kebab |
| `EmptyState`, `SkeletonRows` | "Loading..." text | Designed states: the page keeps its shape while data loads and says what to do when there is none |

---

## 4. The worksheet

Three decisions worth being able to explain:

- **QuickGrid `Theme="bootstrap"`.** QuickGrid ships a default theme whose
  cell padding outranks page CSS on specificity. Any other theme name
  switches its styling off; sorting and pagination are behavior, not
  styling, so they keep working. `app.css` restores the header button's
  basics under `table.cb-grid th .col-title`.
- **Group rows instead of a tree.** The account grid is sorted by fund,
  then department, then account; a group row is emitted whenever the pair
  changes. The grid reads like a printed budget with no tree control.
- **Sticky header only where it works.** A sticky `<th>` inside an
  `overflow-x: auto` wrapper sticks to the wrapper, not the page, and
  covers the first rows. So `.cb-grid-wrap` scrolls sideways under 1200px
  and is `overflow: visible` above it, where the header sticks under the
  top bar.

The inline editor (`AmountCell`) is an input that looks like a value until
hovered or focused, and shows "unsaved blue" while the save round-trip
runs. Nothing crosses the circuit while typing.

---

## 5. Icons

Bootstrap Icons 1.13 is vendored under `wwwroot/lib/bootstrap-icons` (the
CSS and two font files, about 400 KB). No CDN, so the app works on a
network that blocks external hosts, and no NuGet package (ADR-0020). Icons
are decorative here: every one carries `aria-hidden="true"` and sits next
to a text label.

---

## 6. Accessibility pass

- Contrast: every text color in the tokens is at least 4.5:1 on its
  background; pills use the soft backgrounds for that reason.
- Focus: a visible 2px ring on every interactive element via
  `:focus-visible`, plus Bootstrap's ring on form controls.
- Keyboard: dialogs and the drawer close on Escape; row menus are real
  buttons with `aria-label`s; the stepper is an ordered list with
  `aria-current="step"`; the sidebar toggle announces `aria-expanded`.
- Landmarks: `header`, `nav` (labelled per group), `main`, `aside`.
- Motion: the sidebar slide and the skeleton shimmer respect
  `prefers-reduced-motion`.
- Icons are `aria-hidden` and never the only label.

Not done in this phase: live-region wording for toasts beyond
`aria-live="polite"`, and focus return after a dialog closes. Phase 8 made
dialogs take focus when they open; Phase 18 returned it to the opener on close
and kept Tab inside the dialog.
