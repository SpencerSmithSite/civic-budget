// Captures the README screenshots (docs/screenshots) from a running local app. From the repo root:
//   (cd scripts/screenshots && npm run setup)                     # once: Playwright + Chromium
//   PW=<Seed:DemoPassword> VERSION=<draft version id> node scripts/screenshots/capture.mjs
// BASE defaults to http://localhost:5080; OUT to docs/screenshots. Then downsample with
//   sips -Z 1440 docs/screenshots/admin-*.png docs/screenshots/portal-overview.png docs/screenshots/portal-fund.png
//   sips -Z 844 docs/screenshots/portal-phone.png
import { chromium } from 'playwright';
import fs from 'node:fs';

const base = process.env.BASE ?? 'http://localhost:5080';
const out = process.env.OUT ?? 'docs/screenshots';
const pw = process.env.PW;
const version = process.env.VERSION;

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });
const page = await ctx.newPage();

async function shot(name, url, opts = {}) {
  await page.goto(base + url, { waitUntil: 'networkidle' });
  await page.waitForTimeout(opts.wait ?? 1500);
  if (opts.before) await opts.before();
  await page.screenshot({ path: `${out}/${name}.png`, fullPage: opts.fullPage ?? false });
  console.log('wrote', name);
}

// Public portal (no login).
await shot('portal-overview', '/transparency/maple-ridge-oh/2026');
await shot('portal-fund', '/transparency/maple-ridge-oh/2026/funds/2011');

const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true });
const p2 = await phone.newPage();
await p2.goto(base + '/transparency/maple-ridge-oh/2026', { waitUntil: 'networkidle' });
await p2.waitForTimeout(1000);
await p2.screenshot({ path: `${out}/portal-phone.png` });
console.log('wrote portal-phone');
await phone.close();

// Admin (sign in as the Finance Director).
await page.goto(base + '/Account/Login', { waitUntil: 'networkidle' });
await page.fill('input[name="Input.Email"]', 'finance@mapleridge.example');
await page.fill('input[name="Input.Password"]', pw);
await page.click('button[type="submit"]');
await page.waitForURL(u => !u.toString().includes('/Account/Login'));

await shot('admin-overview', '/admin');
await shot('admin-workspace', `/admin/budgets/${version}`, { wait: 2500 });
await shot('admin-report-fund-summary', `/admin/reports/${version}/fund-summary`, { wait: 2000 });
await shot('admin-import', `/admin/budgets/${version}/import`, {
  wait: 2000,
  before: async () => {
    const csv = 'Fund,Department,Account,Amount,Prior Year Actual,Current Year Budget,Justification\r\n1000,PD,5120,61000,,,Contract settlement\r\n4901,ST,5420,1250,,,Fuel for the new truck\r\n1000,PD,9999,10,,,\r\n2011,,5420,10,,,\r\n';
    await page.setInputFiles('#importFile', { name: 'fy2027-lines.csv', mimeType: 'text/csv', buffer: Buffer.from(csv) });
    await page.waitForSelector('.cb-kpis');
    await page.waitForTimeout(800);
  },
});

await browser.close();
