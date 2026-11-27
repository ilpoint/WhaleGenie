import { expect, test } from '@playwright/test';

test('宏列表的开关与卡片状态一致，拖动后按项目顺序保存', async ({ page }) => {
  await page.goto('/?bridge=mock#macros');
  await expect(page.locator('wg-macros')).toBeVisible();

  const cards = page.locator('wg-macros .card');
  await expect(cards).toHaveCount(2);
  const second = cards.nth(1);
  await expect(second.locator('fluent-switch')).toHaveJSProperty('checked', false);
  await expect(second.locator('.dot')).toHaveAttribute('data-enabled', 'false');

  await second.locator('fluent-switch').click();
  await expect(second.locator('.dot')).toHaveAttribute('data-enabled', 'true');
  await expect(second.locator('fluent-switch')).toHaveJSProperty('checked', true);

  await second.locator('.drag-handle').dragTo(cards.first());
  await expect.poll(async () => page.evaluate(() => (
    window as unknown as {
      __mock: { projectState: { macros: { name: string }[] } };
    }).__mock.projectState.macros.map((entry) => entry.name)))
    .toEqual(['游戏连招', '自动保存']);
  await expect.poll(async () => page.evaluate(() => (
    window as unknown as {
      __mock: { projectState: { enabledFirst: boolean } };
    }).__mock.projectState.enabledFirst))
    .toBe(false);
});
