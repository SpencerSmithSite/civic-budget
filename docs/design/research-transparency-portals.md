# Research: budget transparency portals, Ohio emphasis (2026-09-18)

Compiled by browsing live portals and vendor pages. Live pages were visually
confirmed; items marked "could not verify" were not.

## 1. Products, Ohio usage, live examples

| Product | Vendor | Ohio usage (verified) | Live example |
|---|---|---|---|
| Ohio Checkbook | Ohio OBM and Treasurer of State (powered by OpenGov 2014-2019, now state-run) | 1,100+ local entities; verified Hudson, Dublin (2018), Stow, Norwood | https://checkbook.ohio.gov/ ; local: https://checkbook.ohio.gov/local/citiesvillages.aspx?municipality=City%20of%20Hudson |
| OpenGov Transparency | OpenGov | Dayton, Fairfield, Maumee | https://daytonoh.opengov.com/transparency |
| ClearGov Transparency Center | ClearGov | Washington Court House (Feb 2026); free auto-profiles for every OH entity (Hudson profile is stale 2018 Auditor data) | https://cleargov.com/ohio/summit/city/hudson/2018/expenditures?breakdowntype=department&objectid=4981 |
| ClearGov Digital Budget Book | ClearGov | Dublin, OH (2024, 2026 budgets) | https://city-dublin-oh-cleardoc.cleargov.com/9910/292206/d |
| ClearGov Financial Engage | ClearGov | Cleveland "Interactive Budget Portal" | https://clevelandoh.financial-engage.com/home |
| Balancing Act | Polco | Worthington's page still links it, but the site now returns 404 (retired) | https://us.abalancingact.com/federal-budget-simulator |
| Tyler Socrata Open Finance / Open Budget | Tyler Technologies | Cincinnati uses Tyler Data and Insights for open data; a dedicated Open Budget module could not be verified | https://moreheadcitync.budget.socrata.com/#!/year/2027/operating/0/segment2 (NC) |
| Questica OpenBook | Euna Solutions | No Ohio deployment verified | https://cityofcorinth.openbook.questica.com/ (TX) |

Columbus publishes PDF budgets; no interactive citizen portal verified. Westerville: could not verify.

## 2. Design descriptions

