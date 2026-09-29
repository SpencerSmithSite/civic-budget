// Runs axe-core (WCAG 2.2 A and AA, plus axe's best practices) on every page each demo user can
// reach, the sign-in pages, every public portal page at desktop and phone width, and optionally the
// product site. Findings are grouped by rule, with the pages and elements each one was found on.
// From the repo root, with the app running and Playwright set up (see mobile-sweep.mjs):
//   BASE=http://localhost:5000 PW=<Seed:DemoPassword> OUT=/tmp/a11y [SITE=https://spencersmith.site/CivicBudget/] node scripts/screenshots/a11y-sweep.mjs
// Writes OUT/a11y.json (every finding) and prints a summary. It changes no data.
import { chromium } from 'playwright';
import AxeBuilder from '@axe-core/playwright';
import fs from 'node:fs';

const base = process.env.BASE ?? 'http://localhost:5000', pw = process.env.PW, out = process.env.OUT, site = process.env.SITE;
fs.mkdirSync(out, { recursive: true });
const tags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'];
const findings = new Map(); // rule id -> { impact, help, url, wcag, pages: Set, nodes: Map(target -> html) }
let pagesScanned = 0;

async function scan(p, label) {
  const result = await new AxeBuilder({ page: p }).withTags(tags).analyze();
  pagesScanned++;
  for (const v of result.violations) {
    const f = findings.get(v.id) ?? { impact: v.impact, help: v.help, url: v.helpUrl, wcag: v.tags.filter(t => /^wcag\d/.test(t)), pages: new Set(), nodes: new Map() };
    f.pages.add(label);
    for (const n of v.nodes.slice(0, 5)) {
      const target = n.target.join(' ');
      if (!f.nodes.has(target)) f.nodes.set(target, { html: n.html.slice(0, 200), summary: (n.failureSummary ?? '').slice(0, 300), page: label });
    }
    findings.set(v.id, f);
  }
}

async function visit(p, url, label) {
  try { await p.goto(url, { waitUntil: 'networkidle', timeout: 30000 }); }
  catch (e) { console.log('NAV', label, e.message.slice(0, 100)); return; }
  await p.waitForTimeout(1200); // let an interactive page finish its first render
  await scan(p, label);
}

const b = await chromium.launch();

// Anonymous: the sign-in pages and the public portal, at desktop and phone width.
for (const [viewport, suffix] of [[{ width: 1366, height: 850 }, ''], [{ width: 390, height: 844 }, ' @390']]) {
  const ctx = await b.newContext({ viewport });
  const p = await ctx.newPage();
  const portal = '/transparency/maple-ridge-oh/2026';
  const anon = [
    '/Account/Login', '/Account/ForgotPassword', '/Account/AccessDenied', '/Account/Lockout',
    '/transparency', '/transparency/maple-ridge-oh', portal, `${portal}/spending`, `${portal}/revenue`, `${portal}/funds`,
    `${portal}/funds/1000`, `${portal}/funds/1000/departments/110`, `${portal}/search?q=police`, `${portal}/glossary`, `${portal}/years`, `${portal}/outlook`,
  ];
  for (const r of anon) await visit(p, base + r, r + suffix);
  if (site) await visit(p, site, 'product site' + suffix);
  await ctx.close();
}

// Signed in: every page each role can reach, found the way role-sweep.mjs finds them.
const roles = ['admin@mapleridge.example', 'finance@mapleridge.example', 'streets@mapleridge.example', 'viewer@mapleridge.example'];
for (const email of roles) {
  const who = email.split('@')[0];
  const ctx = await b.newContext({ viewport: { width: 1366, height: 850 } });
  const p = await ctx.newPage();
  await p.goto(base + '/Account/Login', { waitUntil: 'networkidle' });
  await p.fill('input[name="Input.Email"]', email); await p.fill('input[name="Input.Password"]', pw);
  await p.evaluate(() => document.querySelector('form').requestSubmit());
  await p.waitForURL(u => !u.toString().includes('/Account/Login'), { timeout: 20000 });
  await p.goto(base + '/admin/budgets', { waitUntil: 'networkidle' }); await p.waitForTimeout(1500);
  const hrefs = await p.evaluate(() => [...new Set([...document.querySelectorAll('a[href]')].map(a => a.getAttribute('href')))]);
  const versions = [...new Set(hrefs.filter(h => /admin\/budgets\/[0-9a-f-]{36}/.test(h)).map(h => h.match(/[0-9a-f-]{36}/)[0]))];
  const navs = await p.evaluate(() => [...new Set([...document.querySelectorAll('nav a[href], aside a[href]')].map(a => a.getAttribute('href')))]);
  const routes = new Set(['/admin', ...navs.filter(h => h && !h.startsWith('http') && !h.startsWith('#') && !h.includes('transparency')).map(h => h.startsWith('/') ? h : '/' + h),
    '/admin/profile-picture', '/Account/Manage', '/Account/Manage/ChangePassword', '/Account/Manage/TwoFactor', '/Account/Manage/EnableAuthenticator']);
  for (const v of versions.slice(0, 2)) {
    for (const s of ['', '/departments', '/import', '/send', '/plan']) routes.add(`/admin/budgets/${v}${s}`);
    for (const s of ['fund-summary', 'department-detail', 'category', 'position-roster', 'personnel-cost', 'benefits-summary', 'certificate', 'fund-projection', 'trends', 'appropriation-measure', 'budget-vs-actual', 'revenue-vs-receipts']) routes.add(`/admin/reports/${v}/${s}`);
  }
  if (versions[0]) {
    await p.goto(`${base}/admin/budgets/${versions[0]}/departments`, { waitUntil: 'networkidle' }); await p.waitForTimeout(1500);
    const depts = await p.evaluate(() => [...new Set([...document.querySelectorAll('a[href*="/departments/"]')].map(a => a.getAttribute('href')))]);
    depts.slice(0, 2).forEach(d => { const r = d.startsWith('/') ? d : '/' + d; routes.add(r); routes.add(r.replace('/departments/', '/personnel/')); });
  }
  for (const r of routes) await visit(p, base + r, `${r.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, '{id}')} as ${who}`);
  console.log(`${who}: ${routes.size} pages`);
  await ctx.close();
}
await b.close();

const order = { critical: 0, serious: 1, moderate: 2, minor: 3 };
const report = [...findings.entries()]
  .map(([id, f]) => ({ id, impact: f.impact, help: f.help, url: f.url, wcag: f.wcag, pages: [...f.pages], nodes: [...f.nodes.entries()].map(([target, n]) => ({ target, ...n })) }))
  .sort((a, b) => order[a.impact] - order[b.impact] || b.pages.length - a.pages.length);
fs.writeFileSync(`${out}/a11y.json`, JSON.stringify({ pagesScanned, report }, null, 2));
console.log(`\n${pagesScanned} pages scanned, ${report.length} rules with findings`);
for (const r of report) console.log(`${r.impact.padEnd(9)} ${r.id.padEnd(32)} ${String(r.pages.length).padStart(3)} pages  ${r.wcag.join(',') || 'best practice'}  ${r.help}`);
