// Captures the README screenshots of forecasting and the AI features (docs/screenshots). The AI
// shots need a model: set Assistant:ApiKey (and Provider and Model for Ollama) in the web project's
// user-secrets first. Answers come from the real model, so they read a little differently each run.
// From the repo root, after `capture.mjs` or on its own:
//   dotnet run --project src/CivicBudget.Web -- --reseed
//   BASE=http://localhost:5000 PW=<Seed:DemoPassword> node scripts/screenshots/capture-ai.mjs
// Nothing is confirmed, so the seed is unchanged afterwards.
import { chromium } from 'playwright';

const base = process.env.BASE ?? 'http://localhost:5000';
const out = process.env.OUT ?? 'docs/screenshots';
const pw = process.env.PW;
if (!pw) throw new Error('Set PW to the demo password (dotnet user-secrets list --project src/CivicBudget.Web).');

const browser = await chromium.launch();
const desktop = { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 };

async function save(page, name, wait = 1200) {
  await page.waitForTimeout(wait);
  await page.screenshot({ path: `${out}/${name}.png` });
  console.log('wrote', name);
}

async function open(page, url) {
  await page.goto(base + url, { waitUntil: 'networkidle' });
}

/** The FY2027 draft and the FY2026 amendment (the adopted budget of the year under way), from the versions list. */
async function versions(page) {
  await open(page, '/admin/budgets');
  const rows = await page.$$eval('a[href*="admin/budgets/"]', links => links.map(a => [a.getAttribute('href'), (a.closest('tr')?.innerText ?? '').replace(/\s+/g, ' ')]));
  const id = (test) => rows.map(([href, text]) => test(text) && href.match(/[0-9a-f-]{36}/)?.[0]).find(Boolean);
  return { draft: id(t => t.includes('2027')), adopted: id(t => t.includes('2026') && t.includes('Amendment')) };
}

/** Opens the assistant, asks, and waits for the answer (a real model takes a few seconds). */
async function ask(page, question) {
  if (!(await page.locator('#assistant-question').isVisible())) {
    await page.click('.cb-assistant-toggle');
  }

  // Wait for one more finished answer (or a refusal) than there was, not for the spinner to go:
  // the spinner appears a moment after Enter, so its absence proves nothing.
  const answers = '#assistant-panel .cb-assistant-msg.cb-assistant-answer:not(.text-secondary), #assistant-panel .cb-assistant-error';
  const before = await page.locator(answers).count();
  await page.fill('#assistant-question', question);
  await page.press('#assistant-question', 'Enter');
  await page.waitForFunction(([sel, n]) => document.querySelectorAll(sel).length > n, [answers, before], { timeout: 120000 });
}

// ---- The fiscal officer: the plan, the projection, and the assistant ------------------------
{
  const ctx = await browser.newContext(desktop);
  const page = await ctx.newPage();
  await open(page, '/Account/Login');
  await page.fill('input[name="Input.Email"]', 'finance@mapleridge.example');
  await page.fill('input[name="Input.Password"]', pw);
  await page.click('button[type="submit"]');
  await page.waitForURL(u => !u.toString().includes('/Account/Login'));
  const { draft, adopted } = await versions(page);

  // Taller than the other shots, so the balances by fund sit under the assumptions that drive them.
  await page.setViewportSize({ width: 1440, height: 1180 });
  await open(page, `/admin/budgets/${draft}/plan`);
  await save(page, 'admin-plan', 2000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await open(page, `/admin/reports/${adopted}/fund-projection`);
  await save(page, 'admin-report-projection', 2000);

  await open(page, `/admin/reports/${adopted}/budget-vs-actual`);
  await ask(page, 'How are actuals compared to the budget so far this year?');
  await save(page, 'admin-assistant', 800);

  // A fresh page, so the panel holds only this question and its card.
  await open(page, `/admin/budgets/${draft}`);
  await ask(page, 'Raise utilities 5% in this budget.');
  await page.locator('#assistant-panel .cb-proposal').scrollIntoViewIfNeeded();
  await page.evaluate(() => document.querySelector('#assistant-panel .cb-proposal')?.scrollIntoView({ block: 'center' }));
  await save(page, 'admin-assistant-proposal', 800);
  await ctx.close();
}

// ---- The public portal: the outlook and a resident's question ---------------------------------
{
  const ctx = await browser.newContext(desktop);
  const page = await ctx.newPage();
  await open(page, '/transparency/maple-ridge-oh/2026/outlook');
  await save(page, 'portal-outlook');

  await open(page, '/transparency/maple-ridge-oh/2026/ask');
  await page.fill('#ask-question', 'How much is budgeted for the police department this year, and how does it compare to last year?');
  await Promise.all([page.waitForNavigation({ timeout: 120000 }), page.click('.pt-ask button[type=submit]')]);
  // The page lands on the answer; show the question heading above it too.
  await page.evaluate(() => scrollTo(0, 0));
  await save(page, 'portal-ask');
  await ctx.close();
}

await browser.close();
