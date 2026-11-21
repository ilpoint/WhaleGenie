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

test('状态条跟随项目推送', async ({ page }) => {
  const status = page.locator('wg-shell .status');
  await expect(status.locator('[data-status="project"]')).toHaveText('未命名项目');
  await expect(status.locator('[data-status="revision"]')).toHaveText('R0');
  await expect(status.locator('[data-status="dirty"]')).toHaveText('已保存');
  await expect(status.locator('[data-status="system"]')).toHaveText('已停用');

  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await page.locator('wg-macros .footer fluent-switch').click();
  await expect(status.locator('[data-status="system"]')).toHaveText('已启用');

  await page.locator('wg-macros .card fluent-switch').first().click();
  await expect(status.locator('[data-status="dirty"]')).toHaveText('有未保存修改');
});

test('导航切换到宏列表与占位页', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await expect(page.locator('wg-macros .card .name').first()).toContainText('自动保存');
  await expect(page.locator('wg-macros')).toContainText('每 1 分钟');

  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await expect(page.locator('wg-placeholder h1')).toHaveText('编辑器');
  await expect(page.locator('wg-placeholder')).toContainText('这一区域仍由原生窗口承担');

  await page.locator('wg-nav button', { hasText: '首页' }).click();
  await expect(page.locator('wg-home h1')).toHaveText('欢迎使用鲸灵');
});

test('宏列表的过滤、搜索与启停', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();

  // 视图 dock：切到"按键"，只剩带键触发的宏。视图 dock 是过滤区的第一排，分组 chips 在它
  // 下面，也用同一种按钮——按第一排定位，"全部"就不会点到分组里去。
  const viewDock = page.locator('wg-macros .filters .dock').first();
  await viewDock.locator('button', { hasText: '按键' }).click();
  await expect(page.locator('wg-macros .card .name')).toContainText('游戏连招');
  const cards = page.locator('wg-macros .card');
  await expect(cards).toHaveCount(1);

  // 搜索：搜"自动"在"按键"视图下没有结果，切回"全部"才有。
  await page.locator('wg-macros input[type="search"]').fill('自动');
  await expect(page.locator('wg-macros .empty')).toBeVisible();
  await viewDock.locator('button', { hasText: '全部' }).click();
  await expect(page.locator('wg-macros .card .name')).toContainText('自动保存');

  // 启停：关掉"自动保存"，程序侧翻转后由推送回来，开关与状态点跟着变。
  await page.locator('wg-macros .card', { hasText: '自动保存' }).locator('fluent-switch').click();
  await expect
    .poll(async () => page.locator('wg-macros .card .dot').first().getAttribute('data-enabled'))
    .toBe('false');
});

test('系统开关推回新状态', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await page.locator('wg-macros .footer fluent-switch').click();
  await expect(page.locator('wg-macros .footer')).toContainText('已启用');
});

test('打开包后标题与列表跟着换，保存清掉未保存标记', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await expect(page.locator('wg-titlebar .title')).toContainText('未命名项目');

  // 打开：文件按钮先问程序，mock 宿主给出一个固定路径；标题与列表都换成包里的。
  // 按钮文案是词表的原话（"打开宏项目"），选择器必须按整句匹配。
  await page.locator('wg-macros .filebar fluent-button', { hasText: '打开宏项目' }).click();
  await expect(page.locator('wg-titlebar .title')).toContainText('opened-project');
  await expect(page.locator('wg-macros .card .name')).toContainText('包里的宏');
  await expect(page.locator('wg-macros .filebar .path')).toContainText('opened-project.wgmacro');

  // 编辑一次（启停翻转）出现未保存标记，保存后由推送清掉。
  await page.locator('wg-macros .card fluent-switch').click();
  await expect(page.locator('wg-macros .filebar fluent-badge')).toContainText('有未保存修改');
  await page.locator('wg-macros .filebar fluent-button', { hasText: '保存宏项目' }).click();
  await expect(page.locator('wg-macros .filebar fluent-badge')).toHaveCount(0);
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

test('文本框焦点上报给宿主', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await page.locator('wg-macros input[type="search"]').click();

  // 宿主靠这条报告决定系统热键是否该让路；mock 只记录方法名，真实标志在 C# 侧与真机验收。
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('window.focus')))
    .toBe(true);
});

test('关闭全部先问页面，确认后清空', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await page.locator('wg-macros .filebar fluent-button', { hasText: '全部关闭' }).click();

  // 程序推 dialog.request，页面把问题画出来；点卡片里的确认键，列表清空、标题回未命名。
  await expect(page.locator('wg-confirm h2')).toContainText('关闭全部宏');
  await page.locator('wg-confirm .actions fluent-button[appearance="accent"]').click();
  await expect(page.locator('wg-macros .card')).toHaveCount(0);
  await expect(page.locator('wg-titlebar .title')).toContainText('未命名项目');
});

test('删除宏先问页面，确认后离开列表', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await page
    .locator('wg-macros .card', { hasText: '自动保存' })
    .locator('.iconbuttons button.danger')
    .click();

  // 程序推删除确认，页面画出来；取消不动，确认后那一张卡离开列表。
  await expect(page.locator('wg-confirm h2')).toContainText('删除宏');
  await page.locator('wg-confirm .actions fluent-button[appearance="subtle"]').click();
  await expect(page.locator('wg-macros .card .name').first()).toContainText('自动保存');
  await expect(page.locator('wg-macros .card')).toHaveCount(2);

  await page
    .locator('wg-macros .card', { hasText: '自动保存' })
    .locator('.iconbuttons button.danger')
    .click();
  await page.locator('wg-confirm .actions fluent-button[appearance="accent"]').click();
  await expect(page.locator('wg-macros .card .name')).not.toContainText('自动保存');
  await expect(page.locator('wg-macros .card .name')).toContainText('游戏连招');
});

test('MCP 浮层切换授权', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await page.locator('wg-macros .filebar fluent-button', { hasText: 'MCP' }).click();
  await expect(page.locator('wg-mcp-panel h2')).toHaveText('MCP');

  // 开关只发命令：mcp.changed 回来后第一颗翻成选中，第二颗不动。
  await page.locator('wg-mcp-panel .switch fluent-switch').first().click();
  await expect
    .poll(async () => page.evaluate(() => {
      const panel = document.querySelector('wg-shell')!.shadowRoot
        .querySelector('wg-macros')!.shadowRoot
        .querySelector('wg-mcp-panel')!.shadowRoot;
      const switches = panel.querySelectorAll('.switch fluent-switch');
      return `${switches[0]?.hasAttribute('checked') ?? '?'}|${switches[1]?.hasAttribute('checked') ?? '?'}`;
    }))
    .toBe('true|false');
});

test('新建与编辑命令过桥', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();

  // 新建宏把当前视图带过去；mock 宿主不真的开原生编辑器，只把命令记下。
  await page.locator('wg-macros .footer fluent-button', { hasText: '新建宏' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.filter(name => name.startsWith('editor'))))
    .toEqual(['editor.newMacro']);

  // 双击卡片把那个宏带过去。
  await page.locator('wg-macros .card', { hasText: '自动保存' }).dblclick();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.filter(name => name.startsWith('editor'))))
    .toEqual(['editor.newMacro', 'editor.open']);
});

test('导入命令过桥', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏' }).click();
  await page.locator('wg-macros .filebar fluent-button', { hasText: '导入' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('project.import')))
    .toBe(true);
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
