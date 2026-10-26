<!-- 在 Hyper-V 虚拟机上跑的一次：Windows 10 22H2（19045）x64、.NET 10.0.13、1280×1024、
     Release 构建从主机拷进虚拟机 C 盘的 wg 目录，经交互式计划任务在控制台会话里运行。 -->

## WhaleGenie MCP range check — http://127.0.0.1:49866/mcp — 2026-10-27 05:34:13

Result: pass, 9 pass, 0 fail, 0 missing, 0 skip

| check | result | detail |
| --- | --- | --- |
| range.state | pass | handle 525578, 5 controls, at (156, 156) 520 by 260 |
| range.front | pass | {"handle":525578,"activated":true} |
| range.draft | pass | one step written into a draft |
| range.run | pass | {"runId":"a29712bf","macro":"按一下","status":"Completed","key":"","detail":"","steps":1,"timedOut":false,"outcomes":[{"stepId":"jqb3","stepType":"input.mouseClick","outcome":"ok","failure":"","results":[]}]} |
| range.effect | pass | presses after the run: 1 |
| range.reset | pass | back to none |
| range.edge.point | pass | refused with Range.OutsideTarget |
| range.edge.script | pass | refused with Range.ActionRefused |
| range.edge.path | pass | refused with Range.PathOutside |

| fact | value |
| --- | --- |
| picture | C:\Users\1\AppData\Local\Temp\whalegenie-range-check.png |

