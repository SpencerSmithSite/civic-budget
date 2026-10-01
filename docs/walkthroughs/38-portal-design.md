# Walkthrough 38: The portal's new look

The transparency portal is what a resident sees, and until now it looked like a well-behaved report:
a small heading, a gray paragraph, then charts and tables of equal weight down a white column. The
numbers were right and nothing said where to start. The product site (spencersmith.site/CivicBudget)
had just been reworked so a buyer can skim it, and a resident skims even more, so I brought the
same design language to the portal. The decisions are in ADR-0050.

Nothing about what the portal shows changed: the same snapshot, the same queries, the same pages, and
still no JavaScript. This phase is markup and CSS, plus two small components.

## 1. Every page opens the same way

`PortalHero` (in `Components/Portal/Common`) is the top of every portal page: a navy band that
continues the header, holding the breadcrumbs, an optional eyebrow, the page's one `h1`, a short
lead, and actions.

| Page | Eyebrow | Actions |
|---|---|---|
| Overview | (none) | Read the budget book, Download the numbers; the year pills on the side |
| A fund | "Special Revenue fund 2011" | (none) |
| A department | its fund's code and name | (none) |
| Search | (none) | the search box itself |

Putting the title inside a component looked risky for accessibility (every page must keep exactly
one `h1`), so `PortalHeroTests` checks that the hero renders one, with the breadcrumbs marked
`aria-current` on the last item. `LeadText` is for leads that may be missing, like a fund with no
description: the hero draws nothing rather than an empty paragraph.

## 2. Edge to edge from inside a centered column

The portal's `<main>` is a centered column (`.pt-in`), so anything inside it stops at the column's
edges. The band needs to reach the window's edges. I could have restructured the layout so each page
owned full-width sections, but then every page would carry its own column wrapper, and one that
forgot would break.

Instead the band paints its own sides:

```css
.pt-hero {
    background: var(--cb-sidebar);
    box-shadow: 0 0 0 100vmax var(--cb-sidebar);
    clip-path: inset(0 -100vmax);
}
```

The shadow spreads the band's color far past its box in every direction, and the clip keeps it to the
band's own height while letting it run sideways. A shadow never counts toward scrollable overflow,
which matters on phones: if anything is wider than the screen, Safari zooms the whole page out. The
phone sweep reports a scroll width of 390 on a 390px screen for every portal page.

## 3. Overlap only where it means something

On the product site I first layered and tilted almost everything, and it was noise. The rule I kept
is that an overlap says "this sits on top, read it first", so only things that earn that get it. On
the portal that is the three headline figures (revenues, expenditures, the projected ending balance)
on the overview and on each fund. The hero takes `Overlap="true"`, which leaves room at its bottom,
and the figures' row (`.pt-kpis--lift`) moves up into that room with a negative margin. Everything
else sits flat.

## 4. Cards, icons, and a footer

- **Cards.** Each chart and table is a white `.pt-card` on the light canvas, so a section reads as
  one thing. A wide table inside a card could have scrolled for the sake of one long heading, so
  portal table headings may wrap now (`table.pt-grid` heads only; the admin grids are untouched).
- **Icons.** `PortalIcon` draws inline SVG paths by name (in, out, balance, funds, outlook, and so
  on). Every icon is `aria-hidden` and sits beside text that says the same thing, so a screen reader
  loses nothing. They mark the headline figures, the adoption details, and the overview's new
  "Explore the budget" cards, which say what each section answers ("Where it goes", "Where it comes
  from") instead of leaving that to the navigation bar.
- **Footer.** Navy, closing the page the way the header opens it. The page is always light; there
  is no dark scheme, matching the product site.

## 5. One small piece of plumbing

The overview's explore grid ends with "Ask a question" when the portal takes questions, and with
"Search" when it does not. The layout already works out whether the portal takes questions (it shows
the link in the header), so it passes the answer down as a cascading value named `PortalAskable`
rather than each page asking the service again:

```razor
<CascadingValue Name="PortalAskable" Value="askable">
    @Body
</CascadingValue>
```

## 6. How I checked it

- Every portal page at 1440 and 390 pixels wide: no sideways scroll, and axe finds nothing.
- The project's sweeps: `scripts/screenshots/a11y-sweep.mjs` (253 pages, as every demo user, no
  findings) and `scripts/screenshots/mobile-sweep.mjs` (no overflow).
- The README's portal screenshots were retaken after a reseed.

## 7. Trying it

```bash
docker compose up -d
dotnet run --project src/CivicBudget.Web
```

Then open http://localhost:5000/transparency/maple-ridge-oh/2026, and narrow the window to phone
width to see the hero, the lifted figures, and the cards stack.
