import http from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from '@playwright/test';

// Measures the shell page without the program: the built page is served from disk and loaded in
// headless Edge, so the two numbers belong to the page alone and not to WebView2 or the app.
const dist = path.resolve(process.env.WHALEGENIE_DIST ?? 'dist');
const rounds = Number(process.env.WHALEGENIE_ROUNDS ?? '2');
const types = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
  '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml',
  '.png': 'image/png', '.woff2': 'font/woff2',
};

const server = http.createServer(async (request, response) => {
  const clean = decodeURIComponent(request.url.split('?')[0]);
  const file = path.join(dist, clean === '/' ? 'index.html' : clean);
  try {
    const data = await readFile(file);
    response.writeHead(200, { 'content-type': types[path.extname(file)] ?? 'application/octet-stream' });
    response.end(data);
  } catch {
    response.writeHead(404);
    response.end('missing');
  }
});
await new Promise((ready) => server.listen(0, ready));
const port = server.address().port;

const browser = await chromium.launch({ channel: process.env.WHALEGENIE_BROWSER ?? 'msedge', headless: true });
try {
  for (let round = 1; round <= rounds; round++) {
    const page = await browser.newPage();
    await page.goto('http://127.0.0.1:' + port + '/', { waitUntil: 'load' });
    const metrics = await page.evaluate(() => {
      const nav = performance.getEntriesByType('navigation')[0];
      return {
        domContentLoaded: Math.round(nav.domContentLoadedEventEnd),
        load: Math.round(nav.loadEventEnd),
      };
    });
    console.log(JSON.stringify({ round, ...metrics }));
    await page.close();
  }
} finally {
  await browser.close();
  server.close();
}