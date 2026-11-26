# WhaleGenie.Core

鲸灵（WhaleGenie）的宏引擎：动作语义、表达式、变量、设备契约与录制翻译。

它是一个普通类库，与界面和 Windows 无关：目标框架 `net10.0`，引用闭包只有 BCL 与 `CsvHelper`。
画面获取、键鼠、浏览器、UIA 这些要碰机器的东西在适配器工程里，通过本库的设备契约接进来。

## 公共面

对外只有四档，判定规则与基线维护写在仓库的 `docs/architecture/core-api.md`：

| 档 | 内容 |
| --- | --- |
| 契约 | 设备契约与设备值类型、表达式与值的家族、动作语义、运行结果与日志、变量表、录制 |
| 引擎入口 | `MacroRunner`、`StepTrial`、`ActionSemanticsCatalog`、`NullDeviceLayer` |
| 进程级共享 | `FrameShelf`、`WhaleGenieInputGate`、`SharedDoors` 等同时只该有一份的东西 |
| 适配器工具 | 适配器与引擎共用的小工具（键词汇表、命令行拆分、颜色比较、PNG 写出等） |

实现细节是 `internal`。公共成员的增删都要改 `PublicAPI.Shipped.txt`，构建会拦住没登记的改动。

## 已知债务

约 1,314 个公共成员还没有 `<summary>`（编译时以 `CS1591` 抑制）。新增公共成员请写注释，别把这条
债务再做大。
