import { expect, test, type Page } from '@playwright/test';

/**
 * 壳的冒烟：加载、导航切换、标题栏按钮与主题推送。跑在 mock 模式上——真实桥的真机验收在
 * 手动清单里，这里守住的是页面本身的约定。
 */

test.beforeEach(async ({ page }) => {
  await page.goto('/?bridge=mock');
  // 等词表与信息都到齐：标题栏能显示鲸灵的名字，说明启动装配走完了。
  await expect(page.locator('wg-titlebar .title')).toContainText('鲸灵');
});

/** 页面按命令域发方法名，具体命令在 params 的 kind 里；断言一条调用，而不是去看方法名。 */
function called(page: Page, method: string, params: Record<string, string>): Promise<boolean> {
  return page.evaluate(({ method: wanted, params: wantedParams }) => window.__mock!.calls.some(
    (call) => call.method === wanted
      && Object.entries(wantedParams).every(([name, value]) => call.params[name] === value)),
  { method, params });
}

test('启动直接落在宏列表', async ({ page }) => {
  await expect(page.locator('wg-macros')).toBeVisible();
  await expect(page.locator('wg-macros .card .name').first()).toContainText('自动保存');
  await expect(page.locator('wg-home')).toHaveCount(0);
});

test('标题栏显示项目自动保存，主壳不再显示编辑器状态条', async ({ page }) => {
  const title = page.locator('wg-titlebar .title');
  await expect(title).toContainText('未命名项目');
  await expect(title).toContainText('项目自动保存已开启');
  await expect(page.locator('wg-shell .status [data-status="editor"]')).toHaveCount(0);
});

test('帮助菜单的关于页显示第三方组件', async ({ page }) => {
  await page.locator('wg-titlebar .menu', { hasText: '帮助' }).getByRole('button', { name: '帮助' }).click();
  await page.getByRole('menuitem', { name: '关于' }).click();
  await expect(page.locator('wg-about h1')).toHaveText('关于');
  await expect(page.locator('wg-about')).toContainText('鲸灵');
  await page.locator('wg-about fluent-button', { hasText: '第三方组件' }).click();
  await expect(page.locator('wg-about #notices')).toContainText('Avalonia');
});

test('activity bar 图标切换页面', async ({ page }) => {
  await expect(page.locator('wg-nav .activity button[aria-label="宏"]')).toHaveCount(1);
  await expect(page.locator('wg-nav .activity button[aria-label="公共宏库"]')).toHaveCount(1);
  await expect(page.locator('wg-nav .activity button[aria-label="编辑器"]')).toHaveCount(0);
  await expect(page.locator('wg-nav .activity button[aria-label="运行调试"]')).toHaveCount(0);
  await expect(page.locator('wg-nav .activity button[aria-label="首页"]')).toHaveCount(0);
  const macros = page.locator('wg-nav .activity button[aria-label="宏"]');
  await macros.click();
  await expect(page.locator('wg-macros .card .name').first()).toContainText('自动保存');

  // The activity bar is the only navigation column; pressing the active icon keeps the page.
  await macros.click();
  await expect(page.locator('wg-macros')).toBeVisible();
  await expect(page.locator('wg-nav .side')).toHaveCount(0);
});

test('标题栏文件与帮助菜单', async ({ page }) => {
  const file = page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' });
  await file.click();
  await expect(page.getByRole('menuitem', { name: '打开宏项目' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: '保存宏项目' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: '导入…' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: '导出宏…' })).toBeVisible();

  const help = page.locator('wg-titlebar .menu', { hasText: '帮助' }).getByRole('button', { name: '帮助' });
  await help.click();
  await expect(page.locator('wg-titlebar .version')).toContainText('0.0.0-mock');
  await expect(page.getByRole('menuitem', { name: '关于' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: '使用手册' })).toBeVisible();

  await page.getByRole('menuitem', { name: '关于' }).click();
  await expect(page.locator('wg-about h1')).toHaveText('关于');
});

test('设置页读取并显示常规设置', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  await expect(page.locator('wg-settings h1')).toHaveText('设置');
  // 轮盘菜单并进设置页后，rail 是六项。
  await expect(page.locator('wg-settings .rail button')).toHaveCount(6);
  await expect(page.locator('wg-settings .header input[type="search"]')).toBeVisible();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.state')))
    .toBe(true);
});

