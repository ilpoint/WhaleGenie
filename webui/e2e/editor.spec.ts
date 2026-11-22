import { expect, test, type Locator, type Page } from '@playwright/test';

async function openEditor(page: Page): Promise<void> {
  await page.goto('/?bridge=mock#editor');
  await expect(page.locator('wg-titlebar .title')).toContainText('鲸灵');
  await expect(page.locator('wg-editor-tabs .tab').first()).toBeVisible();
}

async function selectSource(page: Page, label: string): Promise<void> {
  await page.locator('wg-editor-palette .source', { hasText: label }).click();
  await page.waitForTimeout(60);
}

async function completeActionForm(page: Page): Promise<void> {
  const form = page.locator('wg-action-editor .dialog');
  await expect(form).toBeVisible();

  const nameChip = form.locator('.param[data-parameter="name"] wg-value-field .chip');
  if (await nameChip.count() > 0) {
    await nameChip.first().click();
    const value = page.locator('wg-value-editor .dialog');
    await expect(value).toBeVisible();
    const typed = value.locator('.typed input');
    if (await typed.count() > 0) {
      await typed.fill('found');
      await value.locator('.typed button', { hasText: '插入' }).click();
    } else {
      await value.locator('.panel', { hasText: '变量' })
        .locator('.chips button', { hasText: 'found' }).first().click();
    }
    await value.locator('footer fluent-button').last().click();
    await expect(value).toHaveCount(0);
  }

  if (await form.locator('.condition-row').count() === 0
      && await form.locator('.condition-add').count() > 0) {
    await form.locator('.condition-add button', { hasText: '添加行' }).click();
    const row = form.locator('.condition-row').last();
    await row.locator('input').nth(0).fill('found');
    await row.locator('input').nth(1).fill('1');
  }

  const names = await form.locator('.param').evaluateAll((nodes) => nodes
    .map((node) => node.getAttribute('data-parameter') ?? '')
    .filter((name) => name.length > 0));
  for (const name of names) {
    const param = form.locator('.param[data-parameter="' + name + '"]');
    if (await param.count() === 0) {
      continue;
    }

    const inputs = param.locator('input:not([type="checkbox"]):visible');
    for (let at = 0; at < await inputs.count(); at++) {
      const input = inputs.nth(at);
      if (await input.inputValue() !== '') {
        continue;
      }

      const label = await param.locator('.label, label').first()
        .textContent({ timeout: 1000 }).catch(() => '') ?? '';
      const value = name.includes('key') || label.includes('键')
        ? 'F8'
        : name.includes('path') || label.includes('图片') || label.includes('路径')
          ? 'C:\\shot.png'
          : await input.getAttribute('type') === 'number' ? '1' : 'found';
      await input.fill(value);
      await input.evaluate((element) => (element as HTMLInputElement).blur());
      await page.waitForTimeout(30);
    }
  }

  const confirm = form.locator('footer fluent-button').last();
  await expect(confirm).toBeEnabled();
  await confirm.click();
  await expect(form).toHaveCount(0);
}

async function addStep(page: Page, source: string, action: string): Promise<void> {
  await selectSource(page, source);
  await page.locator('wg-editor-palette .card', { hasText: action }).first().click();
  await page.locator('wg-editor .toolbar fluent-button', { hasText: '添加步骤' }).click();
  await completeActionForm(page);
  await expect(page.locator('wg-step-tree .row').last()).toBeVisible();
}

async function dragActionCard(page: Page, card: Locator, row: Locator): Promise<void> {
  const data = await page.evaluateHandle(() => new DataTransfer());
  await card.evaluate((element, transfer) => {
    transfer.setData('application/x-whalegenie-action', 'control.delay');
    transfer.setData('text/plain', 'control.delay');
    element.dispatchEvent(new DragEvent('dragstart', { dataTransfer: transfer, bubbles: true }));
  }, data);
  await row.evaluate((element, transfer) => {
    const box = element.getBoundingClientRect();
    const init = {
      dataTransfer: transfer,
      bubbles: true,
      cancelable: true,
      clientX: box.left + 40,
      clientY: box.bottom - 3,
    };
    element.dispatchEvent(new DragEvent('dragover', init));
    element.dispatchEvent(new DragEvent('drop', init));
  }, data);
}

