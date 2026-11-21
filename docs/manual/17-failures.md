# 失败原因与分支

所属：[WhaleGenie 使用说明](../manual.md)。

## 失败原因与分支

### 可以分支的失败原因

一步失败时，`control.try` 捕获到的 `caught.key` 是下表中的键；运行调试的
步骤行把键与这张表里的说明并排显示，`control.try` 还把这次失败的详情放在 `detail`。表里的 `{0}` 就是
那段说明。日志行与运行状态用的是另外两套词，不在这张表里。用户自己写的失败原因不产生新键：
`control.fail` 记为 `Run.StepFailedByMacro`，写下的原因放在详情里。

| 键 | 含义与常见原因 |
| --- | --- |
| `Run.AbortRefused` | 没有正在倒计时的关机可以取消。 |
| `Run.AhkNeedsV2` | 这一步用 AutoHotkey v2 运行脚本，而 {0} 是 v1。请安装 AutoHotkey v2，或者在这一步里指定一个已经装好的 v2。 |
| `Run.AlignNeedsNames` | 按列名放的时候，要写的每个单元格都得有个名字，而这一步最宽的一行有 {0} 个单元格。 |
| `Run.AudioRefused` | Windows 不接受这次声音调整：{0} |
| `Run.BackgroundNeedsPointer` | 「后台消息」这一档不做相对移动：它只把消息投给窗口，屏幕上的指针并没有动过。相对移动问的是指针现在在哪儿，改用前台或驱动级，或者用「鼠标移动到」把位置按绝对坐标写全。 |
| `Run.BackgroundPostFailed` | Windows 没有接受这次后台键鼠消息：{0}。目标窗口可能已经关闭，或者它的权限比鲸灵高。 |
| `Run.BadBase64` | 这段文本不是 Base64：{0} |
| `Run.BadEnvironment` | 这一行不是 NAME=value 的写法：{0} |
| `Run.BadEnvironmentName` | 「{0}」不是 Windows 能存环境变量的名字。 |
| `Run.BadExpression` | 这个值不是有效的表达式：{0} |
| `Run.BadInitial` | 局部变量的初始值读不出来，或者与它声明的类型不符：{0} |
| `Run.BadJson` | 这个文件不是有效的 JSON：{0} |
| `Run.BadJsonPath` | 无法解析这个 JSON 路径：{0} |
| `Run.BadList` | 这个值不是列表：{0} |
| `Run.BadMatchMode` | 这不是一种比对文字的方式：{0}。可以写的是 contains、startsWith、endsWith、equals、regex。 |
| `Run.BadModel` | 这个模型跑不起来：{0} |
| `Run.BadModelShape` | 这个模型答出的形状读不了：{0} |
| `Run.BadOption` | 「{0}」不是这一步允许的选项。 |
| `Run.BadArgumentType` | 传给「{0}」的值与它声明的类型不符。 |
| `Run.BadDeclaration` | 这个宏的声明「{0}」是空的、读不出来的、重名的、用了运行保留的名字，或者既必填又给了默认值。 |
| `Run.BadResult` | 返回值的「{0}」无效：结果不在动作目录里，或者结果键、目标变量名重复、读不出来。 |
| `Run.BadRegistryData` | 「{0}」不是一个 32 位注册表值能放下的数字。 |
| `Run.BadRegistryName` | 「{0}」不是注册表能用的键路径：写根之下的那一串，不要带根名，两端也不要带反斜杠。 |
| `Run.BadRowColumn` | 「{0}」的某一行写了这个列表没有的列，或者缺了它要的列。 |
| `Run.BadUserFailureKey` | 用户失败键必须从 User. 开始，后面是能读回来的变量名：{0} |
| `Run.BadPattern` | 这不是一个可用的匹配模式：{0} |
| `Run.RecordWhereNumber` | {0} 这一格要的是数字，却给了一条记录：记录是一组字段，不是数；要取其中一项用 field()。 |
| `Run.RegexTimeout` | 正则表达式在一秒内没有算完：{0} |
| `Run.RegistryRefused` | 这台机器不允许改动注册表：{0} |
| `Run.EnvironmentRefused` | 这台机器不允许写入这个环境变量：{0} |
| `Run.BadPoint` | 交给它的那条记录里没有 x、y（命中、窗口、元素都会带着这两个数，取的是那块矩形的中心）：{0} |
| `Run.BadPoints` | 多点比色的某一处读不出来：{0}。每一处都要有「往右」「往下」和「颜色」，缺一格、或者那一格不是数/颜色，就会报这个。 |
| `Run.BadPosition` | 这不是列表里的位置：请写整数，第一个写 1（{0}）。 |
| `Run.BadRegion` | 交给它的那条记录里没有 left、top、width、height（命中、窗口、之前截下的画面都会带着这四个数）：{0} |
| `Run.BadZip` | 这个文件不是 zip 压缩包，或者压缩包不完整：{0} |
| `Run.BreakOutsideLoop` | break 不在任何循环里，没有可以跳出的循环。 |
| `Run.BrightnessRefused` | 没有屏幕接受这个亮度。 |
| `Run.BrowserBadFilter` | 这一步的限定条件读不出来：{0} |
| `Run.BrowserBadProfile` | 浏览器档案不能叫这个名字：{0} |
| `Run.BrowserFailed` | 没能在浏览器里完成这一步：{0} |
| `Run.BrowserMissing` | 浏览器动作要用的浏览器还没装在这台机器上：{0} |
| `Run.BrowserNoAttribute` | 这一步要读元素的属性，但没说是哪个属性：请填上属性名，例如 href、src。 |
| `Run.BrowserNoDialog` | 没有等到对话框：盒子里的步骤没有让页面问话，也就没有可回答的东西。 |
| `Run.BrowserNoNewTab` | 没有等到新的标签页：这一步跟的那次点击要么没有开新标签页，要么开得比这一步等待的时间还久。 |
| `Run.BrowserNoScript` | 这一步没有填写要在页面里跑的脚本。 |
| `Run.BrowserNoSelector` | 这一步没说要页面上的哪个元素：请写选择器，或在页面上点一下那个元素。 |
| `Run.BrowserNoTab` | 没有可以切过去的标签页：浏览器里没有一个匹配 {0}。 |
| `Run.BrowserNotFound` | 页面上没有这一步要找的那个元素。试过：{0} |
| `Run.BrowserNotOpen` | 还没有打开浏览器；用页面前先打开一个。 |
| `Run.BrowserTabNeedsText` | 这一步要写明标签页的标题或网址里包含什么。 |
| `Run.BrowserTooFew` | 页面上的元素没有「第几个命中」要的那么多，等于没找到。试过：{0} |
| `Run.CaptureFailed` | 这块屏幕区域无法复制：{0} |
| `Run.ClipboardBusy` | 另一个程序正占用剪贴板。 |
| `Run.ClipboardTimeout` | 剪贴板一直没有变化。 |
| `Run.ClipboardWriteFailed` | 剪贴板没有接受这段文本。 |
| `Run.ColourNotFound` | 没有找到这个颜色：{0}。这一步打开了「没找到就算失败」，所以没找到按失败处理；关掉它，没找到只是一个结果（found 为假）。 |
| `Run.NoColourPoints` | 多点找色的图案里一处对比色都没有：只找基准色用「找色」，所以这一步不会跑。 |
| `Run.CommandTimeout` | 命令没有在限定时间内结束：{0} |
| `Run.ContinueOutsideLoop` | continue 不在任何循环里，没有可以跳到下一轮的循环。 |
| `Run.DetectNotFound` | 始终没有检测到：{0} |
| `Run.DeviceFailed` | 鲸灵之外的组件出错：{0} |
| `Run.ElementBadCondition` | 这一步的限定条件读不出来：{0} |
| `Run.ElementNotATable` | 这个元素不是表格：{0} |
| `Run.ElementNotCheckable` | 这个元素不是勾选框：{0} |
| `Run.ElementNotClickable` | 这个元素点不了：{0} |
| `Run.ElementNotExpandable` | 这个元素不能展开或收起：{0} |
| `Run.ElementNotFound` | 元素始终没有出现：{0} |
| `Run.ElementNotInvocable` | 这个元素没有办法不动鼠标地按下：{0} |
| `Run.ElementNotScrollable` | 这个元素无法滚动到可见：{0} |
| `Run.ElementNotSelectable` | 这个元素里没有可以选择的内容：{0} |
| `Run.ElementNotWritable` | 这个元素写不进去：{0} |
| `Run.ElevationNoEnvironment` | {0} 不能同时以管理员身份启动又自带环境变量。 |
| `Run.ElevationRefused` | 已请求以管理员身份启动 {0}，但这个请求被拒绝了。 |
| `Run.EmptyChord` | 这个组合键里没有任何按键。 |
| `Run.EmptyRegion` | 这不是一个可用的屏幕区域：{0} |
| `Run.EmptySelector` | 没有填写要查找的元素选择器。 |
| `Run.Failed` | 该步骤执行失败。 |
| `Run.FileFailed` | 这个文件读写失败：{0} |
| `Run.FileNotFound` | 找不到这个文件：{0} |
| `Run.FolderNotFound` | 找不到这个文件夹：{0} |
| `Run.GlobalMustExist` | 全局变量 {0} 不存在，请先在宏编辑器的变量中心创建 |
| `Run.ImageNotFound` | 始终没有找到参考图片：{0} |
| `Run.IndexOutOfRange` | 下标超出列表范围：{0} |
| `Run.ItemNotFound` | 没有找到要选的那一项：{0} |
| `Run.KeyNotPressable` | 这个键在这台机器上按不出去：{0} |
| `Run.LayoutNotFound` | 这台机器上没有安装匹配的键盘布局：{0} |
| `Run.LayoutNotSwitched` | 已请求当前窗口切换到 {0}，但它始终没有完成切换。 |
| `Run.LockRefused` | Windows 没有锁定屏幕。 |
| `Run.MacroNotFound` | 本项目里没有名为“{0}”的宏。 |
| `Run.MacroTooDeep` | 宏之间的互相调用层级过深：{0} |
| `Run.MissingArgument` | 这个宏要求“{0}”，调用处或直接运行都没有填。 |
| `Run.GivenWithValue` | “{0}”既填了值，又勾了「传空值」：两个只能留一个，运行到这一步会停下。 |
| `Run.MissingCondition` | 这一步没有填写条件。 |
| `Run.MissingField` | 「{0}」是必填的，这一步没有填。 |
| `Run.MissingImage` | 参考图片读不出来：{0} |
| `Run.MissingInterpreter` | 这一步没有写用哪个程序运行脚本。 |
| `Run.MissingItem` | 这一步没有填写要选择哪一项。 |
| `Run.MissingJsonPath` | JSON 的这个位置上没有值。 |
| `Run.MissingModel` | 模型文件读不出来：{0} |
| `Run.MissingName` | 没有填写名称。 |
| `Run.MissingPath` | 没有填写文件路径。 |
| `Run.MissingProgram` | 没有填写程序名。 |
| `Run.MissingQuestion` | 这一步要问一句话，但问题没有写。 |
| `Run.MissingScript` | 这一步没有填写要运行的脚本。 |
| `Run.MissingScriptExtension` | 这一步没有写脚本要用什么后缀。 |
| `Run.MissingTargetWindow` | 后台输入需要目标窗口，请先获取目标窗口。 |
| `Run.MissingVariable` | 没有填写变量名 |
| `Run.MissingWindow` | 没有填写窗口标题：{0} |
| `Run.NoAutoHotkey` | 这台机器上没有 AutoHotkey，无法运行这段脚本。请先安装 AutoHotkey（版本 2），或换一种脚本类型。 |
| `Run.NoDevice` | 这台机器暂不支持该操作：{0} 尚未接入。 |
| `Run.NoDriver` | 驱动级输入需要先装好 usbip-win2 驱动，设置页的驱动区显示状态。 |
| `Run.NoDriverAttached` | 驱动级输入没有送达这台机器：送它的是虚拟键鼠，机器没有认领它们。确认 usbip-win2 驱动装好了，并关掉其它正在使用虚拟设备的程序。 |
| `Run.NoDriverKey` | 虚拟键盘没有这个键：{0} |
| `Run.NoDriverLibrary` | 驱动级输入找不到自带虚拟 USB 设备的库（libVIIPER.dll）：重新完整解压一次发布包。 |
| `Run.NoDriverServer` | 驱动级输入没能把它的服务端和设备装到这台机器上：关掉别的正在驱动虚拟设备的程序（另一个鲸灵，或者自己跑着的 VIIPER 服务端）再试一次。 |
| `Run.NoDriverTyping` | 驱动级输入打不出这个字符：{0} |
| `Run.NoEntryPoint` | 这段脚本没有可以运行的入口：要有一个带 static Main 的类。 |
| `Run.NoForegroundWindow` | 当前没有最前面的窗口，无法执行。 |
| `Run.NoFreeName` | 在 {0} 旁边找不到还能用的名字了。 |
| `Run.NoGamepad` | 本机还没有接上虚拟手柄。 |
| `Run.NoGraphicsCapture` | 这台机器用这种方式读不到画面：{0}。Graphics Capture 需要 Windows 10 2004 或更高版本与 Direct3D 11 设备；显卡不肯交出的显示器它也读不到。 |
| `Run.NoKeys` | 这条序列里一个组合键都没有。 |
| `Run.NoPackages` | 这一步没有写要装哪个包。 |
| `Run.NoPython` | 这台机器上没有 Python，无法运行这段脚本：启动 {0} 失败。请安装 Python 3，并让命令行里的 python 能运行它。 |
| `Run.NoSuchColumn` | 这张表里没有叫 {0} 的列：写表头里的名字，或者写这一列所在的字母。 |
| `Run.NoSuchProcess` | 没有这个正在运行的程序：{0} |
| `Run.NoSuchStep` | 这个宏里没有叫 {0} 的步骤。 |
| `Run.NotAList` | {0} 不是列表 |
| `Run.NotALookingStep` | 这一步不看屏幕，没有可以试找的东西。 |
| `Run.NotANumber` | {0} 不是数字，不能增加 |
| `Run.NothingToRethrow` | 这里没有接住的失败可以重新抛出。 |
| `Run.NotConfirmed` | 这一步做下去无法撤回，没有得到确认：有人点了取消，或者根本没有人在场（触发器、MCP 调用）。按这一步的「失败时」处理。 |
| `Run.NuGetFailed` | NuGet 没能做成这件事：{0} |
| `Run.OneColumnOnly` | 把一列的值做成一个普通列表，只能点名一列，这一步点了不止一列（{0}）。 |
| `Run.OutOfRange` | 「{0}」超出了这一步允许的范围。 |
| `Run.PipFailed` | pip 没能做成这件事：{0} |
| `Run.PixelOutsideScreen` | 这个坐标不在屏幕上：{0} |
| `Run.PowerRefused` | Windows 不接受对这台电脑执行该操作。 |
| `Run.ProcessTimeout` | 进程 {0} 没有在限定时间内结束。 |
| `Run.ProgramFailed` | 这个程序无法启动或运行：{0} |
| `Run.ProgramNotFound` | 始终没有等到 {0} 启动。 |
| `Run.ScreenNotStable` | 等了 {0} 毫秒画面仍在变化。 |
| `Run.ScriptBridgeLost` | 脚本起来了，但没能连上鲸灵：{0} |
| `Run.ScriptCompileFailed` | 脚本没有编译通过：{0} |
| `Run.ScriptFailed` | 脚本以退出码 {0} 结束 |
| `Run.SoundRefused` | 这台机器没有可用的声音设备，放不出这个提示音。 |
| `Run.StepFailedByMacro` | 宏把这一步判为失败。{0} |
| `Run.SystemVariableReadOnly` | 「{0}」是鲸灵自己提供的值，变量文件不能写它。 |
| `Run.TextNotFound` | 始终没有找到文字：{0} |
| `Run.TextNotTyped` | 这段文字没能输出去：没有窗口接收它。目标窗口可能已经关闭，或者它的权限比鲸灵高。 |
| `Run.Timeout` | 该步骤的运行时间超过了允许的 {0} 毫秒。 |
| `Run.DuplicateArgument` | 这一步有两行都写了形参“{0}”，传哪一个并不确定。 |
| `Run.UndeclaredArgument` | 被调用的宏没有形参“{0}”，这一步传的值没有去处。 |
| `Run.UndeclaredResult` | 被调用的宏没有声明要交回“{0}”。 |
| `Run.UnknownBrightnessAction` | 无法识别这个亮度动作：{0} |
| `Run.UnknownDesktopAction` | 无法识别这种整理桌面的方式：{0} |
| `Run.UnknownGamepadButton` | 虚拟手柄没有这个按键：{0} |
| `Run.UnknownGamepadStick` | 这不是一个手柄摇杆：{0} |
| `Run.UnknownGamepadTrigger` | 这不是一个手柄扳机：{0} |
| `Run.UnknownInputMethodAction` | 无法识别这个输入法动作：{0} |
| `Run.UnknownKey` | 无法识别这个键名：{0} |
| `Run.UnknownLabel` | 标签文件里没有这个类：{0} |
| `Run.UnknownNotification` | 无法识别这种通知类型：{0} |
| `Run.UnknownPageKey` | 网页的键盘里没有这个键：{0} |
| `Run.UnknownPowerAction` | 无法识别这个电源动作：{0} |
| `Run.UnknownScriptKind` | 无法识别这种脚本类型：{0} |
| `Run.UnknownSound` | 无法识别这种提示音：{0} |
| `Run.UnknownSystemField` | 无法识别这个系统字段：{0} |
| `Run.UnknownVolumeAction` | 无法识别这个音量动作：{0} |
| `Run.Unsupported` | 引擎尚不支持这个动作：{0} |
| `Run.VariableTableFull` | 这次运行自己的变量表已经装到上限（20 万个名字）。运行期拼出来的名字会一直留到运行结束，每轮都写新名字的循环最容易撞上；用「清除变量」放掉不再需要的名字，或改成每轮复用同一个名字。 |
| `Run.VariableTypeMismatch` | 写进「{0}」的值与它声明的类型不符。数字变量只收数字，列表只收列表，布尔只收真假。 |
| `Run.WaitColorTimeout` | 一直没有等到颜色 {0}。 |
| `Run.WaitTimeout` | 等待超时，{0} 始终没有成立。 |
| `Run.WindowFailed` | 这个窗口不接受该操作：{0} |
| `Run.WindowNotFound` | 没有找到符合该规则的窗口：{0} |
| `Run.WindowRecordField` | 这条窗口记录里没有 {0} 这一项，而现在的匹配方式要从它取值。换一种匹配方式，给记录补上这一项，或者用 field(记录, "字段名") 取出你要的那一项。 |
| `Run.BadWindowRule` | 窗口字段要放一条获取窗口得到的规则，或者放一个保存这类规则的变量；现在写的内容不是规则：{0} |
| `Run.WindowTimeout` | 始终没有等到符合该规则的窗口：{0} |

### 用户自定义失败原因

「判为失败」留空失败键时仍报 `Run.StepFailedByMacro`。填写时只能使用 `User.` 开头、后面是宏语言能读回来的
变量名，例如 `User.NoScreen`；这样的键可以写在「何时捕获」里与 `caught.key` 比较。用户失败键不使用
`Run.` 前缀，也不混进上面的引擎失败键表。
