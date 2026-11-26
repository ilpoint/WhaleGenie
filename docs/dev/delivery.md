# 出图、性能、CI 与发布

所属：[WhaleGenie 开发说明](../dev.md)。

## 界面出图

界面截图用于视觉评审，不作为行为正确性的测试证据。评审时优先截**运行中的窗口**：网页壳用 Debug
暴露的 CDP（Playwright）截页面，原生窗口用系统截图；主题、字体、DPI 缩放都是真的。

## 介绍页与短片

`site/` 是 GitHub Pages 源目录，`build/site.ps1` 将页面及引用资源组装到 `_site/`。推送到
`master` 后由 `.github/workflows/pages.yml` 发布；首次使用前需在仓库 Pages 设置中选择
GitHub Actions。

重录 `site/vibe/index.html` 对应的短片时，使用临时 Playwright 安装和本机 Edge：

```powershell
npm install --prefix "$env:TEMP\whalegenie-site-tools" playwright-core@1.63.0
npx --yes playwright@1.63.0 install ffmpeg
$env:WHALEGENIE_PLAYWRIGHT = "$env:TEMP\whalegenie-site-tools\node_modules\playwright-core"
node build/record-vibe.mjs .
```

该脚本生成无声 H.264 短片、海报和社交分享图，发布成品写入 `site/assets/`。手工评审图仍按上一节
保存到 `%temp%\WhaleGenie`。录制工具不是应用运行依赖，机器需另有 `ffmpeg` 和 Edge。

只改动了页面、不需要重录短片时，用 `--og-only` 重拍社交分享图。它截的是组装后的
`_site/index.html`，所以先跑一次 `build/site.ps1`：

```powershell
node build/record-vibe.mjs . --og-only
```

界面截图更新后，用同一套临时 Playwright 安装重新拼合界面总览图：

```powershell
node build/render-overview.mjs .
```

成品写入 `docs/images/interface-overview.png`，由 `build/site.ps1` 复制进介绍页；README 与
`site/index.html` 引用同一份文件，不各自保存一张。图里是当前的两张截图（主界面与宏编辑器），
底色跟介绍页一致；动作编辑器重新出图后再加进这一张，避免同一张图混用不同版本的外观。

## 性能测量

修改 `WhaleGenie.GraphicsCapture`、`OpenCvVisionDevice`、`PixelSearch`、`PaddleOcrDevice`、
`OpenCvDetectDevice` 或触发器等待间隔后，重新测量受影响路径。用临时工程同时引用 `WhaleGenie.Core`
与 `WhaleGenie.Platform.Windows`，通过 `WindowsDeviceLayer` 取得运行时才使用的真实设备；需要指定
抓法或验证可用性时可直接使用 `ScreenGrabber`。记录 Windows 版本、CPU、显卡、分辨率、模型与参考图
尺寸，先预热，再取多次测量的中位数，不能用单次结果判断回归。

找色与找图的扫描循环已经是引擎内部实现（`PixelSearch`、`ColourReach` 为 `internal`），临时工程
读不到它们，改从动作路径测量：造一条只含该动作的宏，用 `MacroRunner` 跑真实设备和真实参考图，
读 `RunResult` 的耗时与设备记录。这样测到的正是用户会遇到的路径，也不必为了测量把实现重新公开。

找图分别测量足够大的搜索区域与缩小后的区域，找色覆盖提前命中与完整遍历，OCR 与检测把首次模型加载
和后续调用分开。触发器使用真实窗口或短生命周期进程，并区分事件通知与轮询退路。测量前停止其他重负载，
临时工程不留在仓库；修改前后的本机报告用同一场景比较，过期数字不写入长期文档。

## 提交与 CI

`master` 是发布主线。`.github/workflows/ci.yml` 在推送到 `master`、`webview` 与拉取请求上
运行格式检查、构建及全部测试；八套件并行执行（为什么并行、两轮实测与串行的对比写在 `ci.yml` 的
注释里），失败报告保留 7 天，成功运行不保存报告。

需要在本地复现 CI 时，可使用 `act`：

```powershell
act -W .github/workflows/ci.yml -j build-and-test -P windows-latest=-self-hosted `
  --env ACT=true --env DOTNET_INSTALL_DIR="$env:LOCALAPPDATA\act-dotnet"
```

提交信息使用中文并说明原因。手动验收结果须包含环境和观察结果，不能只写“已手动验证”。

## 发布

发布由 `.github/workflows/release.yml` 完成。推送以 `v` 开头的标签，或在 Actions 中手动运行
Release 工作流并填写版本号：

```powershell
git tag v0.1.1
git push origin v0.1.1
```

标签是压缩包名称和程序集版本的唯一来源。工作流生成两个 Windows x64 包：

| 包 | 运行环境 |
| --- | --- |
| `WhaleGenie-<版本>-win-x64.zip` | 需要预装 .NET 10 桌面运行时 |
| `WhaleGenie-<版本>-win-x64-standalone.zip` | 自包含，无需预装 .NET |

本地打包使用：

```powershell
pwsh build/package.ps1 -Tag v0.1.1
```

产物写入已忽略的 `dist/`。发布包采用单文件托管发布，原生库保留在 `lib/`，Playwright 驱动位于
`.playwright/`；包根包含手册、许可证和第三方说明。只发布 Windows x64，不启用裁剪，以避免 Avalonia、
UIA 和 OCR 的反射路径被破坏。

程序按 AGPL-3.0 发布，`LICENSE.txt` 保持许可原文，`NOTICE` 保存署名。发布前确认 GitHub Actions
具有创建 Release 所需的写权限。面向用户的版本说明维护在 `.github/release-notes.md`。