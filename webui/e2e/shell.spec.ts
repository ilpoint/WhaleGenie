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

  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await page.locator('wg-macros .footer fluent-switch').click();
  await expect(status.locator('[data-status="system"]')).toHaveText('已启用');

  await page.locator('wg-macros .card fluent-switch').first().click();
  await expect(status.locator('[data-status="dirty"]')).toHaveText('有未保存修改');
});

test('首页关于进入网页关于页并显示第三方组件', async ({ page }) => {
  await page.locator('wg-home .link', { hasText: '关于' }).click();
  await expect(page.locator('wg-about h1')).toHaveText('关于');
  await expect(page.locator('wg-about')).toContainText('鲸灵');
  await page.locator('wg-about fluent-button', { hasText: '第三方组件' }).click();
  await expect(page.locator('wg-about #notices')).toContainText('Avalonia');
});

test('activity bar 图标切换页面并可折叠侧栏', async ({ page }) => {
  await expect(page.locator('wg-nav .activity svg')).toHaveCount(6);
  const macros = page.locator('wg-nav .activity button[aria-label="宏"]');
  await macros.click();
  await expect(page.locator('wg-macros .card .name').first()).toContainText('自动保存');
  await expect(page.locator('wg-nav .side')).toBeVisible();

  // Pressing the active icon again folds the side bar away; pressing it once more brings it back.
  await macros.click();
  await expect(page.locator('wg-nav .side')).toBeHidden();
  await macros.click();
  await expect(page.locator('wg-nav .side')).toBeVisible();

  await page.locator('wg-nav .activity button[aria-label="首页"]').click();
  await expect(page.locator('wg-home h1')).toHaveText('欢迎使用鲸灵');
});

test('标题栏文件与帮助菜单', async ({ page }) => {
  const file = page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' });
  await file.click();
  await expect(page.getByRole('menuitem', { name: '打开宏项目' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: '保存宏项目' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: '导入…' })).toBeVisible();

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
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.state')))
    .toBe(true);
});

test('导航切换到宏列表与占位页', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await expect(page.locator('wg-macros .card .name').first()).toContainText('自动保存');
  await expect(page.locator('wg-macros')).toContainText('每 1 分钟');

  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await expect(page.locator('wg-editor-gate h1')).toHaveText('编辑器');

  await page.locator('wg-nav button', { hasText: '运行调试' }).click();
  await expect(page.locator('wg-run h1')).toHaveText('运行与调试');
  await expect(page.locator('wg-run')).toContainText('运行面板');

  await page.locator('wg-nav button', { hasText: '首页' }).click();
  await expect(page.locator('wg-home h1')).toHaveText('欢迎使用鲸灵');
});

test('C0 编辑器打开真实宏、改字段并保存', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await expect(page.locator('wg-editor-gate h1')).toHaveText('编辑器');
  const name = page.locator('wg-editor-gate input').first();
  await expect(name).toHaveValue('自动保存');

  await page.locator('wg-editor-gate textarea').fill('C0 快照命令往返');
  await page.locator('wg-editor-gate fluent-button', { hasText: '保存' }).click();

  await expect
    .poll(async () => page.evaluate(() => {
      const seen = window.__mock!.seen;
      return seen.includes('editor.sessionPatch') && seen.includes('editor.sessionSave');
    }))
    .toBe(true);
});

test('C1 文档页签切换保留各自的 C# 会话', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('自动保存');

  await page.getByRole('tab', { name: '游戏连招' }).click();
  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('游戏连招');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionActivate')))
    .toBe(true);
});

test('C1 新建空白宏进入文档页签', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '新建宏' }).click();
  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('未命名宏');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionNew')))
    .toBe(true);
});

test('C1 从项目宏列表打开另一份文档', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .open-macro select').selectOption({ label: '游戏连招' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '打开宏' }).click();
  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('游戏连招');
});

