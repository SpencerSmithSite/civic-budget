# CivicBudget Accessibility Conformance Report

Based on the VPAT® 2.5 (WCAG edition) template. VPAT is a registered trademark of the Information
Technology Industry Council (ITI).

| | |
|---|---|
| **Product** | CivicBudget: the admin application and the public transparency portal |
| **Version** | `main` as of 2026-09-28 (after Phase 31) |
| **Report date** | 2026-09-28 |
| **Contact** | CivicBudget@spencersmith.site |
| **Product description** | Budget preparation for Ohio local governments (admin application, Blazor Interactive Server) and a public transparency portal (static HTML, no JavaScript) |

## Evaluation methods

This is a self-evaluation. No third-party audit has been done.

- **Automated:** axe-core 4.13 with the WCAG 2.0, 2.1, and 2.2 A and AA rules plus axe's best
  practices, run by `scripts/screenshots/a11y-sweep.mjs`. It covered:
  - every page each role can reach (Administrator, Fiscal Officer, Department User, Viewer);
  - the sign-in pages;
  - every portal page at 1366 and 390 pixels wide.

  That is 237 page loads. The final run found no violations.
- **Manual:** two structured reviews, one of the portal and sign-in pages and one of the admin
  application. Each was done by keyboard and pointer in Chromium through Playwright, checking
  focus, the accessibility tree, computed styles, reflow at 320 pixels, 200% text, and text
  spacing. They found 1 serious and 8 moderate issues on the portal side, and 7 serious and
  10 moderate on the admin side. All are fixed except where the tables below say otherwise.
- **Keyboard regression checks:** after the fixes, a script repeats the serious findings: the
  phone menu, dialogs opened from row menus, refused amounts, form errors, sidebar focus, and the
  portal panels.
- **Not done:** testing with screen readers (VoiceOver, NVDA, JAWS), speech input, or real users
  with disabilities. "Announced" below means the text is in a live region or alert in the
  accessibility tree, not that a particular screen reader was heard saying it.

## Applicable standards

| Standard | Included |
|---|---|
| WCAG 2.2 Level A | Yes |
| WCAG 2.2 Level AA | Yes |
| WCAG 2.2 Level AAA | No |
| Revised Section 508 (web content) | Through WCAG: 508 incorporates WCAG 2.0 A and AA, which this report covers |

## Terms

- **Supports:** the functionality meets the criterion without known defects.
- **Partially supports:** some functionality does not meet the criterion.
- **Does not support:** most functionality does not meet the criterion.
- **Not applicable:** the criterion is not relevant to the product.

## Table 1: WCAG 2.2 Level A

| Criterion | Conformance | Remarks |
|---|---|---|
| 1.1.1 Non-text Content | Supports | Icons are hidden from assistive technology or carry text. Every portal chart is an image or a list with a text label and has a table twin; a chart with more than six items says so and points to the table. Logos have text alternatives. |
| 1.2.1 Audio-only and Video-only (Prerecorded) | Not applicable | The application has no audio or video. |
| 1.2.2 Captions (Prerecorded) | Not applicable | No audio. |
| 1.2.3 Audio Description or Media Alternative | Not applicable | No video. |
| 1.3.1 Info and Relationships | Partially supports | Headings, landmarks, lists, labelled fields, fieldsets, `scope` on table headers, and row headers in the worksheet and reports. **Exception:** in the worksheet, the group rows that title a fund and program are data cells spanning the row, not headers. |
| 1.3.2 Meaningful Sequence | Supports | DOM order matches reading order. |
| 1.3.3 Sensory Characteristics | Supports | Instructions do not depend on shape, color, or position. |
| 1.4.1 Use of Color | Supports | Status pills carry text; over-limit funds say "Over by"; changes carry a sign; the portal's selected toggle is also marked with `aria-current`. |
| 1.4.2 Audio Control | Not applicable | No audio. |
| 2.1.1 Keyboard | Supports | Every function works by keyboard, including amounts, row menus, dialogs, drawers, the phone menu, info tips, and the portal toggles. |
| 2.1.2 No Keyboard Trap | Supports | Dialogs and the phone menu keep focus while open, by design, and release it on Escape or close. |
| 2.1.4 Character Key Shortcuts | Not applicable | No single-character shortcuts. |
| 2.2.1 Timing Adjustable | Supports | Idle sign-out after 30 minutes shows a warning two minutes ahead with "Stay signed in", and any key or click extends the session. Failure and warning messages stay until dismissed. |
| 2.2.2 Pause, Stop, Hide | Supports | The sign-in illustration plays twice and stops; nothing else moves for more than five seconds. Motion respects reduced-motion settings. |
| 2.3.1 Three Flashes or Below | Supports | Nothing flashes. |
| 2.4.1 Bypass Blocks | Supports | "Skip to content" is the first link on every page; landmarks name the regions. |
| 2.4.2 Page Titled | Supports | Titles name the page, and the government or product. |
| 2.4.3 Focus Order | Supports | Focus moves into dialogs and the phone menu and returns to the opener, falling back to the page heading if the opener is gone. It returns to a row's menu after a note is saved or cancelled. It lands on the error summary after a failed submit, and on the page status when a pager button disables itself. |
| 2.4.4 Link Purpose (In Context) | Supports | |
| 2.5.1 Pointer Gestures | Supports | No multipoint or path-based gestures. |
| 2.5.2 Pointer Cancellation | Supports | Actions fire on click (up event). |
| 2.5.3 Label in Name | Supports | Accessible names begin with the visible text. |
| 2.5.4 Motion Actuation | Not applicable | |
| 3.1.1 Language of Page | Supports | `lang="en"`. |
| 3.2.1 On Focus | Supports | |
| 3.2.2 On Input | Supports | An amount saves when it is committed (Enter or leaving the field), and the page reports the result. |
| 3.2.6 Consistent Help | Supports | Help is in the same place on every page: info tips beside what they explain. |
| 3.3.1 Error Identification | Supports | Errors are listed in a summary that takes focus and is an alert, and each field's message sits beside it. A refused amount is marked invalid and announced. |
| 3.3.2 Labels or Instructions | Supports | Every field has a visible label; required fields carry `aria-required`, and optional ones say "(optional)"; hints are linked with `aria-describedby`. |
| 3.3.7 Redundant Entry | Supports | Nothing asks for the same information twice in a process. |
| 4.1.1 Parsing | Not applicable | Removed in WCAG 2.2. |
| 4.1.2 Name, Role, Value | Supports | Toggle and disclosure states are real `"true"`/`"false"` strings (`Aria.Bool`); dialogs are named; the phone menu is a dialog while open. |

