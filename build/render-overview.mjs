import { readFileSync, mkdirSync, writeFileSync, mkdtempSync, rmSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

// playwright-core is not a package of the application. A maintainer points this at a local
// install, exactly like build/record-vibe.mjs, so re-rendering the overview never becomes a
// runtime dependency of WhaleGenie.
const require = createRequire(import.meta.url);
const playwrightPath = process.env.WHALEGENIE_PLAYWRIGHT || "playwright-core";
const { chromium } = require(playwrightPath);

const root = resolve(process.argv[2] || process.cwd());
const output = join(root, "docs", "images", "interface-overview.png");

// The sheet is only as current as these two screenshots: an empty state or an older window look
// would be published as the face of the program, so a missing file stops the render instead of
// producing a hole.
const pictures = {
  editor: join(root, "docs", "images", "macro-editor.png"),
  main: join(root, "docs", "images", "main-window.png"),
  logo: join(root, "src", "WhaleGenie", "Assets", "WhaleGenie.png"),
};

function embedded(path) {
  let bytes;
  try {
    bytes = readFileSync(path);
  } catch {
    throw new Error("The overview needs a file the repository does not have: " + path);
  }
  return "data:image/png;base64," + bytes.toString("base64");
}

const images = Object.fromEntries(
  Object.entries(pictures).map(([key, path]) => [key, embedded(path)]),
);

// One sheet in the colours of the introduction page, two windows, no captions: the README and the
// page name them in prose, so the picture stays free of text that would have to be translated
// twice and would date as soon as the wording changed.
const html = [
  '<!doctype html>',
  '<html lang="zh-CN">',
  '<head>',
  '<meta charset="utf-8">',
  '<style>',
  '  * { box-sizing: border-box; }',
  '  html, body { margin: 0; background: #0a0a0a; }',
  '  .sheet {',
  '    position: relative;',
  '    width: 1488px;',
  '    padding: 64px;',
  '    overflow: hidden;',
  '    background:',
  '      radial-gradient(120% 100% at 50% 46%, transparent 30%, rgba(3, 5, 10, .85) 100%),',
  '      radial-gradient(30% 34% at 78% 18%, rgba(198, 210, 226, .26), rgba(96, 130, 176, .22) 44%, transparent 74%),',
  '      radial-gradient(48% 54% at 70% 22%, rgba(52, 100, 166, .34), transparent 76%),',
  '      linear-gradient(180deg, #0a1424 0%, #080b12 58%, #07080b 100%);',
  '    font-family: "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", sans-serif;',
  '  }',
  '  .sheet::after {',
  '    content: "";',
  '    position: absolute; inset: 0;',
  '    opacity: .05;',
  '    background-image: url("data:image/svg+xml,%3Csvg xmlns=%27http://www.w3.org/2000/svg%27 width=%27160%27 height=%27160%27%3E%3Cfilter id=%27n%27%3E%3CfeTurbulence type=%27fractalNoise%27 baseFrequency=%270.9%27 numOctaves=%272%27 stitchTiles=%27stitch%27/%3E%3CfeColorMatrix type=%27saturate%27 values=%270%27/%3E%3C/filter%3E%3Crect width=%27160%27 height=%27160%27 filter=%27url(%23n)%27 opacity=%270.6%27/%3E%3C/svg%3E");',
  '    mix-blend-mode: overlay;',
  '    pointer-events: none;',
  '  }',
  '  .row {',
  '    position: relative;',
  '    display: flex;',
  '    align-items: center;',
  '    justify-content: center;',
  '    gap: 28px;',
  '  }',
  '  .window {',
  '    border-radius: 14px;',
  '    overflow: hidden;',
  '    background: #141416;',
  '    box-shadow:',
  '      0 0 0 1px rgba(255, 255, 255, .13),',
  '      0 30px 64px -18px rgba(0, 0, 0, .85);',
  '  }',
  '  .window img { display: block; width: 100%; height: auto; }',
  '  .window-editor { width: 940px; }',
  '  .window-main { width: 392px; }',
  '  .brand {',
  '    position: relative;',
  '    display: flex;',
  '    align-items: center;',
  '    justify-content: center;',
  '    gap: 9px;',
  '    margin-top: 36px;',
  '    color: rgba(255, 255, 255, .42);',
  '    font-size: 13px;',
  '    letter-spacing: .3em;',
  '    text-transform: uppercase;',
  '  }',
  '  .brand img { width: 20px; height: 20px; border-radius: 5px; opacity: .9; }',
  '  .brand b { font-weight: 500; color: rgba(255, 255, 255, .72); letter-spacing: .1em; }',
  '</style>',
  '</head>',
  '<body>',
  '  <div class="sheet">',
  '    <div class="row">',
  '      <div class="window window-editor"><img src="' + images.editor + '" alt=""></div>',
  '      <div class="window window-main"><img src="' + images.main + '" alt=""></div>',
  '    </div>',
  '    <div class="brand"><img src="' + images.logo + '" alt=""><span>WhaleGenie <b>鲸灵</b></span></div>',
  '  </div>',
  '</body>',
  '</html>',
].join("\n");

const scratch = mkdtempSync(join(tmpdir(), "whalegenie-overview-"));
const pagePath = join(scratch, "overview.html");
writeFileSync(pagePath, html, "utf8");

const edge = process.env.WHALEGENIE_BROWSER
  || "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";

mkdirSync(join(root, "docs", "images"), { recursive: true });

const browser = await chromium.launch({ executablePath: edge, headless: true });
try {
  const context = await browser.newContext({
    viewport: { width: 1488, height: 1200 },
    deviceScaleFactor: 1,
  });
  const page = await context.newPage();
  await page.goto(pathToFileURL(pagePath).href, { waitUntil: "load" });
  await page.evaluate(() => document.fonts.ready);
  const sheet = page.locator(".sheet");
  await sheet.screenshot({ path: output });
  const box = await sheet.boundingBox();
  console.log("Rendered " + output + " (" + box.width + "x" + box.height + ")");
  await context.close();
} finally {
  await browser.close();
  rmSync(scratch, { recursive: true, force: true });
}