async function openStepEditor(page: Page, action: string): Promise<void> {
  await page.locator('wg-step-tree .row', { hasText: action }).first().dblclick();
  await expect(page.locator('wg-action-editor .dialog')).toBeVisible();
}

async function closeStepEditor(page: Page): Promise<void> {
  await page.locator('wg-action-editor footer fluent-button').last().click();
  await expect(page.locator('wg-action-editor .dialog')).toHaveCount(0);
}

test('编辑器显示动作库、堆叠变量中心与宏设置', async ({ page }) => {
  await openEditor(page);
  await expect(page.locator('wg-editor-tabs .tab[aria-selected="true"]')).toContainText('自动保存');
  await expect(page.locator('wg-editor-palette .source')).toHaveCount(4);
  await expect(page.locator('wg-editor-inspector wg-variable-wall')).toBeVisible();
  await expect(page.locator('wg-editor-inspector wg-macro-settings')).toBeVisible();
  await expect(page.locator('wg-editor-inspector > .tabs')).toHaveCount(0);
  await expect(page.locator('wg-macro-settings .title')).toContainText('宏设置');
});

test('从其他页面请求打开宏会进入对应页签', async ({ page }) => {
  await openEditor(page);
  await page.evaluate(() => window.__mock!.push('editor.openRequested', { name: '游戏连招' }));
  await expect(page.locator('wg-editor-tabs .tab[aria-selected="true"]')).toContainText('游戏连招');
  await page.locator('wg-editor-tabs .tab', { hasText: '自动保存' }).click();
  await expect(page.locator('wg-editor-tabs .tab[aria-selected="true"]')).toContainText('自动保存');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionActivate')))
    .toBe(true);
});

test('导出按钮把当前文档写给宿主', async ({ page }) => {
  await openEditor(page);
  await page.locator('wg-editor-tabs .export').click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionExport')))
    .toBe(true);
  await expect(page.locator('wg-editor .notice')).toContainText('已导出');
});

test('新建与关闭干净页签', async ({ page }) => {
  await openEditor(page);
  await page.locator('wg-editor-tabs .new').click();
  await expect(page.locator('wg-editor-tabs .tab', { hasText: '未命名宏' })).toBeVisible();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionNew')))
    .toBe(true);
  const tab = page.locator('wg-editor-tabs .tab-wrap', { hasText: '未命名宏' });
  await tab.locator('.close').click();
  await expect(page.locator('wg-editor-tabs .tab', { hasText: '未命名宏' })).toHaveCount(0);
});

