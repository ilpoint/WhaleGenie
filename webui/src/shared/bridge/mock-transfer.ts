import type { MacroEntry, ProjectExportPlan, ProjectImportPlan, ProjectState } from './protocol.js';

/**
 * 导入导出向导在 mock 宿主上的应答。包的形状与真桥一致：plan 给可勾选的宏与有冲突的全局变量，
 * apply 把勾选的宏加进假项目并推一次 project.changed；两个文件对话框只回一条固定路径。
 */
export class MockTransfer {
  /** 导入向导读到的包；apply 按勾选的名字把其中的宏加进假项目。 */
  private plan: ProjectImportPlan | null = null;

  constructor(
    private readonly project: () => ProjectState,
    private readonly replace: (state: ProjectState) => void,
    private readonly push: (event: string, payload: unknown) => void,
  ) {}

  /** 这条方法是不是 transfer 族的应答。 */
  static handles(method: string): boolean {
    return method === 'project.exportPlan'
      || method === 'project.exportApply'
      || method === 'project.importPlan'
      || method === 'project.importApply'
      || method === 'dialog.pickOpenPackage'
      || method === 'dialog.pickSavePackage';
  }

  answer(method: string, params: Record<string, unknown>): unknown {
    switch (method) {
      case 'project.exportPlan': {
        const plan: ProjectExportPlan = {
          suggestedName: `${this.project().macros[0]?.name ?? 'macros'}.wgmacro`,
          macros: this.project().macros.map((entry) => ({
            name: entry.name,
            kind: entry.triggerSummary,
            steps: 2,
            problem: '',
            isSub: false,
            depth: 0,
          })),
        };
        return plan;
      }
      case 'project.exportApply':
        return { path: String(params['path'] ?? '/mock/export.wgmacro') };
      case 'project.importPlan': {
        const path = String(params['path'] ?? '/mock/opened-project.wgmacro');
        this.plan = {
          source: path,
          fileName: path.split('/').pop() ?? path,
          macros: [
            { name: '包里的宏', kind: '键', steps: 2, problem: '', isSub: false, depth: 0 },
            { name: '读表', kind: '未设置触发入口', steps: 1, problem: '', isSub: true, depth: 1 },
          ],
          globals: [{ name: '计数', type: '数字', conflicts: true }],
        };
        return this.plan;
      }
      case 'project.importApply': {
        const names = (params['names'] as string[] | undefined) ?? [];
        const imported: MacroEntry[] = (this.plan?.macros ?? [])
          .filter((macro) => names.includes(macro.name))
          .map((macro) => ({
            name: macro.name,
            group: '',
            description: '',
            enabled: true,
            running: false,
            triggerSummary: macro.kind,
            triggerActions: '',
            triggerPreview: macro.kind,
            views: ['All'],
          }));
        const next: ProjectState = {
          ...this.project(),
          dirty: true,
          revision: this.project().revision + 1,
          macros: [...this.project().macros, ...imported],
        };
        this.replace(next);
        this.push('project.changed', next);
        return { report: names.join('、'), state: next };
      }
      case 'dialog.pickOpenPackage':
        return '/mock/opened-project.wgmacro';
      default:
        return `/mock/${String(params['suggested'] ?? 'macros.wgmacro')}`;
    }
  }
}