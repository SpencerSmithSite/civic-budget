# Walkthrough 29: The product site

Everything before this phase was the product. This phase is the page that sells it:
**[spencersmith.site/CivicBudget](https://spencersmith.site/CivicBudget/)**. It is written for an ERP
vendor first and a government second. Its job is to get them from "what is this" to the live demo
or the contact form in one scroll. The mockup was approved before any of it was built.

## 1. Where it lives

The site is not in this repository. It is one static page in my portfolio's repository,
`public/CivicBudget/`, next to Council's site, and Vercel serves it with the rest of spencersmith.site.
It is not listed on the portfolio itself.

```
public/CivicBudget/
  index.html        the page
  assets/css        one stylesheet
  assets/js         three small behaviors
  assets/fonts      self-hosted
  assets/img        screenshots, logo, social card
  assets/video      three clips and their posters
```

There is no build step, and the page works without JavaScript:
- the clips keep their native controls;
- the password can be selected by hand;
- the form posts straight to Formspree.

That repository's `docs/civicbudget-site.md` is the maintenance guide.

## 2. What the page says, top to bottom

| Section | What it shows |
|---|---|
| Hero | "The budget, built the way Ohio builds it", over the real worksheet, with the Street fund's check against ORC 5705.39 (the appropriation limit) drawn on top |
| The budget year | Seven steps as the app's own workflow stepper; the steps that exchange data with the ERP are outlined in teal |
| Features | Department requests, the fiscal officer's worksheet, the public portal; each with a clip |
| For ERP vendors | What moves each way (chart, actuals, employees in; the adopted journal out), by API or by file, one adapter per ERP |
| Security | "Designed and built to achieve SOC 2 compliance", six controls, and a plain statement that no report has been issued |
| Live demo | The password, every login and what to try, and a warning about the cold start |
| FAQ, Contact | Four questions; a form through Formspree, subject "CivicBudget inquiry" |

The design uses the app's own tokens: the sidebar's navy, the logo's teal, and the over-limit red,
which appears once, on the fund that breaks the Ohio rule. The type is Public Sans, the U.S.
government's typeface, with IBM Plex Mono for account numbers and the statute citation. Both fonts
are self-hosted, because a page selling security should not send every visitor to a font host.

## 3. The clips

Three silent clips, recorded by Playwright from a freshly seeded copy of the app
(`scripts/screenshots/site-clips.mjs`):

1. **Department:** Parks & Recreation, returned with a note asking for the playground surface to
   come from Capital Projects, moves the line between funds and submits. The total stays
   $110,771.00, because only the fund changed.
2. **Worksheet:** the fiscal officer trims the Street fund's capital outlay from 120,000 to 95,000,
   and the fund's card turns from red to green.
3. **Portal:** on a phone, the overview, the General Fund, and the Police department's own
   narrative.

A few things I learned making them:
- **Headless recordings have no cursor.** Clicks look like magic, so the script injects a small
  pointer that follows the mouse and moves it in steps.
- **The recordings change data.** Each clip saves its edits, so the app is reseeded before
  recording. A reseed gives every record a new id, so the script looks the budget up rather than
  hard-coding it.
- **Phone recordings capture at CSS size.** A 2x phone recorded into a larger video fills only a
  quarter of the frame. They are recorded at 390 × 844.
- **Encoding.** Each clip is cut to start after the page load and cropped to drop the admin
  sidebar, so the numbers read larger. It is encoded as H.264 MP4, 200 to 800 KB. A GIF of the
  same clip would be ten times the size.

On the page they play only while on screen, never for someone who prefers reduced motion, and each
has a Pause button. WCAG 2.2.2 requires a way to stop moving content that lasts more than five
seconds. Each clip has a caption saying what happens, since there is no sound.

## 4. The address

The address keeps the product's capitals, `/CivicBudget/`, and people will type it in lowercase.
The obvious fix, a redirect from `/civicbudget/`, loops forever: Next matches redirect sources
without regard to case, so `/CivicBudget/` matches it too.

What works is a rewrite from `/CivicBudget/:path+` to the same path:
- the exact spelling is a public file, which Next serves (case-sensitively) before rewrites run;
- any other spelling misses and is rewritten onto the real folder.

I checked both against a production build, since only a production build applies these rules. The
page's canonical link keeps search engines on one address.

## 5. Checks

- **No sideways scroll** at 1440 and 390 pixels, in light and dark.
- **No console errors or failed requests** on the page.
- **Clips:** they play when scrolled into view, stay still under reduced motion, and start from Play.
- **Production build:** every spelling of the address serves the page and its files; an unknown
  file is still a 404; Council is unaffected.
- **Portfolio:** unchanged. The site is not listed on spencersmith.site's home page; it is reached
  by its address.
