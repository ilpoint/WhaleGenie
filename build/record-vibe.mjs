import { execFileSync } from "node:child_process";
import { copyFileSync, mkdirSync, mkdtempSync, rmSync } from "node:fs";
import { createRequire } from "node:module";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

// playwright-core is deliberately not a package of the application. A maintainer points this at a
// local install, so recording the clip never becomes another runtime dependency of WhaleGenie.
const require = createRequire(import.meta.url);
const playwrightPath = process.env.WHALEGENIE_PLAYWRIGHT || "playwright-core";
const { chromium } = require(playwrightPath);

const root = resolve(process.argv[2] || process.cwd());
const site = join(root, "_site");
const finalAssets = join(root, "site", "assets");
const videoPath = join(finalAssets, "whalegenie-vibe.mp4");
const posterPath = join(finalAssets, "whalegenie-vibe-poster.png");
const ogPath = join(finalAssets, "whalegenie-og.png");
const temporary = mkdtempSync(join(tmpdir(), "whalegenie-vibe-"));

mkdirSync(finalAssets, { recursive: true });

const edge = process.env.WHALEGENIE_BROWSER
  || "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";

const browser = await chromium.launch({ executablePath: edge, headless: true });

try {
  const context = await browser.newContext({
    viewport: { width: 1280, height: 720 },
    deviceScaleFactor: 1,
    recordVideo: { dir: temporary, size: { width: 1280, height: 720 } },
  });

  const page = await context.newPage();
  await page.goto(pathToFileURL(join(site, "vibe", "index.html")).href, {
    waitUntil: "networkidle",
  });

  // The title card is fully in place a little over two seconds after the page starts.
  await page.waitForTimeout(2800);
  await page.screenshot({ path: posterPath });

  await page.waitForFunction(() => window.__vibeDone === true, null, { timeout: 30000 });
  const video = page.video();
  await context.close();

  const webm = await video.path();
  execFileSync("ffmpeg", [
    "-y",
    "-i", webm,
    "-an",
    "-c:v", "libx264",
    "-preset", "medium",
    "-crf", "20",
    "-pix_fmt", "yuv420p",
    "-movflags", "+faststart",
    videoPath,
  ], { stdio: "inherit" });

  copyFileSync(videoPath, join(site, "assets", "whalegenie-vibe.mp4"));
  copyFileSync(posterPath, join(site, "assets", "whalegenie-vibe-poster.png"));

  const ogContext = await browser.newContext({
    viewport: { width: 1200, height: 630 },
    deviceScaleFactor: 1,
  });
  const ogPage = await ogContext.newPage();
  await ogPage.goto(pathToFileURL(join(site, "index.html")).href, { waitUntil: "networkidle" });
  await ogPage.waitForTimeout(900);
  await ogPage.screenshot({ path: ogPath });
  await ogContext.close();

  console.log(`Recorded ${dirname(videoPath)}`);
} finally {
  await browser.close();
  rmSync(temporary, { recursive: true, force: true });
}
