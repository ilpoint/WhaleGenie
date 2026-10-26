## WhaleGenie MCP self-check — http://127.0.0.1:59482/mcp — 2026-10-27 01:42:08

Result: pass, 36 pass, 0 fail, 0 missing, 0 skip

| check | result | detail |
| --- | --- | --- |
| server.instructions | pass | 3255 characters |
| tools.list | pass | 48 tools, every required one present |
| tools.optional | pass | every one of 38 optional tools present |
| prompts.list | pass | 2 prompts |
| prompts.text | pass | both prompts carry the workflow |
| loop.status | pass | path=C:\Users\q1233\AppData\Local\Temp\wg-mcp-doctor-f258f36cf94740f781c5e9091128e8ce\doctor.wgmacro revision=0 |
| loop.open | pass | draft b4ae84f2 at revision 0 |
| discovery.batch | pass | 3 actions in one call, 4225 characters |
| loop.put | pass | 2 steps written |
| loop.validate | pass | no blocking problem |
| loop.apply | pass | revision 7 |
| reject.double_apply | pass | {"error":{"code":"AlreadyApplied","message":"This draft has already been applied.","hint":"Open the project again to get a fresh draft; one draft is written once.","retryable":false,"details":null}} |
| loop.save | pass | C:\Users\q1233\AppData\Local\Temp\wg-mcp-doctor-f258f36cf94740f781c5e9091128e8ce\doctor.wgmacro |
| patch.put | pass | 40 steps, 40 ids in the outline |
| patch.ids | pass | 40 ids, 40 distinct |
| patch.set | pass | one parameter for 125 bytes against 1914 for the whole macro |
| patch.value | pass | ms is 777 |
| patch.structure | pass | insert and remove kept the count at 40 (was 40) |
| patch.row | pass | one row changed and one added for 279 bytes |
| patch.refuse | pass | {"error":{"code":"BadPlacement","message":"\"control.case\" is a hidden action and cannot stand in the top level.","hint":"An ordinary list takes visible actions, plus the grouping container the editor makes.","retryable":true,"details":{"s… |
| reject.action_key | pass | located it: [aa22] UnknownAction: This step's action is not in the catalogue any more: delete the step, or pick the action again. |
| reject.child_keys | pass | located it: [bb22] CaseOutsideSwitch: A case can only run in a switch's branch list. |
| reject.record_field | pass | located it: [cc22] WindowBounds: Set at least one side, or pick the window bounds, so the mover has something to change. |
| reject.enum_value | pass | located it: [dd22] BadOption: "Match mode (operator)" is not one of the answers "bogus" allows. |
| reject.bad_json | pass | An error occurred invoking 'whalegenie_put_macro'. |
| signature.tool | pass | the signature names the argument columns and gives a call skeleton |
| action.callShape | pass | the catalogue names where a call's parameters come from and the rows they have |
| resources.listed | pass | 2 resources, 1 templates: whalegenie://actions / whalegenie://action-matrix / whalegenie://actions/{key} |
| resources.read | pass | the catalogue reads back as a resource with 176 actions |
| gate.control_off | pass | sending input is refused until the person allows it |
| gate.run_off | pass | running a macro is refused until the person allows it |
| range.absent | pass | this server has no range window and says so |
| gate.apply_invalid | pass | {"error":{"code":"DraftHasBlockingProblems","message":"The draft has 1 blocking problem(s), so it was not applied.","hint":"Call whalegenie_validate, fix what it names, then apply again. Pass force when the problems are known and wanted.","… |
| cleanup.b4ae84f2 | pass | discarded |
| cleanup.94c44652 | pass | discarded |
| cleanup.86b1b039 | pass | discarded |

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
| scenario.longestCall | 4225 |
| scenario.requestBytes | 480 |
| scenario.responseBytes | 6484 |
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
| 1 | whalegenie_project_status | 2 | 202 | 31 |  |
| 2 | whalegenie_open_project | 2 | 159 | 6 |  |
| 3 | whalegenie_get_actions | 56 | 4225 | 121 |  |
| 4 | whalegenie_macro_template | 25 | 905 | 11 |  |
| 5 | whalegenie_put_macro | 327 | 395 | 55 |  |
| 6 | whalegenie_validate | 22 | 70 | 2 |  |
| 7 | whalegenie_apply | 22 | 64 | 21 |  |
| 8 | whalegenie_apply | 22 | 198 | 3 | yes |
| 9 | whalegenie_save_project | 2 | 266 | 13 |  |
| 10 | whalegenie_list_actions | 11 | 1206 | 12 |  |
| 11 | whalegenie_list_macros | 22 | 125 | 7 |  |
| 12 | whalegenie_list_runs | 11 | 34 | 4 |  |
| 13 | whalegenie_get_run_log | 2 | 172 | 2 | yes |
| 14 | whalegenie_open_project | 2 | 222 | 5 |  |
| 15 | whalegenie_put_macro | 1914 | 2690 | 5 |  |
| 16 | whalegenie_patch_macro | 125 | 2765 | 20 |  |
| 17 | whalegenie_get_macro | 54 | 3189 | 3 |  |
| 18 | whalegenie_patch_macro | 179 | 2834 | 6 |  |
| 19 | whalegenie_get_macro | 54 | 3187 | 2 |  |
| 20 | whalegenie_put_macro | 240 | 198 | 6 |  |
| 21 | whalegenie_patch_macro | 220 | 2838 | 13 |  |
| 22 | whalegenie_patch_macro | 279 | 3045 | 13 |  |
| 23 | whalegenie_get_macro | 54 | 3344 | 2 |  |
| 24 | whalegenie_patch_macro | 140 | 287 | 3 | yes |
| 25 | whalegenie_open_project | 2 | 222 | 1 |  |
| 26 | whalegenie_put_macro | 116 | 350 | 19 |  |
| 27 | whalegenie_validate | 22 | 235 | 1 |  |
| 28 | whalegenie_put_macro | 127 | 304 | 3 |  |
| 29 | whalegenie_validate | 22 | 190 | 1 |  |
| 30 | whalegenie_put_macro | 142 | 339 | 3 |  |
| 31 | whalegenie_validate | 22 | 226 | 1 |  |
| 32 | whalegenie_put_macro | 213 | 391 | 3 |  |
| 33 | whalegenie_validate | 22 | 205 | 1 |  |
| 34 | whalegenie_put_macro | 51 | 50 | 5 | yes |
| 35 | whalegenie_put_macro | 224 | 574 | 4 |  |
| 36 | whalegenie_get_macro_signature | 37 | 1211 | 8 |  |
| 37 | whalegenie_get_action | 26 | 2121 | 2 |  |
| 38 | whalegenie_discard | 22 | 24 | 1 |  |
| 39 | whalegenie_mouse_move | 13 | 287 | 4 | yes |
| 40 | whalegenie_run_macro | 32 | 327 | 5 | yes |
| 41 | whalegenie_range_state | 2 | 229 | 2 | yes |
| 42 | whalegenie_open_project | 2 | 222 | 1 |  |
| 43 | whalegenie_put_macro | 107 | 360 | 2 |  |
| 44 | whalegenie_apply | 22 | 462 | 1 | yes |
| 45 | whalegenie_discard | 22 | 24 | 0 |  |
| 46 | whalegenie_discard | 22 | 24 | 0 |  |
| 47 | whalegenie_discard | 22 | 24 | 0 |  |