test('导航切换到宏列表与占位页', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
  await expect(page.locator('wg-macros .card .name').first()).toContainText('自动保存');
  await expect(page.locator('wg-macros')).toContainText('每 1 分钟');

  await page.locator('wg-nav .activity button[aria-label="公共宏库"]').click();
  await expect(page.locator('wg-library')).toBeVisible();

  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
  await expect(page.locator('wg-macros')).toBeVisible();
});

test('宏列表的过滤、搜索与启停', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();

  // 视图 dock：切到"按键"，只剩带键触发的宏。视图 dock 是过滤区的第一排，分组 chips 在它
  // 下面、不带 role——按第一排的 role=tab 定位。dock 文案来自桥下发的词表，按 views 的
  // 顺序点第二格（按键），不把词表内容写进断言。
  const viewDock = page.locator('wg-macros .filters .dock').first();
  const views = viewDock.getByRole('tab');
  await expect(views).toHaveCount(7);
  await views.nth(1).click();
  await expect(views.nth(1)).toHaveAttribute('aria-current', 'true');
  await expect(page.locator('wg-macros .card .name')).toContainText('游戏连招');
  const cards = page.locator('wg-macros .card');
  await expect(cards).toHaveCount(1);

  // 搜索：搜"自动"在"按键"视图下没有结果，切回"全部"才有。
  await page.locator('wg-macros input[type="search"]').fill('自动');
  await expect(page.locator('wg-macros .empty')).toBeVisible();
  await views.nth(0).click();
  await expect(page.locator('wg-macros .card .name')).toContainText('自动保存');

  // 启停：关掉"自动保存"，程序侧翻转后由推送回来，开关与状态点跟着变。
  await page.locator('wg-macros .card', { hasText: '自动保存' }).locator('fluent-switch').click();
  await expect
    .poll(async () => page.locator('wg-macros .card .dot').first().getAttribute('data-enabled'))
    .toBe('false');
});

test('侧栏系统开关在任何页面都能按，并显示开关键', async ({ page }) => {
  await expect(page.locator('wg-nav .system .hotkey')).toContainText('Ctrl');
  await page.locator('wg-nav .system .power').click();
  await expect(page.locator('wg-nav .system')).toHaveAttribute('data-enabled', 'true');
  await page.locator('wg-nav .activity button[aria-label="公共宏库"]').click();
  await expect(page.locator('wg-nav .system .power')).toHaveAttribute('aria-pressed', 'true');
});

test('打开包后标题与列表跟着换，保存清掉未保存标记', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
  await expect(page.locator('wg-titlebar .title')).toContainText('未命名项目');

  // 打开：文件按钮先问程序，mock 宿主给出一个固定路径；标题与列表都换成包里的。
  // 按钮文案是词表的原话（"打开宏项目"），选择器必须按整句匹配。
  await page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' }).click();
  await page.getByRole('menuitem', { name: '打开宏项目' }).click();
  const preview = page.locator('wg-transfer');
  await expect(preview).toContainText('包内容');
  await expect(preview).toContainText('包里的宏');
  await preview.locator('fluent-button', { hasText: '打开宏项目' }).click();
  await expect(page.locator('wg-titlebar .title')).toContainText('opened-project');
  await expect(page.locator('wg-macros .card .name')).toContainText('包里的宏');
  await expect(page.locator('wg-macros .filebar .path')).toContainText('opened-project.wgmacro');

  // 编辑一次（启停翻转）出现未保存标记，保存后由推送清掉。
  await page.locator('wg-macros .card fluent-switch').click();
  await expect(page.locator('wg-macros .filebar fluent-badge')).toContainText('有未保存修改');
  await page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' }).click();
  await page.getByRole('menuitem', { name: '保存宏项目' }).click();
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
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
  await page.locator('wg-macros input[type="search"]').click();

  // 宿主靠这条报告决定系统热键是否该让路；mock 只记录方法名，真实标志在 C# 侧与真机验收。
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('window.focus')))
    .toBe(true);
});

test('关闭全部先问页面，确认后清空', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
  await page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' }).click();
  await page.getByRole('menuitem', { name: '全部关闭' }).click();

  // 程序推 dialog.request，页面把问题画出来；点卡片里的确认键，列表清空、标题回未命名。
  await expect(page.locator('wg-confirm h2')).toContainText('关闭全部宏');
  await page.locator('wg-confirm .actions fluent-button[appearance="accent"]').click();
  await expect(page.locator('wg-macros .card')).toHaveCount(0);
  await expect(page.locator('wg-titlebar .title')).toContainText('未命名项目');
});

