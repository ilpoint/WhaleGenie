## WhaleGenie MCP range check — http://127.0.0.1:14351/mcp — 2026-10-27 05:34:00

Result: pass, 9 pass, 0 fail, 0 missing, 0 skip

| check | result | detail |
| --- | --- | --- |
| range.state | pass | handle 54658872, 5 controls, at (26, 26) 520 by 260 |
| range.front | pass | {"handle":54658872,"activated":true} |
| range.draft | pass | one step written into a draft |
| range.run | pass | {"runId":"9e8e6604","macro":"按一下","status":"Completed","key":"","detail":"","steps":1,"timedOut":false,"outcomes":[{"stepId":"vgkk","stepType":"input.mouseClick","outcome":"ok","failure":"","results":[]}]} |
| range.effect | pass | presses after the run: 1 |
| range.reset | pass | back to none |
| range.edge.point | pass | refused with Range.OutsideTarget |
| range.edge.script | pass | refused with Range.ActionRefused |
| range.edge.path | pass | refused with Range.PathOutside |

| fact | value |
| --- | --- |
| picture | C:\Users\q1233\AppData\Local\Temp\whalegenie-range-check.png |