test('C1 步骤树可以加步骤与改参数', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await expect(page.locator('wg-editor-gate h1')).toHaveText('编辑器');

  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await expect(page.locator('wg-editor-gate .step-name')).toHaveText('延迟');

  await page.locator('wg-editor-gate .step').click();
  const parameter = page.locator('wg-editor-gate .inspector .param input').first();
  await expect(parameter).toBeVisible();
  await parameter.fill('750');
  await parameter.press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionParameter')))
    .toBe(true);
});

test('C1 步骤可以放进块内部', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '重复' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '重复' }).click();

  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '延迟' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加到选中步骤内' }).click();
  await expect(page.locator('wg-editor-gate .tree ol .step-name')).toHaveText('延迟');
});

test('C1 步骤可改备注、超时与失败处理', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '延迟' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '延迟' }).click();

  const meta = page.locator('wg-editor-gate .step-meta');
  await meta.locator('input').nth(0).fill('retry it');
  await meta.locator('input').nth(0).press('Tab');
  await meta.locator('input').nth(1).fill('500');
  await meta.locator('input').nth(1).press('Tab');
  await meta.locator('select').selectOption({ label: '继续下一步' });

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionStepMeta')))
    .toBe(true);
});

test('C1 可以开始与停止录制', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '开始录制' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionRecordStart')))
    .toBe(true);
  await page.locator('wg-editor-gate fluent-button', { hasText: '停止录制' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionRecordStop')))
    .toBe(true);
});

test('C1 条件参数可按行编辑', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .action-search').fill('等待条件');
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '等待条件' }).click();

  const rows = page.locator('wg-editor-gate .condition-row');
  await expect(rows).toHaveCount(1);
  await page.locator('wg-editor-gate .condition-row input').nth(0).fill('$found');
  await page.locator('wg-editor-gate .condition-row input').nth(0).press('Tab');
  await page.locator('wg-editor-gate .condition-row select').first().selectOption('Equal');
  await page.locator('wg-editor-gate .condition-row input').nth(1).fill('1');
  await page.locator('wg-editor-gate .condition-row input').nth(1).press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionConditionRow')))
    .toBe(true);
});

test('C1 宏可声明参数与返回值', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加形参' }).click();
  const parameters = page.locator('wg-editor-gate .declarations').first();
  await parameters.locator('input').first().fill('count');
  await parameters.locator('input').first().press('Tab');
  await parameters.locator('input[type="checkbox"]').check();
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加结果' }).click();

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionParameterDeclAdd')))
    .toBe(true);
});

test('C1 记录形参可声明字段', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加形参' }).click();
  const row = page.locator('wg-editor-gate .declarations').first().locator('tbody tr').first();
  await row.locator('select').selectOption('record');
  await page.locator('wg-editor-gate .record-field-list fluent-button', { hasText: '添加字段' }).click();
  await page.locator('wg-editor-gate .record-field-row input').fill('left');
  await page.locator('wg-editor-gate .record-field-row input').press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionParameterFieldPatch')))
    .toBe(true);
});

test('C1 宏设置可改输入限制与启停键', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  const settings = page.locator('wg-editor-gate .macro-settings');
  await settings.locator('input[type="checkbox"]').uncheck();
  await settings.locator('input[type="number"]').fill('30');
  await settings.locator('input[type="number"]').press('Tab');
  await settings.locator('input').last().fill('F8');
  await settings.locator('input').last().press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionMacroSetting')))
    .toBe(true);
});

test('C1 子宏文档可从父宏打开', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .submacros fluent-button', { hasText: '辅助宏' }).click();

  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('辅助宏');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionSubOpen')))
    .toBe(true);
});

test('C1 条件可以嵌套组', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .action-search').fill('等待条件');
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '等待条件' }).click();

  await page.locator('wg-editor-gate .condition-add fluent-button', { hasText: '添加组' }).click();
  const nested = page.locator('wg-editor-gate .condition-nested');
  await expect(nested).toHaveCount(1);
  await nested.locator('.condition-add fluent-button', { hasText: '添加行' }).click();
  await expect(nested.locator('.condition-row')).toHaveCount(1);
});

