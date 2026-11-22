# webui

网页壳（Lit + Fluent UI Web Components）。结构、命名、边界与拆分方法以
[`docs/dev/webui.md`](../docs/dev/webui.md) 为准；当前桥与页面边界见
[`docs/architecture/interface.md`](../docs/architecture/interface.md)。

```powershell
npm install
npm run dev          # mock 宿主热重载
npm run check        # lint + 单测 + 类型检查 + 构建
npm run test
npm run build        # 产物是单个 dist/index.html
npx playwright test  # 系统 Edge + vite preview
```