## Table 2: WCAG 2.2 Level AA

| Criterion | Conformance | Remarks |
|---|---|---|
| 1.2.4 Captions (Live) | Not applicable | |
| 1.2.5 Audio Description (Prerecorded) | Not applicable | |
| 1.3.4 Orientation | Supports | |
| 1.3.5 Identify Input Purpose | Supports | Sign-in and account fields use `autocomplete` (username, current-password, new-password, one-time-code). |
| 1.4.3 Contrast (Minimum) | Supports | Text is 4.5:1 or better on every background, including alert tints (the muted grey and success green were darkened for this). |
| 1.4.4 Resize Text | Supports | Readable at 200% text. |
| 1.4.5 Images of Text | Supports | None, apart from the logo. |
| 1.4.10 Reflow | Supports | No sideways scrolling at 320 CSS pixels, except inside wide data tables, which scroll on their own and can be reached by keyboard. |
| 1.4.11 Non-text Contrast | Partially supports | Focus rings, field borders (3.3:1), and status marks meet 3:1. **Exception:** in the worksheet, editable amount cells show their border only on hover or focus, a deliberate design so the grid reads like a printed budget; the column header and the focus ring identify them. |
| 1.4.12 Text Spacing | Supports | |
| 1.4.13 Content on Hover or Focus | Supports | Info tips can be hovered, stay while the pointer is on them, and close with Escape. |
| 2.4.5 Multiple Ways | Supports | Navigation, breadcrumbs, and search on the portal. |
| 2.4.6 Headings and Labels | Supports | Page headings are the title alone; card titles are headings. |
| 2.4.7 Focus Visible | Supports | A visible ring on every control, white on the navy bars and drawn inside the portal's clipped toggle. |
| 2.4.11 Focus Not Obscured (Minimum) | Supports | The sticky top bar never covered a focused control in testing. |
| 2.5.7 Dragging Movements | Not applicable | No dragging. |
| 2.5.8 Target Size (Minimum) | Supports | Targets are at least 24 by 24 pixels or meet the spacing exception. |
| 3.1.2 Language of Parts | Supports | |
| 3.2.3 Consistent Navigation | Supports | |
| 3.2.4 Consistent Identification | Supports | |
| 3.3.3 Error Suggestion | Supports | Messages say what to type ("an amount of zero or more, such as 1,250.00"). |
| 3.3.4 Error Prevention (Legal, Financial, Data) | Supports | Adopting, publishing, sending to the ERP, removing lines, and returning requests all ask for confirmation first, and amendments correct adopted budgets. |
| 3.3.8 Accessible Authentication (Minimum) | Supports | Passwords can be pasted or filled by a password manager; two-step codes can be pasted; no cognitive test. |
| 4.1.3 Status Messages | Supports | Saves, refusals, fund-limit changes, filter counts, and page changes are status or alert messages. The idle warning is read once, not every second. |

## Known limitations and plans

- **Not tested with screen readers.** The next step is a pass with VoiceOver and NVDA on the
  worksheet, the dialogs, and the idle warning.
- **1.3.1:** the worksheet's fund and program group rows should become header cells.
- **1.4.11:** whether editable amount cells should show a faint border at rest is a design
  decision still open.
- **Admin form fields** point to their errors through a focused summary rather than
  `aria-describedby` on each field.

The product website (spencersmith.site/CivicBudget) is not part of this report. It was reviewed
in the same pass, and its findings were fixed as well.