test('C1 动作选择器可按名称过滤', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .action-search').fill('延迟');
  await expect(page.locator('wg-editor-gate .toolbar select option')).toHaveCount(1);
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await expect(page.locator('wg-editor-gate .step-name')).toHaveText('延迟');
});

test('C1 编辑器快捷键保存与关闭', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.keyboard.press('Control+s');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionSave')))
    .toBe(true);
});

test('C1 步骤可复制', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '延迟' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.getByRole('button', { name: '复制步骤' }).first().click();
  await expect(page.locator('wg-editor-gate .step')).toHaveCount(2);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionStepDuplicate')))
    .toBe(true);
});

test('C1 取值构建器可插入变量与函数', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '延迟' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '延迟' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '构建取值' }).first().click();
  await page.locator('wg-editor-gate .value-dialog .value-buttons button', { hasText: 'found' }).click();
  await page.locator('wg-editor-gate .value-dialog fluent-button', { hasText: '确定' }).click();

  await expect(page.locator('wg-editor-gate .inspector .param input').first()).toHaveValue('$found');
});

test('C1 取值选择器可插入变量', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '延迟' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '延迟' }).click();
  await page.locator('wg-editor-gate .value-picker').first().selectOption({ label: 'found' });

  await expect(page.locator('wg-editor-gate .inspector .param input').first()).toHaveValue('$found');
});

test('C1 步骤可启用停用', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step input[type="checkbox"]').first().uncheck();

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionStepEnabled')))
    .toBe(true);
});

test('C1 键位字段可捕获按键', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '按键' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '按键' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '设置组合键' }).click();
  await page.keyboard.press('F8');

  await expect(page.locator('wg-editor-gate .key-row input')).toHaveValue('F8');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionParameter')))
    .toBe(true);
});

test('C1 图片字段可调用原生选择器', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '找图' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '找图' }).click();
  await page.locator('wg-editor-gate .file-row fluent-button', { hasText: '浏览' }).click();

  await expect(page.locator('wg-editor-gate .file-row input')).toHaveValue('C:\\shot.png');
});

test('C1 行表参数可加行并填单元格', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '重复' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '重复' }).click();

  await page.locator('wg-editor-gate fluent-button', { hasText: '添加行' }).click();
  const cells = page.locator('wg-editor-gate .row-list tbody input');
  await cells.nth(0).fill('found');
  await cells.nth(0).press('Tab');
  await cells.nth(1).fill('hit');
  await cells.nth(1).press('Tab');

  await expect(page.locator('wg-editor-gate .row-list tbody tr')).toHaveCount(1);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionParameterRowCell')))
    .toBe(true);
});

test('C1 颜色字段可用取色器改值', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .toolbar select').selectOption({ label: '颜色' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加步骤' }).click();
  await page.locator('wg-editor-gate .step', { hasText: '颜色' }).click();

  const picker = page.locator('wg-editor-gate .color-row input[type="color"]');
  await picker.evaluate((element) => {
    const input = element as HTMLInputElement;
    input.value = '#ff8800';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await expect(page.locator('wg-editor-gate .color-row input').last()).toHaveValue('#ff8800');
});

test('C1 程序与字串触发可编辑', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .trigger-add select').selectOption({ label: '程序' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加入口' }).click();
  const process = page.locator('wg-editor-gate .process-fields input');
  await process.fill('notepad');
  await process.press('Tab');
  await page.locator('wg-editor-gate .process-fields select').selectOption({ label: '退出' });

  await page.locator('wg-editor-gate .trigger-add select').selectOption({ label: '字串' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加入口' }).click();
  const hotstring = page.locator('wg-editor-gate .hotstring-field');
  await hotstring.fill('hello');
  await hotstring.press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerProcess')))
    .toBe(true);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerHotstring')))
    .toBe(true);
});

