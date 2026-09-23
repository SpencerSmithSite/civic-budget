// Role-by-role sweep: every route, capturing console errors, page errors, failed requests,
// the Blazor error bar, suspicious text, and a screenshot. Output: one line per problem.
import { chromium } from 'playwright';
import fs from 'node:fs';
const base = process.env.BASE, pw = process.env.PW, out = process.env.OUT;
fs.mkdirSync(out, { recursive: true });
const roles = ['admin@mapleridge.example', 'finance@mapleridge.example', 'police@mapleridge.example', 'streets@mapleridge.example', 'viewer@mapleridge.example', 'admin@pinehollow.example'];
const b = await chromium.launch();
const problems = [];
for (const email of roles) {
  const who = email.split('@')[0] + '-' + email.split('@')[1].split('.')[0];
  const ctx = await b.newContext({ viewport: { width: 1366, height: 850 }, ignoreHTTPSErrors: true });
  const p = await ctx.newPage();
  let current = '';
  p.on('console', m => { if (m.type() === 'error') problems.push(`${who} ${current} CONSOLE ${m.text().slice(0, 200)}`); });
  p.on('pageerror', e => problems.push(`${who} ${current} PAGEERROR ${e.message.slice(0, 200)}`));
  p.on('response', r => { const s = r.status(); if (s >= 400 && !r.url().includes('/health')) problems.push(`${who} ${current} HTTP ${s} ${r.url().replace(base, '')}`); });
  await p.goto(base + '/Account/Login', { waitUntil: 'networkidle' });
  await p.fill('input[name="Input.Email"]', email); await p.fill('input[name="Input.Password"]', pw);
  await p.evaluate(() => document.querySelector('form').requestSubmit());
  await p.waitForURL(u => !u.toString().includes('/Account/Login'), { timeout: 20000 }).catch(() => problems.push(`${who} LOGIN FAILED`));
  await p.waitForLoadState('networkidle');
  // discover routes from the nav and version links
  await p.goto(base + '/admin/budgets', { waitUntil: 'networkidle' }); await p.waitForTimeout(1500);
  const hrefs = await p.evaluate(() => [...new Set([...document.querySelectorAll('a[href]')].map(a => a.getAttribute('href')))]);
  const versions = [...new Set(hrefs.filter(h => /admin\/budgets\/[0-9a-f-]{36}/.test(h)).map(h => h.match(/[0-9a-f-]{36}/)[0]))];
  const navs = await p.evaluate(() => [...new Set([...document.querySelectorAll('nav a[href], aside a[href]')].map(a => a.getAttribute('href')))]);
  const routes = new Set(['/', '/admin', ...navs.filter(h => h && !h.startsWith('http') && !h.startsWith('#')).map(h => h.startsWith('/') ? h : '/' + h), '/admin/profile-picture', '/Account/Manage', '/Account/Manage/ChangePassword']);
  for (const v of versions) {
    for (const s of ['', '/departments', '/import']) routes.add(`/admin/budgets/${v}${s}`);
    for (const s of ['fund-summary', 'department-detail', 'category']) routes.add(`/admin/reports/${v}/${s}`);
  }
  // department pages of the first version
  if (versions[0]) {
    await p.goto(`${base}/admin/budgets/${versions[0]}/departments`, { waitUntil: 'networkidle' }); await p.waitForTimeout(1500);
    const depts = await p.evaluate(() => [...new Set([...document.querySelectorAll('a[href*="/departments/"]')].map(a => a.getAttribute('href')))]);
    depts.slice(0, 3).forEach(d => routes.add(d.startsWith('/') ? d : '/' + d));
  }
  let i = 0;
  for (const r of routes) {
    current = r;
    let status = 0;
    try { const resp = await p.goto(base + r, { waitUntil: 'networkidle', timeout: 30000 }); status = resp?.status() ?? 0; }
    catch (e) { problems.push(`${who} ${r} NAV ${e.message.slice(0, 120)}`); continue; }
    await p.waitForTimeout(1200);
    const info = await p.evaluate(() => {
      const err = document.getElementById('blazor-error-ui');
      const errShown = err && getComputedStyle(err).display !== 'none';
      const text = document.body.innerText;
      const bad = ['NaN', 'undefined', 'System.', 'Exception', '{0}', '$-0', 'Infinity'].filter(t => text.includes(t));
      return { errShown, bad, title: document.title, url: location.pathname };
    });
    if (status >= 400) problems.push(`${who} ${r} STATUS ${status} (${info.title})`);
    if (info.errShown) problems.push(`${who} ${r} BLAZOR ERROR BAR`);
    if (info.bad.length) problems.push(`${who} ${r} TEXT ${info.bad.join(',')}`);
    if (info.url !== r.split('?')[0] && !(r === '/admin' && info.url.includes('my-department'))) problems.push(`${who} ${r} REDIRECTED -> ${info.url} (${info.title})`);
    await p.screenshot({ path: `${out}/${who}-${String(i++).padStart(2, '0')}-${r.replace(/[^a-z0-9]+/gi, '_').slice(0, 60)}.png`, fullPage: true });
  }
  console.log(`${who}: ${routes.size} routes`);
  await ctx.close();
}
await b.close();
fs.writeFileSync(`${out}/problems.txt`, [...new Set(problems)].join('\n'));
console.log([...new Set(problems)].join('\n') || 'no problems');
