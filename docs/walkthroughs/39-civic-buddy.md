# Walkthrough 39: Civic Buddy

The AI assistant now has a name, Civic Buddy, in the admin app and on the transparency portal. On
the portal it moved out of the menu, where "Ask a question" was the last of eight items in the top
right corner, into a launcher that floats in the corner of every page. The decisions are in
ADR-0051.

Nothing about what Civic Buddy can do changed: the staff assistant still works through the
Application services as the signed-in user (walkthroughs 35 and 36), and the portal's still reads
only the published snapshot (walkthrough 37). This phase is about being found, and about being
plainly an AI.

## 1. A name, and "AI" beside it

Everything people see says Civic Buddy: the top bar's button, the panel's heading, the settings
switches, the security log ("Asked Civic Buddy"), the audit trail ("Through Civic Buddy: Raise
utilities 5%"), and the messages. The code keeps its names (`AssistantService`, the
`Assistant:*` settings), because they say what the pieces do, and renaming them would change the
live demo's configuration for a word only developers read.

I wanted it to be obvious that this is an AI and not a person at a help desk, so:

- An **"AI" badge** sits beside the name wherever the name is a heading (`.cb-ai-badge` in the
  admin app, `.pt-ai-badge` on the portal).
- **The mark** (`BuddyMark`) is the CivicBudget tile with sparkles where the building would be:
  the sign people already read as "AI", and no face or headset. It is always `aria-hidden`,
  because the name beside it says the same thing.
- **Both prompts** now start with "You are Civic Buddy" and tell the model to say it is an AI if
  someone asks what it is. `AssistantRulesTests` and `PortalPromptTests` check both.

## 2. A launcher on every portal page, without JavaScript

The portal has no JavaScript at all, so a floating chat window had to work without one. It is a
`<details>` element:

```razor
<details class="pt-buddy">
    <summary class="pt-buddy-launch">
        <BuddyMark Size="36" />
        <span class="pt-buddy-closed"><b>Ask Civic Buddy</b><small>AI answers from this budget</small></span>
        <span class="pt-buddy-open">Close</span>
    </summary>
    <section class="pt-buddy-panel" aria-labelledby="buddy-title">...</section>
</details>
```

The summary is the button: the browser opens and closes it, and a screen reader hears "expanded"
or "collapsed". CSS pins the launcher to the bottom right corner and opens the panel above it; on a
phone the panel is a sheet across the screen, and the footer gets extra room underneath so the
launcher never covers its links.

`PortalLayout` shows it on every page of a portal that takes questions (the `PortalAskable` value
from walkthrough 38) except the ask page, which is Civic Buddy itself.

## 3. Asking from any page

Only the ask page can answer, so the panel's form posts there. Blazor's server-rendered forms are
matched by name: a post is handled by the form whose `@formname` equals the posted `_handler`
field. The ask page's form name is now a constant, and the panel sends it:

```razor
<form method="post" action="@Action" class="pt-buddy-form">
    <input type="hidden" name="_handler" value="@PortalAsk.FormName" />
    <BuddyComposer IdPrefix="buddy" Year="Budget.FiscalYear" />
</form>
```

The suggested questions ("Which department spends the most?") are buttons, each in its own small
form with the question in a hidden field. They are not links on purpose: a link is a GET that
crawlers follow, and every question costs a model call and counts against the government's
monthly allowance. A POST is what the rate limiter and the monthly cap already guard (ADR-0049).

The box and the suggestions are shared components (`BuddyComposer`, `BuddySuggestions`,
`BuddyNote`), used by both the floating panel and the ask page, so both say the same thing the
same way.

## 4. The ask page as a conversation

The answer used to sit under an "Answer" heading. Now it reads as an exchange: the question on the
right in navy, then the reply under Civic Buddy's mark, name, and badge, with "Looked at" below
it, then the box for the next question. A hidden heading ("Civic Buddy's answer") keeps the page's
outline for screen readers.

## 5. How I checked it

- `CivicBuddyTests`: the launcher is a closed `<details>` named for Civic Buddy with the "AI"
  badge; every form in the panel posts to the ask page with the right `_handler`; the note says it
  is an AI and can be wrong.
- In a browser with JavaScript turned off, a suggested question asked from the overview landed on
  the ask page with Civic Buddy's answer.
- The accessibility sweep now also scans the ask page and the panel opened, at desktop and phone
  widths: no findings. The phone sweep shows no sideways scroll with the panel open or closed.

## 6. Trying it

```bash
dotnet run --project src/CivicBudget.Web
```

Open http://localhost:5000/transparency/maple-ridge-oh/2026 and click **Ask Civic Buddy** in the
bottom right corner. In the admin app, sign in as `finance@mapleridge.example` and click
**Civic Buddy** in the top bar. Both need a model connected (`Assistant:ApiKey`).