test('C1 文件触发可设路径与变化', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .trigger-add select').selectOption({ label: '文件' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加入口' }).click();

  const path = page.locator('wg-editor-gate .file-fields input').first();
  await path.fill('notes.txt');
  await path.press('Tab');
  await page.locator('wg-editor-gate .file-fields select').selectOption({ label: '修改' });

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerFile')))
    .toBe(true);
});

test('C1 定时触发可设间隔与单位', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate .trigger-add select').selectOption({ label: '定时' });
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加入口' }).click();

  const interval = page.locator('wg-editor-gate .timer-fields input[type="number"]');
  await interval.fill('7');
  await interval.press('Tab');
  await page.locator('wg-editor-gate .timer-fields select').last().selectOption({ label: '分钟' });

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerTimer')))
    .toBe(true);
});

test('C1 局部变量墙可新建与改名', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '新建变量' }).first().click();

  const name = page.locator('wg-editor-gate .variable-row input').first();
  await name.fill('count');
  await name.press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionVariablePatch')))
    .toBe(true);
  await expect(page.locator('wg-editor-gate .variable-row')).toHaveCount(1);
});

test('C1 全局变量墙可新建与改名', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '新建变量' }).nth(1).click();
  const name = page.locator('wg-editor-gate .global-row input').first();
  await name.fill('score');
  await name.press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionGlobalPatch')))
    .toBe(true);
});

test('C1 编辑宏的触发入口与按键', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '编辑器' }).click();
  await page.locator('wg-editor-gate fluent-button', { hasText: '添加入口' }).click();
  const binding = page.locator('wg-editor-gate .triggers input[data-capture]').first();
  await expect(binding).toBeVisible();
  await page.locator('wg-editor-gate .triggers fluent-button', { hasText: '设置组合键' }).click();
  await page.keyboard.press('F8');
  await expect(binding).toHaveValue('F8');

  await page.locator('wg-editor-gate .triggers select').selectOption({ label: '按住期间循环（长按）' });
  await page.locator('wg-editor-gate .triggers .once input').check();
  await page.locator('wg-editor-gate .triggers .enabled input').uncheck();

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerEnabled')))
    .toBe(true);

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerBinding')))
    .toBe(true);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerLoop')))
    .toBe(true);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerOnce')))
    .toBe(true);
});

test('宏列表的过滤、搜索与启停', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();

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
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await page.locator('wg-macros .footer fluent-switch').click();
  await expect(page.locator('wg-macros .footer')).toContainText('已启用');
});

test('打开包后标题与列表跟着换，保存清掉未保存标记', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await expect(page.locator('wg-titlebar .title')).toContainText('未命名项目');

  // 打开：文件按钮先问程序，mock 宿主给出一个固定路径；标题与列表都换成包里的。
  // 按钮文案是词表的原话（"打开宏项目"），选择器必须按整句匹配。
  await page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' }).click();
  await page.getByRole('menuitem', { name: '打开宏项目' }).click();
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
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await page.locator('wg-macros input[type="search"]').click();

  // 宿主靠这条报告决定系统热键是否该让路；mock 只记录方法名，真实标志在 C# 侧与真机验收。
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('window.focus')))
    .toBe(true);
});

test('关闭全部先问页面，确认后清空', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' }).click();
  await page.getByRole('menuitem', { name: '全部关闭' }).click();

  // 程序推 dialog.request，页面把问题画出来；点卡片里的确认键，列表清空、标题回未命名。
  await expect(page.locator('wg-confirm h2')).toContainText('关闭全部宏');
  await page.locator('wg-confirm .actions fluent-button[appearance="accent"]').click();
  await expect(page.locator('wg-macros .card')).toHaveCount(0);
  await expect(page.locator('wg-titlebar .title')).toContainText('未命名项目');
});

