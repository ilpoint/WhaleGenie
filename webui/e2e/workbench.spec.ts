import { expect, test } from '@playwright/test';

test('宏工作台是独立的编辑/运行页面，主壳不出现编辑器导航', async ({ page }) => {
  await page.goto('/?bridge=mock&window=workbench#editor');
  await expect(page.locator('wg-workbench')).toBeVisible();
  await expect(page.locator('wg-nav')).toHaveCount(0);

  const tabs = page.locator('wg-workbench [role="tab"]');
  await expect(tabs).toHaveCount(2);
  await expect(tabs.nth(0)).toContainText('编辑器');
  await expect(tabs.nth(1)).toContainText('运行调试');
  await expect(tabs.nth(1).locator('svg.lucide-bug-play')).toHaveCount(1);

  await tabs.nth(1).click();
  await expect(page).toHaveURL(/#run$/);
  await expect(page.locator('wg-run')).toBeVisible();
});

test('主壳活动栏只保留宏、公共宏库和设置', async ({ page }) => {
  await page.goto('/?bridge=mock#macros');
  await expect(page.locator('wg-shell')).toBeVisible();
  await expect(page.locator('wg-nav .activity button[aria-label="宏"]')).toHaveCount(1);
  await expect(page.locator('wg-nav .activity button[aria-label="公共宏库"]')).toHaveCount(1);
  await expect(page.locator('wg-nav .activity button[aria-label="编辑器"]')).toHaveCount(0);
  await expect(page.locator('wg-nav .activity button[aria-label="运行调试"]')).toHaveCount(0);
});
