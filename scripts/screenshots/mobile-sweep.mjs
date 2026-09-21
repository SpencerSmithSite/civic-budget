// Visits every page at iPhone width and reports any that overflow sideways (which makes Safari
// zoom the whole page out). From the repo root, with the app running and Playwright set up:
//   (cd scripts/screenshots && npm run setup && npx playwright install webkit)
//   BASE=http://localhost:5000 PW=<Seed:DemoPassword> node scripts/screenshots/mobile-sweep.mjs
// Prints "ok" or "OVERFLOW" per route with the widest offending elements. Every route should be ok.
import { webkit, devices } from 'playwright';
const base = process.env.BASE; const pw = process.env.PW;
const b = await webkit.launch(); const ctx = await b.newContext({ ...devices['iPhone 14'], ignoreHTTPSErrors: true }); const p = await ctx.newPage();
async function login(email) {
  await p.goto(base + '/Account/Login', { waitUntil: 'networkidle' });
  await p.fill('input[name="Input.Email"]', email); await p.fill('input[name="Input.Password"]', pw);
  await p.click('button[type="submit"]'); await p.waitForURL(u => !u.toString().includes('/Account/Login'));
}
await login('admin@mapleridge.example');
await p.goto(base + '/admin/budgets', { waitUntil: 'networkidle' }); await p.waitForTimeout(2000);
const version = await p.evaluate(() => [...document.querySelectorAll('a[href*="admin/budgets/"]')].map(a => a.getAttribute('href').split('/')[2]).find(x => x && x.length > 30));
await p.goto(base + `/admin/budgets/${version}/departments`, { waitUntil: 'networkidle' }); await p.waitForTimeout(2000);
const dept = await p.evaluate(() => [...document.querySelectorAll('a[href*="/departments/"]')].map(a => a.getAttribute('href').split('/departments/')[1]).find(Boolean));
console.log('version', version, 'dept', dept);
const routes = ['/admin', '/admin/budgets', `/admin/budgets/${version}`, `/admin/budgets/${version}/departments`, `/admin/budgets/${version}/departments/${dept}`, `/admin/budgets/${version}/import`, '/admin/reports', `/admin/reports/${version}/fund-summary`, `/admin/reports/${version}/department-detail`, `/admin/reports/${version}/category`, '/admin/funds', '/admin/funds/new', '/admin/departments', '/admin/accounts', '/admin/fiscal-years', '/admin/chart-sync', '/admin/users', '/admin/settings', '/admin/profile-picture', '/admin/my-department', '/Account/Manage', '/Account/Manage/ChangePassword', '/', '/transparency', '/transparency/maple-ridge-oh/2026', '/transparency/maple-ridge-oh/2026/spending', '/transparency/maple-ridge-oh/2026/revenue', '/transparency/maple-ridge-oh/2026/funds', '/transparency/maple-ridge-oh/2026/funds/1000', '/transparency/maple-ridge-oh/2026/years', '/transparency/maple-ridge-oh/2026/search?q=police', '/transparency/maple-ridge-oh/2026/glossary'];
for (const r of routes) {
  try { await p.goto(base + r, { waitUntil: 'networkidle', timeout: 30000 }); } catch { console.log(r, 'TIMEOUT'); continue; }
  await p.waitForTimeout(4000);
  const res = await p.evaluate(() => {
    const vw = window.innerWidth; const sw = document.documentElement.scrollWidth;
    const scrollers = new Set();
    const offenders = [];
    for (const el of document.querySelectorAll('body *')) {
      const r = el.getBoundingClientRect(); if (r.width === 0) continue;
      if (r.right > vw + 2) {
        // skip if inside a horizontally scrolling ancestor
        let a = el.parentElement, inScroller = false;
        while (a) { const ox = getComputedStyle(a).overflowX; if ((ox === 'auto' || ox === 'scroll') && a.scrollWidth > a.clientWidth) { inScroller = true; break; } a = a.parentElement; }
        if (!inScroller) offenders.push(`${el.tagName.toLowerCase()}${el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\s+/).slice(0,2).join('.') : ''} r=${Math.round(r.right)} l=${Math.round(r.left)}`);
      }
      if (offenders.length > 6) break;
    }
    return { vw, sw, offenders };
  });
  console.log(`${res.sw > res.vw ? 'OVERFLOW' : 'ok      '} ${r}  scrollW=${res.sw}/${res.vw} ${res.offenders.slice(0,4).join(' | ')}`);
}
await b.close();
