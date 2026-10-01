# Changelog

The short version of each phase. The long version, with the reasoning, is the matching
walkthrough in [docs/walkthroughs](docs/walkthroughs). Format loosely follows
[Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]
### Changed
- **The README, the demo script, and the product site lead with the AI features and forecasting:** a new "AI and forecasting" section in the README with six screenshots (the assistant answering, a proposal card, a resident's question, the multi-year plan, the projection, the outlook), the two new phases in "Problems worth reading about", and a demo script that shows the assistant and the plan early. `scripts/screenshots/capture-ai.mjs` takes the new screenshots (it needs a model), and `site-clips.mjs` records the assistant, the question box, and the plan. The existing screenshot script typed into Police overtime, which is calculated from positions now; it edits two typed General Fund lines instead.
- **The live demo has the AI features:** the assistant and the portal's question box run on GLM 5.3 Flash (Ollama Cloud). `scripts/azure-assistant.sh` stores the key as a Container App secret (`--off` removes it); the Bicep template takes it as an optional parameter for the web app only, `azure-setup.sh` keeps it on a rerun, and the demo's portal takes 200 questions a month per government.
### Fixed
- **A position split evenly between funds gives its odd cent to the lowest fund number**, not the fund with the lower id. Ids made in the same millisecond differ from one seed to the next, so the demo's Street fund over-limit amount moved by a cent or two between reseeds. The other funds' parts now round down, so the fund with the largest share (the lowest number, on a tie) always takes the odd cent rather than sometimes giving it away. The calculator gets the numbers from `PayrollRules.FundCodes` (ADR-0038, amended). The demo's Street fund is now over its limit by $21,908.68 on every reseed.
- **A form opened before the nightly reset** (or before a deploy that changed the keys) no longer submits to a blank white page. Its antiforgery token was sealed with keys the reset deletes, and the app answered with an empty 400. `ExpiredFormMiddleware` now shows a short "This page expired" page with a link back to the same address; sign-in keeps its ReturnUrl when it stays on the site, and a post-only endpoint such as sign-out links home. The portal's question box, which has no token, is unaffected.
- The waiting screen and the new page share one shell (`PlainPage`) instead of two copies of the header and styles.

## Phase 40: 2026-10-01 (Civic Buddy)
### Changed
- **The AI assistant is Civic Buddy** (ADR-0051) in the admin app and on the portal: the top bar's button, the panel, Government settings, the security log ("Asked Civic Buddy"), and the audit trail ("Through Civic Buddy: ..."). An "AI" badge and a sparkle mark (`BuddyMark`) sit beside the name; both prompts tell the model its name and to say it is an AI.
- **On the portal, Civic Buddy floats in the corner of every page** of a government that takes questions, replacing the "Ask a question" menu item. It opens a panel with the question box and four suggested questions, without JavaScript (a `<details>` element; each post names the ask page's form).
- The ask page reads as a conversation: the question on the right, the reply under Civic Buddy's name.
### Added
- `CivicBuddyTests`, and prompt tests for the name. The accessibility sweep also scans the ask page and the open panel.

## Phase 39: 2026-10-01 (the portal's new look)
### Changed
- **The transparency portal takes the product site's look** (ADR-0050): every page opens on a navy band (`PortalHero`) with its breadcrumbs, title, and a short lead; the overview's and each fund's headline figures sit as cards over the band's edge; charts and tables are white cards on a light page; icons mark the headline figures, the adoption details, and a new "Explore the budget" grid on the overview; the footer is navy. The search box sits in the band, and the portal index lists governments as cards.
- Wide tables fit their card: column headings wrap.
- Still no JavaScript; the overview offers "Ask a question" only when the portal takes questions (`PortalAskable`, from the layout).
### Removed
- The unused `.pt-h1`, `.pt-head`, `.pt-lead`, `.pt-book`, and `.pt-govlist` styles.

## Phase 38: 2026-09-29 (questions from the public)
### Added
- **"Ask a question"** on the transparency portal: a resident asks about the published budget in their own words and gets a short answer with a link to the page that shows it. Answers come only from the published snapshot of that government, through nine read-only tools; drafts, actual spending, and anything about people are not there to find, and it says so.
- **Works without JavaScript**: a plain form post, no cookies.
- **Limits**: 10 questions per address per hour (refused before anything reads the database), 500 characters per question, and a monthly ceiling per government (`Assistant:PortalQuestionsPerMonth`, 1,000 by default) counted in one atomic update.
- **A second switch** in Government settings for the Administrator, separate from the staff assistant, audited; changing it refreshes the portal's cached pages.
- **Evaluation set** for the public bot: published figures, "not published" for actuals and drafts, the glossary, and a question that tries to change its rules.
### Changed
- A question posted to the portal with no model connected is refused before any database read, so the free demo keeps ADR-0031's rule (amended).
- The assistant's settings card no longer says it "cannot change anything" (it proposes changes since Phase 37).

## Phase 37: 2026-09-29 (the assistant proposes, you confirm)
### Added
- **Actions** in the assistant: set the multi-year plan, change lines in bulk, fix a fund over its limit, start next year's budget, bring in actuals from the ERP, and write the budget message or a department narrative. Each is shown as a card with the change before and after, and only the card's button makes it.
- **"Check my budget"**: what must be fixed before council (a fund over the Ohio limit, a failing certificate check) and what is worth a look (unsubmitted requests, missing narratives, large changes with no justification, later plan years that overspend, zeroed lines).
- **Downloads**: the assistant links a report's PDF or spreadsheet, and the link saves the file.
- **Audit**: a confirmed change is recorded as "Through the assistant: ..." beside the change itself.
- `IBudgetEntryService.UpdateLineAmountsAsync` (several lines at once, all or nothing, refused if a line moved since the preview) and `IBudgetPlanService.PreviewPlanAsync`.
### Changed
- After a confirmed change, the page under the panel reloads its data.
- The evaluation set reads seeded amounts from the services instead of hard-coding them (a split position's odd cent can land on a different fund after a reseed).

## Phase 36: 2026-09-29 (assistant foundation)
### Added
- **Assistant** in the admin app's top bar: ask about the budget and actuals ("how are actuals compared to our budget so far this year?") or how to do something ("how do I print the budget book?"), and it looks the answer up, links the page, or opens it.
- **Nine read-only tools** over the Application services, run as the signed-in user, so the assistant sees exactly what the user's account can see.
- **Help topics** on every page (`[HelpTopic]`), from which the assistant finds pages the user may open.
- **Government setting:** the Administrator turns the assistant on or off; it is audited. Nothing appears unless the operator has connected a model (`Assistant:ApiKey`).
- **Security log:** every question, with the tools it used and without its text.
- **Two providers:** Anthropic (Claude) or Ollama (cloud or local), chosen by `Assistant:Provider`; both through the same SDK and interface.
- **Evaluation set** against the real model, run when the app's `Assistant:ApiKey` is set in user-secrets.
### Packages
- Microsoft.Extensions.AI.Abstractions, Microsoft.Extensions.AI, Anthropic (the official Claude SDK), and Markdig.

## Phase 35: 2026-09-29 (printable budget book)
### Added
- **Budget book:** the whole budget as one PDF: cover, contents, the budget message, the budget at a glance, a page for each fund, the departments with their narratives, and the certificate of estimated resources.
- **Optional sections,** chosen when printing: the multi-year outlook, personnel cost, every account line, and the glossary. The government's defaults decide the published book.
- **Budget message** on each budget version (heading, text, signer), written until adoption and carried into amendments and next year's budget.
- **Proposed marking:** a budget council has not adopted says "Proposed" on the cover and at the top of every page.
- **Published with the budget:** publishing keeps the book with the snapshot, and the portal links to that copy from the overview and footer.
- **Budget book page** (Tools, then Budget book, or its card on Reports).
### Changed
- The glossary's terms are one shared list for the portal and the book.
- The seeded demo's published budgets get their books after seeding; the FY2026 and FY2027 budgets carry a fictional mayor's message.

## Phase 34: 2026-09-28 (ERP partner kit)
### Added
- **ERP partner kit** in `docs/partners`: an integration guide for ERP vendors, the API as OpenAPI 3.1, the four file layouts, and a sample of every request, answer, and file.
- **HTTP adapter** (`HttpErpAdapter`) for all four exchanges: an ERP that implements the published API connects by configuration, with no code in CivicBudget.
- **ERP connections** per government from the operator's secret store (`Erp:Connections:{government id}`), checked when the app starts: HTTPS only, a key required.
- **The chart from the ERP's API:** the Chart sync page fetches the chart beside the upload, like actuals and employees. The demo's simulated ERP serves the seeded chart.
- **Reference ERP** (`samples/CivicBudget.ReferenceErp`): a small runnable server implementing the API over the demo data.
- **Contract tests:** the OpenAPI file is checked against the code field by field, and every sample file is read through the real code.
### Changed
- The ERP API is offered only to governments with a connection; the others keep the file uploads.

## Phase 33: 2026-09-28 (multi-year plan)
### Added
- **Multi-year plan** on every budget version: up to ten years, five by default, counting the budget year. Each future year has a revenue and an expenditure percentage, and any line's future year can be typed over (a project, a grant that ends).
- **Fund balances roll forward:** each fund's ending balance becomes the next year's beginning balance, and an overspent year is flagged in words, with the amount over.
- **Plan page** (Tools, then Multi-year plan): the percentages, balances by fund and year, and every line by year. Department heads plan their own lines while their request is open.
- **XLSX export** of the plan, and a card on the Reports page.
- **Portal Outlook page:** the published plan, all funds by year with the assumed changes, and each fund's ending balance.
### Changed
- Amendments copy the plan; starting next year's budget moves it on a year. Adoption locks it.
- Published snapshots store one row per fund per planned year.
- The seeded FY2027 budget plans a $250,000 Maple Street project in FY2029; FY2025 is a one-year plan.

## Phase 32: 2026-09-28 (finish accessibility)
### Changed
- **Editable amounts** on the worksheet and department pages are always outlined, so the numbers you can change are visible at a glance. The border is 3:1 against the row, and darkens on hover.
- **Grouped tables** put each group (a fund, a program, a department, a category) in its own row group, headed by a row-group header cell. That covers the worksheet, the department page, and seven reports.
- **Row cells:** the account that names a row is its header cell, styled like the others.
### Added
- **Spoken amounts:** the portal's short figures ("$3.70M") carry a spoken form ("$3.70 million").
- **"$" and "%" links** are read as "Dollars" and "Percent".
- **Table names:** every table has a name.
- **Scrolling tables:** a table that scrolls inside its card becomes a named, keyboard-reachable region wherever it overflows.
- **Loading:** a loading placeholder says "Loading" to screen readers.
- `docs/accessibility/screen-reader-checklist.md`: a 15-minute VoiceOver pass.
### Fixed
- The Add line dialog's hint is tied to the Program field.
- The conformance report now rates every WCAG 2.2 A and AA criterion as supported or not applicable, pending the VoiceOver pass.

## Phase 31: 2026-09-28 (accessibility self-scan)
### Added
- `scripts/screenshots/a11y-sweep.mjs`: axe-core (WCAG 2.2 A and AA plus best practices) on every page for every role, the sign-in pages, and the portal at desktop and phone width.
- **`docs/accessibility/ACR.md`**: the accessibility conformance report in the VPAT 2.5 WCAG format.
- **Skip to content** links on every page.
- An error summary that takes focus on every form, admin and sign-in.
- `Aria.Bool` for ARIA states.
### Fixed
- **Contrast:**
  - secondary text, green status pills, and links passing 4.5:1 on every tint (links had fallen back to Bootstrap's blue);
  - form fields now have a 3:1 border;
  - focus rings are visible on the navy bars and the sidebar.
- **Focus:**
  - the phone menu is a dialog that takes and returns focus;
  - dialogs opened from row menus give focus back to the menu;
  - focus no longer drops when a button disables itself (narrative save, pager, a refused dialog);
  - after a note, focus returns to its row.
- **Announcements:**
  - a refused amount is marked invalid and announced;
  - an edit that takes a fund over its limit, or back within it, says so;
  - the filter count is a status;
  - the idle warning is read once, not every second, and gives focus back.
- **States:** toggles and disclosure buttons now expose real `aria-expanded` and `aria-pressed` values.
- **Tips and messages:**
  - info tips can be hovered and dismissed with Escape;
  - failure and warning toasts stay until dismissed.
- **Structure:**
  - headings hold the title alone, and card titles are headings;
  - empty action headers are named;
  - the sidebar, fund panel, and narrative are named landmarks;
  - portal tables that scroll can be reached by keyboard;
  - portal panel radios are a named group;
  - the portal nav marks the current page.
- **Sign-in pages:**
  - the card title is the page's heading;
  - the illustration stops after two plays;
  - the password toggle keeps one name with a pressed state.
- The workflow stepper fits at 320 pixels.

## Phase 30: 2026-09-28 (the product site)
### Added
- **spencersmith.site/CivicBudget**, in the spencersmith.site repository:
  - a hero with the Ohio fund check;
  - the budget year as the app's own stepper;
  - department requests, the worksheet, and the portal, each with a silent clip;
  - what moves between the ERP and CivicBudget;
  - the security summary;
  - the live demo with every login;
  - FAQ, and a contact form through Formspree.
- `scripts/screenshots/site-clips.mjs`: records the site's three clips from a freshly seeded app.
- The site is reached by its address and is not listed on the portfolio's home page.

## Phase 29: 2026-09-28 (SOC 2 by design)
### Added
- **Security log** under Administration: every sign-in, wrong password, lockout, second step, recovery code, sign-out, idle sign-out, password change, export, and refused request, with the address it came from. Kept apart from the audit trail.
- **Idle sign-out**:
  - a session ends after 30 minutes without activity, with a two-minute warning and "Stay signed in";
  - "Remember me" keeps a device signed in for 14 days;
  - deactivations and role changes reach open sessions within 5 minutes.
- **Rate limits** on sign-in, two-step, and password-reset forms (per address) and exports (per user), with a plain "Too many attempts" page.
- **Security headers** on every response: a Content Security Policy that allows only this site's scripts plus a per-request nonce, no framing, nosniff, a strict referrer policy.
- **Download everything** (Government settings): every table of the government's data as CSV files in a ZIP, without passwords, two-step keys, or images.
- **`--offboard`**: export, then delete, a government that leaves. **`--maintenance`**: the daily retention job (security events and finished emails after a year).
- **CI:** NuGet audit on every restore (any known vulnerability fails the build); a `security` workflow with CodeQL and the package check on every change and weekly; Dependabot for the npm scripts.
- **`docs/security`**: a trust overview ("designed and built to achieve SOC 2 compliance"), a control matrix mapped to NIST CSF 2.0 and NIST 800-53 (GovRAMP), and nine operator policies. `SECURITY.md` for reporting vulnerabilities.
### Changed
- Sessions follow the idle and remember-me rules above, not a fixed 8 hours.
- The browser helpers moved from an inline script to `js/civicbudget.js`.
- The client address is taken from `X-Forwarded-For` behind the load balancer.

## Phase 28: 2026-09-27 (email, two-step sign-in, and onboarding)
### Added
- **Email** through a transactional outbox, with each email saved together with the change that caused it:
  - a department submitted (to Administrators and Fiscal Officers);
  - a request returned (to the department);
  - forgot password (a single-use reset link);
  - a welcome link for new users to choose their own password.
- **Delivery modes:**
  - **Email outbox** under Administration: every email the government's users were sent, readable by its Administrators, with working links. Without a mail server (the demo) emails are kept here.
  - With a mail server (`Email:Mode=Smtp`) a background sender delivers them through MailKit, woken by each save rather than a timer, and retries a refusal.
- **Forgot your password?** on the sign-in page, and reset pages that answer the same way whether or not the address has an account.
- **Two-step sign-in** with any authenticator app:
  - set up from a QR code, with ten recovery codes;
  - sign-in with a code (remember the browser for 14 days) or a recovery code.
- **Administrator controls for two-step sign-in:** require it for the whole government (Government settings), or reset it for a user who lost their phone. Every change is an audit event.
- **`--provision`**: set up a new government and its first Administrator from the command line; the Administrator is emailed a link to choose a password.
- **Getting started** under Setup: what a government still needs before its first budget, worked out from what exists, with a banner on the Overview until it is done.
- MailKit and QRCoder (both MIT).
### Changed
- Creating a user defaults to emailing them a link rather than typing a temporary password.
- Links and redirects honour `X-Forwarded-Proto` behind the load balancer; `App:PublicUrl` can set the address outright.
- The Overview shows an empty state for a government with no budget yet.

## Phase 27: 2026-09-27 (employees from the ERP, and personnel reports)
### Added
- **Employees from the ERP** under Setup: bring the payroll into a budget being prepared, from the ERP's API (simulated in the demo) or a payroll export (CSV or XLSX, one row per employee). The preview shows each employee's step (new position, vacancy filled, updated with what changed, left vacant) and every budget line that would move before anything is written.
- Positions keep the ERP's employee number; a sync refreshes what the ERP owns (pay, plans, funds) and keeps the budget's plans (raise, step increase, months, longevity, other pay). One unmatched employee refuses the whole sync. Each sync is logged and audited.
- **Position Roster**, **Personnel Cost by Fund**, and **Benefits Summary** reports, each exported to XLSX.
- Demo: the simulated ERP's payroll has moved on since the FY2027 budget started (a hire into the police vacancy, a raise, a retirement); Pine Hollow's payroll is ready for a government that sets up personnel from scratch.
### Changed
- The seed's positions and the simulated payroll come from one roster (`MapleRidgePersonnel.Roster`).
- `PositionCost` carries each fund's cost by kind and the pensionable and taxable pay behind it.

## Phase 26: 2026-09-27 (personnel budgeting)
### Added
- **Positions** for each department in a budget version, filled or vacant:
  - pay as a salary or an hourly rate (typed with a raise from a chosen month, or from a pay scale's grade and step with a step increase);
  - the months paid;
  - longevity and other pay;
  - retirement (with an optional employer pick-up of the employee's share);
  - insurance by plan and tier;
  - the funds that pay for it.
- A personnel page per department: positions, a live cost breakdown as a position is edited, and the budget lines the positions add up to.
- **Calculated lines**: salary and benefit lines are the sum of their positions, show "from N positions", and cannot be typed over, removed, or changed by an import.
- **Personnel settings** per fiscal year under Setup:
  - retirement systems (OPERS, OPERS law enforcement, OP&F police and fire, Social Security);
  - Medicare and the BWC workers' compensation rate;
  - insurance premiums by tier less the employee share;
  - kinds of extra pay (amount, hours at a multiple of the rate, or a percent of base pay; pensionable and taxable flags);
  - longevity (step table, percentage of pay, or amount per year of service, read back in plain English with a worked example);
  - pay scales.
- Saving a year's settings reprices that year's open budgets; a change that would leave a position unpriceable is refused with the positions named.
- Amendments copy positions; starting next year's budget carries them forward at the rate each ends the year.
- Demo: 16 positions in Maple Ridge's FY2027 draft (Police, Finance, Streets & Service split between two funds).
### Changed
- `BudgetLine.PositionCount`; the migration test allows the named percentage and hours columns (`decimal(9,4)`).
- The role sweep opens the personnel pages too.

## Phase 25: 2026-09-27 (reports the ERP cannot produce alone)
### Added
- **Budget vs. Actual**: each appropriation against the ERP's spending and encumbrances, what is left, a bar with a tick for the year's pace, and last year at the same point.
- **Revenue vs. Receipts**: each estimate against what has come in, measured against how much of last year's total had arrived by the same month.
- **Projected Fund Balances**: budgeted against projected year-end balances, each line projected from last year's monthly pattern, committed money counted as spent.
- **Multi-year Trends**: each fund's budgets and actuals year by year up to the budget.
- **Appropriation Measure** (ORC 5705.38): by fund and department, with personal services set out separately and transfers out beside the departments; its columns are report settings (default: personal services and fringe benefits).
- The reports index grouped into the budget, legal documents, and reports against the ERP's books. Every new report exports to XLSX.
### Changed
- Report settings has a second editor for the appropriation measure's columns; the column rules are shared (`ReportColumnRules`).

## Phase 24: 2026-09-27 (report settings and the certificate of estimated resources)
### Added
- **Certificate of Estimated Resources** (ORC 5705.36) for any budget version, numbered with it ("Amended Certificate No. 1"): as issued and as a detailed schedule, one row per fund with fund-type subtotals and a grand total.
- Balances from the ERP's closed prior year (cash less carried encumbrances, reserves, and nonspendable balances, plus or minus unpaid advances), or the budget's estimate before that year closes.
- Reconciliations on the certificate: appropriations within each fund's total, beginning balances equal to the certified carryover, every column mapped, columns adding to the budget's revenue; an amended certificate lists the revenue estimates that moved and why.
- PDF (landscape Letter, signature lines for the budget commission and the preparer), XLSX, and print.
- **Report settings** under Setup: the certificate's revenue columns as named sets of accounts (up to four, each account in one), the county, the preparer, and the column headings; defaults to one "Taxes" column of the accounts categorized as taxes.
- Reserves and advances per fund and year, entered from the certificate.
- PDFsharp-MigraDoc (MIT) and the Source Sans 3 typeface (OFL), embedded.
### Fixed
- A subtotal label pinned on a phone lost its shading.

## Phase 23: 2026-09-27 (send the budget to VIP)
### Added
- **Send to ERP** on the latest adopted version: a page that shows exactly what the budget journal posts (only what changed since VIP last took the year), takes the journal's description and posting date once, and sends it by API or as an import file (`Account, Amount, Description, Date`).
- Every send is kept: posted, refused (with each refused account and VIP's reason), no answer, waiting for import, imported, or discarded, and each is an audit event on the version.
- A send with no answer is retried under the same id and VIP recognizes it; an import file counts once someone confirms it was loaded. One unfinished send per year, enforced in the service and by a filtered unique index.
- The simulated ERP accepts journals on the accounts in its own chart and refuses the whole journal otherwise; the seed records the FY2025 and FY2026 originals as already sent, so the demo opens on the FY2026 amendment's two changes.
- `CanSendToErp` policy (Administrator, Fiscal Officer).
### Changed
- The app names no ERP vendor: "Send to ERP", "ERP (simulated)", and "the ERP" in messages, including the actuals sync from Phase 22. The simulators are `SimulatedErpActualsApi` and `SimulatedErpBudgetApi`.

## Phase 22: 2026-09-27 (actuals from VIP)
### Added
- **Actuals sync** under Setup: fetch a fiscal year's books from VIP, or upload an export (`Fiscal Year, Type, Account, Period, Amount`), preview receipts, disbursements, open encumbrances, and cash by fund, and apply. A sync replaces the year; one unknown account code refuses the whole file.
- A closed year fills the prior-year actuals of the open budget two years on (audited, listed in the preview); starting a budget and adding a line take them from the ERP too.
- Department pages show this year's spending under each line's current budget, with a bar that turns amber past the budget; the line drawer adds open encumbrances.
- A simulated ERP for development and the live demo, keeping the demo governments' fictional books by month; the seed starts with FY2025 and FY2026 already synced.
### Fixed
- A long fund name in a department grid's total row forced the table wider than the screen.

## Phase 21: 2026-09-27 (show password)
### Added
- A show/hide button inside every password field: sign-in, change password, and the temporary password fields on the user form (`PasswordInput`). It is labelled for screen readers, reports its state with `aria-pressed`, and stays hidden without script.

## Phase 20: 2026-09-24 (README screenshots)
### Added
- The user form shows what the selected role can and cannot see and do, and updates as the role changes.
### Changed
- README screenshots retaken from fresh seed data: budget entry, a department's request, the portal, user access, four phone screens, and seven more under "More screens". `capture.mjs` finds every page itself and needs only the demo password.
- Audit values that are amounts show with thousands separators in the activity feed and line history.
### Fixed
- Portal pages on a phone started their first heading against the header: a phone padding shorthand also zeroed the main area's top padding.
- A fieldset's legend (Departments this user may edit, Account number format) rendered at heading size.

## Phase 19: 2026-09-24 (documentation and comments)
### Changed
- README rewritten for a first-time reader: what the app is, the live demo and its logins, how it is built, the problems worth reading about, and how to read the repository.
- ARCHITECTURE, SPEC, DECISIONS, the walkthroughs, interview prep, and the demo script brought up to date with the code and written in the first person. ADRs are in numeric order, and decisions that later changed are amended in place.
- Every code comment reread against its code: stale statements fixed (a self-contradicting index comment, a "placeholder" that was not one, a removed health endpoint), phase numbers removed, and a "why" added where a reader would ask.
### Fixed
- Import commit crashed on codes written with leading zeros (`04901`) that the preview had matched; analysed rows now carry the matched ids.
- Validator messages for negative comparatives on a new line.

## Phase 18: 2026-09-24 (budget rules and known gaps)
### Added
- **Start the budget** on the fiscal years page: an open year with no budget starts empty or from last year's latest adopted version, changed by a percentage (every line, appropriations only, or revenue estimates only; optionally rounded to whole dollars). Last year's adopted amount becomes the comparative, each fund begins with last year's projected ending balance, and lines on retired codes are listed, not silently dropped.
- Optimistic concurrency on budget versions (`Revision`): a save based on a stale read is refused with "someone else changed this budget" instead of overwriting.
### Changed
- Only the latest adopted version of a year can be published.
- A department's total is its expenditure appropriations everywhere (researched: ORC 5705.38(C), the UAN chart's Other Financing Uses program, Michigan's uniform chart); its revenue and transfers are shown beside it, outside the total. The workspace's by-department view used to add revenue in, and the department page transfers out.
- Every amount on a line is positive, for revenues and expenditures alike, enforced in the domain and in every import column.
- Tab stays inside an open dialog or drawer.
- The ECR repository moved to the one-time AWS stack so a first AWS deploy can push.
- Integration tests restore a seeded template per test: 1m40s to 40s.
- GitHub Actions pinned by commit SHA; CI's Bicep pinned by version and sha256.

## Phase 17: 2026-09-23 (maintenance)
A review of the whole codebase (one layer at a time, each finding checked against the code) and a walk through the running app as every demo user. About 90 fixes; the headline ones:
### Security
- Sign-in no longer follows a return URL of `//other-host` (`LocalUrl`); a temporary password cannot be "changed" to itself; exports (`*.xlsx`) no longer skip the forced password change; uploaded images must be PNG, JPEG, or WebP whose bytes match (no SVG), capped at the stored size, and are served with `nosniff` and a sandboxing CSP; CSV text that a spreadsheet would run as a formula is written as text.
- Setup services (funds, accounts, departments, fiscal years, settings) check the caller's role themselves; every admin page is pinned to its policy by a test.
- The portal cache varies only on the query keys pages read, so it cannot be bypassed with junk parameters; the anonymous `/health/ready`, which queried the database per call, is removed.
### Fixed
- Admin pages that did not refresh: Add line, deactivate/lock/close on five lists, Start an amendment (the page kept showing the adopted budget), and phone cards that ignored the pager.
- An amount cell keeps showing a refused value; amounts past `decimal(18,2)` crashed the save; the settings preview crashed on a negative width; an admin page error killed the connection with no message (now an error boundary and the error bar).
- ERP chart sync could retype an account budget lines use; closed fiscal years accepted amendments; audit text could exceed its column after the action succeeded; import rejected its own zero-padded export and read `1234,56` as 123,456.
- Changing a government's public address stranded its published budgets on the old one; portal not-found pages were empty; search and year pages answered 200 for unknown years.
- A failed database wake-up check left the site on the waiting screen until restart.
- Owned values (the account number format) were never audited.
### Changed
- Times show in Eastern time with the zone named; amounts format as en-US whatever the server culture.
- Portal and sign-in pages load no JavaScript; the portal loads a budget once per request (the funds page made about 54 queries).
- Indexes: fund-level lines unique per version, one active snapshot per year, recent activity by government and time.
- Deploys wait for CI and check the image before pushing; the image is published for its platform (about 64 MB smaller on disk); NuGet is cached in CI.
- Accessibility: contrast, focus returned from dialogs and drawers, one h1 per page, error toasts that stay, linked charts that screen readers can use.

## Phase 16: 2026-09-21 (v1.1)
### Fixed
- A database that paused while the container stayed up (an open admin tab can keep it running) no longer hangs the next page load: after 55 minutes without a page request, the next one checks the database first and shows the waiting screen if it is asleep (`DatabaseWaker`).
- The waiting screen now actually appears on a cold start. Data Protection reads its key ring during host startup and the keys live in SQL Server, so on a resuming database EF retried that read for about fifty seconds before Kestrel ever started listening. The key ring is loaded lazily instead (`DataProtectionStartup.DeferKeyRingLoad`); against a database address that hangs, time to the first page went from 31 seconds to 1.
### Changed
- Cold start: the host listens as soon as the process is up and prepares the database behind it (`DatabaseStartupService`). Until it is ready, every page request gets a waiting screen in the app's style (503 with `Retry-After`) that counts the seconds and continues to the requested page on its own; `/health/startup` is the in-memory check it polls. The Azure startup probe checks every two seconds instead of ten. First paint after an idle hour drops from about 65 seconds to about 20; the site itself still appears at about 65 while serverless SQL resumes.

## Phase 15: 2026-09-21 (v1.1)
### Added
- Phones: list pages (funds, departments, chart of accounts, fiscal years, users, budget versions, publishing history, chart sync history, department board, overview) render as cards (`ListCard`); working grids keep three columns with a chevron that opens a bottom sheet carrying every figure, the editable amount, the note, and history (`LineDetail`); Fund Summary shows a certificate card per fund and Department Detail a card per department. Tables remain on wider screens and in print.

## Phase 14: 2026-09-21 (v1.1)
### Fixed
- Phones: the budget workspace and department pages no longer force Safari to zoom out. Grid columns can shrink below their content (`minmax(0, 1fr)`), hidden tooltips leave the layout, the settings page's limit-mode options are shorter, and toolbar filters go full width under 576px.
### Added
- Phones: data grids pin their first column while scrolling sideways; `scripts/screenshots/mobile-sweep.mjs` checks every route for horizontal overflow.

## Phase 13: 2026-09-20 (v1.1)
### Added
- Azure hosting for the live demo: `infra/azure/main.bicep` (serverless Azure SQL under the free offer, Container App on the consumption plan, nightly reset job, capped Log Analytics), `scripts/azure-setup.sh` (one-time create plus GitHub OIDC wiring), `.github/workflows/deploy-azure.yml` (image to GHCR and roll on every push to `main`), and a `bicep-build` CI job.
- `dotnet CivicBudget.Web.dll --reseed`: drops every table, migrates, and seeds (`DatabaseInitializer.ResetAsync`); the demo runs it nightly.
- README "Live demo" section with the demo logins.
### Changed
- The database initializer waits up to two minutes for SQL Server (a paused serverless database resumes in about one).

## Phase 12: 2026-09-20 (v1.1)
### Added
- Portal header shows the CivicBudget mark, or the government's own logo uploaded under Government settings (`GovernmentLogos`, migration `AddGovernmentLogos`, served at `/transparency/{slug}/logo`).
- Glossary page (`/transparency/{slug}/{year}/glossary`) with the accessibility statement.
- Overview: "Where does the money go?" and "Where does it come from?" are two panels on a sliding track switched by tabs, still without JavaScript.
### Changed
- Overview: the resolution, published date, and version line moved from the top to the foot of the page; "every line of the adopted budget" removed.
- Portal footer is one line: budget, downloads, glossary, accessibility.

## Phase 11: 2026-09-20 (v1.1)
### Changed
- Front door and sign-in page read like a product, not a project: one centered headline on the navy half, "Sign in to view and enter data" and a link to the public portal on the other. The portfolio, demo, stack, and "fictional data" copy is gone from the app (the README still says it); the redundant "Sign in" link left the public header.

## Phase 10: 2026-09-20 (v1.1)
### Added
- A CivicBudget logo mark (civic building on a teal tile) as inline SVG and favicon.
- Profile pictures: upload on **Profile picture** (account menu), resized in the browser to 256 px, stored in `UserAvatars`, shown in the top bar, user list, recent activity, and line history; administrators can remove a user's picture. Migration `AddUserAvatars`.
- `[NotAudited]` for entity properties whose change is already a named audit event.
### Changed
- The admin sidebar shows the government's name beside the mark instead of the product name.
- Department request submit/return no longer write id and timestamp field changes to the activity feed.

## Phase 9d: 2026-09-19 (v1.1)
### Added
- Department-first budgeting: sign-in lands in the app, and a department user's home is **My department**, which opens their department for the budget in progress (or a board of their departments).
- Department entry page: every account of the department across its funds as full numbers with prior actual, current budget, request, and change; fund subtotals and a running department total; revenue credited to the department shown apart; a narrative editor; Submit to the fiscal officer.
- Per-department request status on a version (in progress / submitted / returned). Submitting locks the department's lines and narrative for department users; the fiscal officer can return a request with a note. Both are audited.
- Department board (`/admin/budgets/{id}/departments`) showing every department's status and totals, with Return; a chip per department above the workspace grid.
- The department narrative prints on the Department Budget Detail report and on the public portal ("From the department"); snapshots freeze it (`PublishedBudgetSnapshotDepartments`).
### Changed
- Seed: FY2026 narratives reach the portal; FY2027 is mid-round (Police submitted, Parks returned). Reseed with `docker compose down -v && docker compose up -d`.
- Migration `AddDepartmentRequests`.

## Phase 9c: 2026-09-19 (v1.1)
### Changed
- Administrator has complete access: everything the Fiscal Officer can do plus users and settings.
- Role names shown as Administrator, Fiscal Officer, Department User, Viewer.
- A department user's audit trail (recent activity, line history) is limited to their own departments' lines.
### Added
- Temporary passwords: accounts created or reset by an administrator must change their password at the next sign-in (`MustChangePassword`, claim, middleware).
- User administration is audited (create, update, reset, lock, unlock).

## Phase 9b: 2026-09-19 (v1.1)
### Added
- Chart of accounts sync from the parent ERP: upload its export, preview adds/updates/deactivations, apply; sync log with a change drawer; audit event. Never deletes.
- `Government.ChartSource` (Local/Erp): under Erp the setup screens are read-only with a "Managed by the ERP" banner and the services refuse writes; an Administrator can switch back.
- `IErpChartSource` adapter boundary (ADR-0025) with a file-based first implementation.

## Phase 9a: 2026-09-19 (v1.1)
### Added
- Full Ohio-style account numbers (`1000-725-121`, `1000-110` for revenue) composed under a per-government `AccountNumberFormat` (widths, separator, Program/Department), shown in grids, reports, exports, the portal, and searchable everywhere; import accepts an `Account Number` column.
- Government settings: account number format with a live example.
### Changed
- Seed department codes are UAN program numbers; Pine Hollow demonstrates a dotted county-style format. Reseed with `docker compose down -v && docker compose up -d`.
- Published snapshot lines store the composed number (`AddAccountNumberFormat` migration backfills).

## Phase 8: 2026-09-18
### Changed
- Explanatory subtitles removed across the admin app; the useful ones are info tips beside titles and KPI labels. Portal section leads trimmed.
- Overview: budget versions table no longer overflows into the activity card.
- Accessibility: named account menu and brand links, disambiguated amount input labels, dialogs and drawer take focus on open, styled access-denied page.
- Account grid Export button now downloads the XLSX (was a placeholder).
### Added
- README screenshots and the Playwright script that regenerates them; demo script; spec status table; walkthrough 09.

## Phase 7: 2026-09-18
### Added
- `Dockerfile` (multi-stage, non-root, healthcheck) and `docker-compose.full.yml` for a one-command containerized demo.
- AWS CDK app in C# (`infra/CivicBudget.Infra`): VPC, ECR, RDS SQL Server Express, Secrets Manager, Fargate service behind an ALB, CloudWatch logs, Budgets alarm; a one-time GitHub OIDC stack; 17 assertion tests; `cdk synth` and Docker build in CI; gated `deploy.yml`.
- `DatabaseOptions`: connection string composed from `Database:*` settings so ECS can inject the RDS-managed password; `MigrateOnStartup` and `SeedDemoData` switches.
- Data Protection keys persisted in SQL Server (`DataProtectionKeys` table) so cookies survive container restarts.
- 404 tests total (+17).

## Phase 6: 2026-09-18
### Added
- Import of budget lines from CSV or XLSX with a validation preview (per-row Add/Update/Unchanged/Error), committed through the aggregate in one save with an audit event; never deletes.
- XLSX export of the workspace lines (same layout as the import), the three reports, and the setup lists.
- Reports: Budget Summary by Fund, Department Budget Detail (filter by department), Revenue vs. Expenditure by Category; on screen, printable, and as XLSX.
- `CsvReader`, `ISpreadsheetReader` (ClosedXML), `Labels` for plain-language enum names.
- 387 tests total (+40).

## Phase 5: 2026-09-18
### Added
- Public transparency portal at `/transparency/{slug}/{year?}`: overview with KPIs, where it goes, where it comes from, funds, fund and department drill-downs, year over year, search. Static SSR, no login, no JavaScript.
- `ISnapshotQueryService` read model over the read-only portal context; `Breakdown` bar chart with `$ | %` toggle and table twin.
- CSV and XLSX downloads of every published line (ClosedXML added).
- Output caching for portal pages with eviction by government tag on publish/unpublish; portal responses are `public, max-age=600` with no antiforgery cookie (ADR-0021).
- 347 tests total (+55).
### Changed
- Both DbContexts use split queries for multi-collection includes.

## Phase 4.5: 2026-09-18
### Added
- Design research (Ohio vendors, admin budgeting UIs, transparency portals), design brief, and eight approved mockups under `docs/design/`.
- Theme: design tokens as CSS variables over Bootstrap; Bootstrap Icons vendored.
- Admin shell with dark module sidebar, breadcrumb top bar, user menu, and off-canvas navigation on small screens.
- Components: page header, status pills, workflow stepper, KPI cards, toasts, restyled confirm dialog, side drawer, row menus, empty and skeleton states.
### Changed
- Every admin screen restyled: workspace with fund rail and grouped worksheet, overview with budget KPIs and activity, all lists, users, settings, login, home, account pages.
- `window.confirm` removed; outcomes reported with toasts.

## Phase 4: 2026-09-17
### Added
- Workflow: Propose, Return to draft, Adopt with resolution number; Ohio appropriation limit enforced at transitions (Block refuses, Warn requires acknowledgement); audit events per transition.
- Amendments: new draft copied from an adopted version with a reason; adoption supersedes the prior version.
- Publishing: immutable `PublishedBudgetSnapshot` with denormalized lines and funds; publish, unpublish, republish with history; `PublicPortalDbContext` read-only over the three snapshot tables.
- `ConfirmDialog` component and the workflow bar; publishing history on the version list.
- 292 tests total (+19).

## Phase 3: 2026-09-17
### Added
- Budget entry: version list, workspace with by-department and by-account-line modes, inline editing, add/remove lines, justification notes.
- Fund balance panel with estimated resources, appropriations, projected ending balance, and the appropriation limit in Warn/Block severity; Finance Director edits beginning balances inline.
- Audit trail: `AuditInterceptor` records create/delete/field changes for `[Audited]` entities with user and UTC time; per-line history view; `AddAuditTrail` migration.
- 273 tests total (+25).
### Changed
- Domain entity keys declared `ValueGeneratedNever` so aggregates can add children through their own methods (ADR-0018).
- Fixed ports (5001/5000) and readable console logging in Development; `DatabaseInitializer` waits for SQL Server.
- Dependabot: EF Core 10.0.12, GitHub Actions majors.

## Phase 2: 2026-09-16
### Added
- ASP.NET Core Identity with cookie sign-in; `ApplicationUser` (government, display name, department assignments); login, logout, profile, and change-password pages; lockout after 5 failed attempts.
- Claims-based tenant resolution: `CurrentUserContext` filled per HTTP request and per circuit.
- Roles (Admin, Finance Director, Department Head, Viewer), nine named policies, resource-based budget line edit rule.
- Application layer: `Result`, FluentValidation validators, `ICivicBudgetDbContext`, setup services, user administration contract.
- Admin area (Bootstrap + QuickGrid): overview, funds, departments, chart of accounts, fiscal years, users, government settings.
- Demo users (one per role) seeded with a password from user-secrets.
- 248 tests total (+69).
### Changed
- Code comments use plain punctuation (no em dashes); seed account names use hyphens.

## Phase 1: 2026-09-16
### Added
- Solution scaffold: Domain / Application / Infrastructure / Web + four test projects; central package management; analyzers with warnings-as-errors.
- Domain model: Government, Fund, Department, Account, FiscalYear, BudgetVersion (Draft → Proposed → Adopted, amendments), BudgetLine, FundBeginningBalance; `FundBalanceCalculator`, `AppropriationLimitCheck`, `Money` rounding.
- EF Core: `CivicBudgetDbContext`, entity configurations, `decimal(18,2)` convention, `InitialCreate` migration, tenant query filters, `TenantSaveChangesInterceptor`.
- Seed data for Village of Maple Ridge (3 fiscal years, 4 versions, 380 lines) and Pine Hollow Township.
- `docker-compose.yml` (SQL Server 2022), `scripts/dev-setup.sh`, health checks, JSON console logging.
- GitHub Actions `ci.yml`, PR template, Dependabot.
- 179 tests: domain, architecture, bUnit smoke, Testcontainers integration.

## Phase 0: 2026-09-15
### Added
- Functional specification (`docs/SPEC.md`), architecture (`docs/ARCHITECTURE.md`),
  decision records ADR-0001…0011 (`docs/DECISIONS.md`), roadmap, `CLAUDE.md`.