test('删除宏先问页面，确认后离开列表', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
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
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
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

test('公共宏库可新建和导入', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="公共宏库"]').click();
  await page.locator('wg-library .tools fluent-button', { hasText: '新建' }).click();
  await expect(page.locator('wg-library .card', { hasText: '公共宏 2' })).toBeVisible();

  await page.locator('wg-library .tools fluent-button', { hasText: '从文件导入' }).click();
  const libraryPreview = page.locator('wg-transfer');
  await expect(libraryPreview).toContainText('包内容');
  await libraryPreview.locator('fluent-button', { hasText: '合并公共宏' }).click();
  await expect
    .poll(() => page.evaluate(() => window.__mock!.seen.includes('library.import')))
    .toBe(true);
  await libraryPreview.locator('fluent-button', { hasText: '关闭' }).click();
  await expect(page.locator('wg-library .card', { hasText: '包里的宏' })).toBeVisible();
});

test('设置页自定义主题可改强调色', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  const theme = page.locator('wg-settings .card').filter({ hasText: '主题' }).first();
  await theme.locator('select').selectOption('custom');
  const accent = theme.locator('input[type="color"]');
  await expect(accent).toBeVisible();
  await accent.fill('#ff0000');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('theme.set')))
    .toBe(true);
});

test('设置页内编辑轮盘动作', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  const wheel = page.locator('wg-settings .card').filter({ hasText: '轮盘动作管理' }).first();
  await wheel.locator('.wheel-slot').nth(1).click();
  const editor = wheel.locator('.slot-editor');
  await expect(editor).toBeVisible();
  await editor.locator('select').selectOption({ label: '备份' });
  await editor.locator('input').fill('备用动作');
  await editor.locator('fluent-button', { hasText: '保存' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.wheel.slot.set')))
    .toBe(true);
});

test('设置页内编辑菜单呼出键与窗口规则', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  const menu = page.locator('wg-settings .card').filter({ hasText: '菜单呼出键' }).first();
  await menu.locator('fluent-button', { hasText: '鼠标中键按下（默认）' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.menu.capture')))
    .toBe(true);

  const rules = page.locator('wg-settings .card').filter({ hasText: '下半区窗口规则' }).first();
  await rules.locator('fluent-button', { hasText: '新增规则' }).click();
  const dialog = page.locator('wg-window-rule');
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('.window').first()).toContainText('Notepad');
  await dialog.locator('.window').first().click();
  await dialog.locator('fluent-button', { hasText: '使用这个窗口' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.menu.rule.add')))
    .toBe(true);
});

test('设置页可改驱动与画面来源', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  const driver = page.locator('wg-settings .card').filter({ hasText: '驱动级输入' }).first();
  await driver.locator('input[type="checkbox"]').first().check();
  const reading = page.locator('wg-settings .card').filter({ hasText: '窗口画面' }).first();
  await reading.locator('select').nth(0).selectOption('PrintWindow');
  await reading.locator('select').nth(1).selectOption('Gdi');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.filter((name) => name === 'settings.update').length))
    .toBeGreaterThanOrEqual(3);
});

test('设置页可打开存储位置', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  await expect(page.locator('wg-settings .storage-row')).toHaveCount(1);
  await page.locator('wg-settings .storage-row fluent-button', { hasText: '打开' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.openFolder')))
    .toBe(true);
});

test('设置页可捕获系统热键', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  const hotkey = page.locator('wg-settings .hotkey').first();
  await hotkey.locator('fluent-button', { hasText: '设置快捷键' }).click();
  await page.keyboard.press('Control+Alt+F9');
  await expect(hotkey.locator('input')).toHaveValue('Ctrl+Alt+F9');
  await expect(page.locator('wg-nav .system .hotkey')).toContainText('Alt');
});

test('设置页可改语言与主题', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  const general = page.locator('wg-settings .card').first();
  await general.locator('select').selectOption('en');
  const theme = page.locator('wg-settings .card')
    .filter({ has: page.getByRole('heading', { name: 'Theme', exact: true }) }).first();
  await theme.locator('select').selectOption('light');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.filter((name) => name === 'settings.update').length))
    .toBeGreaterThanOrEqual(1);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('theme.set')))
    .toBe(true);
});

