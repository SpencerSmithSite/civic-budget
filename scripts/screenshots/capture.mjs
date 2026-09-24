// Captures the README screenshots (docs/screenshots) from a running local app with fresh seed data.
// From the repo root:
//   (cd scripts/screenshots && npm run setup)          # once: Playwright + Chromium
//   dotnet run --project src/CivicBudget.Web -- --reseed   # optional: start from the seed
//   BASE=http://localhost:5000 PW=<Seed:DemoPassword> node scripts/screenshots/capture.mjs
// OUT defaults to docs/screenshots. Screens are taken at 2x and saved as they are; the README
// scales them. Desktop is 1440 x 900, phone is 390 x 844. Every page is found by clicking
// through the app from the seed, so no ids need to be passed in.
import { chromium } from 'playwright';

const base = process.env.BASE ?? 'http://localhost:5000';
const out = process.env.OUT ?? 'docs/screenshots';
const pw = process.env.PW;
if (!pw) throw new Error('Set PW to the demo password (dotnet user-secrets list --project src/CivicBudget.Web).');

const browser = await chromium.launch();
const desktop = { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 };
const phone = { viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true };

async function save(page, name, opts = {}) {
  await page.waitForTimeout(opts.wait ?? 1200);
  await page.screenshot({ path: `${out}/${name}.png`, fullPage: opts.fullPage ?? false });
  console.log('wrote', name);
}

async function open(page, url) {
  await page.goto(base + url, { waitUntil: 'networkidle' });
}

async function signIn(page, email) {
  await open(page, '/Account/Login');
  await page.fill('input[name="Input.Email"]', email);
  await page.fill('input[name="Input.Password"]', pw);
  await page.click('button[type="submit"]');
  await page.waitForURL(u => !u.toString().includes('/Account/Login'));
  await page.waitForLoadState('networkidle');
}

/** The FY2027 draft's id, found through the overview's "Continue" link. */
async function draftVersionId(page) {
  await open(page, '/admin/budgets');
  const href = await page.locator('a[href*="admin/budgets/"]').evaluateAll(links =>
    links.map(a => a.getAttribute('href')).find(h => /budgets\/[0-9a-f-]{36}$/.test(h ?? '')));
  // The versions list is newest first, so the first version link is FY2027's draft.
  return href.match(/[0-9a-f-]{36}/)[0];
}

// ---- Public, signed out --------------------------------------------------------------------
{
  const ctx = await browser.newContext(desktop);
  const page = await ctx.newPage();
  await open(page, '/Account/Login');
  await save(page, 'sign-in', { wait: 2500 });
  await open(page, '/transparency/maple-ridge-oh/2026');
  await save(page, 'portal-overview');
  await open(page, '/transparency/maple-ridge-oh/2026/funds/2011');
  await save(page, 'portal-fund');
  await ctx.close();

  const mobile = await browser.newContext(phone);
  const p = await mobile.newPage();
  await open(p, '/transparency/maple-ridge-oh/2026');
  await save(p, 'portal-phone');
  await open(p, '/transparency/maple-ridge-oh/2026/funds/1000/departments/110');
  await save(p, 'portal-phone-department');
  await mobile.close();
}

// ---- The fiscal officer ----------------------------------------------------------------------
let version;
{
  const ctx = await browser.newContext(desktop);
  const page = await ctx.newPage();
  await signIn(page, 'finance@mapleridge.example');
  version = await draftVersionId(page);

  await open(page, `/admin/budgets/${version}`);
  await save(page, 'admin-workspace', { wait: 2500 });
  await open(page, `/admin/budgets/${version}/departments`);
  await save(page, 'admin-department-board', { wait: 2000 });
  await open(page, `/admin/reports/${version}/fund-summary`);
  await save(page, 'admin-report-fund-summary', { wait: 2000 });
  await open(page, `/admin/budgets/${version}/import`);
  const csv = 'Fund,Department,Account,Amount,Prior Year Actual,Current Year Budget,Justification\r\n'
    + '1000,110,5120,61000,,,Contract settlement\r\n'
    + '4901,620,5420,1250,,,Fuel for the new truck\r\n'
    + '1000,110,9999,10,,,\r\n'
    + '2011,,5420,10,,,\r\n';
  await page.setInputFiles('#importFile', { name: 'fy2027-lines.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
  await page.waitForSelector('.cb-kpis');
  await save(page, 'admin-import', { wait: 1500 });

  // The seed is attributed to "system", which the overview's activity feed leaves out, so make
  // two ordinary edits first: the feed then shows what a working day looks like.
  await open(page, `/admin/budgets/${version}`);
  for (const [label, amount] of [['5120 Overtime, 110', '42,500.00'], ['5420 Fuel, 620', '18,400.00']]) {
    const input = page.getByLabel(`Proposed amount for ${label}`);
    await input.fill(amount);
    await input.press('Enter');
    await page.waitForTimeout(1200);
  }
  await open(page, '/admin');
  await save(page, 'admin-overview', { wait: 1500 });
  await ctx.close();

  const mobile = await browser.newContext(phone);
  const p = await mobile.newPage();
  await signIn(p, 'finance@mapleridge.example');
  await open(p, `/admin/budgets/${version}`);
  await save(p, 'admin-phone-workspace', { wait: 2500 });
  await p.locator('.cb-row-open').first().click();
  await save(p, 'admin-phone-line', { wait: 1500 });
  await open(p, `/admin/reports/${version}/fund-summary`);
  await save(p, 'admin-phone-fund-summary', { wait: 2000 });
  await mobile.close();
}

// ---- A department user -----------------------------------------------------------------------
{
  // Streets holds two departments, so sign-in lands on the board; Parks was returned with a note.
  const ctx = await browser.newContext(desktop);
  const page = await ctx.newPage();
  await signIn(page, 'streets@mapleridge.example');
  await page.waitForTimeout(1500);
  await page.getByRole('link', { name: /Parks/ }).first().click();
  await page.waitForFunction(() => location.pathname.includes('/departments/'));
  await save(page, 'admin-department', { wait: 2500 });
  await ctx.close();

}

// ---- The administrator: who can see what ----------------------------------------------------
{
  const ctx = await browser.newContext(desktop);
  const page = await ctx.newPage();
  await signIn(page, 'admin@mapleridge.example');
  await open(page, '/admin/users');
  // The edit link sits in the row's menu; read its address rather than open the menu.
  const edit = await page.locator('tr', { hasText: 'Sam Okafor' }).locator('a[href*="admin/users/"]').first().getAttribute('href');
  await open(page, '/' + edit);
  await save(page, 'admin-user-access', { wait: 1500 });
  await ctx.close();
}

await browser.close();
