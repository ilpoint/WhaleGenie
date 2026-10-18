# andrsrxn/icons（只取描边轮廓）

界面上动作分类、步骤工具栏、菜单、录制条与窗口按钮的图标来自 Andrés Raxón 的图标集：

- 上游：<https://github.com/andrsrxn/icons>，npm 包 `@andrsrxn/icons@7.6.0`（2026-10-15 发布）；
  Iconify 的集合数据取于 2026-10-16。
- 许可：MIT，版权 2026 Andrés Raxón；原文见同目录的 `LICENSE`。
- 取用的 58 个图标：`workflow`、`file`、`variable`、`clipboard`、`cpu`、`settings`、`app-window`、
  `keyboard`、`eye`、`text-scan`、`cursor-click`、`browser`、`code`、`split`、`undo`、`redo`、`pencil`、
  `toggle`、`copy`、`scissors`、`paste`、`trash`、`group`、`external-link`、`arrow-up`、`arrow-down`、
  `x`、`clock`、`play`、`pause`、`grid-four`、`minus`、`sun`、`pin`、`moon`、`folder`、`folder-open`、
  `save`、`file-plus`、`download`、`search`、`reload`、`sparkle`、`message`、`question-mark`、`info`、
  `list`、`mouse`、`game-controller`、`dropper`、`cursor`、`filter`、`record`、`sliders`、`plus`、
  `chevron-down`、`chevron-right`、`warning-triangle`。

## 改了什么

原图标是双色的：一层 20% 不透明度的填充，加一层 `stroke-width="1.5"` 的描边。这个程序一个图标只用
一支画刷，所以只保留了描边，填充层没有带进来。转换时还做了两件事：

- `<rect>` 与 `<circle>` 摊平成路径；`clipboard`、`keyboard`、`copy`、`paste` 上的 `rotate` 与
  `matrix` 变换也一并算进坐标。
- 同一个图标里有多条路径的，接成一条路径数据；原本以相对 `m` 开头的第二条路径改成绝对 `M`，否则它会
  从上一个子路径的终点算起。

坐标、线宽与 24 格画布都没有改。生成的几何在 `WhaleGenie/Models/AppIcons.cs`，由
`WhaleGenie/Views/IconView.cs` 按 24 格缩放绘制。分类栏与工具栏按 24 格排，图标之间的相对大小不变；
菜单、录制条和窗口按钮是单个图标，用 `FitToBounds` 填满自己的方框。描边在 1.2–1.5 像素之间，按控件
大小取，不随缩放变细。

## 更新时

换版本时不要直接覆盖 `AppIcons.cs`：先把新版本的这 58 个图标按同样的规则摊平，再逐个看一遍 16 像素下
的辨识度，尤其是 `variable`、`group`、`cursor-click` 这些线条较多的，以及 10–12 像素的 `minus`、`x`、
`chevron-*`。上游改过图形时，这张对照图（16、24、48 像素各一行）是判断有没有变糊的最快办法。
