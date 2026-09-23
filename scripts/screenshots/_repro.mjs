import { chromium } from 'playwright';
const base = process.env.BASE, pw = process.env.PW;
const b = await chromium.launch();
async function session(email) {
  const ctx = await b.newContext({ viewport: { width: 1366, height: 850 }, ignoreHTTPSErrors: true });
  const p = await ctx.newPage();
  await p.goto(base + '/Account/Login', { waitUntil: 'networkidle' });
  await p.fill('input[name="Input.Email"]', email); await p.fill('input[name="Input.Password"]', pw);
  await p.evaluate(() => document.querySelector('form').requestSubmit());
  await p.waitForURL(u => !u.toString().includes('/Account/Login')); await p.waitForLoadState('networkidle');
  return p;
}
const settle = p => p.waitForTimeout(1500);

// B + C as the fiscal officer.
const p = await session('finance@mapleridge.example');
await p.goto(base + '/admin/budgets', { waitUntil: 'networkidle' }); await settle(p);
const links = await p.evaluate(() => [...document.querySelectorAll('table a[href*="admin/budgets/"]')].map(a => ({ href: a.getAttribute('href'), text: a.closest('tr').innerText.replace(/\s+/g, ' ') })));
const draft = links.find(l => /Draft/.test(l.text)); const adopted = links.find(l => /FY2026/.test(l.text) && /Adopted/.test(l.text) && /Amendment/.test(l.text)) ?? links.find(l => /Adopted/.test(l.text));
// B. add a line
await p.goto(base + '/' + draft.href.replace(/^\//, ''), { waitUntil: 'networkidle' }); await settle(p);
const countText = async () => (await p.locator('text=/\\d+ of \\d+ lines/').first().innerText().catch(() => '?'));
const before = await countText();
await p.locator('button', { hasText: 'Add line' }).first().click(); await p.waitForTimeout(500);
const fundVal = await p.locator('#add-fund option').nth(1).getAttribute('value'); await p.selectOption('#add-fund', fundVal);
const acctOpts = await p.locator('#add-account option').evaluateAll(os => os.map(o => o.value).filter(Boolean));
const deptOpts = await p.locator('select#add-dept option').evaluateAll(os => os.map(o => o.value).filter(Boolean)).catch(() => []);
await p.selectOption('#add-account', acctOpts[acctOpts.length - 1]);
if (deptOpts.length) await p.selectOption('select#add-dept', deptOpts[deptOpts.length - 1]);
await p.fill('#add-amount', '1234');
await p.locator('.modal.show button', { hasText: 'Add line' }).click(); await settle(p);
const toast = await p.locator('.toast, .cb-toast').allInnerTexts();
const after = await countText();
await p.reload({ waitUntil: 'networkidle' }); await settle(p);
console.log(`B add line: before "${before}", right after "${after}", after reload "${await countText()}"; toast: ${toast.join(' | ').slice(0, 80)}`);
// C. start an amendment from the adopted version
await p.goto(base + '/' + adopted.href.replace(/^\//, ''), { waitUntil: 'networkidle' }); await settle(p);
const amendBtn = p.locator('button', { hasText: 'Start an amendment' });
if (await amendBtn.count()) {
  await amendBtn.click(); await p.fill('#reason', 'Repro: supplemental appropriation');
  await p.locator('.modal.show button', { hasText: 'Create amendment' }).click(); await p.waitForTimeout(2500);
  const pills = (await p.locator('.cb-pill, .badge').allInnerTexts()).join(',');
  const stillAmend = await p.locator('button', { hasText: 'Start an amendment' }).count();
  console.log(`C amendment: url ${p.url().replace(base, '')}; header pills [${pills.slice(0, 80)}]; "Start an amendment" still shown: ${stillAmend > 0}`);
} else console.log(`C: no "Start an amendment" on ${adopted.text.slice(0, 60)}`);
await b.close();
