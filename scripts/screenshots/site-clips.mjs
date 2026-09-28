// Records the three silent clips on the product site (spencersmith.site/CivicBudget): a department
// moving a line and submitting, the fiscal officer bringing the Street fund within its limit, and
// the public portal on a phone. Each ends saved, so reseed before recording again.
// From the repo root, with a freshly seeded app running and Playwright set up (see mobile-sweep.mjs):
//   dotnet run --project src/CivicBudget.Web -- --reseed
//   BASE=http://localhost:5000 PW=<Seed:DemoPassword> OUT=/tmp/clips node scripts/screenshots/site-clips.mjs
// ONLY=worksheet|department|portal records one. Then encode each for the site, trimming the page load
// (the .start file holds the second to cut from) and cropping the admin sidebar:
//   ffmpeg -ss $(cat worksheet.start) -i worksheet.webm -vf "crop=1200:900:240:0,fps=30" -c:v libx264 -preset slow -crf 27 -pix_fmt yuv420p -movflags +faststart -an worksheet.mp4
//   ffmpeg -ss $(cat portal.start) -i portal.webm -vf fps=30 -c:v libx264 -preset slow -crf 25 -pix_fmt yuv420p -movflags +faststart -an portal.mp4
// and a poster from the first frame (same -ss and crop, -frames:v 1, then cwebp -q 80).
import { chromium } from 'playwright';
import fs from 'node:fs';
const base = process.env.BASE ?? 'http://localhost:5000', pw = process.env.PW, out = process.env.OUT;
fs.mkdirSync(out, { recursive: true });
const only = process.env.ONLY;
const b = await chromium.launch();

// A cursor that follows the mouse, since headless recordings have none.
const cursor = () => {
  addEventListener('DOMContentLoaded', () => {
    const c = document.createElement('div');
    c.style.cssText = 'position:fixed;z-index:2147483647;left:-40px;top:-40px;width:22px;height:22px;pointer-events:none;transition:transform .12s;';
    c.innerHTML = '<svg width="22" height="22" viewBox="0 0 22 22"><path d="M3 2l15 8.5-6.5 1.6L8.6 19z" fill="#111" stroke="#fff" stroke-width="1.5" stroke-linejoin="round"/></svg>';
    document.body.appendChild(c);
    addEventListener('mousemove', e => { c.style.left = e.clientX - 3 + 'px'; c.style.top = e.clientY - 2 + 'px'; }, true);
    addEventListener('mousedown', () => c.style.transform = 'scale(.8)', true);
    addEventListener('mouseup', () => c.style.transform = '', true);
  });
};

async function signedIn(email, viewport, extra = {}) {
  const ctx = await b.newContext({ viewport, ...extra });
  const p = await ctx.newPage();
  await p.goto(base + '/Account/Login');
  await p.fill('input[name="Input.Email"]', email); await p.fill('input[name="Input.Password"]', pw);
  await p.evaluate(() => document.querySelector('form').requestSubmit());
  await p.waitForURL(u => !u.toString().includes('Login'));
  const state = await ctx.storageState(); await ctx.close();
  return state;
}

async function record(name, viewport, storageState, extra, script) {
  const dir = `${out}/raw-${name}`; fs.rmSync(dir, { recursive: true, force: true });
  const ctx = await b.newContext({ viewport, storageState, recordVideo: { dir, size: extra.videoSize ?? viewport }, ...extra.ctx });
  await ctx.addInitScript(cursor);
  const p = await ctx.newPage();
  const t0 = Date.now(); let start = 0;
  const mark = () => { start = (Date.now() - t0) / 1000; };
  await script(p, mark);
  await ctx.close();
  const file = fs.readdirSync(dir)[0];
  fs.renameSync(`${dir}/${file}`, `${out}/${name}.webm`);
  fs.writeFileSync(`${out}/${name}.start`, String(start));
  console.log(name, 'recorded, trim from', start.toFixed(1), 's');
}

async function glide(p, locator, steps = 25) {
  const box = await locator.boundingBox();
  await p.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps });
}

// The FY2027 draft is the newest version; ids change with every reseed, so look it up.
const version = await (async () => {
  const ctx = await b.newContext({ storageState: await signedIn('finance@mapleridge.example', { width: 1440, height: 900 }) });
  const p = await ctx.newPage();
  await p.goto(base + '/admin/budgets', { waitUntil: 'networkidle' });
  const href = await p.locator('a[href*="admin/budgets/"]').first().getAttribute('href');
  await ctx.close();
  return href.match(/[0-9a-f-]{36}/)[0];
})();

