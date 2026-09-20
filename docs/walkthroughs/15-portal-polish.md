# Walkthrough 15 — Portal polish

Phase 12. Spencer's review of the transparency portal: a logo instead of
initials (the government's own if it uploads one), less text at the top of
the overview, the two breakdowns as one sliding section, and the glossary
on its own page.

## 1. The brand

`PortalLayout` shows the CivicBudget mark (`<Logo>`) by default. When the
government's administrator uploads a logo under **Government settings**,
the header shows that instead: `PortalBudgetDto.LogoVersion` carries the
upload time and the `<img>` points at `/transparency/{slug}/logo?v=ticks`.

The upload follows the profile-picture pattern (walkthrough 14): the
browser resizes to 512 px, `IGovernmentLogoService` checks type and size,
the bytes go to `GovernmentLogos`, and the government's portal pages are
evicted from the output cache because the header changed. The portal reads
the table through `PublicPortalDbContext`, the one non-snapshot table it
maps, by joining through an active snapshot's government id (ADR-0029).

## 2. The overview

- The resolution, published date, and version line moved from under the
  headline to the foot of the page, above the footer, as a citation.
- "Where does the money go?" and "Where does it come from?" are one section:
  `PortalPanels` puts both `Breakdown`s on a track two panels wide, with two
  radio inputs styled as tabs. `:checked` slides the track, hides the other
  panel (`visibility`, then `max-height: 0`), and highlights the tab. No
  script; reduced motion turns the slide off. The `$ | %` toggle reloads the
  page, so the revenue panel's toggle links carry `?view=revenue`.
- The "every line of the adopted budget" tail is gone.

## 3. The glossary

`/transparency/{slug}/{year}/glossary` (`PortalGlossary.razor`) explains the
words the portal uses, a few more than before (beginning balance, transfer,
account number, resolution), and carries the accessibility statement under
`#accessibility`. The footer is one line: which budget, downloads, Glossary,
Accessibility.

## 4. Things to read

1. `Components/Layout/PortalLayout.razor` and `Components/Portal/PortalOverview.razor`
2. `Components/Portal/Common/PortalPanels.razor` and the `.pt-panels` CSS
3. `Infrastructure/Portal/GovernmentLogoService.cs`, `SnapshotQueryService.GetLogoAsync`
4. `tests/CivicBudget.IntegrationTests/GovernmentLogoTests.cs`, `PortalPanelsTests.cs`
5. ADR-0029