test('脏页签关闭前询问，取消保留，确认后保存', async ({ page }) => {
  await openEditor(page);
  await page.locator('wg-editor-tabs .new').click();
  await addStep(page, '流程控制', '延迟');
  const tab = page.locator('wg-editor-tabs .tab-wrap', { hasText: '未命名宏' });
  await tab.locator('.close').click();
  await expect(page.locator('wg-editor-tabs .confirm')).toBeVisible();
  await page.locator('wg-editor-tabs .confirm fluent-button', { hasText: '取消' }).click();
  await expect(tab).toBeVisible();
  await tab.locator('.close').click();
  await page.locator('wg-editor-tabs .confirm fluent-button', { hasText: '保存' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionSave')))
    .toBe(true);
  await expect(page.locator('wg-editor-tabs .tab', { hasText: '未命名宏' })).toHaveCount(0);
});

test('公共宏库可列出并放入调用步骤', async ({ page }) => {
  await openEditor(page);
  await selectSource(page, '公共宏');
  await expect(page.locator('wg-editor-palette .sub-card', { hasText: '发送邮件' })).toBeVisible();
  await page.locator('wg-editor-palette .sub-card', { hasText: '发送邮件' }).click();
  await expect(page.locator('wg-step-tree .row').first()).toContainText('调用宏');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionPublicMacroAdd')))
    .toBe(true);
});

test('动作库属于工作区而不是侧栏', async ({ page }) => {
  await openEditor(page);
  await expect(page.locator('wg-editor wg-editor-palette')).toBeVisible();
  await expect(page.locator('wg-nav .side')).toBeHidden();
  const box = await page.locator('wg-editor .palette-column').boundingBox();
  expect(box?.width ?? 0).toBeGreaterThanOrEqual(279);
});

test('双击动作卡先打开表单，取消不添加步骤', async ({ page }) => {
  await openEditor(page);
  await selectSource(page, '流程控制');
  await page.locator('wg-editor-palette .action-search').fill('延迟');
  await page.locator('wg-editor-palette .card', { hasText: '延迟' }).dblclick();
  await expect(page.locator('wg-action-editor .dialog')).toBeVisible();
  await expect(page.locator('wg-step-tree .row')).toHaveCount(0);
  await page.locator('wg-action-editor footer fluent-button', { hasText: '取消' }).click();
  await expect(page.locator('wg-action-editor .dialog')).toHaveCount(0);
  await expect(page.locator('wg-step-tree .row')).toHaveCount(0);
});

test('编辑步骤表单可切页并在取消后保留原值', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  await openStepEditor(page, '延迟');
  const ms = page.locator('wg-action-editor .param[data-parameter="ms"] wg-value-field input');
  await expect(ms).toHaveValue('1');
  await page.locator('wg-action-editor .tabs button', { hasText: '执行与容错' }).click();
  await expect(page.locator('wg-action-editor .step-meta')).toBeVisible();
  await page.locator('wg-action-editor footer fluent-button', { hasText: '取消' }).click();
  await expect(page.locator('wg-action-editor .dialog')).toHaveCount(0);
  await openStepEditor(page, '延迟');
  await expect(page.locator('wg-action-editor .param[data-parameter="ms"] wg-value-field input'))
    .toHaveValue('1');
  await page.locator('wg-action-editor footer fluent-button', { hasText: '取消' }).click();
});

test('拖动作卡到步骤列表会先打开表单再插入接缝', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  await addStep(page, '流程控制', '备注');
  const rows = page.locator('wg-step-tree .row');
  await expect(rows).toHaveCount(2);
  await selectSource(page, '流程控制');
  await page.locator('wg-editor-palette .action-search').fill('延迟');
  const card = page.locator('wg-editor-palette .card', { hasText: '延迟' });
  const target = rows.nth(1);
  await dragActionCard(page, card, target);
  await expect(page.locator('wg-action-editor .dialog')).toBeVisible();
  await expect(page.locator('wg-step-tree .row')).toHaveCount(2);
  await completeActionForm(page);
  await expect(page.locator('wg-step-tree .row')).toHaveCount(3);
  await expect(page.locator('wg-step-tree .row .name').nth(2)).toContainText('延迟');
});

test('动作表单的其它设置展示步骤 JSON', async ({ page }) => {
  await openEditor(page);
  await selectSource(page, '流程控制');
  await page.locator('wg-editor-palette .action-search').fill('延迟');
  await page.locator('wg-editor-palette .card', { hasText: '延迟' }).dblclick();
  await page.locator('wg-action-editor .tabs button', { hasText: '其它设置' }).click();
  await expect(page.locator('wg-action-editor .json')).toContainText('control.delay');
  await page.locator('wg-action-editor footer fluent-button', { hasText: '取消' }).click();
});