test('设置页可开关自动保存项目', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  const general = page.locator('wg-settings .card').first();
  const autoSave = general.locator('input[type="checkbox"]').nth(1);
  await expect(autoSave).toBeChecked();
  await autoSave.uncheck();

  await expect
    .poll(() => page.evaluate(() => window.__mock!.calls.some((call) =>
      call.method === 'settings.update'
      && (call.params as { patch?: { autoSaveProject?: boolean } }).patch?.autoSaveProject === false)))
    .toBe(true);
});

test('设置页可改常规与画面选项', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  await expect(page.locator('wg-settings h1')).toHaveText('设置');
  const general = page.locator('wg-settings .card').first();
  await general.locator('input[type="checkbox"]').first().uncheck();
  const frames = page.locator('wg-settings .card')
    .filter({ has: page.getByRole('heading', { name: '画面捕获', exact: true }) }).first();
  await frames.locator('input[type="checkbox"]').first().uncheck();
  await frames.locator('input[type="number"]').first().fill('30');
  await frames.locator('input[type="number"]').first().press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.update')))
    .toBe(true);
});

test('运行页载入宏并驱动运行面板', async ({ page }) => {
  await page.goto('/?bridge=mock&window=workbench#run');
  await expect(page.locator('wg-workbench')).toBeVisible();
  await expect(page.locator('wg-run .side .macros fluent-button')).toHaveCount(2);
  await page.locator('wg-run .side .macros fluent-button').first().click();
  await expect(page.locator('wg-run .step')).toHaveCount(2);
  await page.locator('wg-run .args input').first().fill('abc');
  await page.locator('wg-run .args input').first().press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('run.argument')))
    .toBe(true);
  await expect(page.locator('wg-run .vars')).toContainText('count');
  await expect(page.locator('wg-run .vars')).toContainText('found');
  await page.locator('wg-run .step input[type="checkbox"]').first().check();
  await page.locator('wg-run .rule input[type="number"]').first().fill('2');
  await page.locator('wg-run .rule input').nth(1).fill('i == 1');
  await page.locator('wg-run .rule input').nth(1).press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('run.breakpointRule')))
    .toBe(true);
  await page.locator('wg-run .toolbar fluent-button', { hasText: '运行' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('run.command')))
    .toBe(true);
});

test('公共宏库把条目交给工作台，而不是切主壳路由', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="公共宏库"]').click();
  const card = page.locator('wg-library .card', { hasText: '打开记事本' });
  await card.locator('fluent-button', { hasText: '在编辑器中插入' }).click();

  await expect
    .poll(() => page.evaluate(() => window.__mock!.seen.includes('app.openEditor')))
    .toBe(true);
  await expect(page).not.toHaveURL(/#editor$/);
});

test('公共宏库页列出并删除公共宏', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="公共宏库"]').click();
  await expect(page.locator('wg-library h1')).toHaveText('公共宏库');
  await expect(page.locator('wg-library')).toContainText('没有触发方式');
  await expect(page.locator('wg-library .card')).toHaveCount(1);
  await page.locator('wg-library .card fluent-button[aria-label="从公共宏库删除"]').click();
  await expect(page.locator('wg-library .card')).toHaveCount(0);
  await expect(page.locator('wg-library .empty')).toContainText('公共宏库还是空的');
});

test('宿主询问未保存宏时使用网页对话框并回传答案', async ({ page }) => {
  await page.goto('/?bridge=mock&window=workbench#editor');
  await expect(page.locator('wg-workbench')).toBeVisible();
  await page.evaluate(() => window.__mock!.push('editor.leaveAsked', {
    requestId: 41,
    documents: ['Macro 1'],
  }));

  const dialog = page.locator('wg-editor-unsaved .dialog');
  await expect(dialog).toContainText('Macro 1');
  await dialog.locator('fluent-button', { hasText: '取消' }).click();
  await expect
    .poll(() => page.evaluate(() => window.__mock!.calls.some((call) =>
      call.method === 'editor.leaveAnswered'
      && call.params.requestId === 41
      && call.params.leave === false)))
    .toBe(true);

  await page.evaluate(() => window.__mock!.push('editor.leaveAsked', {
    requestId: 42,
    documents: ['Macro 2'],
  }));
  await expect(page.locator('wg-editor-unsaved .dialog')).toContainText('Macro 2');
  await page.locator('wg-editor-unsaved .dialog fluent-button', { hasText: '放弃修改' }).click();
  await expect
    .poll(() => page.evaluate(() => window.__mock!.calls.some((call) =>
      call.method === 'editor.leaveAnswered'
      && call.params.requestId === 42
      && call.params.leave === true)))
    .toBe(true);
});

