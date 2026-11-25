import { expect, test, type Page } from '@playwright/test';

/**
 * 子宏页签的创建与导入。这些场景与主编辑器分开，是因为它们验证的是“父宏列表何时变化”，
 * 而不是步骤树本身；共享的打开与选源动作小到可以直接写在文件里。
 */

async function openEditor(page: Page): Promise<void> {
  await page.goto('/?bridge=mock#macros');
  await expect(page.locator('wg-titlebar .title')).toContainText('鲸灵');
  await page.locator('wg-macros .card', { hasText: '自动保存' }).dblclick();
  await expect(page.locator('wg-editor-tabs .tab').first()).toBeVisible();
}

async function selectSource(page: Page, label: string): Promise<void> {
  await page.locator('wg-editor-palette .source', { hasText: label }).click();
  await page.waitForTimeout(60);
}

async function clearSearch(page: Page): Promise<void> {
  await page.locator('wg-editor-palette .action-search').evaluate((input) => {
    (input as HTMLInputElement).value = '';
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
}

function called(page: Page, method: string, params: Record<string, string>): Promise<boolean> {
  return page.evaluate(({ method: wanted, params: wantedParams }) => {
    const mock = (window as unknown as {
      __mock?: { calls: { method: string; params: Record<string, unknown> }[] };
    }).__mock;
    return mock?.calls.some(
      (call) => call.method === wanted
        && Object.entries(wantedParams).every(([name, value]) => call.params[name] === value),
    ) ?? false;
  }, { method, params });
}

test('新建子宏只是草稿页签，保存后才进列表；导入从对话框选择来源', async ({ page }) => {
  await openEditor(page);
  await selectSource(page, '子宏');
  const rows = page.locator('wg-editor-palette .sub-row');
  await expect(rows).toHaveCount(1);

  await page.locator('wg-editor-palette .sub-new').click();
  await expect
    .poll(() => called(page, 'editor.session', { kind: 'subMacroCreate' }))
    .toBe(true);
  // The draft is a tab; the owner's list must not grow until it is saved.
  await expect(rows).toHaveCount(1);
  const tab = page.locator('wg-editor-tabs .tab[aria-selected="true"]');
  await expect(tab).toContainText('子宏');
  await page.keyboard.press('Control+s');
  await expect(rows).toHaveCount(2);
  const name = (await rows.last().locator('.sub-open').textContent())?.trim() ?? '';
  expect(name.length).toBeGreaterThan(0);

  // 调用步骤加在拥有子宏的宏里，这样删除仍在调用它的子宏会被拒绝。
  await page.locator('wg-editor-tabs .tab', { hasText: '自动保存' }).click();
  await selectSource(page, '流程控制');
  await page.locator('wg-editor-palette .action-search').fill('调用宏');
  await page.locator('wg-editor-palette .card', { hasText: '调用宏' }).dblclick();
  const form = page.locator('wg-action-editor .dialog');
  await expect(form).toBeVisible();
  await form.locator('.param[data-parameter="macro"] select').selectOption(name);
  await form.locator('footer fluent-button').last().click();
  await expect(form).toHaveCount(0);
  await expect(page.locator('wg-step-tree .row', { hasText: '调用 ' + name })).toBeVisible();

  // 删除仍在被调用的子宏：桥拒绝，页面错误条保留桥自己的句子
  await selectSource(page, '子宏');
  await clearSearch(page);
  const row = page.locator('wg-editor-palette .sub-row[data-submacro="' + name + '"]');
  await row.locator('.sub-remove').click();
  const deleteDialog = page.locator('wg-submacro-delete-dialog .dialog');
  await expect(deleteDialog).toBeVisible();
  await deleteDialog.locator('.footer fluent-button').last().click();
  await expect(page.locator('wg-editor > .error')).toContainText('仍在被调用');
  await expect(rows).toHaveCount(2);

  // 导入：对话框从工程宏里挑一个，作为子宏加入并打开页签
  await page.locator('wg-editor-palette .sub-copy').click();
  const importDialog = page.locator('wg-submacro-copy-dialog .dialog');
  await expect(importDialog).toBeVisible();
  await importDialog.locator('.sources button', { hasText: '当前工程' }).click();
  await importDialog.locator('.candidates button', { hasText: '游戏连招' }).click();
  await expect(importDialog).toHaveCount(0);
  await expect
    .poll(() => called(page, 'editor.session', { kind: 'subMacroImport' }))
    .toBe(true);
  await expect(rows).toHaveCount(3);
});