test('动作库可按来源、分类与名称过滤', async ({ page }) => {
  await openEditor(page);
  await selectSource(page, '流程控制');
  await expect(page.locator('wg-editor-palette .card', { hasText: '重复' })).toBeVisible();
  await page.locator('wg-editor-palette .action-search').fill('重复');
  await expect(page.locator('wg-editor-palette .card')).toHaveCount(1);
  await selectSource(page, '基础动作');
  await page.locator('wg-editor-palette .action-search').evaluate((input) => {
    (input as HTMLInputElement).value = '';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await page.locator('wg-editor-palette .rail button', { hasText: '键鼠' }).click();
  await expect(page.locator('wg-editor-palette .card', { hasText: '按键' })).toBeVisible();
  await expect(page.locator('wg-editor-palette .card', { hasText: '读取文件' })).toHaveCount(0);
});

test('数字字段带内嵌取值钮，取值窗口可插入并确认', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  await openStepEditor(page, '延迟');
  const field = page.locator('wg-action-editor .param[data-parameter="ms"] wg-value-field');
  await expect(field.locator('.chip')).toHaveText(/取值/);
  await field.locator('.chip').click();
  await expect(page.locator('wg-value-editor .dialog')).toBeVisible();
  await page.locator('wg-value-editor .band-head button[title*="清空"]').click();
  await page.locator('wg-value-editor .typed button', { hasText: '数字' }).click();
  await page.locator('wg-value-editor .typed input').fill('750');
  await page.locator('wg-value-editor .typed button', { hasText: '插入' }).click();
  await expect(page.locator('wg-value-editor .band')).toContainText('750');
  await page.locator('wg-value-editor footer fluent-button', { hasText: '确定' }).click();
  await expect(page.locator('wg-value-editor .dialog')).toHaveCount(0);
  await expect(field.locator('input.number')).toHaveValue('750');
  await page.locator('wg-action-editor footer fluent-button').last().click();
  await expect(page.locator('wg-action-editor .dialog')).toHaveCount(0);
  await expect(page.locator('wg-step-tree .row .detail').first()).toContainText('750');
});

test('取值窗口可从变量墙插入名字', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '基础动作', '设置变量');
  await openStepEditor(page, '设置变量');
  const field = page.locator('wg-action-editor .param[data-parameter="value"] wg-value-field');
  await field.locator('.chip').click();
  await expect(page.locator('wg-value-editor .dialog')).toBeVisible();
  await page.locator('wg-value-editor .panel', { hasText: '变量' })
    .locator('.chips button', { hasText: 'found' }).click();
  await expect(page.locator('wg-value-editor .band')).toContainText('found');
  await page.locator('wg-value-editor footer fluent-button', { hasText: '确定' }).click();
  await expect(page.locator('wg-value-editor .dialog')).toHaveCount(0);
  await expect(field).toContainText('found');
});

test('步骤元数据在独立编辑窗修改', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  await openStepEditor(page, '延迟');
  await page.locator('wg-action-editor .tabs button', { hasText: '执行与容错' }).click();
  const meta = page.locator('wg-action-editor .step-meta');
  const numbers = meta.locator('input[type="number"]');
  await numbers.nth(0).fill('500');
  await numbers.nth(0).press('Tab');
  await meta.locator('label', { hasText: '继续下一步' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.actionMeta')))
    .toBe(true);
  await closeStepEditor(page);
});

test('块的分节可以把子步骤加进去', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '重复');
  await selectSource(page, '基础动作');
  await page.locator('wg-editor-palette .action-search').fill('设置变量');
  await page.locator('wg-editor-palette .card', { hasText: '设置变量' }).click();
  await page.locator('wg-step-tree .section-add').first().click();
  await completeActionForm(page);
  await expect(page.locator('wg-step-tree .section-head', { hasText: '循环体' })).toBeVisible();
  await expect(page.locator('wg-step-tree .sections .row .name')).toHaveText('设置变量');
});

