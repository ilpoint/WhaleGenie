# 动作矩阵

生成时间：2026-10-27 01:51；动作 176 个。

这份文件由 `WhaleGenie.Core.Tests/Bench` 生成，不是手写的。每个动作都按目录声明合成一步，
在替身设备层上跑一遍，然后核四件事：返回值是否写出来且形状对、副作用声明是否与设备账本一致、
结局是否符合预期、有没有参数根本给不出值。改前改后各留一份，数字对不上就是代码变了。

## 合计

| 通过 | 不符 | 缺夹具 | 不可达 | 合计 |
| ---: | ---: | ---: | ---: | ---: |
| 176 | 0 | 0 | 0 | 176 |

| 分组 | 动作 | 通过 | 不符 | 缺夹具 | 不可达 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Browser | 24 | 24 | 0 | 0 | 0 |
| Clipboard | 10 | 10 | 0 | 0 | 0 |
| Control | 37 | 37 | 0 | 0 | 0 |
| Data | 3 | 3 | 0 | 0 | 0 |
| File | 19 | 19 | 0 | 0 | 0 |
| Input | 20 | 20 | 0 | 0 | 0 |
| Ocr | 3 | 3 | 0 | 0 | 0 |
| Process | 8 | 8 | 0 | 0 | 0 |
| Script | 6 | 6 | 0 | 0 | 0 |
| System | 9 | 9 | 0 | 0 | 0 |
| Uia | 13 | 13 | 0 | 0 | 0 |
| Vision | 14 | 14 | 0 | 0 | 0 |
| Window | 10 | 10 | 0 | 0 | 0 |

## 待修清单

没有：每个动作都跑通，且声明与账本一致。

## 逐行