test('原生菜单请求编辑会进入工作台编辑器', async ({ page }) => {
  await page.goto('/?bridge=mock&window=workbench#editor');
  await expect(page.locator('wg-workbench')).toBeVisible();
  await page.evaluate(() => window.__mock!.push('editor.openRequested', { name: '游戏连招' }));
  await expect
    .poll(() => called(page, 'editor.session', { kind: 'open', name: '游戏连招' }))
    .toBe(true);
  await expect(page).toHaveURL(/#editor$/);
  await expect(page.locator('wg-editor-tabs .tab[aria-selected="true"]')).toContainText('游戏连招');
});

test('从主壳新建与编辑把请求交给工作台', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();

  await page.locator('wg-macros .filebar fluent-button', { hasText: '新建宏' }).click();
  await expect
    .poll(() => page.evaluate(() => window.__mock!.seen.includes('app.newEditor')))
    .toBe(true);
  await expect(page).not.toHaveURL(/#editor$/);

  await page.locator('wg-macros .card', { hasText: '自动保存' }).dblclick();
  await expect
    .poll(() => page.evaluate(() => window.__mock!.seen.includes('app.openEditor')))
    .toBe(true);
  await expect(page).not.toHaveURL(/#editor$/);
});

test('宏卡片的运行按钮把请求交给工作台', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();
  await page.locator('wg-macros .card', { hasText: '自动保存' })
    .locator('.iconbuttons button[title="运行调试"]').click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('app.openRun')))
    .toBe(true);
  await expect(page).not.toHaveURL(/#run$/);
});

test('导入导出向导走完一个往返', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="宏"]').click();

  // Import: the picker answers a path, the plan lists the package, the wizard asks the ticks and
  // the global conflict, and the apply report is shown before the wizard closes.
  await page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' }).click();
  await page.getByRole('menuitem', { name: '导入…' }).click();
  const wizard = page.locator('wg-transfer');
  await expect(wizard.locator('.row', { hasText: '包里的宏' })).toBeVisible();
  await expect(wizard).toContainText('opened-project.wgmacro');
  await expect(wizard.locator('.conflicts')).toContainText('计数');

  await wizard.locator('.row', { hasText: '包里的宏' }).locator('input[type="checkbox"]').check();
  await wizard.locator('select').selectOption('keep');
  await wizard.locator('fluent-button', { hasText: '导入' }).click();
  await expect(wizard.locator('.report')).toContainText('包里的宏');
  await wizard.locator('fluent-button', { hasText: '关闭' }).click();
  await expect(wizard.locator('.dialog')).toHaveCount(0);

  await expect(page.locator('wg-macros .card', { hasText: '包里的宏' })).toBeVisible();
  await expect
    .poll(() => called(page, 'project.preview', { path: '/mock/opened-project.wgmacro' }))
    .toBe(true);
  await expect
    .poll(() => page.evaluate(() => window.__mock!.seen.includes('project.importApply')))
    .toBe(true);

  // Export: the plan comes first, the file picker second, and the write carries the ticked name.
  await page.locator('wg-macros .card', { hasText: '包里的宏' })
    .locator('.iconbuttons button[title="导出宏…"]').click();
  await expect(wizard.locator('.row', { hasText: '包里的宏' }).locator('input')).toBeChecked();
  await wizard.locator('fluent-button', { hasText: '导出' }).click();
  await expect(wizard.locator('.report')).toContainText('/mock/');
  await wizard.locator('fluent-button', { hasText: '关闭' }).click();

  const exportCalls = await page.evaluate(() => window.__mock!.seen.filter(
    (method) => method === 'project.exportPlan' || method === 'project.exportApply'));
  expect(exportCalls).toEqual(['project.exportPlan', 'project.exportApply']);
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