test('多选步骤可一起启用、分组并留下一个块', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  await addStep(page, '流程控制', '备注');
  const rows = page.locator('wg-step-tree .row');
  await rows.nth(0).click();
  await rows.nth(1).click({ modifiers: ['Control'] });
  await expect(page.locator('wg-step-tree .row[data-selected="true"]')).toHaveCount(2);
  await page.keyboard.press('Control+e');
  await expect(page.locator('wg-step-tree .row input.enabled').nth(0)).not.toBeChecked();
  await expect(page.locator('wg-step-tree .row input.enabled').nth(1)).not.toBeChecked();
  await page.keyboard.press('Control+g');
  await expect(page.locator('wg-step-tree > .scroll > .list > .step')).toHaveCount(1);
  await expect(page.locator('wg-step-tree .section-head .label')).toHaveText('步骤');
  await expect(page.locator('wg-step-tree .sections .row')).toHaveCount(2);
});

test('右键菜单可复制并粘贴选中的步骤', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  const first = page.locator('wg-step-tree .row').first();
  await first.click({ button: 'right' });
  await expect(page.locator('wg-step-tree .menu')).toBeVisible();
  await page.locator('wg-step-tree .menu button', { hasText: '复制选中' }).click();
  await expect(page.locator('wg-step-tree .menu')).toHaveCount(0);
  await page.locator('wg-step-tree .row').first().click();
  await page.keyboard.press('Control+v');
  await expect(page.locator('wg-step-tree .row')).toHaveCount(2);
});

test('Escape 清空多选，Ctrl+A 选中整棵树', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '重复');
  await addStep(page, '基础动作', '设置变量');
  await page.locator('wg-step-tree .row').first().click();
  await page.keyboard.press('Control+a');
  await expect(page.locator('wg-step-tree .row[data-selected="true"]')).toHaveCount(2);
  await page.keyboard.press('Escape');
  await expect(page.locator('wg-step-tree .row[data-selected="true"]')).toHaveCount(0);
});

test('拖到另一行下方会改变步骤顺序', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  await addStep(page, '流程控制', '备注');
  const rows = page.locator('wg-step-tree .row');
  const targetBox = await rows.nth(1).boundingBox();
  await rows.nth(0).dragTo(rows.nth(1), {
    targetPosition: { x: 40, y: Math.max(2, (targetBox?.height ?? 30) - 3) },
  });
  await expect(page.locator('wg-step-tree .row .name').nth(0)).toHaveText('备注');
  await expect(page.locator('wg-step-tree .row .name').nth(1)).toHaveText('延迟');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionStepsMove')))
    .toBe(true);
});

test('多分支可以加一支', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '多分支');
  await page.locator('wg-step-tree .section-add').first().click();
  await expect(page.locator('wg-step-tree .row .name').filter({ hasText: /^分支$/ })).toHaveCount(1);
  await expect(page.locator('wg-step-tree .row')).toHaveCount(2);
});

test('条件参数按行编辑', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '等待条件');
  await openStepEditor(page, '等待条件');
  await page.locator('wg-action-editor .condition-add button', { hasText: '添加行' }).click();
  const row = page.locator('wg-action-editor .condition-row').first();
  await row.locator('input').nth(0).fill('$found');
  await row.locator('input').nth(0).press('Tab');
  await row.locator('select').first().selectOption('Equal');
  await row.locator('input').nth(1).fill('1');
  await row.locator('input').nth(1).press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionConditionRow')))
    .toBe(true);
  await closeStepEditor(page);
});