### Ohio Checkbook
- Palette: navy (#0d4b8b / #26456e) header, a red accent rule, white cards on light grey, muted
  navy/grey chart fills. Tagline "Putting government transparency at your fingertips."
- Overview: three-column card grid; each card has an uppercase title, a huge KPI ("$116B"), a navy
  "FY 2027 YTD total" pill, one chart, and "Expand This Chart." Charts: labeled horizontal bars,
  stacked bars by year, and a pie with 8+ unlabeled grey slices and no legend (an anti-pattern).
- Drill-down: quick-link tile grid with icons; local governments via hamburger menu > A-Z list >
  entity page. Local pages are embedded Tableau Server dashboards with a Parameters/Filters sidebar
  ("Broken Down By: Fund", "View As: Bar", "Show Top Values: 4"): clunky, slow, not responsive.
- Search/download: entity search box (shows a validation error before input); bulk downloads on
  the DataOhio Portal.
- Accessibility signals: footer glossary, ORC citation, contact email; Tableau embeds and reCAPTCHA
  are poor for screen readers and keyboards.

### OpenGov Transparency (Dayton)
- Layout: persistent left sidebar (city logo, report list, Filters | Views tabs). Content: title,
  "Updated On" date, Back / History / Reset toolbar, "Broken down by Types" with filter pills, five
  chart-type toggles (table, stacked area, line, pie, bar), "Sort Large to Small."
- Plain language: saved views phrased as questions ("How much income tax revenue was budgeted and
  received for this year?").
- Charts: categorical palette (blue, light blue, orange, tan, purple, pink, teal, green); hatched
  fill distinguishes budget vs actual; legend truncates long names and collapses to "More (13
  grouped)." Otherwise color-only encoding.
- Table: always present below the chart ("Data", expandable rows). Share to social/email; download
  as image or CSV.
- Mobile: the sidebar does not collapse at 375px; the chart becomes about 1,700px tall with 27
  y-ticks; horizontal overflow. Weak.

### ClearGov Transparency Center (Hudson, Washington Court House)
- Palette: ClearGov blue (#1e5aa8) nav, green tab underline, bright chart blue, a
  green/orange/purple/yellow/navy/teal donut. Serif entity name on a map hero.
- Overview: tabs Overview | Revenues | Expenditures | Demographics | Debt. A Demographic Snapshot
  with icon, big number, and percent change. Each card has a title, SHARE, and a "Data source ...
  (Data through 2026)" caption.
- Expenditures: year pill selector, Fund dropdown, By Department / By Object toggle, a giant total,
  chart tabs Pie | Bar | Mountain, a donut with a name + percent legend. Then one row per department:
  uppercase name, green total, a benchmark ("15% more than similar towns", arrow plus text), a
  six-year mini bar chart, View Breakdown and View Analysis.
- Gaps: free profiles lag years; a sticky Back to Top; a Google Translate widget.
- Digital Budget Book (Dublin): left table of contents, PDF / Share tabs, page thumbnails, justified
  body text, inline photos and charts. Marketing claims "ADA-optimized"; could not verify.
- Financial Engage (Cleveland): story page with hero photo, narrative priorities, chart placeholders,
  a subscribe form, a PDF download, an accessibility statement in the footer.

### Tyler Socrata Open Budget (Morehead City, NC): the strongest reference pattern
- Overview: hero with a mission statement, "Common Questions" dropdown, search; "Financial Summary"
  cards: "Revenue Budget $42.66 Million, 2027 Projected Revenues, one-line description, Explore."
- Drill-down page: header with the total, year selector and period; tabs "Where's it Going?" /
  "How's it Funded?"; "Operating Budget broken down by Function"; a hint banner "Select a segment on
  the chart to explore details"; a left rail records the drill path and acts as a breadcrumb; Back.
- Charts: horizontal bars, light blue for revised budget and navy for actuals, labeled axes;
  toggles Snapshot / Pie / Over Time; "Show As $ | %"; sort dropdown. The data table is always
  shown (Function, Revised Budget, %, Actuals, %, Total). Export, Share, View and Download.
- Palette: dark slate header, light grey body, two-tone blue; restrained.

### Questica OpenBook (Corinth, TX)
- Card grid: "$59.1 Million, Revenue Budget/Actual, description, date." Detail: left panel (Budget
  Year, Current Year Budget in blue / Actual in red swatches), tabs Amount | Percentage | Summary |
  History, Search Budget, Sort By, grouped horizontal bars by fund category. Footer: Data Export and
  Accessibility Statement on every page. Vendor claims WCAG AA/508.

### Balancing Act (Polco)
- A sticky "Budget Deficit" bar with a draggable marker, a "Where the Money Goes" donut with about
  14 rainbow slices and no labels (color-only), then stacked bars and per-category sliders. Strong
  engagement idea, weak chart accessibility; renders poorly at 800px.

## 3. Screenshot and image URLs
- ClearGov transparency marketing: https://cleargov.com/assets/Financial-Visualizations-Mobile.webp ,
  https://cleargov.com/assets/Feedback-Manager-Mobile.webp
- ClearGov Digital Budget Book: https://cleargov.com/assets/DBB-web-mobile-PDF-versions-mobile-1.webp ,
  https://cleargov.com/assets/DBB-chart-builder-mobile-1.webp
- Ohio Checkbook logo: https://checkbook.ohio.gov/Images/Logos/Checkbook-main-logo-2.svg
- Tyler Open Finance showcase: https://tylertech.data.socrata.com/stories/s/Tyler-Open-Finance-Showcase/4dza-id9a/
- OpenGov, Tyler, and Euna marketing pages returned 403/404 or placeholders; the live pages above
  are the best screenshots.

## 4. Takeaways for CivicBudget's portal
1. Lead with two to four KPI cards, not a chart wall. Strong portals open with "$X Million, FY,
   one-sentence description, Explore" (Socrata, OpenBook). Ohio Checkbook's six-chart grid is busier
   and less readable.
2. Phrase navigation as citizen questions: "Where's it going?" / "How's it funded?" / "What changed
   from last year?" (Socrata tabs, OpenGov saved views).
3. Horizontal bars beat pies. Use sorted horizontal bars with direct value labels and a "Show as
   $ | %" toggle; if a donut is offered, cap at six or seven slices plus "Other" with a name + percent
   legend. Never ship an unlabeled grey pie or a 14-slice rainbow.
4. Every chart gets a table twin and a CSV. Socrata and OpenGov render the data table below the
   chart by default; OpenBook and Socrata keep "Data Export" in a persistent footer. This is also the
   WCAG 1.1.1 / 1.3.1 answer.
5. Drill-down = breadcrumb + "broken down by" selector + Back: Total > Fund > Department > Object
   with a visible path rail (Socrata) or pills (OpenGov). Avoid Tableau-style parameter sidebars.
6. Do not rely on color alone. Encode budget vs actual with hue and pattern or position (OpenGov
   hatching, Socrata light/dark pairing); deltas with arrow, sign, and text. Target 4.5:1 text and
   3:1 chart-fill contrast.
7. Mobile-first means the sidebar collapses. Stack KPI cards, collapse nav into a sheet, cap chart
   height, prefer bars over wide time series on phones.
8. Trust chrome: "Data through ... / Updated on" captions, a data-source line, a glossary,
   plain-language "What is a fund?" tooltips, a contact link. Cheap and credible.
9. Small-government differentiators: a prior-year delta per department, fast static-first
   rendering (no iframes or reCAPTCHA), an accessibility statement in the footer, one lightweight
   engagement hook (a question box).
10. A downloadable budget-book view: Ohio cities value GFOA presentation (Dublin's awards). A
    print-friendly narrative page with a table of contents and PDF export mirrors ClearGov's Digital
    Budget Book without a heavy CMS.
