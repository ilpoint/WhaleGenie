## WhaleGenie MCP self-check — http://127.0.0.1:51735/mcp — 2026-10-27 00:50:05

Result: pass, 33 pass, 0 fail, 0 missing, 0 skip

| check | result | detail |
| --- | --- | --- |
| server.instructions | pass | 3255 characters |
| tools.list | pass | 48 tools, every required one present |
| tools.optional | pass | every optional tool present |
| prompts.list | pass | 2 prompts |
| prompts.text | pass | both prompts carry the workflow |
| loop.status | pass | path=C:\Users\q1233\AppData\Local\Temp\wg-mcp-doctor-f2808892190d4c7db0d65b79f77f1c29\doctor.wgmacro revision=0 |
| loop.open | pass | draft ce6fd8ef at revision 0 |
| discovery.batch | pass | 3 actions in one call, 3593 characters |
| loop.put | pass | 2 steps written |
| loop.validate | pass | no blocking problem |
| loop.apply | pass | revision 7 |
| reject.double_apply | pass | {"error":{"code":"AlreadyApplied","message":"This draft has already been applied.","hint":"Open the project again to get a fresh draft; one draft is written once.","retryable":false,"details":null}} |
| loop.save | pass | C:\Users\q1233\AppData\Local\Temp\wg-mcp-doctor-f2808892190d4c7db0d65b79f77f1c29\doctor.wgmacro |
| patch.put | pass | 40 steps, 40 ids in the outline |
| patch.ids | pass | 40 ids, 40 distinct |
| patch.set | pass | one parameter for 125 bytes against 1914 for the whole macro |
| patch.value | pass | ms is 777 |
| patch.structure | pass | insert and remove kept the count at 40 (was 40) |
| patch.row | pass | one row changed and one added for 279 bytes |
| patch.refuse | pass | {"error":{"code":"BadPlacement","message":"\"control.case\" is a hidden action and cannot stand in the top level.","hint":"An ordinary list takes visible actions, plus the grouping container the editor makes.","retryable":true,"details":{"s… |
| reject.action_key | pass | located it: [aa22] UnknownAction: 这一步的动作已经不在目录里了：删掉这一步，或者重新选一个动作。 |
| reject.child_keys | pass | located it: [bb22] CaseOutsideSwitch: Case 只能放在多分支的分支列表里运行。 |
| reject.record_field | pass | located it: [cc22] WindowBounds: 至少填写一条边，或先拾取窗口位置和尺寸，否则这一步不会改动窗口。 |
| reject.enum_value | pass | located it: [dd22] BadOption: 「比对方式（运算符）」不是「bogus」允许的选项。 |
| reject.bad_json | pass | An error occurred invoking 'whalegenie_put_macro'. |
| signature.tool | pass | the signature names the argument columns and gives a call skeleton |
| action.callShape | pass | the catalogue names where a call's parameters come from and the rows they have |
| resources.listed | pass | 1 resources, 1 templates: whalegenie://actions / whalegenie://actions/{key} |
| resources.read | pass | the catalogue reads back as a resource with 176 actions |
| gate.apply_invalid | pass | {"error":{"code":"DraftHasBlockingProblems","message":"The draft has 1 blocking problem(s), so it was not applied.","hint":"Call whalegenie_validate, fix what it names, then apply again. Pass force when the problems are known and wanted.","… |
| cleanup.ce6fd8ef | pass | discarded |
| cleanup.1933737d | pass | discarded |
| cleanup.2b28bf56 | pass | discarded |

| metric | value |
| --- | --- |
| macro.ids | 40 |
| macro.resendBytes | 1914 |
| macro.steps | 40 |
| paging.measured | 4 |
| paging.withTotal | 3 |
| patch.requestBytes | 125 |
| patch.rowBytes | 279 |
| patch.saving | 15.31 |
| prompts.count | 2 |
| scenario.calls | 9 |
| scenario.longestCall | 4253 |
| scenario.requestBytes | 480 |
| scenario.responseBytes | 6452 |
| schema.enumParameters | 6 |
| schema.enumRatio | 0.06 |
| schema.jsonInString | 0 |
| schema.parameters | 94 |
| schema.stringWithoutEnum | 26 |
| tools.absentOptional | 0 |
| tools.count | 48 |
| tools.withSchema | 48 |
| tools.withTitle | 48 |

| fact | value |
| --- | --- |
| paging.whalegenie_get_run_log | unavailable |
| paging.whalegenie_list_actions | total,hasMore |
| paging.whalegenie_list_macros | total,hasMore |
| paging.whalegenie_list_runs | total |
| scenario.policy | status, open, one batch describe, template, put, validate, apply, save |

| # | tool | request | response | ms | error |
| --- | --- | --- | --- | --- | --- |
| 1 | whalegenie_project_status | 2 | 202 | 26 |  |
| 2 | whalegenie_open_project | 2 | 159 | 5 |  |
| 3 | whalegenie_get_actions | 56 | 4253 | 133 |  |
| 4 | whalegenie_macro_template | 25 | 905 | 10 |  |
| 5 | whalegenie_put_macro | 327 | 335 | 53 |  |
| 6 | whalegenie_validate | 22 | 70 | 1 |  |
| 7 | whalegenie_apply | 22 | 64 | 20 |  |
| 8 | whalegenie_apply | 22 | 198 | 2 | yes |
| 9 | whalegenie_save_project | 2 | 266 | 11 |  |
| 10 | whalegenie_list_actions | 11 | 1215 | 8 |  |
| 11 | whalegenie_list_macros | 22 | 125 | 5 |  |
| 12 | whalegenie_list_runs | 11 | 34 | 2 |  |
| 13 | whalegenie_get_run_log | 2 | 172 | 1 | yes |
| 14 | whalegenie_open_project | 2 | 222 | 4 |  |
| 15 | whalegenie_put_macro | 1914 | 2090 | 4 |  |
| 16 | whalegenie_patch_macro | 125 | 2165 | 14 |  |
| 17 | whalegenie_get_macro | 54 | 3189 | 2 |  |
| 18 | whalegenie_patch_macro | 179 | 2234 | 5 |  |
| 19 | whalegenie_get_macro | 54 | 3187 | 1 |  |
| 20 | whalegenie_put_macro | 240 | 183 | 4 |  |
| 21 | whalegenie_patch_macro | 220 | 2223 | 11 |  |
| 22 | whalegenie_patch_macro | 279 | 2430 | 19 |  |
| 23 | whalegenie_get_macro | 54 | 3344 | 1 |  |
| 24 | whalegenie_patch_macro | 140 | 287 | 2 | yes |
| 25 | whalegenie_open_project | 2 | 222 | 0 |  |
| 26 | whalegenie_put_macro | 116 | 333 | 2 |  |
| 27 | whalegenie_validate | 22 | 233 | 1 |  |
| 28 | whalegenie_put_macro | 127 | 296 | 2 |  |
| 29 | whalegenie_validate | 22 | 197 | 0 |  |
| 30 | whalegenie_put_macro | 142 | 333 | 2 |  |
| 31 | whalegenie_validate | 22 | 235 | 0 |  |
| 32 | whalegenie_put_macro | 213 | 360 | 2 |  |
| 33 | whalegenie_validate | 22 | 204 | 0 |  |
| 34 | whalegenie_put_macro | 51 | 50 | 3 | yes |
| 35 | whalegenie_put_macro | 224 | 570 | 3 |  |
| 36 | whalegenie_get_macro_signature | 37 | 1211 | 7 |  |
| 37 | whalegenie_get_action | 26 | 2175 | 1 |  |
| 38 | whalegenie_discard | 22 | 24 | 1 |  |
| 39 | whalegenie_open_project | 2 | 222 | 1 |  |
| 40 | whalegenie_put_macro | 107 | 343 | 2 |  |
| 41 | whalegenie_apply | 22 | 460 | 2 | yes |
| 42 | whalegenie_discard | 22 | 24 | 0 |  |
| 43 | whalegenie_discard | 22 | 24 | 0 |  |
| 44 | whalegenie_discard | 22 | 24 | 0 |  |

