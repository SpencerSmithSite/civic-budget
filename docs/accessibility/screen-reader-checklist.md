# Screen reader checklist (VoiceOver on a Mac)

A 15-minute pass that confirms what the accessibility conformance report could only infer from the
accessibility tree. It uses the local app with fresh seed data, signed in as
`finance@mapleridge.example`, in Safari (VoiceOver works best with Safari).

## Setup

1. Reseed and run the app:
   ```bash
   dotnet run --project src/CivicBudget.Web -- --reseed
   dotnet run --project src/CivicBudget.Web
   ```
   Open it in Safari.
2. Press **Cmd + F5** to turn VoiceOver on, and again to turn it off. VO means Control + Option.
3. Useful keys:
   - **VO + Right Arrow**: read the next item.
   - **Tab**: next control.
   - **VO + U**: the rotor, a list of headings, links, form controls, and landmarks.
   - **VO + Command + H**: next heading.
   - **VO + Command + T**: next table.
   - In a table, **VO + Arrow keys** move cell by cell, and **VO + C** says the column header.

Write down anything that is missing, wrong, repeated, or confusing, with the page and what you
heard.

## 1. Sign in

- [ ] The first Tab reaches **"Skip to content, link"**.
- [ ] The rotor's headings list has **"Sign in, heading level 1"**.
- [ ] The Email and Password fields are read by their labels.
- [ ] The eye button says **"Show password, toggle button, not pressed"**. After you press it, it
  says **pressed**, and focus stays on the button.
- [ ] Submit with a wrong password. VoiceOver lands on and reads **"Invalid login attempt."**
  without your moving.

## 2. The worksheet (FY2027 Original, "By account line")

- [ ] On arrival VoiceOver reads the heading **"Original budget"**, without the status or the help
  text.
- [ ] "By department" and "By account line" say **selected** or **not selected** (pressed).
- [ ] VO + Command + T finds the table **"Budget lines"**.
- [ ] Moving down the Account column, each group row is read as a **row header**, for example
  "1000 General Fund · Revenues and transfers in". Within a row, the account is its header.
- [ ] The FY2027 proposed field reads **"Proposed amount for 4110 Real Estate Taxes, 1000, edit
  text, 437,081.00"**.
- [ ] Type `abc` and press Tab. You hear **"Not saved: ... must be an amount of zero or more"**,
  and the field is **invalid**.
- [ ] Change a Street fund (2011) line enough to push it over its limit, then back. Each toast says
  **"Fund 2011 is now over..."** and then **"...back within its estimated resources"**.
- [ ] Type in Search lines. The count **"n of 99 lines"** is announced after a pause.
- [ ] Open "···" on a row, then History, then press Escape. Focus is back on **"Actions for
  4110"**.
- [ ] Open "Add line". You hear **"Add a budget line, dialog"**, and Tab stays inside it. The
  Program field also reads **"Expenditure accounts need a department"**. Escape returns you to
  the Add line button.

## 3. A department request (Parks & Recreation)

- [ ] The returned note is read once, as a status.
- [ ] Each fund's table is named ("1000 General Fund"), and the account names head their rows.
- [ ] The narrative box reads its label and **"n of 4000 characters"**.
- [ ] Submit to fiscal officer: the dialog is named, and Cancel returns focus.

## 4. Menus and dialogs on a phone-sized window

Make Safari's window narrow, under 992 pixels wide.
- [ ] "Menu" says **collapsed**. Press it: VoiceOver says **"Menu, dialog"** and focus is inside.
- [ ] Tab cycles within the menu. Escape closes it, and focus is back on **"Menu, collapsed"**.

## 5. The idle warning

Temporarily set `IdleTimeout` in `SessionPolicy` to 3 minutes, rebuild, then wait on the
worksheet without touching anything.

- [ ] After 1 minute you hear **"You will be signed out soon, alert dialog"**, the countdown
  sentence once, and **"Stay signed in, button"**.
- [ ] VoiceOver does **not** repeat the countdown every second.
- [ ] Stay signed in returns you to where you were.

Put `IdleTimeout` back to 30 minutes afterwards.

## 6. The public portal

- [ ] "Skip to content" works. The Portal navigation says **current page** on the section you are
  in.
- [ ] Revenues is read as **"$3.70 million"**, not "3 dollars 70 M".
- [ ] "Spending or revenue" is read as a group of two radio buttons. Choosing the second shows
  revenue.
- [ ] The "$" and "%" links are read as **"Dollars"** and **"Percent"**; the chosen one is
  **current**.
- [ ] Each bar chart is read either as a list of linked items or as one image with a summary. A
  long one ends with "The table lists every amount".
- [ ] "Show as a table" opens a table you can move through cell by cell.

## When you are done

Send me what you found. I'll fix it and mark the report's screen-reader line as done, with the date
and the VoiceOver and Safari versions.