if (!only || only === 'worksheet') {
  const finance = await signedIn('finance@mapleridge.example', { width: 1440, height: 900 });
  await record('worksheet', { width: 1440, height: 900 }, finance, {}, async (p, mark) => {
    await p.goto(`${base}/admin/budgets/${version}`, { waitUntil: 'networkidle' });
    await p.selectOption('select >> nth=0', { label: '2011 Street Construction, Maintenance & Repair' });
    await p.waitForTimeout(1200);
    const input = p.getByLabel('Proposed amount for 5520 Capital Outlay - Infrastructure, 620');
    await input.scrollIntoViewIfNeeded(); await p.mouse.move(700, 300); await p.waitForTimeout(400);
    mark();
    await p.waitForTimeout(1600);
    await glide(p, input, 30); await p.waitForTimeout(300);
    await input.click(); await input.press('ControlOrMeta+a'); await p.waitForTimeout(300);
    await input.pressSequentially('95000', { delay: 140 }); await p.waitForTimeout(400);
    await input.press('Enter');
    await p.waitForTimeout(500); await p.mouse.move(1180, 820, { steps: 25 });
    await p.waitForTimeout(3600);
  });
}

if (!only || only === 'department') {
  const streets = await signedIn('streets@mapleridge.example', { width: 1440, height: 900 });
  await record('department', { width: 1440, height: 900 }, streets, {}, async (p, mark) => {
    await p.goto(`${base}/admin/budgets/${version}/departments`, { waitUntil: 'networkidle' });
    const parks = p.locator('a:visible', { hasText: 'Parks' }).first();
    await parks.click(); await p.waitForLoadState('networkidle'); await p.waitForTimeout(1200);
    await p.mouse.move(900, 250); mark();
    await p.waitForTimeout(1800);
    // The fiscal officer's note asks for the playground surface to come from Capital Projects.
    const general = p.getByLabel('FY2027 request for 1000-310-5510 Capital Outlay - Equipment');
    await glide(p, general, 30); await general.click(); await general.press('ControlOrMeta+a');
    await general.pressSequentially('0', { delay: 160 }); await general.press('Enter'); await p.waitForTimeout(1100);
    const capital = p.getByLabel('FY2027 request for 4901-310-5510 Capital Outlay - Equipment');
    await capital.scrollIntoViewIfNeeded(); await glide(p, capital, 25); await capital.click(); await capital.press('ControlOrMeta+a');
    await capital.pressSequentially('39444', { delay: 140 }); await capital.press('Enter'); await p.waitForTimeout(1100);
    await p.evaluate(() => scrollTo({ top: 0, behavior: 'smooth' })); await p.waitForTimeout(900);
    const submit = p.locator('button:visible', { hasText: 'Submit to fiscal officer' }).first();
    await glide(p, submit, 30); await p.waitForTimeout(250); await submit.click(); await p.waitForTimeout(900);
    const confirm = p.locator('.modal.show, [role=dialog]').getByRole('button', { name: 'Submit to fiscal officer' });
    await glide(p, confirm, 20); await p.waitForTimeout(250); await confirm.click();
    await p.waitForTimeout(3200);
  });
}

if (!only || only === 'portal') {
  await record('portal', { width: 390, height: 844 }, undefined, { ctx: { isMobile: true, hasTouch: true } }, async (p, mark) => {
    await p.goto(`${base}/transparency/maple-ridge-oh`, { waitUntil: 'networkidle' }); await p.waitForTimeout(800);
    mark(); await p.waitForTimeout(1600);
    const scroll = async (n) => { for (let i = 0; i < n; i++) { await p.mouse.wheel(0, 70); await p.waitForTimeout(70); } };
    await scroll(9); await p.waitForTimeout(1200);
    const general = p.locator('a[href$="/funds/1000"]:visible').first();
    await general.scrollIntoViewIfNeeded(); await p.waitForTimeout(500); await general.tap();
    await p.waitForLoadState('networkidle'); await p.waitForTimeout(1300);
    await scroll(5); await p.waitForTimeout(700);
    const police = p.locator('a[href*="/departments/110"]:visible').first();
    await police.scrollIntoViewIfNeeded(); await p.waitForTimeout(500); await police.tap();
    await p.waitForLoadState('networkidle'); await p.waitForTimeout(1500);
    await scroll(4); await p.waitForTimeout(2200);
  });
}
await b.close();