test('删除宏先问页面，确认后离开列表', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
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
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
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

test('工程宏可加入宏库并覆盖条目', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏库' }).click();
  await page.locator('wg-library .source select').selectOption({ label: '自动保存' });
  await page.locator('wg-library .source fluent-button', { hasText: '添加选中的工程宏' }).click();
  await expect(page.locator('wg-library .card')).toHaveCount(2);
  await page.locator('wg-library .card', { hasText: '自动保存' })
    .locator('fluent-button[title="用选中的工程宏覆盖"]').click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('library.replace')))
    .toBe(true);
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
  await menu.locator('fluent-button', { hasText: '鼠标中键（默认）' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.menu.capture')))
    .toBe(true);

  const rules = page.locator('wg-settings .card').filter({ hasText: '下方区域窗口规则' }).first();
  await rules.locator('fluent-button', { hasText: '添加规则' }).click();
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

test('设置页可改常规与画面选项', async ({ page }) => {
  await page.locator('wg-nav .activity button[aria-label="设置"]').click();
  await expect(page.locator('wg-settings h1')).toHaveText('设置');
  const general = page.locator('wg-settings .card').first();
  await general.locator('input[type="checkbox"]').first().uncheck();
  const frames = page.locator('wg-settings .card')
    .filter({ has: page.getByRole('heading', { name: '画面', exact: true }) }).first();
  await frames.locator('input[type="checkbox"]').first().uncheck();
  await frames.locator('input[type="number"]').first().fill('30');
  await frames.locator('input[type="number"]').first().press('Tab');

  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('settings.update')))
    .toBe(true);
});

test('运行页载入宏并驱动运行面板', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '运行调试' }).click();
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

test('宏库页列出并删除公共宏', async ({ page }) => {
  await page.locator('wg-nav button', { hasText: '宏库' }).click();
  await expect(page.locator('wg-library h1')).toHaveText('共享宏');
  await expect(page.locator('wg-library .card')).toHaveCount(1);
  await page.locator('wg-library .card fluent-button[aria-label="从宏库删除"]').click();
  await expect(page.locator('wg-library .card')).toHaveCount(0);
  await expect(page.locator('wg-library .empty')).toHaveText('宏库还没有宏。');
});

test('原生菜单请求编辑会进入网页编辑器', async ({ page }) => {
  await expect(page.locator('wg-nav')).toBeVisible();
  await page.evaluate(() => window.__mock!.push('editor.openRequested', { name: '游戏连招' }));
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionOpen')))
    .toBe(true);
  await expect(page).toHaveURL(/#editor$/);
  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('游戏连招');
});

test('从宏列表新建与编辑进入网页编辑器', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();

  await page.locator('wg-macros .footer fluent-button', { hasText: '新建宏' }).click();
  await expect(page).toHaveURL(/#editor$/);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.filter((name) => name.startsWith('editor'))))
    .toContain('editor.sessionNew');
  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('未命名宏');

  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await page.locator('wg-macros .card', { hasText: '自动保存' }).dblclick();
  await expect(page.locator('wg-editor-gate input').first()).toHaveValue('自动保存');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionOpen')))
    .toBe(true);
});

test('宏卡片的运行按钮进入网页运行面板', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await page.locator('wg-macros .card', { hasText: '自动保存' })
    .locator('.iconbuttons button[title="运行调试"]').click();
  await expect(page.locator('wg-run')).toBeVisible();
  await expect(page.locator('wg-run')).toContainText('Step one');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('app.openRun')))
    .toBe(true);
});

test('导入命令过桥', async ({ page }) => {
  await page.locator('wg-nav .side').getByText('宏', { exact: true }).click();
  await page.locator('wg-titlebar .menu', { hasText: '文件' }).getByRole('button', { name: '文件' }).click();
  await page.getByRole('menuitem', { name: '导入…' }).click();
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