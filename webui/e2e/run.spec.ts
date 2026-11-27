import { expect, test, type Page } from '@playwright/test';

/**
 * 运行页的冒烟：跑在 mock 上，守住这一页新补的调试数据——等触发的宏、每步的结局与结果、
 * 监视值、输入速率与换宏说明，以及历史取景的切换。真实桥与真机取景仍走手动清单。
 */

test.beforeEach(async ({ page }) => {
  await page.goto('/?bridge=mock&window=workbench#run');
  await expect(page.locator('wg-workbench')).toBeVisible();
});

/** mock 记下的方法名，用来断言页面向程序发了哪条命令。 */
function seen(page: Page, method: string): Promise<boolean> {
  return page.evaluate(
    (wanted) => (window as unknown as { __mock: { seen: string[] } }).__mock.seen.includes(wanted),
    method,
  );
}

test('运行页显示触发器、每步结果与历史取景，切换取景只发 run.lookSelect', async ({ page }) => {
  await page.locator('wg-run .side .macros fluent-button').first().click();

  // 等触发的宏：一个本该跑却没跑的宏，答案先在这一块里找。
  await expect(page.locator('wg-run .triggers .trigger-row')).toHaveCount(2);
  await expect(page.locator('wg-run .triggers .trigger-row .name').first()).toContainText('自动保存');

  // 每步的结局与它算出的值。
  await expect(page.locator('wg-run .step .outcome').first()).toHaveText('已完成');
  await expect(page.locator('wg-run .step .result').first()).toContainText('found = 12');

  // 监视值、输入速率与正在发输入的那次运行。
  await expect(page.locator('wg-run .vars .watch')).toHaveCount(2);
  await expect(page.locator('wg-run .vars .watch').first()).toContainText('count');
  await expect(page.locator('wg-run .rate')).toContainText('120');

  // 历史取景：两张缩略图，当前那张描着高亮。
  await expect(page.locator('wg-run .looks .look-thumb')).toHaveCount(2);
  await expect(page.locator('wg-run .looks .look-thumb.active')).toHaveAttribute('data-step', 's1');
  await page.locator('wg-run .looks .look-thumb').nth(1).click();
  await expect(page.locator('wg-run .looks .look-thumb.active')).toHaveAttribute('data-step', 's2');
  await expect.poll(async () => seen(page, 'run.lookSelect')).toBe(true);

  // 换一个宏来调试：程序把换宏的说明写在面板上。
  await page.locator('wg-run .side .macros fluent-button').nth(1).click();
  await expect(page.locator('wg-run .swap')).toContainText('游戏连招');
});
