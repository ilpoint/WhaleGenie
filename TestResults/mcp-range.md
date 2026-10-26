## WhaleGenie MCP range check — http://127.0.0.1:19965/mcp — 2026-10-27 06:17:15

Result: fail, 4 pass, 1 fail, 0 missing, 4 skip

| check | result | detail |
| --- | --- | --- |
| range.state | pass | handle 5441226, 5 controls, at (26, 26) 520 by 260 |
| range.front | fail | another window keeps the foreground (WIN 上的 Win10 - 虚拟机连接); last answer: {"handle":5441226,"activated":false,"requested":true} |
| range.draft | skip | not tried: the range could not be brought to the front |
| range.run | skip | not tried: the range could not be brought to the front |
| range.effect | skip | not tried: the range could not be brought to the front |
| range.reset | skip | not tried: the range could not be brought to the front |
| range.edge.point | pass | refused with Range.OutsideTarget |
| range.edge.script | pass | refused with Range.ActionRefused |
| range.edge.path | pass | refused with Range.PathOutside |

