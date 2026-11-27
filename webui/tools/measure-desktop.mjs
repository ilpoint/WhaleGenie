import { spawn, execSync } from 'node:child_process';
import { chromium } from '@playwright/test';

// Measures what only the real program can answer, over the CDP door a debug build opens: how long a
// start takes to reach its first frame and its macro list, and how long opening a macro takes to
// reach a form you could type in.
//
// Every start-relative number is counted from the moment the process was spawned; the page's own
// clock (timeOrigin + performance.now) is what turns the page's timings into that scale.
const exe = process.env.WHALEGENIE_EXE;
const port = process.env.WHALEGENIE_CDP ?? '9223';
const rounds = Number(process.env.WHALEGENIE_ROUNDS ?? '3');
if (!exe) {
  console.error('WHALEGENIE_EXE is not set');
  process.exit(1);
}

const sleep = (ms) => new Promise((ready) => setTimeout(ready, ms));

async function poll(check, timeoutMs, everyMs = 20) {
  const until = Date.now() + timeoutMs;
  while (Date.now() < until) {
    const answer = await check();
    if (answer) return answer;
    await sleep(everyMs);
  }
  return null;
}

async function round(index) {
  const started = Date.now();
  const child = spawn(exe, [], { detached: false, stdio: 'ignore' });
  try {
    const version = await poll(async () => {
      try {
        const response = await fetch('http://127.0.0.1:' + port + '/json/version');
        return response.ok ? await response.json() : null;
      } catch {
        return null;
      }
    }, 40000);
    if (!version) throw new Error('the CDP port never opened');

    const browser = await chromium.connectOverCDP('http://127.0.0.1:' + port);
    try {
      const page = await poll(async () =>
        browser.contexts().flatMap((context) => context.pages())
          .find((candidate) => candidate.url().includes('index.html')) ?? null, 40000);
      if (!page) throw new Error('the shell page never appeared');

      await page.waitForLoadState('load');
      const paint = await poll(() => page.evaluate(() => {
        const paints = performance.getEntriesByType('paint');
        if (!paints.length) return null;
        const nav = performance.getEntriesByType('navigation')[0];
        return {
          origin: performance.timeOrigin,
          firstPaint: paints[0].startTime,
          domContentLoaded: nav ? nav.domContentLoadedEventEnd : 0,
          load: nav ? nav.loadEventEnd : 0,
        };
      }), 15000);
      if (!paint) throw new Error('the page never painted');

      const rows = await poll(() => page.evaluate(() => {
        const shell = document.querySelector('wg-shell');
        const macros = shell && shell.shadowRoot && shell.shadowRoot.querySelector('wg-macros');
        const list = macros && macros.shadowRoot && macros.shadowRoot.querySelector('div.list');
        return list ? list.querySelectorAll('div.card').length : 0;
      }), 40000);
      if (!rows) throw new Error('the macro list never filled');
      const listAt = await page.evaluate(() => performance.now());

      const opened = Date.now();
      await page.locator('wg-macros div.card button[aria-label="编辑"]').first().click();
      const form = await page.locator('wg-editor input').first().waitFor({ state: 'visible', timeout: 20000 })
        .then(() => Date.now())
        .catch(() => null);

      return {
        round: index,
        startToFirstFrame: Math.round(paint.origin + paint.firstPaint - started),
        pageStartToFirstFrame: Math.round(paint.firstPaint),
        startToMacroList: Math.round(paint.origin + listAt - started),
        clickToForm: form === null ? null : form - opened,
        pageDomContentLoaded: Math.round(paint.domContentLoaded),
        pageLoad: Math.round(paint.load),
        rows,
      };
    } finally {
      await browser.close().catch(() => {});
    }
  } finally {
    try { execSync('taskkill /PID ' + child.pid + ' /T /F', { stdio: 'ignore' }); } catch { child.kill(); }
    await sleep(1200);
  }
}

const results = [];
for (let index = 1; index <= rounds; index++) {
  const result = await round(index);
  results.push(result);
  console.log(JSON.stringify(result));
}
const spread = (key) => {
  const values = results.map((result) => result[key]).filter((value) => value !== null);
  return values.length ? Math.min(...values) + '-' + Math.max(...values) + ' ms' : 'n/a';
};
console.log('SUMMARY ' + JSON.stringify({
  startToFirstFrame: spread('startToFirstFrame'),
  startToMacroList: spread('startToMacroList'),
  clickToForm: spread('clickToForm'),
  pageDomContentLoaded: spread('pageDomContentLoaded'),
}));