| 动作 | 分组 | 结论 | 说明 |
| --- | --- | --- | --- |
| `control.sequence` | Control | 通过 | 设备调用 0 次 |
| `control.holdInput` | Control | 通过 | 设备调用 0 次 |
| `control.holdBrowser` | Control | 通过 | 设备调用 0 次 |
| `control.delay` | Control | 通过 | 设备调用 0 次 |
| `control.waitUntil` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.repeat` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.while` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.forEach` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.for` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.if` | Control | 通过 | 设备调用 0 次 |
| `control.switch` | Control | 通过 | 设备调用 0 次 |
| `control.case` | Control | 通过 | 设备调用 0 次 |
| `control.try` | Control | 通过 | 结果 2/2；设备调用 0 次 |
| `control.break` | Control | 通过 | 设备调用 0 次 |
| `control.continue` | Control | 通过 | 设备调用 0 次 |
| `control.return` | Control | 通过 | 设备调用 0 次 |
| `control.rethrow` | Control | 通过 | 设备调用 2 次 |
| `control.stop` | Control | 通过 | 设备调用 0 次 |
| `control.fail` | Control | 通过 | 设备调用 2 次 |
| `control.note` | Control | 通过 | 设备调用 0 次 |
| `control.setVariable` | Control | 通过 | 设备调用 0 次 |
| `control.incrementGlobal` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.listAdd` | Control | 通过 | 设备调用 0 次 |
| `control.listInsert` | Control | 通过 | 设备调用 0 次 |
| `control.listRemoveAt` | Control | 通过 | 设备调用 0 次 |
| `control.listSet` | Control | 通过 | 设备调用 0 次 |
| `control.listClear` | Control | 通过 | 设备调用 0 次 |
| `control.log` | Control | 通过 | 设备调用 0 次 |
| `control.ask` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.input` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.runMacro` | Control | 通过 | 设备调用 0 次 |
| `control.startMacro` | Control | 通过 | 设备调用 0 次 |
| `control.waitMacro` | Control | 通过 | 设备调用 0 次 |
| `control.runStatus` | Control | 通过 | 结果 2/3；这次设置没写出：error；设备调用 0 次 |
| `control.cancelMacro` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.waitAll` | Control | 通过 | 结果 1/1；设备调用 0 次 |
| `control.waitAny` | Control | 通过 | 结果 2/2；设备调用 0 次 |
| `file.readText` | File | 通过 | 结果 2/2；设备调用 1 次 |
| `file.writeText` | File | 通过 | 设备调用 1 次 |
| `file.appendLog` | File | 通过 | 设备调用 2 次 |
| `file.exists` | File | 通过 | 结果 1/1；设备调用 1 次 |
| `file.delete` | File | 通过 | 设备调用 1 次 |
| `file.copy` | File | 通过 | 结果 1/1；设备调用 2 次 |
| `file.move` | File | 通过 | 结果 1/1；设备调用 2 次 |
| `file.createFolder` | File | 通过 | 设备调用 1 次 |
| `file.path` | File | 通过 | 结果 1/1；设备调用 0 次 |
| `file.unzip` | File | 通过 | 设备调用 1 次 |
| `file.zip` | File | 通过 | 设备调用 1 次 |
| `file.deleteFolder` | File | 通过 | 设备调用 1 次 |
| `file.listFiles` | File | 通过 | 结果 1/1；设备调用 1 次 |
| `file.readJson` | File | 通过 | 结果 1/1；设备调用 1 次 |
| `file.writeJson` | File | 通过 | 设备调用 2 次 |
| `file.readCsv` | File | 通过 | 结果 1/1；设备调用 1 次 |
| `file.writeCsv` | File | 通过 | 设备调用 1 次 |
| `file.saveVariables` | File | 通过 | 设备调用 1 次 |
| `file.loadVariables` | File | 通过 | 设备调用 1 次 |
| `data.base64Encode` | Data | 通过 | 结果 1/1；设备调用 0 次 |
| `data.base64Decode` | Data | 通过 | 结果 1/1；设备调用 0 次 |
| `data.hash` | Data | 通过 | 结果 1/1；设备调用 0 次 |
| `clipboard.writeText` | Clipboard | 通过 | 设备调用 1 次 |
| `clipboard.readText` | Clipboard | 通过 | 结果 1/1；设备调用 1 次 |
| `clipboard.readImage` | Clipboard | 通过 | 结果 3/3；设备调用 1 次 |
| `clipboard.writeImage` | Clipboard | 通过 | 设备调用 2 次 |
| `clipboard.readFiles` | Clipboard | 通过 | 结果 1/1；设备调用 1 次 |
| `clipboard.writeFiles` | Clipboard | 通过 | 设备调用 2 次 |
| `clipboard.clear` | Clipboard | 通过 | 设备调用 1 次 |
| `clipboard.waitChange` | Clipboard | 通过 | 结果 1/1；设备调用 1 次 |
| `clipboard.copy` | Clipboard | 通过 | 结果 1/1；设备调用 2 次 |
| `clipboard.paste` | Clipboard | 通过 | 设备调用 1 次 |
| `process.start` | Process | 通过 | 结果 1/1；设备调用 2 次 |
| `process.waitFor` | Process | 通过 | 结果 1/1；设备调用 1 次 |
| `process.waitExit` | Process | 通过 | 结果 1/1；设备调用 2 次 |
| `process.exists` | Process | 通过 | 结果 1/1；设备调用 1 次 |
| `process.list` | Process | 通过 | 结果 1/1；设备调用 1 次 |
| `process.info` | Process | 通过 | 结果 1/1；设备调用 1 次 |
| `process.kill` | Process | 通过 | 结果 1/1；设备调用 1 次 |
| `command.run` | Process | 通过 | 结果 2/3；这次设置没写出：error；设备调用 1 次 |
| `system.info` | System | 通过 | 结果 1/1；设备调用 1 次 |
| `screen.list` | System | 通过 | 结果 1/1；设备调用 0 次 |
| `system.environment` | System | 通过 | 结果 1/1；设备调用 1 次 |
| `system.power` | System | 通过 | 设备调用 1 次 |
| `system.volume` | System | 通过 | 结果 1/1；设备调用 1 次 |
| `system.sound` | System | 通过 | 设备调用 1 次 |
| `system.notify` | System | 通过 | 设备调用 1 次 |
| `system.ime` | System | 通过 | 结果 2/2；设备调用 1 次 |
| `system.brightness` | System | 通过 | 结果 1/1；设备调用 1 次 |
| `window.exists` | Window | 通过 | 结果 1/1；设备调用 1 次 |
| `window.waitFor` | Window | 通过 | 结果 2/2；设备调用 5 次 |
| `window.activate` | Window | 通过 | 设备调用 2 次 |
| `window.minimize` | Window | 通过 | 设备调用 2 次 |
| `window.maximize` | Window | 通过 | 设备调用 2 次 |
| `window.restore` | Window | 通过 | 设备调用 2 次 |
| `window.move` | Window | 通过 | 设备调用 2 次 |
| `window.close` | Window | 通过 | 设备调用 2 次 |
| `window.list` | Window | 通过 | 结果 1/1；设备调用 1 次 |
| `window.info` | Window | 通过 | 结果 1/1；设备调用 5 次 |
| `input.keyPress` | Input | 通过 | 设备调用 2 次 |
| `input.waitKey` | Input | 通过 | 结果 2/2；设备调用 0 次 |
| `input.keyDown` | Input | 通过 | 设备调用 3 次 |
| `input.keyUp` | Input | 通过 | 设备调用 2 次 |
| `input.hotkey` | Input | 通过 | 设备调用 2 次 |
| `input.keySequence` | Input | 通过 | 设备调用 2 次 |
| `input.typeText` | Input | 通过 | 设备调用 2 次 |
| `input.mouseMove` | Input | 通过 | 结果 2/2；设备调用 4 次 |
| `input.mouseMoveRelative` | Input | 通过 | 结果 1/1；设备调用 1 次 |
| `input.mouseClick` | Input | 通过 | 结果 2/2；设备调用 4 次 |
| `input.mouseDoubleClick` | Input | 通过 | 结果 2/2；设备调用 4 次 |
| `input.mouseDown` | Input | 通过 | 结果 2/2；设备调用 5 次 |
| `input.mouseUp` | Input | 通过 | 结果 2/2；设备调用 4 次 |
| `input.mouseScroll` | Input | 通过 | 结果 2/2；设备调用 4 次 |
| `input.mouseDrag` | Input | 通过 | 结果 4/4；设备调用 6 次 |
| `gamepad.connect` | Input | 通过 | 设备调用 2 次 |
| `gamepad.button` | Input | 通过 | 设备调用 3 次 |
| `gamepad.stick` | Input | 通过 | 设备调用 2 次 |
| `gamepad.trigger` | Input | 通过 | 设备调用 2 次 |
| `gamepad.release` | Input | 通过 | 设备调用 2 次 |
| `vision.capture` | Vision | 通过 | 结果 7/7；设备调用 8 次 |
| `vision.captureWindow` | Vision | 通过 | 结果 7/7；设备调用 4 次 |
| `vision.findImage` | Vision | 通过 | 结果 7/7；设备调用 7 次 |
| `vision.waitImage` | Vision | 通过 | 结果 6/6；设备调用 7 次 |
| `vision.clickImage` | Vision | 通过 | 结果 6/6；设备调用 8 次 |
| `vision.getPixel` | Vision | 通过 | 结果 4/4；设备调用 5 次 |
| `vision.waitColor` | Vision | 通过 | 结果 2/2；设备调用 9 次 |
| `vision.findColor` | Vision | 通过 | 结果 8/8；设备调用 5 次 |
| `vision.waitStable` | Vision | 通过 | 结果 2/2；设备调用 485 次 |
| `vision.findColors` | Vision | 通过 | 结果 7/7；设备调用 5 次 |
| `vision.detect` | Vision | 通过 | 结果 7/7；设备调用 6 次 |
| `vision.waitDetect` | Vision | 通过 | 结果 6/6；设备调用 6 次 |
| `vision.clickDetect` | Vision | 通过 | 结果 6/6；设备调用 7 次 |
| `ocr.recognize` | Ocr | 通过 | 结果 3/4；这次设置没写出：table；设备调用 8 次 |
| `ocr.findText` | Ocr | 通过 | 结果 7/7；设备调用 6 次 |
| `ocr.clickText` | Ocr | 通过 | 结果 6/6；设备调用 7 次 |
| `uia.exists` | Uia | 通过 | 结果 1/1；设备调用 1 次 |
| `uia.find` | Uia | 通过 | 结果 4/4；设备调用 2 次 |
| `uia.waitElement` | Uia | 通过 | 结果 1/1；设备调用 2 次 |
| `uia.click` | Uia | 通过 | 设备调用 2 次 |
| `uia.invoke` | Uia | 通过 | 设备调用 2 次 |
| `uia.setText` | Uia | 通过 | 设备调用 1 次 |
| `uia.read` | Uia | 通过 | 结果 1/1；设备调用 1 次 |
| `uia.select` | Uia | 通过 | 设备调用 1 次 |
| `uia.check` | Uia | 通过 | 设备调用 1 次 |
| `uia.expand` | Uia | 通过 | 设备调用 1 次 |
| `uia.scrollIntoView` | Uia | 通过 | 设备调用 1 次 |
| `uia.readTable` | Uia | 通过 | 结果 1/1；设备调用 1 次 |
| `uia.focusWindow` | Uia | 通过 | 设备调用 1 次 |
| `browser.open` | Browser | 通过 | 设备调用 1 次 |
| `browser.goTo` | Browser | 通过 | 设备调用 1 次 |
| `browser.click` | Browser | 通过 | 设备调用 1 次 |
| `browser.fill` | Browser | 通过 | 设备调用 1 次 |
| `browser.readText` | Browser | 通过 | 结果 1/1；设备调用 1 次 |
| `browser.readAll` | Browser | 通过 | 结果 1/1；设备调用 1 次 |
| `browser.readTable` | Browser | 通过 | 结果 1/1；设备调用 1 次 |
| `browser.readPage` | Browser | 通过 | 结果 2/2；设备调用 1 次 |
| `browser.select` | Browser | 通过 | 设备调用 1 次 |
| `browser.check` | Browser | 通过 | 设备调用 1 次 |
| `browser.hover` | Browser | 通过 | 设备调用 1 次 |
| `browser.press` | Browser | 通过 | 设备调用 1 次 |
| `browser.waitFor` | Browser | 通过 | 设备调用 1 次 |
| `browser.waitForUrl` | Browser | 通过 | 设备调用 1 次 |
| `browser.waitForLoad` | Browser | 通过 | 设备调用 1 次 |
| `browser.scroll` | Browser | 通过 | 设备调用 1 次 |
| `browser.download` | Browser | 通过 | 结果 2/2；设备调用 1 次 |
| `browser.dialog` | Browser | 通过 | 结果 1/1；设备调用 1 次 |
| `browser.evaluate` | Browser | 通过 | 结果 1/1；设备调用 1 次 |
| `browser.upload` | Browser | 通过 | 设备调用 1 次 |
| `browser.switchTab` | Browser | 通过 | 设备调用 1 次 |
| `browser.closeTab` | Browser | 通过 | 设备调用 1 次 |
| `browser.close` | Browser | 通过 | 设备调用 1 次 |
| `script.run` | Script | 通过 | 结果 2/3；这次设置没写出：error；设备调用 3 次 |
| `script.python` | Script | 通过 | 结果 2/3；这次设置没写出：error；设备调用 1 次 |
| `script.pip` | Script | 通过 | 结果 2/3；这次设置没写出：error；设备调用 1 次 |
| `script.csharp` | Script | 通过 | 结果 2/3；这次设置没写出：error；设备调用 1 次 |
| `script.ahk` | Script | 通过 | 结果 2/3；这次设置没写出：error；设备调用 1 次 |
| `script.nuget` | Script | 通过 | 结果 2/3；这次设置没写出：error；设备调用 1 次 |
| `browser.exists` | Browser | 通过 | 结果 1/1；设备调用 1 次 |
| `vision.pixelMatches` | Vision | 通过 | 结果 1/1；设备调用 3 次 |
