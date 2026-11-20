import { expect, test } from '@playwright/test';

/**
 * 壳的冒烟：加载、导航切换、标题栏按钮与主题推送。跑在 mock 模式上——真实桥的真机验收在
 * 手动清单里，这里守住的是页面本身的约定。
 */

test.beforeEach(async ({ page }) => {
  await page.goto('/?bridge=mock');
  // 等词表与信息都到齐：标题栏能显示鲸灵的名字，说明启动装配走完了。
  await expect(page.locator('wg-titlebar .title')).toContainText('鲸灵');
});

test('首页显示程序信息', async ({ page }) => {
  await expect(page.locator('wg-home h1')).toHaveText('欢迎使用鲸灵');
  await expect(page.locator('wg-home')).toContainText('0.0.0-mock');
  await expect(page.locator('wg-home')).toContainText('未命名项目');
});

test('导航切换到占位页', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await expect(page.locator('wg-placeholder h1')).toHaveText('宏');
  await expect(page.locator('wg-placeholder')).toContainText('这一区域仍由原生窗口承担');

  await page.locator('wg-nav button', { hasText: '首页' }).click();
  await expect(page.locator('wg-home h1')).toHaveText('欢迎使用鲸灵');
});

test('标题栏按钮推回窗口状态', async ({ page }) => {
  const maximize = page.locator('wg-titlebar button[title="最大化"]');
  await maximize.click();
  // mock 推 window.changed 之后，同一颗按钮的语义换成"还原"。
  await expect(page.locator('wg-titlebar button[title="还原"]')).toBeVisible();

  const pin = page.locator('wg-titlebar button.pin');
  await pin.click();
  await expect(pin).toHaveAttribute('data-active', 'true');
});

test('主题推送改变页面令牌', async ({ page }) => {
  await page.evaluate(() => {
    const transport = (window as unknown as { __mock?: MockProbe }).__mock;
    transport?.push('theme.changed', { mode: 'custom', baseLight: true, accentHex: '#C8401C' });
  });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
});

interface MockProbe {
  push(event: string, payload: unknown): void;
}

// 把 mock 挂到 window 上，让测试能假装宿主推事件。
declare global {
  interface Window {
    __mock?: MockProbe;
  }
}