test('宏设置改名并保存，页签跟着改名', async ({ page }) => {
  await openEditor(page);
  const name = page.locator('wg-macro-settings [data-field="name"]');
  await name.fill('我的宏');
  await name.press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionPatch')))
    .toBe(true);
  await page.locator('wg-macro-settings .footer fluent-button', { hasText: '保存' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionSave')))
    .toBe(true);
  await expect(page.locator('wg-editor-tabs .tab[aria-selected="true"]')).toContainText('我的宏');
});

test('专属设置可改输入限制与启停键', async ({ page }) => {
  await openEditor(page);
  await page.locator('wg-macro-settings .tabs button', { hasText: '专属设置' }).click();
  await page.locator('wg-macro-settings [data-setting="followInputLimit"]').uncheck();
  const limit = page.locator('wg-macro-settings [data-setting="inputLimit"]');
  await limit.fill('30');
  await limit.press('Tab');
  const toggle = page.locator('wg-macro-settings [data-setting="toggleKey"]');
  await toggle.fill('F8');
  await toggle.press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionMacroSetting')))
    .toBe(true);
});

test('宏可声明形参与返回值', async ({ page }) => {
  await openEditor(page);
  await page.locator('wg-macro-settings .tabs button', { hasText: '专属设置' }).click();
  await page.getByRole('button', { name: '添加形参' }).click();
  const parameter = page.locator('wg-macro-settings .declaration').first();
  await parameter.locator('input').first().fill('count');
  await parameter.locator('input').first().press('Tab');
  await parameter.locator('input[type="checkbox"]').check();
  await page.getByRole('button', { name: '添加结果' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionParameterDeclAdd')))
    .toBe(true);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionResultDeclAdd')))
    .toBe(true);
});

test('触发入口可添加并捕获按键', async ({ page }) => {
  await openEditor(page);
  await page.locator('wg-macro-settings select[aria-label="添加入口"]').selectOption({ label: '按键' });
  await page.locator('wg-macro-settings button.link-button', { hasText: '添加入口' }).click();
  const trigger = page.locator('wg-macro-settings .trigger').first();
  await expect(trigger).toBeVisible();
  await page.locator('wg-macro-settings button.link-button', { hasText: '设置组合键' }).click();
  await page.keyboard.press('F8');
  await expect(page.locator('wg-macro-settings [data-trigger-capture]')).toHaveValue('F8');
  await trigger.locator('input[type="checkbox"]').uncheck();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerBinding')))
    .toBe(true);
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionTriggerEnabled')))
    .toBe(true);
});

test('变量中心始终可见并按组过滤', async ({ page }) => {
  await openEditor(page);
  await page.locator('wg-variable-wall .filters button', { hasText: '局部变量' }).click();
  await expect(page.locator('wg-variable-wall .var', { hasText: 'found' })).toBeVisible();
  await page.locator('wg-variable-wall .head button', { hasText: '局部变量' }).click();
  const dialog = page.locator('wg-local-variables');
  await expect(dialog.locator('.dialog')).toBeVisible();
  await dialog.locator('button.link-button', { hasText: '新建变量' }).click();
  const name = dialog.locator('.row input').first();
  await name.fill('count');
  await name.press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionVariablePatch')))
    .toBe(true);
  await dialog.locator('fluent-button', { hasText: '关闭' }).click();
});

test('键位字段可捕获按键', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '基础动作', '按键');
  await openStepEditor(page, '按键');
  await page.locator('wg-action-editor .param[data-parameter="key"] button', { hasText: '设置组合键' }).click();
  await page.keyboard.press('F8');
  await expect(page.locator('wg-action-editor .param[data-parameter="key"] input')).toHaveValue('F8');
  await closeStepEditor(page);
});

test('颜色字段的拾取按钮向桥发命令', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '基础动作', '屏幕取色');
  await openStepEditor(page, '屏幕取色');
  await page.locator('wg-action-editor .param[data-parameter="hex"] button',
    { hasText: '从屏幕上拾取颜色' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.actionPick')))
    .toBe(true);
  await closeStepEditor(page);
});

test('图片字段可调用原生选择器', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '基础动作', '找图');
  await openStepEditor(page, '找图');
  await page.locator('wg-action-editor .param[data-parameter="path"] button', { hasText: '浏览' }).click();
  await expect(page.locator('wg-action-editor .param[data-parameter="path"] input'))
    .toHaveValue('C:\\shot.png');
  await closeStepEditor(page);
});

test('条件格的取值钮打开 Token 窗口并写回', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '等待条件');
  await openStepEditor(page, '等待条件');
  await page.locator('wg-action-editor .condition-add button', { hasText: '添加行' }).click();
  const row = page.locator('wg-action-editor .condition-row').first();
  await row.locator('.chip').first().click();
  await expect(page.locator('wg-value-editor .dialog')).toBeVisible();
  await page.locator('wg-value-editor .band-head button[title*="清空"]').click();
  await page.locator('wg-value-editor .typed input').fill('found');
  await page.locator('wg-value-editor .typed button', { hasText: '插入' }).click();
  await page.locator('wg-value-editor footer fluent-button', { hasText: '确定' }).click();
  await expect(row.locator('input').first()).toHaveValue('found');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.valueOpen')))
    .toBe(true);
});

test('返回值表可勾选并命名', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '重复');
  await openStepEditor(page, '重复');
  const returns = page.locator('wg-action-editor wg-action-returns');
  await expect(returns.locator('input[type="checkbox"]').first()).toBeChecked();
  const name = returns.locator('input[type="text"]').first();
  await name.fill('found');
  await name.press('Tab');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.actionReturns')))
    .toBe(true);
});

test('返回值表的重名提示可换一个名字', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '重复');
  await openStepEditor(page, '重复');
  const returns = page.locator('wg-action-editor wg-action-returns');
  await expect(returns.locator('input[type="checkbox"]').first()).toBeChecked();
  const name = returns.locator('input[type="text"]').first();
  await name.fill('found');
  await name.press('Tab');
  await closeStepEditor(page);
});

test('开始与停止录制会把捕获的步骤写回', async ({ page }) => {
  await openEditor(page);
  const record = page.locator('wg-editor-palette .record');
  await record.click();
  await expect(record).toHaveAttribute('data-recording', 'true');
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionRecordStart')))
    .toBe(true);
  await record.click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('editor.sessionRecordStop')))
    .toBe(true);
  await expect(page.locator('wg-step-tree .row')).toHaveCount(2);
});

test('运行调试在宏设置底部并进入运行页', async ({ page }) => {
  await openEditor(page);
  await addStep(page, '流程控制', '延迟');
  await page.locator('wg-macro-settings .footer fluent-button', { hasText: '运行调试' }).click();
  await expect
    .poll(async () => page.evaluate(() => window.__mock!.seen.includes('app.openRun')))
    .toBe(true);
  await expect(page.locator('wg-run')).toBeVisible();
});

test('主题与语言推送刷新编辑器', async ({ page }) => {
  await openEditor(page);
  await page.evaluate(() => window.__mock!.push('theme.changed', {
    mode: 'light', baseLight: true, accentHex: null, defaultAccent: '#C8401C',
  }));
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  const background = await page.locator('wg-editor-palette')
    .evaluate((element) => getComputedStyle(element).backgroundColor);
  expect(background).toBe('rgb(245, 245, 245)');
  await page.evaluate(() => window.__bridge!.call('settings.update', { field: 'language', value: 'en' }));
  await expect(page.locator('wg-editor-palette .source', { hasText: 'Basic actions' })).toBeVisible();
});

test('选中态、块卡片与页签的视觉状态可读', async ({ page }) => {
  await openEditor(page);
  await selectSource(page, '流程控制');
  await page.locator('wg-editor-palette .card', { hasText: '重复' }).click();
  const card = page.locator('wg-editor-palette .card', { hasText: '重复' });
  expect(await card.evaluate((element) => getComputedStyle(element).backgroundColor))
    .not.toBe('rgba(0, 0, 0, 0)');
  await page.locator('wg-editor .toolbar fluent-button', { hasText: '添加步骤' }).click();
  await completeActionForm(page);
  const block = page.locator('wg-step-tree .block > .row');
  expect(await block.evaluate((element) => getComputedStyle(element).borderTopWidth)).toBe('1px');
  const tab = page.locator('wg-editor-tabs .tab-wrap[data-active="true"]');
  expect(await tab.evaluate((element) => getComputedStyle(element).borderBottomColor))
    .not.toBe('rgba(0, 0, 0, 0)');
});
