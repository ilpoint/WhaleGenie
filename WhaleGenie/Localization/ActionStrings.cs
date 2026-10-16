using System.Collections.Generic;
using System.Globalization;

namespace WhaleGenie.Localization;

/// <summary>
/// Chinese text for the action catalogue. Keys are
/// <c>{actionKey}.name</c>, <c>{actionKey}.desc</c> and
/// <c>{actionKey}.{parameter}.label</c> / <c>.hint</c>; anything missing falls
/// back to the English text stored in the catalogue itself.
/// </summary>
internal static class ActionStrings
{
    /// <summary>
    /// The hint the fourteen input actions share for their input-mode picker. They were written
    /// once rather than fourteen times, so a change to how input is described stays in one place.
    /// </summary>
    private const string InputModeHint =
        "这一步的输入怎么送出去。前台发给当前有焦点的窗口；后台把消息投给下面的目标窗口，"
        + "不用抢焦点，屏幕上的鼠标也不会真的移动；驱动级通过虚拟 USB 设备发送，"
        + "需要 usbip-win2 驱动，设置窗口里能看它在不在。";

    /// <summary>
    /// The hint for the window background input posts its messages at. The window is recognised the
    /// way a window action recognises one, so the hint says so in the same words.
    /// </summary>
    private const string TargetWindowHint =
        "后台输入把消息投给哪个窗口，靠下面两行认出，认法与窗口动作一致，不区分大小写。"
        + "只有输入方式选后台消息时用得到它。";

    /// <summary>
    /// The hint every action that takes a position or a rectangle shares for its coordinate-origin
    /// picker, written once for the same reason the input-mode hint is.
    /// </summary>
    private const string AnchorModeHint =
        "这一步的坐标从哪儿量起，默认屏幕像素。屏幕像素＝桌面上的位置；窗口左上角＝从目标窗口"
        + "外框的左上角算起；窗口客户区＝从窗口边框内侧算起。选后两种时这一步跟着窗口走："
        + "窗口移动或缩放之后，坐标仍落在窗口里的同一处。";

    /// <summary>The matching hint for the window those coordinates are measured from.</summary>
    private const string AnchorWindowHint =
        "坐标基准选窗口时才用：这个窗口的标题，填一部分即可，不区分大小写。"
        + "留空表示运行时最前面的那个窗口。";

    /// <summary>
    /// The same rule as the window actions use, said for a step that reads the screen: the window
    /// the coordinates are counted from is also the window the pixels come out of, so which window
    /// it is matters twice over.
    /// </summary>
    private const string AnchorWindowRuleHint =
        "坐标基准选窗口时才用：这个窗口靠下面两行认出，认法与窗口动作一致，不区分大小写；"
        + "这一步读到的画面也取自这个窗口。留空表示运行时最前面的那个窗口。";

    /// <summary>
    /// The switch every step that writes its place as two numbers gains for taking that place from
    /// a record instead, and what that record says. Written once rather than once per field, so
    /// "把刚找到的东西点一下" is described in the same words wherever a place is written down.
    /// </summary>
    private const string RecordPlaceSwitchLabel = "位置取自一条记录";

    private const string RecordPlaceSwitchHint =
        "勾上后位置取自下面那一格里的记录，上面的数字不写进宏文件，也不再参与。位置就是上一步刚找到"
        + "的东西时用它。";

    private const string RecordFieldLabel = "记录";

    private const string RecordPlaceFieldHint =
        "取出位置的那条记录：命中、窗口、元素都带着 x、y，取的是那块矩形的中心，单位是屏幕像素。"
        + "算出这条记录的算式也可以填在这里。记录里的数是屏幕像素，所以「坐标基准」只挪写出来的"
        + "数字。";

    /// <summary>
    /// The same pair for a rectangle written down as four numbers, which is what reading or copying
    /// one part of the screen asks for: a hit, a window or a picture taken earlier carries all four
    /// sides, so "读刚找到的那一块" is one field rather than four.
    /// </summary>
    private const string RecordBoxSwitchLabel = "矩形取自一条记录";

    private const string RecordBoxSwitchHint =
        "勾上后矩形取自下面那一格里的记录，上面四个数不写进宏文件，也不再参与。矩形就是上一步刚找到"
        + "或刚读出来的那一块时用它。";

    private const string RecordBoxFieldHint =
        "取出矩形的那条记录：命中、窗口、之前截下的画面都带着 left、top、width、height，单位是屏幕"
        + "像素。算出这条记录的算式也可以填在这里。记录里的数是屏幕像素，所以「坐标基准」只挪写出来"
        + "的数字。";

    /// <summary>
    /// The same pair for the four fields a window's own rectangle is written in, where the numbers
    /// are the window as it is seen and a window record says exactly the same four things.
    /// </summary>
    private const string WindowRecordSwitchLabel = "位置与大小取自一条记录";

    /// <summary>
    /// The switch every mouse action shares for taking its place from the pointer rather than from
    /// numbers, which is what a macro means when the place is wherever the user left the mouse.
    /// </summary>
    private const string AtPointerLabel = "就在指针当前所在位置";

    private const string AtPointerHint =
        "勾上后这一步不写坐标，运行时在指针当时停留的地方完成，也不移动指针。要按用户自己放好的位置"
        + "动作时用它；勾上后上面的“X”“Y”不写进宏文件，也不再参与。";

    /// <summary>
    /// The hint every step that can hand a result over gives its returns table. It is written once
    /// rather than forty-seven times, so the way a result is named is described in one place: the
    /// name of the note and the name of the result, which the row itself may override.
    /// </summary>
    /// <summary>
    /// The hint the nine actions that pick a mouse button share. Written once rather than nine
    /// times so the wording cannot drift apart.
    /// </summary>
    private const string MouseButtonHint = "用哪个鼠标键，默认左键。";

    private const string ReturnsHintText =
        "勾上这一步要留下的结果：每一项各自成为一个变量，名字默认取这一步的备注与这个结果的名字，"
        + "重名时补上步骤标识，也可以在下面每一行自己改，后面的步骤按这个名字取用。"
        + "一个都不勾也可以，这一步就不留下任何结果。";

    /// <summary>
    /// The label and hint every step that looks at the screen shares for how old a picture it will
    /// accept, written once for the same reason the capture picker is.
    /// </summary>
    private const string FrameMaxAgeLabel = "接受多久以内的画面";

    private const string FrameMaxAgeHint =
        "这一步接受的画面可以有多旧，单位毫秒，0–60000，默认 0，也就是每次都重新读屏幕。"
        + "连着看同一个地方时改它：填一个毫秒数之后，同一来源（同一个窗口，或桌面）上刚抓过、"
        + "又不超过这个时长的画面会被直接裁下来复用，第一次读的是整个窗口或整块屏幕，"
        + "省下来的是它后面那几次。";

    /// <summary>
    /// The label and the two hints every step that looks at the screen shares for how long it leaves
    /// between two looks. One field, because both readings answer one question — how many times a
    /// second the macro reads the screen; two hints, because a wait spends that number as its own
    /// clock while a step that looks once is only held to it.
    /// </summary>
    private const string LookIntervalLabel = "查看间隔";

    private const string LookGapHint =
        "同一步骤两次看屏幕之间至少隔多久，单位毫秒，0–60000，默认 0，也就是轮到它就立刻看。"
        + "这一步写在循环里、每圈都读同一个地方时改它：读得比用得上的还快，多出来的那几次"
        + "都是白花的。7 毫秒约等于每秒 144 次。";

    /// <summary>
    /// The hint for the field on a step that waits: the beat it looks on, which is left empty so
    /// that the screen itself says how fast looking can tell anything.
    /// </summary>
    private const string LookPollHint =
        "等待期间多久看一次屏幕，单位毫秒，0–60000。留空表示按屏幕刷新一次的时间来看："
        + "60 赫兹的屏 16 毫秒一次，240 赫兹的屏 4 毫秒一次，也就是每隔一次刷新看一回；"
        + "填 0 表示上一次看完了立刻再看。等得起就调大，省下的是机器的开销；要看的东西一闪而过就调小。";

    /// <summary>The hint every window action shares for the text it recognises a window by.</summary>
    private const string WindowTitleHint =
        "窗口靠这段文字认出：它和「匹配方式」里选的那一种比，不区分大小写。留空表示最前面那个窗口。";

    /// <summary>What an empty match text means to a step that lists windows rather than acting on one.</summary>
    private const string WindowListTitleHint =
        "窗口靠这段文字认出：它和「匹配方式」里选的那一种比，不区分大小写。留空表示所有窗口（一次列完，"
        + "不做筛选）。";

    /// <summary>
    /// Where the picture a capture step just took is written out. The two capture steps ask it the
    /// same way, so the line under it is written once.
    /// </summary>
    private const string CaptureFileHint =
        "这张图写到哪个文件；留空就只交给变量、不写文件（默认留空）。格式一律是 PNG，"
        + "同名文件会被替换。";

    /// <summary>The label every window action gives the text its window is recognised by.</summary>
    private const string WindowMatchTextLabel = "匹配内容";

    /// <summary>
    /// The one choice every window rule asks: what the text is held up against, and how. It is
    /// written once because it is the same nine ways wherever a window is recognised, and the three
    /// families of actions that ask it each add the sentence about what the settings beside the
    /// field do.
    /// </summary>
    private const string WindowHowLabel = "匹配方式";

    private const string WindowHowHint =
        "这个窗口靠什么认出来：「匹配内容」里那段文字和窗口的哪一部分比、怎么比，不区分大小写。标题上有"
        + "五档：包含、开头、结尾、等于、正则；进程名与窗口类名各两档：包含或等于。标题随文档名或界面语言"
        + "变化时，改用进程名或窗口类名；名字会带上版本号之类的尾巴时，用「等于」而不是「包含」。";

    /// <summary>
    /// How the writing a step read is held up against the writing it looks for. Written once because
    /// the five ways are the whole program's (the engine's <c>TextCompare</c>): the two actions that
    /// read the screen and the two conditions about writing ask exactly the same question.
    /// </summary>
    private const string TextMatchHint =
        "读到的文字与要查找的文字怎么比，五档：包含（默认）、以…开头、以…结尾、等于、正则匹配，都不区分"
        + "大小写。「等于」在比之前去掉两端的空格，屏幕上读到的字常带着边上的空白。只看一头时用「以…开头」"
        + "或「以…结尾」，不必写正则；这一段要当通配用才选「正则匹配」。这一格留空按「包含」走；手写的宏"
        + "文件里写了这五档以外的字，这一步失败并写出为什么。";

    private const string WindowHowTitleContains = "标题包含";
    private const string WindowHowTitleStartsWith = "标题开头";
    private const string WindowHowTitleEndsWith = "标题结尾";
    private const string WindowHowTitleEquals = "标题等于";
    private const string WindowHowTitleRegex = "标题正则";
    private const string WindowHowProcess = "进程名";
    private const string WindowHowProcessEquals = "进程名等于";
    private const string WindowHowClass = "窗口类名";
    private const string WindowHowClassEquals = "窗口类名等于";

    /// <summary>The hint the four text-file actions share for their encoding picker.</summary>
    private const string EncodingHintText =
        "文件里的文字按什么编码变成字节。默认 UTF-8，它也是现在通用的写法；带 BOM 的是记事本默认保存的样子；"
        + "GBK 是中文 Windows 自己写文本文件用的编码；UTF-16 是一些老 Windows 程序认的。"
        + "读取时如果文件开头有 BOM，会以 BOM 为准；没有 BOM 又根本不是 UTF-8 的，按本机代码页读"
        + "（中文 Windows 是 GBK），别的机器导出的 CSV 因此也能直接读。";

    private const string EncodingUtf8 = "UTF-8";
    private const string EncodingUtf8Bom = "UTF-8（带 BOM）";
    private const string EncodingGbk = "GBK（中文）";
    private const string EncodingUtf16 = "UTF-16";

    /// <summary>
    /// The hint the actions that can measure from a control share for the control they measure from,
    /// written once for the same reason the coordinate-origin hint is.
    /// </summary>
    private const string AnchorElementHint =
        "坐标基准选界面元素时才用：这个元素的写法，例如 Button[name='Save']，可以用"
        + "“拾取元素”从屏幕上取。每次运行时都会重新找它，元素移动之后坐标仍落在它身上。";

    /// <summary>The hint every search-region field shares, in the wording the English one uses.</summary>
    private const string RegionHintText =
        "搜索范围，一行一块，可以填多块。每行填 x、y、宽、高四个数字，或用“框选”拖出来；"
        + "一行也可以填变量：图片变量（在前一步截取的那张图上搜索）或写成 x,y,宽,高 的矩形"
        + "变量，还可以填一条带矩形的记录（命中、窗口、元素都行）。记录里的数是屏幕像素，"
        + "「坐标基准」只挪写出来的四个数字。留空表示整个屏幕（默认）。";

    /// <summary>The hint the "which hit" fields share.</summary>
    private const string MatchIndexHint =
        "“命中排序”里的第几个，从 1 开始，默认 1。超出命中数量时这一步按没找到处理。";

    /// <summary>
    /// What every field naming an element of a page starts by saying about itself: the ways of
    /// naming it are written one to a line and tried in that order, and the one thing such a line
    /// holds that is not a selector is a variable's text. What a later line does and does not stand
    /// in for is spelled out here rather than left to be discovered from a run that did not go the
    /// way it was meant to: an element that was found and then refused is not what a fallback is for.
    /// </summary>
    private const string PageTargetHint =
        "选择器一行一条：从上往下依次试，第一条能认出元素的就是这一步用的；多写一条是页面改版之后仍然"
        + "找得到的办法。换下一条只在这一条认不到元素的时候生效，认到了却点不动不换。其中的 $变量 会"
        + "替换成它的值。四种按页面上的字认的写法另有名字：label= 表单标签、placeholder= 占位文字、"
        + "alt= 图片说明、title= 提示文字，都按“包含”比、忽略大小写。元素在框架里时，先写 "
        + "frame=<框架的选择器> >> 。";

    /// <summary>
    /// The hint the "which match" fields of the page actions share. What is being counted comes
    /// first, because "the second one" says nothing until it is said of what.
    /// </summary>
    private static string PageMatchIndexHint(string what) =>
        what + "是第几个：选择器可能同时对上多个元素，这里说这一步要的是它们中的第几个，按在页面上出现"
        + "的顺序从 1 数起，默认 1；页面上没有这么多时按没找到处理。";

    /// <summary>
    /// The hint the "narrow down" field of every page action shares: what a condition is for, which
    /// ways of narrowing there are, and what leaving it empty means. Only the words for the element
    /// being meant change from one action to the next, so the rest is written once.
    /// </summary>
    private const string NarrowHint =
        "还必须是哪一个：选择器同时对上多个元素时，用这里的限定说清要的是它们中的哪一个，一行一条，"
        + "所有限定同时成立。可以限定元素里面显示的文字、里面有没有某个元素、它在哪个容器里面、"
        + "或者只要看得见的。值里可以写变量名，运行时替换成它的值；一行什么都没填就不算数。"
        + "一条都不写时只用选择器认元素，与没有这一格时完全一样。";

    /// <summary>
    /// The hint every action that lists several reference pictures shares for its picker: which of
    /// the pictures the step goes with.
    /// </summary>
    private const string ImageSetHint =
        "上面列出的几张参考图，这一步认哪一张。默认“认出的第一张”，是同一个东西有两套画法时的用法；"
        + "“最像的那一张”把每一张都比一遍，取分数最高的一张，用来在两张相像的图标里挑更像的一张；"
        + "“每一张都要”要求这几张同时都在，缺一张就当没找到，这一步只对整套都在的画面动手。"
        + "交回命中时以列在第一张的那张为准，“第几个命中”也在这张里数。";

    /// <summary>The hint every "which way to recognise the picture" picker shares.</summary>
    private const string MatchAlgorithmHint =
        "参考图用哪种方式识别，默认归一化相关。归一化相关：先抵消整体明暗再比对，画面变亮变暗"
        + "仍认得出。相关：不抵消明暗，参考图很小时更稳。逐像素：要求两处几乎一样，纯色图标、"
        + "对话框截图适用。特征比对：把两边明显的特征点配对，画面缩放或有小改动也认得出，"
        + "速度较慢，且只返回一个命中。";

    /// <summary>The hint the "leave this colour out" fields share.</summary>
    private const string IgnoreColourHint =
        "参考图里这个颜色不参与比对，用于参考图中会变化的部分，例如按钮上的数字、会走动的进度条。"
        + "留空表示整张图都参与（默认）。";

    /// <summary>The hint the "least feature pairs" fields share.</summary>
    private const string MinFeaturesHint =
        "只在识别算法选“特征比对”时有效。要配对上多少对特征点才算找到，1–500，越大越严格，"
        + "默认 6。";

    /// <summary>The hint every "what order the hits are counted in" picker shares.</summary>
    private const string MatchOrderHint =
        "命中按什么顺序编号，也就是“第几个命中”数的是哪一个，默认阅读顺序。阅读顺序：先向下"
        + "再向右。分数最高：最像的那一个优先。面积最大：框最大的那一个优先。随机：打乱，"
        + "用于不希望每次都挑同一个的场景。";

    /// <summary>
    /// The same for the colour finders, whose hits are all one pixel: they are not offered a
    /// "biggest first", so their hint must not talk about one.
    /// </summary>
    private const string MatchOrderColourHint =
        "命中按什么顺序编号，也就是“第几个命中”数的是哪一个，默认阅读顺序。阅读顺序：先向下"
        + "再向右。分数最高：离目标颜色最近的那个像素优先。随机：打乱，用于不希望每次都挑"
        + "同一个的场景。";

    /// <summary>The hint the three reading actions share for the shape a reading has to have.</summary>
    private const string ExpectedHint =
        "可选的正则：整段读取既要含上面要查找的文字，又要符合这个写法才算命中，默认不限制。"
        + "用于一屏里出现多个相同字样、只认其中一行的场合，例如 HP 120/300 用 ^HP \\d+/\\d+$ 找得到，"
        + "HP 已满 不会误中。写法与“匹配方式”里的正则相同。";

    /// <summary>
    /// What the three reading actions keep of each piece they read: one question asked in three
    /// places, so it is written once and all three answer the same way.
    /// </summary>
    private const string ContentHint =
        "读到的一段文字留下什么：全部文字照原样留下；只读数字、只留字母、只留中文、只留字母与中文"
        + "这几档只留下要的那类字符（首尾的空格去掉、中间连续的空格并成一个），一个字符也不剩下的"
        + "段落就当没读到；按正则提取把「提取的正则」套在每一段上，只留下匹配到的那一段。全角字母"
        + "与全角数字按半角算；随包那一份模型本来就中英文都读，所以「只留中文」是在读出来的结果里"
        + "挑中文，不是换一份模型。";

    private const string ContentAll = "全部文字";
    private const string ContentDigits = "只读数字";
    private const string ContentLetters = "只留字母";
    private const string ContentChinese = "只留中文";
    private const string ContentLettersAndChinese = "只留字母与中文";
    private const string ContentByPattern = "按正则提取";

    /// <summary>
    /// The field that carries the pattern, asked for only while the reading is set to keep what a
    /// pattern matches: what goes in it is written differently from a piece of writing, so it is not
    /// called the same thing.
    /// </summary>
    private const string ContentPatternLabel = "提取的正则";

    /// <summary>The hint the three reading actions share for that pattern.</summary>
    private const string ContentPatternHint =
        "把每一段读到的文字都套一遍这个正则：匹配不上的段落不要，匹配上的段落只留下括号里的第一组"
        + "（正则里没有括号时留下整个匹配）。写法与“匹配方式”里的正则相同，最前面写 (?i) 不区分"
        + "大小写；留空表示不做提取，读到的文字原样留下。例如 单号：(\\d+) 会把「单号：88231」"
        + "只留下 88231。";

    /// <summary>The hint the three reading actions share for their corrections list.</summary>
    private const string FixTextHint =
        "可选的纠错表：一行一条，写成“模型读到的 = 实际内容”，例如 l00 = 100、O = 0。"
        + "用于识别模型固定读错某种字体的场合；读取结果先按这张表改正，再参与比对。";

    /// <summary>The hint the three reading actions share for the colour the writing is in.</summary>
    private const string ColourFilterHint =
        "可选的文字颜色。填上以后，识别前先把不是这个颜色的像素丢掉，用于带描边、带阴影或压在"
        + "画面上的文字。留空表示画面原样送去识别（默认）。";

    /// <summary>The hint the three reading actions share for how far off that colour may be.</summary>
    private const string ColourToleranceHint =
        "像素与文字颜色的距离在这个范围内仍算文字，0–100，默认 10。文字带渐变或有抗锯齿边缘时"
        + "调大。";

    /// <summary>The label the hit record of every search action goes by.</summary>
    private const string HitRecordLabel = "命中";

    /// <summary>The label of the list of every hit a search action found.</summary>
    private const string EveryHitLabel = "全部命中";

    /// <summary>
    /// The hint the picture-searching actions share for their hit record: one record holding the
    /// same parts whatever was found, so a macro reads the size of a picture the same way it reads
    /// the size of a line of writing.
    /// </summary>
    private const string HitRecordHintImage =
        "这次找到的那一处，一条记录：「x」「y」是正中的位置（动作要点或移到的那个点），「left」"
        + "「top」是矩形左上角（搜索区域从那里开始量），「width」「height」是宽高，「score」是这一处"
        + "的相似度（越大越像，1 表示完全一样），「box」是整块矩形，写成 左,上,宽,高，填进搜索区域"
        + "那一行的变量格即可看同一处。取其中一项用 field($变量, \"x\")。没找到时为空。";

    /// <summary>The same hint for the colour searches, whose hit is a single pixel.</summary>
    private const string HitRecordHintColour =
        "这次找到的那个像素，一条记录：「x」「y」是它的位置，「left」「top」是同一个位置，「width」"
        + "「height」是 1，「score」是这个像素与要找的颜色的接近程度（1 表示完全一样），「box」写成"
        + "左,上,宽,高。取其中一项用 field($变量, \"x\")。没找到时为空。";

    /// <summary>The same hint for the text searches, whose hit also carries what was read.</summary>
    private const string HitRecordHintText =
        "这次读到的那一处，一条记录：「x」「y」是正中的位置（点文字落的就是这里），「left」「top」是"
        + "矩形左上角（搜索区域从那里开始量），「width」「height」是宽高，「text」是这一处读到的文字，"
        + "「score」是识别模型给的读数分数（越大越有把握），「box」是整块矩形，写成 左,上,宽,高。"
        + "取其中一项用 field($变量, \"x\")。没找到时为空。";

    /// <summary>The same hint for the element searches, whose hit carries the element's own text.</summary>
    private const string HitRecordHintElement =
        "这次找到的那个元素，一条记录：「x」「y」是它的正中（后面的步骤按它量位置、点它），「left」"
        + "「top」是它左上角，「width」「height」是它的宽高，「text」是元素自带的文本，「box」是整块"
        + "矩形，写成 左,上,宽,高。取其中一项用 field($变量, \"x\")。没找到时为空。";

    /// <summary>The hint the "every match" results of the search actions share.</summary>
    private const string EveryHitHint =
        "勾上后把这次找到的每一处都记下来，一条命中是一条与上面同样的记录，顺序就是“命中排序”里的"
        + "顺序：“第几个命中”数的就是这一列。可以直接 count() 数一数、get() 取出其中一条、用 forEach "
        + "逐个走一遍。不勾选时只记录选中的那一处。";

    /// <summary>
    /// The hint the two text-searching actions share for their lowest-score field. The numbers
    /// are what the reading model actually gave on a real screen, because the score is the
    /// model's own and has no range a user could otherwise guess.
    /// </summary>
    private const string MinScoreHint =
        "识别分低于这个数的读取直接丢弃。识别分是识别模型自己的打分，不是百分比，也没有固定"
        + "上下限。实机参考：清晰的文字约 40，模糊到快认不出约 24，花背景上的乱码约 16。"
        + "填 0 表示全部接受（默认）。";

    /// <summary>
    /// The window filter the element actions and the element conditions share. It is the same field
    /// in fourteen of them, so it is written once rather than fourteen times the same sentence.
    /// </summary>
    private const string ElementWindowHint =
        "可选：只在认得出的窗口里找，「匹配内容」与「匹配方式」两行一起说怎么认；留空表示在所有窗口里找。";

    /// <summary>
    /// What an empty match text means to a step that looks inside a window: it looks inside them
    /// all, which is the answer a step that names no window has always given.
    /// </summary>
    private const string ElementLookInExtra = "不填「匹配内容」时在每个窗口里找。";

    /// <summary>How a selector is written, as much of it as every element action says.</summary>
    private const string SelectorPickHint =
        "也可用“拾取元素”直接在屏幕上拾取控件；每次查找都重新找一遍，元素挪了位置也找得到。";

    internal static readonly Dictionary<string, string> Chinese = new()
    {
        // ------------------------------------------------------------------ control
        ["control.waitUntil.name"] = "等待直到",
        ["control.waitUntil.desc"] = "反复判断一个条件，成立之后再继续往下走。",
        ["control.waitUntil.condition.label"] = "条件",
        ["control.waitUntil.condition.hint"] = "会一遍遍判断这个条件，成立之后才继续往下走。颜色、图片、文字、"
            + "界面元素、变量比较都可以用来等；等多久由这一步设置里的「超时」说（默认 10 秒），到点"
            + "取消这一步并判为失败。",
        ["control.waitUntil.pollMs.label"] = "检查间隔",
        ["control.waitUntil.pollMs.hint"] = "两次判断之间等多久，单位是毫秒，范围 10 到 60000，默认 200。",

        ["control.sequence.name"] = "顺序执行",
        ["control.sequence.desc"] = "按顺序依次执行子步骤。",
        ["control.sequence.name.label"] = "名称",
        ["control.sequence.name.hint"] = "可选，步骤列表里显示的名称；留空就显示这个动作的名字。",
        ["control.sequence.steps.label"] = "子步骤",
        ["control.sequence.steps.hint"] = "按顺序执行的子步骤。",

        ["control.delay.name"] = "延迟",
        ["control.delay.desc"] = "执行下一步之前等待固定时长。",
        ["control.delay.ms.label"] = "等待时长",
        ["control.delay.ms.hint"] = "等待多久，单位是毫秒，默认 1000。",

        ["control.delayRandom.name"] = "随机延迟",
        ["control.delayRandom.desc"] = "等待一个随机时长，让宏看起来不像机械执行。",
        ["control.delayRandom.minMs.label"] = "最短等待",
        ["control.delayRandom.minMs.hint"] = "最短等待时长，单位是毫秒，默认 400。",
        ["control.delayRandom.maxMs.label"] = "最长等待",
        ["control.delayRandom.maxMs.hint"] = "最长等待时长，单位是毫秒，默认 1200；写得不小于「最短等待」。"
            + "每次运行在这两个数之间随机取一个数等下去。",

        ["control.repeat.name"] = "重复",
        ["control.repeat.desc"] = "把子步骤重复执行固定次数。已经知道要跑几遍时用它。",
        ["control.repeat.times.label"] = "次数",
        ["control.repeat.times.hint"] = "重复执行的次数，从 1 起，默认 10。",
        ["control.repeat.intervalMs.label"] = "间隔",
        ["control.repeat.intervalMs.hint"] = "每轮之间等多久，单位是毫秒，默认 0。",
        ["control.repeat.body.label"] = "循环体步骤",
        ["control.repeat.body.hint"] = "每轮执行一次的子步骤。",

        ["control.while.name"] = "当…时循环",
        ["control.while.desc"] = "条件成立期间反复执行子步骤。要等某个状态变了才停时用它。",
        ["control.while.condition.label"] = "条件",
        ["control.while.condition.hint"] = "每轮开始前判断一次，成立才执行这一轮。",
        ["control.while.maxIterations.label"] = "最大轮数",
        ["control.while.maxIterations.hint"] = "轮数上限，从 1 起，默认 1000；到这个数就结束这个循环（不算"
            + "失败），用于条件一直成立时兜底。",
        ["control.while.body.label"] = "循环体步骤",
        ["control.while.body.hint"] = "条件成立时执行的子步骤。",

        ["control.forEach.name"] = "遍历",
        ["control.forEach.desc"] = "对列表或变量中的每一项各执行一次子步骤。有一批值要逐个处理时用它。",
        ["control.forEach.items.label"] = "列表项",
        ["control.forEach.items.hint"] = "一个列表，或保存列表的 $变量。例如 [1, 2, 3]、split($text, \",\") 或 $names。",
        ["control.forEach.itemVariable.label"] = "当前项变量",
        ["control.forEach.itemVariable.hint"] = "接收当前这一项的变量名，默认 item，子步骤里可以读它。",
        ["control.forEach.indexVariable.label"] = "序号变量",
        ["control.forEach.indexVariable.hint"] = "可选，接收当前是第几轮的变量名，从 0 数起；留空就不记轮次。",
        ["control.forEach.reverse.label"] = "倒序遍历",
        ["control.forEach.reverse.hint"] = "从最后一项往前遍历，默认关。",
        ["control.forEach.body.label"] = "循环体步骤",
        ["control.forEach.body.hint"] = "每一项执行一次的子步骤。",

        ["control.for.name"] = "计数循环",
        ["control.for.desc"] = "从一个数数到另一个数，每数一个执行一遍子步骤。需要「现在是第几轮」时用它。",
        ["control.for.from.label"] = "起始值",
        ["control.for.from.hint"] = "计数器第一次取的值，默认 1，不小于 -1000000。",
        ["control.for.to.label"] = "结束值",
        ["control.for.to.hint"] = "计数器最后一次取的值，默认 5，这个值本身也算在内：从 1 数到 3 会执行三次。",
        ["control.for.step.label"] = "步长",
        ["control.for.step.hint"] = "每轮计数器增加多少，默认 1。填负数就是往下数；填 0 表示按起始值与结束值"
            + "自动定方向：起始值大于结束值时往下数。",
        ["control.for.intervalMs.label"] = "间隔",
        ["control.for.intervalMs.hint"] = "每轮之间等多久，单位是毫秒，默认 0。",
        ["control.for.variable.label"] = "计数器变量",
        ["control.for.variable.hint"] = "接收本轮数值的变量名，默认 i，子步骤里可以读它。",
        ["control.for.body.label"] = "循环体步骤",
        ["control.for.body.hint"] = "每轮执行一次的子步骤。",

        ["control.if.name"] = "条件分支",
        ["control.if.desc"] = "根据条件在两个子步骤块中选择一个执行。",
        ["control.if.condition.label"] = "条件",
        ["control.if.condition.hint"] = "执行到这一步时判断一次：成立就走「满足时步骤」，不成立就走「不满足时"
            + "步骤」。",
        ["control.if.then.label"] = "满足时步骤",
        ["control.if.then.hint"] = "条件成立时执行的子步骤。",
        ["control.if.else.label"] = "不满足时步骤",
        ["control.if.else.hint"] = "条件不成立时执行的子步骤。",

        ["control.switch.name"] = "多分支",
        ["control.switch.desc"] = "拿一个值从上到下比对各分支，命中第一个就执行它的步骤，都没命中就走“其他情况”。",
        ["control.switch.value.label"] = "要比对的值（操作数）",
        ["control.switch.value.hint"] = "执行到这一步时读取一次，用来和各分支填的值比较。从下拉列表里选一个变量，"
            + "也可以直接手输变量名、数字或文本。",
        ["control.switch.matchMode.label"] = "比对方式（运算符）",
        ["control.switch.matchMode.hint"] = "决定分支里填的值怎么和上面的值比较：等于、不等于、包含、"
            + "开头 / 结尾、大于 / 小于、正则，只能从这个下拉框里选。选“正则匹配”时，"
            + "每条分支的分支值就是那条分支的正则表达式。",
        ["control.switch.matchMode.option.equals"] = "等于",
        ["control.switch.matchMode.option.notEquals"] = "不等于",
        ["control.switch.matchMode.option.contains"] = "包含",
        ["control.switch.matchMode.option.startsWith"] = "以…开头",
        ["control.switch.matchMode.option.endsWith"] = "以…结尾",
        ["control.switch.matchMode.option.greaterThan"] = "大于",
        ["control.switch.matchMode.option.greaterOrEqual"] = "大于等于",
        ["control.switch.matchMode.option.lessThan"] = "小于",
        ["control.switch.matchMode.option.lessOrEqual"] = "小于等于",
        ["control.switch.matchMode.option.regex"] = "正则匹配",
        ["control.switch.cases.label"] = "分支",
        ["control.switch.cases.hint"] = "一行一个分支，从上到下比对；命中第一个分支后执行该分支，其余分支不再比对。",
        ["control.switch.otherwise.label"] = "其他情况步骤",
        ["control.switch.otherwise.hint"] = "所有分支都没命中时执行的子步骤。",

        ["control.case.name"] = "分支",
        ["control.case.desc"] = "多分支里的一条：它应答哪些值，以及命中后要执行的步骤。",
        ["control.case.values.label"] = "分支值（操作数）",
        ["control.case.values.hint"] = "这条分支应答的值，从下拉列表里选变量或直接手输，用“;”隔开多个；"
            + "每个值都按上面的比对方式和要比对的值比较。比对方式选了“正则匹配”时，"
            + "这里填的就是正则表达式。",
        ["control.case.body.label"] = "分支步骤",
        ["control.case.body.hint"] = "这条分支命中时执行的子步骤。",

        ["control.try.name"] = "尝试 / 捕获 / 收尾",
        ["control.try.desc"] = "先执行尝试的步骤，失败时改走捕获的步骤，最后无论如何都执行收尾的步骤。",
        ["control.try.body.label"] = "尝试步骤",
        ["control.try.body.hint"] = "先尝试执行的子步骤。",
        ["control.try.catch.label"] = "捕获步骤",
        ["control.try.catch.hint"] = "尝试失败时执行的子步骤。",
        ["control.try.finally.label"] = "收尾步骤",
        ["control.try.finally.hint"] = "无论成功、失败还是被跳过都会执行的子步骤。",

        ["control.break.name"] = "跳出循环",
        ["control.break.desc"] = "立即跳出最内层循环。",

        ["control.continue.name"] = "继续下一轮",
        ["control.continue.desc"] = "跳过本轮剩余步骤，直接开始下一轮。",

        ["control.stop.name"] = "停止宏",
        ["control.stop.desc"] = "停止正在运行的宏。",
        ["control.stop.reason.label"] = "原因",
        ["control.stop.reason.hint"] = "可选，写进日志的一句话，可以填变量或算式。",

        ["control.setVariable.name"] = "设置变量",
        ["control.setVariable.desc"] = "保存一个值，供后面的步骤使用。",
        ["control.setVariable.name.label"] = "变量名",
        ["control.setVariable.name.hint"] = "局部变量在这里命名即可；全局变量必须先在变量中心创建。",
        ["control.setVariable.value.label"] = "值",
        ["control.setVariable.value.hint"] = "要保存的值：数字、文本、$变量，或公式，例如 $count + 1、"
            + "upper($name)。",
        ["control.setVariable.scope.label"] = "作用范围",
        ["control.setVariable.scope.hint"] = "这个值放在哪一层，默认局部变量。局部变量只属于本宏；全局变量由"
            + "所有宏共享，必须先在变量中心创建。",
        ["control.setVariable.scope.option.local"] = "局部变量（仅本宏）",
        ["control.setVariable.scope.option.global"] = "全局变量（所有宏共享）",

        ["control.calculate.name"] = "变量运算",
        ["control.calculate.desc"] = "算出一个公式的结果，存进变量里。",
        ["control.calculate.name.label"] = "结果变量名",
        ["control.calculate.name.hint"] = "算出来的值存在这里。局部变量在这里命名即可；"
            + "全局变量必须先在变量中心创建，由所有宏共享。",
        ["control.calculate.scope.label"] = "作用范围",
        ["control.calculate.scope.hint"] = "结果放在哪一层，默认局部变量。局部变量只属于本宏；全局变量由所有宏"
            + "共享，必须先在变量中心创建。",
        ["control.calculate.scope.option.local"] = "局部变量（仅本宏）",
        ["control.calculate.scope.option.global"] = "全局变量（所有宏共享）",
        ["control.calculate.value.label"] = "表达式",
        ["control.calculate.value.hint"] = "要算的公式，比如 $count + 1、$total * 0.85，"
            + "或者 concat($first, \" \", $last)。字段旁的「表达式」按钮能把它一块块拼出来。",

        ["control.listCreate.name"] = "创建列表",
        ["control.listCreate.desc"] = "建立一个列表变量，覆盖它原来的内容。",
        ["control.listCreate.name.label"] = "变量名",
        ["control.listCreate.name.hint"] = "列表的名字，在这里命名即可建立局部列表；全局列表必须先在变量中心"
            + "创建。列表里可以放任意类型的值，包括别的列表。",
        ["control.listCreate.scope.label"] = "作用范围",
        ["control.listCreate.scope.hint"] = "列表放在哪一层，默认局部列表。局部列表只属于本宏；全局列表由所有"
            + "宏共享，必须先在变量中心创建。",
        ["control.listCreate.scope.option.local"] = "局部列表（仅本宏）",
        ["control.listCreate.scope.option.global"] = "全局列表（所有宏共享）",
        ["control.listCreate.items.label"] = "初始项",
        ["control.listCreate.items.hint"] = "列表一开始包含哪些值，写成列表或 $变量；留空则建立空列表。",

        ["control.listAdd.name"] = "列表追加",
        ["control.listAdd.desc"] = "在一个列表的末尾加入一个或多个值。",
        ["control.listAdd.name.label"] = "列表变量",
        ["control.listAdd.name.hint"] = "要追加的列表，下拉里列出宏当前看得见的列表。",
        ["control.listAdd.value.label"] = "追加的值",
        ["control.listAdd.value.hint"] = "放到列表末尾的值，可以填变量或算式。",

        ["control.listInsert.name"] = "列表插入",
        ["control.listInsert.desc"] = "把值插入到列表的指定位置。",
        ["control.listInsert.name.label"] = "列表变量",
        ["control.listInsert.name.hint"] = "要插入的列表，下拉里列出宏当前看得见的列表。",
        ["control.listInsert.index.label"] = "位置",
        ["control.listInsert.index.hint"] = "插入位置，从 0 数起，0 表示最前面。写 0 到列表长度之间的数；"
            + "超出范围时按最近的一端算。",
        ["control.listInsert.value.label"] = "值",
        ["control.listInsert.value.hint"] = "插入到这个位置的值，原来在这个位置的项往后挪一位。",

        ["control.listRemoveAt.name"] = "列表移除",
        ["control.listRemoveAt.desc"] = "把列表指定位置的项移除。",
        ["control.listRemoveAt.name.label"] = "列表变量",
        ["control.listRemoveAt.name.hint"] = "要移除项的列表，下拉里列出宏当前看得见的列表。",
        ["control.listRemoveAt.index.label"] = "位置",
        ["control.listRemoveAt.index.hint"] = "要移除的位置，从 0 数起；-1 表示最后一项。",

        ["control.listSet.name"] = "列表改一项",
        ["control.listSet.desc"] = "把列表某一项换成别的值，其他项不动。",
        ["control.listSet.name.label"] = "列表变量",
        ["control.listSet.name.hint"] = "要改的列表，下拉里列出宏当前看得见的列表。",
        ["control.listSet.index.label"] = "位置",
        ["control.listSet.index.hint"] =
            "要换掉的位置，从 0 数起；-1 表示最后一项。列表没有这个位置时会停下报错，"
            + "而不是当作没事发生。",
        ["control.listSet.value.label"] = "值",
        ["control.listSet.value.hint"] = "放进这个位置的值，其他项不动。",

        ["control.listClear.name"] = "清空列表",
        ["control.listClear.desc"] = "清空列表内容，变量本身保留。",
        ["control.listClear.name.label"] = "列表变量",
        ["control.listClear.name.hint"] = "要清空的列表，下拉里列出宏当前看得见的列表；变量本身留着，只是里面"
            + "没有项了。",

        ["control.log.name"] = "写日志",
        ["control.log.desc"] = "向鲸灵日志写入一条消息。",
        ["control.log.message.label"] = "消息",
        ["control.log.message.hint"] = "写进日志的文字，可以写多行，也可以填变量或算式。",
        ["control.log.level.label"] = "级别",
        ["control.log.level.hint"] = "这条日志记成哪一级，默认「信息」。",

        ["control.fail.name"] = "判为失败",
        ["control.fail.desc"] = "把这一步记成失败，交给这一步的失败策略处理。",
        ["control.fail.reason.label"] = "失败原因",
        ["control.fail.reason.hint"] = "哪里不对，写进日志，也写进步骤自己的错误值，后面的步骤读得到。"
            + "可以不填。",

        ["control.note.name"] = "说明",
        ["control.note.desc"] = "在宏中间写一行说明，运行时不做事。",
        ["control.note.text.label"] = "说明",
        ["control.note.text.hint"] = "这一段是干什么的，写给后面读这个宏的人看。按写下的原文显示："
            + "不替换变量，也不写进日志。",

        ["control.ask.name"] = "询问使用者",
        ["control.ask.desc"] = "问一句话，按对方的回答继续。",
        ["control.ask.message.label"] = "问题",
        ["control.ask.message.hint"] = "要问的整句话，宏等回答时显示给坐在电脑前的人。",
        ["control.ask.detail.label"] = "补充说明",
        ["control.ask.detail.hint"] = "写在问题下面的补充内容，例如宏接下来要做什么；留空时只显示问题"
            + "本身。",
        ["control.ask.return.confirmed.label"] = "是否确定",
        ["control.ask.return.confirmed.hint"] = "对方点「确定」为真，点「取消」为假；没有人在场可问时"
            + "也是假，触发器发起的运行就是这样。",

        ["control.input.name"] = "询问文字",
        ["control.input.desc"] = "要一行文字，存进变量。",
        ["control.input.message.label"] = "问题",
        ["control.input.message.hint"] = "要问的整句话，宏等回答时显示给坐在电脑前的人。",
        ["control.input.default.label"] = "输入框的初始内容",
        ["control.input.default.hint"] = "对方还没打字时输入框里的内容。它只是一个起点，"
            + "对方不修改时它也就是回答。",
        ["control.input.return.text.label"] = "回答",
        ["control.input.return.text.hint"] = "对方写下的内容。对方取消、或者没有人在场可问时为空。",

        ["control.runMacro.name"] = "执行其他宏",
        ["control.runMacro.desc"] = "运行本项目里的另一个宏，效果相当于把它的步骤写在这里。"
            + "被调用的宏使用自己的一套变量：这一步按它声明的形参逐个填值，"
            + "再勾选要取回的结果。",
        ["control.runMacro.macro.label"] = "宏",
        ["control.runMacro.macro.hint"] = "本项目里的另一个宏；下面的形参与结果都由它声明。"
            + "改名或删除后这里需要跟着改。",

        // --------------------------------------------------------------------- file
        ["file.readText.name"] = "读取文本",
        ["file.readText.desc"] = "把文本文件读入变量。",
        ["file.readText.path.label"] = "文件",
        ["file.readText.path.hint"] = "要读取的文件。只写文件名时会相对宏文件夹解析。",
        ["file.readText.storeAs.label"] = "读成什么",
        ["file.readText.storeAs.hint"] = "整个文件读成一段文本，还是一行一项读成列表，默认「一段文本」。逐条处理一份名单或路径时选"
            + "「一行一项的列表」。",
        ["file.readText.storeAs.option.text"] = "一段文本",
        ["file.readText.storeAs.option.lines"] = "一行一项的列表",
        ["file.readText.limit.label"] = "只读前几行",
        ["file.readText.limit.hint"] = "只读文件的前多少行，范围为 0 到 1000000，默认 0；0 表示整份都读，大文件只要前面几行时用它。",
        ["file.readText.skipBlankLines.label"] = "去掉空行",
        ["file.readText.skipBlankLines.hint"] = "丢弃没有内容的行，这样名单中间夹着的空行不会进入列表。默认不勾选。",
        ["file.readText.trim.label"] = "去掉每行两端的空格",
        ["file.readText.trim.hint"] = "去掉每一行两端的空格。默认不勾选。",
        ["file.readText.encoding.label"] = "编码",
        ["file.readText.encoding.hint"] = EncodingHintText,
        ["file.readText.encoding.option.utf8"] = EncodingUtf8,
        ["file.readText.encoding.option.utf8bom"] = EncodingUtf8Bom,
        ["file.readText.encoding.option.gbk"] = EncodingGbk,
        ["file.readText.encoding.option.utf16"] = EncodingUtf16,

        ["file.writeText.name"] = "写入文本",
        ["file.writeText.desc"] = "把文本写入文件，可以替换原内容或追加到末尾。",
        ["file.writeText.path.label"] = "文件",
        ["file.writeText.path.hint"] = "要写入的文件。只写文件名时会相对宏文件夹解析。",
        ["file.writeText.text.label"] = "文本",
        ["file.writeText.text.hint"] = "要写入的文本。按一个值读取，变量或表达式都可以写成它。",
        ["file.writeText.mode.label"] = "方式",
        ["file.writeText.mode.hint"] = "替换原文件，或把内容接到已有内容后面。默认「替换原内容」。",
        ["file.writeText.mode.option.overwrite"] = "替换原内容",
        ["file.writeText.mode.option.append"] = "追加到末尾",
        ["file.writeText.newline.label"] = "换行",
        ["file.writeText.newline.hint"] = "要不要在文本前后补一个换行：不加、写完之后补一个，或追加时文件最后一行还没有结束"
            + "时先补一个。默认不加。",
        ["file.writeText.newline.option.none"] = "不加",
        ["file.writeText.newline.option.end"] = "写完换一行",
        ["file.writeText.newline.option.start"] = "追加前先换一行（上一行没结束时）",
        ["file.writeText.encoding.label"] = "编码",
        ["file.writeText.encoding.hint"] = EncodingHintText,
        ["file.writeText.encoding.option.utf8"] = EncodingUtf8,
        ["file.writeText.encoding.option.utf8bom"] = EncodingUtf8Bom,
        ["file.writeText.encoding.option.gbk"] = EncodingGbk,
        ["file.writeText.encoding.option.utf16"] = EncodingUtf16,

        ["file.appendLog.name"] = "追加日志",
        ["file.appendLog.desc"] = "往日志文件的末尾加一行，文件不在就新建。",
        ["file.appendLog.path.label"] = "日志文件",
        ["file.appendLog.path.hint"] = "要写入的日志文件。只写文件名时会相对宏文件夹解析。",
        ["file.appendLog.text.label"] = "这一行",
        ["file.appendLog.text.hint"] = "这一行写什么。按一个值读取，变量或表达式都可以写成它；每行末尾自动补一个换行，"
            + "追加时文件最后一行还没有结束会先补一个换行。",
        ["file.appendLog.timestamp.label"] = "前面写上时间",
        ["file.appendLog.timestamp.hint"] = "在每行前面写上日期和时间。默认勾选；事后翻看日志需要它分辨先后。",

        ["file.exists.name"] = "文件是否存在",
        ["file.exists.desc"] = "检查文件或文件夹是否存在。",
        ["file.exists.path.label"] = "文件",
        ["file.exists.path.hint"] = "要查找的文件或文件夹。只写名称时会相对宏文件夹解析。",

        ["file.delete.name"] = "删除文件",
        ["file.delete.desc"] = "删除一个文件。",
        ["file.delete.path.label"] = "文件",
        ["file.delete.path.hint"] = "要删除的文件。只写文件名时会相对宏文件夹解析。",
        ["file.delete.toRecycleBin.label"] = "放进回收站",
        ["file.delete.toRecycleBin.hint"] = "把文件放进回收站而不是彻底删除，放进回收站的文件可以还原。默认不勾选。",

        ["file.copy.name"] = "复制文件",
        ["file.copy.desc"] = "把文件复制到另一个位置。",
        ["file.copy.from.label"] = "源文件",
        ["file.copy.from.hint"] = "要复制的文件。只写文件名时会相对宏文件夹解析。",
        ["file.copy.to.label"] = "目标",
        ["file.copy.to.hint"] = "副本要放到哪里。只写文件名时会相对宏文件夹解析。",
        ["file.copy.ifExists.label"] = "目标已存在时",
        ["file.copy.ifExists.hint"] = "目标已有同名文件时的做法：替换它、保留原文件（这一步什么也不做），或在文件名后"
            + "补一个编号（report (2).csv）。默认替换它。",
        ["file.copy.ifExists.option.overwrite"] = "替换它",
        ["file.copy.ifExists.option.skip"] = "留着它，什么也不做",
        ["file.copy.ifExists.option.unique"] = "给新文件编号",

        ["file.move.name"] = "移动 / 重命名文件",
        ["file.move.desc"] = "把文件移到别处，同名目录里换个名字就是重命名。",
        ["file.move.from.label"] = "源文件",
        ["file.move.from.hint"] = "要移动的文件。只写文件名时会相对宏文件夹解析。",
        ["file.move.to.label"] = "目标",
        ["file.move.to.hint"] = "移到哪里，连文件名一起写。只写文件名时会相对宏文件夹解析。",
        ["file.move.ifExists.label"] = "目标已存在时",
        ["file.move.ifExists.hint"] = "目标已有同名文件时的做法：替换它、保留原文件（这一步什么也不做），或给移过来的"
            + "文件补一个编号（report (2).csv）。默认替换它。",
        ["file.move.ifExists.option.overwrite"] = "替换它",
        ["file.move.ifExists.option.skip"] = "留着它，什么也不做",
        ["file.move.ifExists.option.unique"] = "给移过来的文件编号",

        ["file.createFolder.name"] = "新建文件夹",
        ["file.createFolder.desc"] = "创建文件夹，上面缺的目录会一起建出来。",
        ["file.createFolder.path.label"] = "文件夹",
        ["file.createFolder.path.hint"] = "要创建的文件夹。只写名字时会相对宏文件夹解析。",

        ["file.path.name"] = "路径处理",
        ["file.path.desc"] = "得到一个路径的组成部分：拼接、拆分，或解析出它所指向的位置。",
        ["file.path.operation.label"] = "要算什么",
        ["file.path.operation.hint"] = "「拼接文件夹和文件名」把路径和下面的名字接起来；其余几项各取路径的一部分，"
            + "或者给出固定位置的文件夹。默认「拼接文件夹和文件名」。",
        ["file.path.operation.option.combine"] = "拼接文件夹和文件名",
        ["file.path.operation.option.folder"] = "所在的文件夹",
        ["file.path.operation.option.name"] = "文件名",
        ["file.path.operation.option.baseName"] = "文件名（不带扩展名）",
        ["file.path.operation.option.extension"] = "扩展名",
        ["file.path.operation.option.full"] = "完整路径",
        ["file.path.operation.option.temp"] = "临时文件夹",
        ["file.path.operation.option.macros"] = "宏文件夹",
        ["file.path.path.label"] = "路径",
        ["file.path.path.hint"] = "要处理的路径。选两个文件夹相关的项时用不到它。",
        ["file.path.name.label"] = "名字",
        ["file.path.name.hint"] = "拼接时接在后面的部分。只有「拼接文件夹和文件名」用得到它。",

        ["file.unzip.name"] = "解压",
        ["file.unzip.desc"] = "把 zip 压缩包解压到文件夹。",
        ["file.unzip.from.label"] = "压缩包",
        ["file.unzip.from.hint"] = "要解压的 zip 文件。只写文件名时会相对宏文件夹解析。",
        ["file.unzip.folder.label"] = "解压到",
        ["file.unzip.folder.hint"] = "内容解压到哪个文件夹，不存在时会建出来。",
        ["file.unzip.overwrite.label"] = "覆盖",
        ["file.unzip.overwrite.hint"] = "目标已有同名文件时替换它。默认勾选。",

        ["file.zip.name"] = "压缩文件夹",
        ["file.zip.desc"] = "把文件夹打包成一个 zip 压缩包。",
        ["file.zip.folder.label"] = "文件夹",
        ["file.zip.folder.hint"] = "要打包的文件夹。只写文件夹名时会相对宏文件夹解析。",
        ["file.zip.to.label"] = "压缩包",
        ["file.zip.to.hint"] = "要写出的 zip 文件，已存在时会替换。只写文件名时会相对宏文件夹解析。",

        ["file.deleteFolder.name"] = "删除文件夹",
        ["file.deleteFolder.desc"] = "删除文件夹；可选择连同其中的内容一起删除。",
        ["file.deleteFolder.path.label"] = "文件夹",
        ["file.deleteFolder.path.hint"] = "要删除的文件夹。只写名字时会相对宏文件夹解析。",
        ["file.deleteFolder.recurse.label"] = "连里面的内容一起删",
        ["file.deleteFolder.recurse.hint"] = "连同文件夹里的内容一起删除。默认不勾选；不勾选时，文件夹非空则保留原样，"
            + "其中的内容不会被删除。",

        ["file.listFiles.name"] = "列出文件",
        ["file.listFiles.desc"] = "把文件夹里的文件收集成列表。",
        ["file.listFiles.folder.label"] = "文件夹",
        ["file.listFiles.folder.hint"] = "要在其中查找的文件夹。只写名称时会相对宏文件夹解析。",
        ["file.listFiles.pattern.label"] = "匹配模式",
        ["file.listFiles.pattern.hint"] = "要收集哪些文件，例如 *.txt。可以使用通配符，也可以用分号一次写多个，"
            + "例如 *.txt; report?.xlsx。默认留空，表示所有文件。",
        ["file.listFiles.recurse.label"] = "包含子文件夹",
        ["file.listFiles.recurse.hint"] = "连上面那个文件夹里的子文件夹一起找，默认关：只列那一个文件夹里的东西。",
        ["file.listFiles.sortBy.label"] = "排序依据",
        ["file.listFiles.sortBy.hint"] = "按哪一项给文件排序，默认按名称。排在一起的文件（例如同样大小）再按名称排，"
            + "所以同一个文件夹每次列出来的顺序都相同。",
        ["file.listFiles.sortBy.option.name"] = "名称",
        ["file.listFiles.sortBy.option.modified"] = "修改时间",
        ["file.listFiles.sortBy.option.created"] = "创建时间",
        ["file.listFiles.sortBy.option.size"] = "大小",
        ["file.listFiles.sortBy.option.extension"] = "扩展名",
        ["file.listFiles.descending.label"] = "倒过来排",
        ["file.listFiles.descending.hint"] = "把顺序倒过来，最大的、最新的或从 Z 到 A 排在最前面。默认不勾选。",
        ["file.listFiles.depth.label"] = "最多往里几层",
        ["file.listFiles.depth.hint"] = "勾了「包含子文件夹」时最多往里看几层，范围为 0 到 64，默认 0；0 表示一直往下看。",
        ["file.listFiles.withinCount.label"] = "最近改过的",
        ["file.listFiles.withinCount.hint"] = "只留下最近这么多分钟 / 小时 / 天里改动过的文件，「今天改过的文件」就是这么问的。"
            + "范围为 0 到 100000，默认 0；0 表示不限时间。",
        ["file.listFiles.withinUnit.label"] = "单位",
        ["file.listFiles.withinUnit.hint"] = "上面那个数字按什么算，默认「天」。",
        ["file.listFiles.withinUnit.option.minutes"] = "分钟",
        ["file.listFiles.withinUnit.option.hours"] = "小时",
        ["file.listFiles.withinUnit.option.days"] = "天",
        ["file.listFiles.minKb.label"] = "最小多大（KB）",
        ["file.listFiles.minKb.hint"] = "只留下这么大或更大的文件，单位为 KB，范围为 0 到 1000000，默认 0；0 表示不限大小。",
        ["file.listFiles.maxKb.label"] = "最大多大（KB）",
        ["file.listFiles.maxKb.hint"] = "只留下这么大或更小的文件，单位为 KB，范围为 0 到 1000000，默认 0；0 表示不限大小。",
        ["file.listFiles.relative.label"] = "只要文件夹里的路径",
        ["file.listFiles.relative.hint"] = "每一项写成它在这个文件夹里的路径，而不是磁盘上的完整路径。默认不勾选。",

        ["file.readJson.name"] = "读取 JSON",
        ["file.readJson.desc"] = "从 JSON 文件中读取一个值。",
        ["file.readJson.path.label"] = "文件",
        ["file.readJson.path.hint"] = "要读取的 JSON 文件。只写文件名时会相对宏文件夹解析。",
        ["file.readJson.query.label"] = "位置",
        ["file.readJson.query.hint"] = "值的路径，例如 server.name 或 items[0].id。默认留空，表示整个文件。",

        ["file.writeJson.name"] = "写入 JSON",
        ["file.writeJson.desc"] = "设置 JSON 文件中的一个值，保留其余内容。",
        ["file.writeJson.path.label"] = "文件",
        ["file.writeJson.path.hint"] = "要更新的 JSON 文件。只写文件名时会相对宏文件夹解析。",
        ["file.writeJson.query.label"] = "位置",
        ["file.writeJson.query.hint"] = "值的路径，例如 server.name。途经的缺失对象会自动创建。",
        ["file.writeJson.value.label"] = "值",
        ["file.writeJson.value.hint"] = "要存入的值。按一个值读取，变量或表达式都可以写成它。",

        ["file.readCsv.name"] = "读取 CSV",
        ["file.readCsv.desc"] = "把 CSV 文件读取成行列表。",
        ["file.readCsv.path.label"] = "文件",
        ["file.readCsv.path.hint"] = "要读取的 CSV 文件。只写文件名时会相对宏文件夹解析。",
        ["file.readCsv.separator.label"] = "分隔符",
        ["file.readCsv.separator.hint"] = "两个单元格之间的字符，默认「自动识别」。自动识别按文件的第一行判断，"
            + "文本文件里没有地方写明它用的是什么字符，列表里没有的字符写在下面那一格。",
        ["file.readCsv.separator.option.auto"] = "自动识别",
        ["file.readCsv.separator.option.comma"] = "逗号 ,",
        ["file.readCsv.separator.option.semicolon"] = "分号 ;",
        ["file.readCsv.separator.option.tab"] = "制表符",
        ["file.readCsv.separator.option.pipe"] = "竖线 |",
        ["file.readCsv.separator.option.space"] = "空格",
        ["file.readCsv.separatorText.label"] = "或者这个字符",
        ["file.readCsv.separatorText.hint"] = "上面列表里没有的分隔符写在这里，例如 : 或 #。填了就按这里写的来，"
            + "不填就用上面选的，默认留空。",
        ["file.readCsv.hasHeader.label"] = "首行是表头",
        ["file.readCsv.hasHeader.hint"] = "第一行是列名。默认勾选：结果中不含第一行，第一行的名字用来给每一行的单元格命名，"
            + "按名字取单元格靠的就是它。",
        ["file.readCsv.skipBlankLines.label"] = "跳过空行",
        ["file.readCsv.skipBlankLines.hint"] = "空行不算一行，默认勾选。别的程序导出的文件常在末尾留一个空行，"
            + "不跳过就会读出一行空单元格。",
        ["file.readCsv.startRow.label"] = "从第几行开始",
        ["file.readCsv.startRow.hint"] = "从文件的第几行开始读，从 1 数起，默认 1。首行是表头时，表头就是这一行。",
        ["file.readCsv.maxRows.label"] = "最多读几行",
        ["file.readCsv.maxRows.hint"] = "最多读取这么多行，范围为 0 到 1000000，默认 0；0 表示从这里一直读到文件末尾。",
        ["file.readCsv.trim.label"] = "去掉空格",
        ["file.readCsv.trim.hint"] = "去掉每个单元格两端的空格。默认不勾选；手写的文件里常有这些空格。",
        ["file.readCsv.columns.label"] = "只读这几列",
        ["file.readCsv.columns.hint"] =
            "要保留的列，按表头里的名字写，或者按它所在的字母写：金额、B、金额,B。每一行只留这几列，"
            + "顺序按这里写的来；留空表示所有列。",
        ["file.readCsv.shape.label"] = "结果形状",
        ["file.readCsv.shape.hint"] = "给出每行一条记录的表，或者在只点名了一列时，直接给出那一列的值（一个普通"
            + "列表，可以直接遍历或求和）。默认「每行一条记录」。",
        ["file.readCsv.shape.option.rows"] = "每行一条记录",
        ["file.readCsv.shape.option.values"] = "一列的值",
        ["file.readCsv.matchColumn.label"] = "只读这些行",
        ["file.readCsv.matchColumn.hint"] = "只读这一列的单元格等于下面那个值的行，列名写法与上面一样。"
            + "默认留空，表示每一行都读。",
        ["file.readCsv.matchValue.label"] = "等于",
        ["file.readCsv.matchValue.hint"] = "这些行里那个单元格要等于的值，也可以写成 $变量。",
        ["file.readCsv.matchMode.label"] = "匹配方式",
        ["file.readCsv.matchMode.hint"] = "单元格与这个值怎么比：比文字的几档是等于、包含、以…开头、以…结尾、"
            + "正则匹配（与别处用的是同一批词，都不区分大小写，「等于」比之前去掉两端的空格），另有不等于、"
            + "大于、小于那几档，只能从这个下拉框里选。默认「等于」。",
        ["file.readCsv.matchMode.option.equals"] = "等于",
        ["file.readCsv.matchMode.option.notEquals"] = "不等于",
        ["file.readCsv.matchMode.option.contains"] = "包含",
        ["file.readCsv.matchMode.option.startsWith"] = "以…开头",
        ["file.readCsv.matchMode.option.endsWith"] = "以…结尾",
        ["file.readCsv.matchMode.option.greaterThan"] = "大于",
        ["file.readCsv.matchMode.option.greaterOrEqual"] = "大于等于",
        ["file.readCsv.matchMode.option.lessThan"] = "小于",
        ["file.readCsv.matchMode.option.lessOrEqual"] = "小于等于",
        ["file.readCsv.matchMode.option.regex"] = "正则匹配",
        ["file.readCsv.encoding.label"] = "编码",
        ["file.readCsv.encoding.hint"] = EncodingHintText,
        ["file.readCsv.encoding.option.utf8"] = EncodingUtf8,
        ["file.readCsv.encoding.option.utf8bom"] = EncodingUtf8Bom,
        ["file.readCsv.encoding.option.gbk"] = EncodingGbk,
        ["file.readCsv.encoding.option.utf16"] = EncodingUtf16,

        ["file.writeCsv.name"] = "写入 CSV",
        ["file.writeCsv.desc"] = "把行列表写入 CSV 文件。",
        ["file.writeCsv.path.label"] = "文件",
        ["file.writeCsv.path.hint"] = "要写入的 CSV 文件。只写文件名时会相对宏文件夹解析。",
        ["file.writeCsv.rows.label"] = "行",
        ["file.writeCsv.rows.hint"] = "行列表。每一行本身也可以是一个单元格列表。",
        ["file.writeCsv.header.label"] = "表头行",
        ["file.writeCsv.header.hint"] = "写在数据上面的列名：可以是一个列表，也可以直接用分隔符连成一行写出来。"
            + "追加到一个还不存在的文件时会写上，追加到已有的文件时不写，所以流水账只有一个表头。"
            + "默认留空，表示不写表头。",
        ["file.writeCsv.separator.label"] = "分隔符",
        ["file.writeCsv.separator.hint"] = "两个单元格之间的字符，默认「逗号 ,」。",
        ["file.writeCsv.separator.option.comma"] = "逗号 ,",
        ["file.writeCsv.separator.option.semicolon"] = "分号 ;",
        ["file.writeCsv.separator.option.tab"] = "制表符",
        ["file.writeCsv.separator.option.pipe"] = "竖线 |",
        ["file.writeCsv.separator.option.space"] = "空格",
        ["file.writeCsv.separatorText.label"] = "或者这个字符",
        ["file.writeCsv.separatorText.hint"] = "上面列表里没有的分隔符写在这里，例如 : 或 #。填了就按这里写的来，"
            + "不填就用上面选的，默认留空。",
        ["file.writeCsv.mode.label"] = "写入方式",
        ["file.writeCsv.mode.hint"] = "从头写这个文件，或把这几行接在文件已有内容的后面。默认「覆盖整个文件」。",
        ["file.writeCsv.mode.option.replace"] = "覆盖整个文件",
        ["file.writeCsv.mode.option.append"] = "接在最后一行后面",
        ["file.writeCsv.align.label"] = "按列名放进同名的列",
        ["file.writeCsv.align.hint"] =
            "往一个已经有表格的文件里追加时：每个单元格放进第一行里同名的那一列，"
            + "而不是每行放在同一个位置上。这样文件里有、这一步没写的列不会把后面的内容挤错位。"
            + "这些名字来自上面那一栏表头，文件里没有那个名字时就说出来，而不是随手塞进某一列。",
        ["file.writeCsv.lineEnding.label"] = "换行符",
        ["file.writeCsv.lineEnding.hint"] = "一行以什么字符结束：CRLF 是 Windows 的写法，LF 是其他平台的写法。默认 Windows (CRLF)。",
        ["file.writeCsv.lineEnding.option.windows"] = "Windows (CRLF)",
        ["file.writeCsv.lineEnding.option.unix"] = "Unix (LF)",
        ["file.writeCsv.quoteAll.label"] = "每个单元格都加引号",
        ["file.writeCsv.quoteAll.hint"] = "给所有单元格都加引号，而不是只给需要加引号的加。默认不勾选；"
            + "有些程序只认全部都加引号的写法。",
        ["file.writeCsv.emptyCells.label"] = "空单元格写成",
        ["file.writeCsv.emptyCells.hint"] = "没有内容的单元格怎么写：两个分隔符之间留空，还是写成两个引号。默认两个分隔符之间留空；"
            + "两种写法读回来都是空单元格，引号是给非要看见一个格子的程序用的。",
        ["file.writeCsv.emptyCells.option.bare"] = "两个分隔符之间留空",
        ["file.writeCsv.emptyCells.option.quoted"] = "写成两个引号",
        ["file.writeCsv.encoding.label"] = "编码",
        ["file.writeCsv.encoding.hint"] = EncodingHintText,
        ["file.writeCsv.encoding.option.utf8"] = EncodingUtf8,
        ["file.writeCsv.encoding.option.utf8bom"] = EncodingUtf8Bom,
        ["file.writeCsv.encoding.option.gbk"] = EncodingGbk,
        ["file.writeCsv.encoding.option.utf16"] = EncodingUtf16,

        ["file.saveVariables.name"] = "保存变量",
        ["file.saveVariables.desc"] = "把变量写入 JSON 文件，方便下次运行读取。",
        ["file.saveVariables.path.label"] = "文件",
        ["file.saveVariables.path.hint"] = "要写入的文件。只写文件名时会相对宏文件夹解析。",
        ["file.saveVariables.names.label"] = "哪些变量",
        ["file.saveVariables.names.hint"] = "要保存的名称，用逗号分隔。默认留空，表示所有局部变量和共享变量。",

        ["file.loadVariables.name"] = "读取变量",
        ["file.loadVariables.desc"] = "读取由“保存变量”写出的文件，恢复变量。",
        ["file.loadVariables.path.label"] = "文件",
        ["file.loadVariables.path.hint"] = "要读取的文件。只写文件名时会相对宏文件夹解析。",

        // ------------------------------------------------------------------- data
        ["data.base64Encode.name"] = "Base64 编码",
        ["data.base64Encode.desc"] = "把文本编码成 Base64，放进 URL 或令牌时常用这种形式。",
        ["data.base64Encode.text.label"] = "文本",
        ["data.base64Encode.text.hint"] = "要编码的文本。按一个值读取，变量或表达式都可以写成它；编码时按 UTF-8 处理。",

        ["data.base64Decode.name"] = "Base64 解码",
        ["data.base64Decode.desc"] = "把 Base64 变回它原本的文本。",
        ["data.base64Decode.text.label"] = "Base64",
        ["data.base64Decode.text.hint"] = "要解码的 Base64 文本。按一个值读取，变量或表达式都可以写成它；解出来的文本按 UTF-8 读。",

        ["data.hash.name"] = "计算哈希",
        ["data.hash.desc"] = "算出一段文本或一个文件的校验和。",
        ["data.hash.algorithm.label"] = "算法",
        ["data.hash.algorithm.hint"] = "用哪种校验和，默认 SHA-256。MD5 和 SHA-1 用来对上别的程序给出的值。",
        ["data.hash.algorithm.option.md5"] = "MD5",
        ["data.hash.algorithm.option.sha1"] = "SHA-1",
        ["data.hash.algorithm.option.sha256"] = "SHA-256",
        ["data.hash.algorithm.option.sha512"] = "SHA-512",
        ["data.hash.source.label"] = "算什么",
        ["data.hash.source.hint"] = "校验和算在什么上面：下面这一段文本，还是磁盘上的一个文件，"
            + "默认算文本。",
        ["data.hash.source.option.text"] = "一段文本",
        ["data.hash.source.option.file"] = "一个文件",
        ["data.hash.text.label"] = "文本",
        ["data.hash.text.hint"] = "要算校验和的文本，按 UTF-8 处理。按一个值读取，变量或表达式都可以写成它。",
        ["data.hash.path.label"] = "文件",
        ["data.hash.path.hint"] = "要算校验和的文件。按块读取，所以文件多大都不影响这一步跑不跑得成。",

        // ---------------------------------------------------------------- clipboard
        ["clipboard.writeText.name"] = "复制到剪贴板",
        ["clipboard.writeText.desc"] = "把文本放进剪贴板，相当于复制。",
        ["clipboard.writeText.text.label"] = "文本",
        ["clipboard.writeText.text.hint"] = "要放进剪贴板的文本。按一个值读取，变量或表达式都可以写成它。",

        ["clipboard.readText.name"] = "读取剪贴板",
        ["clipboard.readText.desc"] = "把剪贴板上的文本读入变量。",

        ["clipboard.readImage.name"] = "读取剪贴板图片",
        ["clipboard.readImage.desc"] = "把剪贴板上的图片读进图像变量。",

        ["clipboard.writeImage.name"] = "复制图片到剪贴板",
        ["clipboard.writeImage.desc"] = "把一张图片放到剪贴板上，等同于复制它。",
        ["clipboard.writeImage.image.label"] = "图片",
        ["clipboard.writeImage.image.hint"] = "可以是图片文件，也可以是前面步骤填好的图像变量。",

        ["clipboard.readFiles.name"] = "读取剪贴板文件",
        ["clipboard.readFiles.desc"] = "把剪贴板上的文件路径读成列表。",

        ["clipboard.writeFiles.name"] = "复制文件到剪贴板",
        ["clipboard.writeFiles.desc"] = "把文件放到剪贴板上，粘贴出来的是文件本身。",
        ["clipboard.writeFiles.files.label"] = "文件",
        ["clipboard.writeFiles.files.hint"] = "路径列表，也可以只写一个路径。只写文件名时会相对宏文件夹解析；每个文件都要真的存在，"
            + "否则粘贴时会在对方程序里失败。",

        ["clipboard.clear.name"] = "清空剪贴板",
        ["clipboard.clear.desc"] = "清空剪贴板内容。",

        ["clipboard.waitChange.name"] = "等待剪贴板",
        ["clipboard.waitChange.desc"] = "等待剪贴板上有新的内容被复制。",
        ["clipboard.waitChange.timeoutMs.label"] = "超时",
        ["clipboard.waitChange.timeoutMs.hint"] = "最多等多久，单位毫秒，默认 5000；到点即失败。",

        ["clipboard.copy.name"] = "复制",
        ["clipboard.copy.desc"] = "发送复制快捷键，并保留复制到的内容。",
        ["clipboard.copy.keys.label"] = "快捷键",
        ["clipboard.copy.keys.hint"] = "用 + 连接的一组按键，用于复制所选内容，默认 Ctrl+C。",
        ["clipboard.copy.timeoutMs.label"] = "超时",
        ["clipboard.copy.timeoutMs.hint"] = "按下快捷键后等待剪贴板变化的时长，单位毫秒，默认 1500。",

        ["clipboard.paste.name"] = "粘贴",
        ["clipboard.paste.desc"] = "把文本放进剪贴板，并发送粘贴快捷键。",
        ["clipboard.paste.text.label"] = "文本",
        ["clipboard.paste.text.hint"] = "要粘贴的文本。默认留空，表示直接粘贴剪贴板里已有的内容。",
        ["clipboard.paste.keys.label"] = "快捷键",
        ["clipboard.paste.keys.hint"] = "用 + 连接的一组按键，用于粘贴，默认 Ctrl+V。",

        // ------------------------------------------------------------------ process
        ["process.start.name"] = "启动程序",
        ["process.start.desc"] = "启动另一个程序，可以选择不显示窗口。",
        ["process.start.file.label"] = "程序",
        ["process.start.file.hint"] = "要启动的程序，例如 notepad.exe，也可以写完整路径。",
        ["process.start.arguments.label"] = "参数",
        ["process.start.arguments.hint"] = "传给程序的参数。按一个值读取，变量或表达式都可以写成它；默认留空，表示不传参数。",
        ["process.start.workingDirectory.label"] = "工作目录",
        ["process.start.workingDirectory.hint"] = "程序在哪个目录下启动。默认留空，表示沿用鲸灵自己的工作目录。",
        ["process.start.hidden.label"] = "隐藏窗口",
        ["process.start.hidden.hint"] = "启动时不显示程序窗口。默认勾选；需要点完窗口才装得下去的安装程序要取消勾选。",
        ["process.start.runAsAdmin.label"] = "以管理员身份运行",
        ["process.start.runAsAdmin.hint"] = "请 Windows 用管理员权限启动这个程序。默认不勾选；系统会弹出自己的确认框，拒绝就让这一步失败。"
            + "以管理员身份启动的程序无法隐藏窗口。",
        ["process.start.environment.label"] = "环境变量",
        ["process.start.environment.hint"] = "这个程序启动时使用的环境变量，一行一个 NAME=value。默认留空，表示沿用鲸灵自身的环境变量；"
            + "以 # 开头的行会被忽略。自带环境变量启动时，这里要写程序本身，不能写文档或快捷方式。",

        ["process.waitFor.name"] = "等待程序",
        ["process.waitFor.desc"] = "等待某个程序开始运行。",
        ["process.waitFor.name.label"] = "程序",
        ["process.waitFor.name.hint"] = "要等待的程序名。按完整名称匹配，不区分大小写，末尾的 .exe 可写可不写；"
            + "按一个值读取，变量或表达式都可以写成它。",
        ["process.waitFor.timeoutMs.label"] = "超时",
        ["process.waitFor.timeoutMs.hint"] = "最多等多久，单位毫秒，默认 10000；到点程序还没出现即失败。",

        ["process.waitExit.name"] = "等待程序结束",
        ["process.waitExit.desc"] = "等待本宏启动的某个程序结束。",
        ["process.waitExit.id.label"] = "进程 id",
        ["process.waitExit.id.hint"] = "要等待的进程 id，通常是「启动程序」交回的 processId。按一个值读取，变量或表达式都可以写成它。",
        ["process.waitExit.timeoutMs.label"] = "超时",
        ["process.waitExit.timeoutMs.hint"] = "最多等多久，单位毫秒，默认 60000；到点程序还没结束即失败。",

        ["process.exists.name"] = "程序是否在运行",
        ["process.exists.desc"] = "检查某个程序是否正在运行。",
        ["process.exists.name.label"] = "程序",
        ["process.exists.name.hint"] = "要查找的程序名。按完整名称匹配，不区分大小写，末尾的 .exe 可写可不写；"
            + "按一个值读取，变量或表达式都可以写成它。",

        ["process.list.name"] = "列出程序",
        ["process.list.desc"] = "把正在运行的程序名收集成列表。",

        ["process.info.name"] = "程序信息",
        ["process.info.desc"] = "读取一个正在运行的程序的信息。",
        ["process.info.target.label"] = "程序",
        ["process.info.target.hint"] = "程序名，或进程 id。程序名按完整名称匹配，不区分大小写，末尾的 .exe 可写可不写。",

        ["process.kill.name"] = "结束程序",
        ["process.kill.desc"] = "关闭一个程序，默认先请它正常退出。",
        ["process.kill.target.label"] = "程序",
        ["process.kill.target.hint"] = "程序名，或进程 id。程序名按完整名称匹配，不区分大小写，末尾的 .exe 可写可不写。",
        ["process.kill.force.label"] = "强制关闭",
        ["process.kill.force.hint"] = "直接结束进程，而不是先请它退出。默认不勾选。",

        ["command.run.name"] = "执行命令",
        ["command.run.desc"] = "执行一条命令并收集它的输出。",
        ["command.run.file.label"] = "命令",
        ["command.run.file.hint"] = "要运行的命令，例如 cmd.exe，也可以写完整路径。",
        ["command.run.arguments.label"] = "参数",
        ["command.run.arguments.hint"] = "传给命令的参数，例如 /c dir。按一个值读取，变量或表达式都可以写成它；默认留空，表示不传参数。",
        ["command.run.workingDirectory.label"] = "工作目录",
        ["command.run.workingDirectory.hint"] = "命令在哪个目录下运行。默认留空，表示沿用鲸灵自己的工作目录。",
        ["command.run.environment.label"] = "环境变量",
        ["command.run.environment.hint"] = "这条命令运行时使用的环境变量，一行一个 NAME=value。默认留空，表示沿用鲸灵自身的环境变量；"
            + "以 # 开头的行会被忽略。",
        ["command.run.standardInput.label"] = "标准输入",
        ["command.run.standardInput.hint"] = "命令从标准输入读取的内容。默认留空，表示不提供任何输入；内容按 UTF-8 发送，"
            + "其中的 $变量 会替换为该变量的值。",
        ["command.run.timeoutMs.label"] = "超时",
        ["command.run.timeoutMs.hint"] = "命令最多可以运行多久，单位毫秒，默认 30000；到点结束这条命令并判这一步失败。",
        ["command.run.streamOutput.label"] = "输出实时写入日志",
        ["command.run.streamOutput.hint"] = "命令运行期间，它打印的每一行会立即写入运行日志，便于观察耗时较长的构建或脚本的进度。"
            + "默认不勾选；结果变量里仍然是完整输出。",
        ["command.run.outputEncoding.label"] = "输出编码",
        ["command.run.outputEncoding.hint"] = "命令按哪个编码打印，默认按本机自己的代码页读。Windows 自带的程序（cmd.exe、Windows PowerShell、"
            + "Python）输出到管道时用的就是它；Node.js 这类无论在哪个系统都按 UTF-8 输出的程序，"
            + "要在这里改成 UTF-8，否则中文会变成问号。",
        ["command.run.outputEncoding.option.system"] = "本机代码页",
        ["command.run.outputEncoding.option.utf8"] = "UTF-8",
        ["command.run.outputEncoding.option.utf16"] = "UTF-16",

        // ------------------------------------------------------------------ system
        ["system.info.name"] = "系统信息",
        ["system.info.desc"] = "读取关于这台机器的一条信息，例如用户名或某个目录。",
        ["system.info.field.label"] = "字段",
        ["system.info.field.hint"] = "要读取哪一项，默认「用户名」。",
        ["system.info.field.option.userName"] = "用户名",
        ["system.info.field.option.machineName"] = "计算机名",
        ["system.info.field.option.userDomain"] = "域",
        ["system.info.field.option.osVersion"] = "Windows 版本",
        ["system.info.field.option.processorCount"] = "处理器数量",
        ["system.info.field.option.systemFolder"] = "Windows 目录",
        ["system.info.field.option.tempFolder"] = "临时目录",
        ["system.info.field.option.userFolder"] = "用户目录",
        ["system.info.field.option.desktopFolder"] = "桌面",
        ["system.info.field.option.documentsFolder"] = "文档",
        ["system.info.field.option.downloadsFolder"] = "下载",
        ["system.info.field.option.startupFolder"] = "启动目录",
        ["system.info.field.option.appDataFolder"] = "AppData",
        ["system.info.field.option.programFiles"] = "Program Files",
        ["system.info.field.option.networkConnected"] = "网络已连接",
        ["system.info.field.option.networkNames"] = "连接的网卡",
        ["system.info.field.option.localIp"] = "本机 IP",
        ["system.info.field.option.batteryPercent"] = "电池百分比",
        ["system.info.field.option.onAcPower"] = "已接电源",
        ["system.info.field.option.dpi"] = "DPI",
        ["system.info.field.option.dpiPercent"] = "缩放百分比",
        ["system.info.field.option.uptimeSeconds"] = "已开机秒数",

        ["screen.list.name"] = "列出显示器",
        ["screen.list.desc"] = "读出桌面是怎么摆的：每一块显示器和它占的那块矩形。",
        ["screen.list.returns.label"] = "要留下的结果",
        ["screen.list.returns.hint"] = ReturnsHintText,
        ["screen.list.return.displays.label"] = "显示器",
        ["screen.list.return.displays.hint"] = "一块显示器一条记录，顺序就是 Windows 列出的顺序："
            + "x、y（这块显示器的中心），然后是 left、top、width、height、right、bottom、primary，"
            + "以及 box，也就是那块矩形写成"
            + "「左,上,宽,高」，区域那一格可以直接用它。主显示器左边的显示器 left 是负数，"
            + "瞄准它时那正是要填的数。取其中一块的一项用 field(get($变量, 0), \"left\")，"
            + "全部走一遍用 forEach。",

        ["system.environment.name"] = "环境变量",
        ["system.environment.desc"] = "读取一个环境变量。",
        ["system.environment.name.label"] = "名称",
        ["system.environment.name.hint"] = "环境变量的名称，不区分大小写。按一个值读取，变量或表达式都可以写成它。",
        ["system.power.name"] = "电源",
        ["system.power.desc"] = "锁屏、关闭显示器、睡眠、注销、重启或关闭这台电脑。",
        ["system.power.what.label"] = "执行",
        ["system.power.what.hint"] = "等同于开始菜单中的电源按钮，默认「锁屏」。锁屏和关闭显示器不会造成任何损失；"
            + "注销、重启和关机会结束正在运行的程序，未保存的内容会随之丢失。",
        ["system.power.what.option.lock"] = "锁屏",
        ["system.power.what.option.monitorOff"] = "关闭显示器",
        ["system.power.what.option.signOut"] = "注销",
        ["system.power.what.option.sleep"] = "睡眠",
        ["system.power.what.option.hibernate"] = "休眠",
        ["system.power.what.option.restart"] = "重启",
        ["system.power.what.option.shutDown"] = "关机",
        ["system.power.what.option.abortShutdown"] = "取消倒计时中的关机",
        ["system.power.graceSeconds.label"] = "提示秒数",
        ["system.power.graceSeconds.hint"] = "重启或关机前给多少秒提醒，范围为 0 到 600，默认 0。屏幕上会出现倒计时，"
            + "倒计时期间可以用「取消倒计时中的关机」或 shutdown /a 撤销；填 0 表示立刻执行。"
            + "其他选项用不到这个值。",
        ["system.volume.name"] = "音量",
        ["system.volume.desc"] = "读取或调整 Windows 正在用的那只音箱的音量。",
        ["system.volume.what.label"] = "执行",
        ["system.volume.what.hint"] = "执行哪一项：读取、设为指定值、调大、调小、静音、取消静音，默认「读取」。"
            + "这是整台机器的音量，与键盘上的音量键不同：音量键调的是当前有焦点的那个程序。",
        ["system.volume.what.option.get"] = "读取",
        ["system.volume.what.option.set"] = "设为指定值",
        ["system.volume.what.option.up"] = "调大",
        ["system.volume.what.option.down"] = "调小",
        ["system.volume.what.option.mute"] = "静音",
        ["system.volume.what.option.unmute"] = "取消静音",
        ["system.volume.what.option.toggleMute"] = "静音/取消静音",
        ["system.volume.percent.label"] = "音量百分比",
        ["system.volume.percent.hint"] = "要设成的音量，范围为 0 到 100，默认 50。只在「设为指定值」时使用。",
        ["system.volume.stepPercent.label"] = "调整幅度",
        ["system.volume.stepPercent.hint"] = "一次调大或调小多少个百分点，范围为 1 到 100，默认 5。只在「调大」和「调小」时使用，"
            + "两端都不会超过 0 和 100。",
        ["system.sound.name"] = "提示音",
        ["system.sound.desc"] = "播放系统自带的一种提示音。",
        ["system.sound.what.label"] = "播放",
        ["system.sound.what.hint"] = "播放哪一种系统提示音，默认「默认提示音」。宏跑完或者中途出错时，可以用它发出声音，"
            + "不必在屏幕上留东西。具体是什么声音跟着系统的声音设置走：已经静音的机器没有声音，"
            + "这一步照样算完成；机器上没有声音设备时这一步会失败并说明原因。",
        ["system.sound.what.option.default"] = "默认提示音",
        ["system.sound.what.option.information"] = "信息",
        ["system.sound.what.option.warning"] = "警告",
        ["system.sound.what.option.error"] = "错误",
        ["system.sound.what.option.question"] = "疑问",
        ["system.notify.name"] = "通知",
        ["system.notify.desc"] = "在通知区域旁边弹出一条通知。",
        ["system.notify.heading.label"] = "标题",
        ["system.notify.heading.hint"] = "通知的标题。默认留空，此时用程序自己的名字做标题。",
        ["system.notify.message.label"] = "内容",
        ["system.notify.message.hint"] = "通知要说的话。其中的变量会照别处一样替换成变量的值，所以宏可以直接报告它刚做了什么。",
        ["system.notify.what.label"] = "类型",
        ["system.notify.what.hint"] = "这条通知算哪一类，默认「提醒」。机器关闭了通知时它什么都不显示，这一步照样算完成。",
        ["system.notify.what.option.information"] = "提醒",
        ["system.notify.what.option.warning"] = "警告",
        ["system.notify.what.option.error"] = "错误",
        ["system.ime.name"] = "输入法",
        ["system.ime.desc"] = "读取最前面那个窗口的键盘布局，或者把它切到另一种布局。",
        ["system.ime.what.label"] = "执行",
        ["system.ime.what.hint"] = "这几项都作用于当前最前面的窗口，也就是正在输入的那个，默认「当前是哪种布局」。"
            + "宏在中文布局下发送西文按键，得到的是候选词而不是快捷键，因此先切换到英文布局往往是"
            + "宏能否可用的关键。中文输入法自身的中英切换只能通过按键完成，Windows 把这个状态记在"
            + "正在输入的那个程序里，发送 Ctrl+Space 或 Shift 即为此事。",
        ["system.ime.what.option.get"] = "当前是哪种布局",
        ["system.ime.what.option.list"] = "列出已安装的布局",
        ["system.ime.what.option.switch"] = "切换到另一种布局",
        ["system.ime.layout.label"] = "布局",
        ["system.ime.layout.hint"] = "要切换到的键盘布局，供「切换到另一种布局」使用。写名称的一部分即可匹配；"
            + "可选名称可以用「列出已安装的布局」查询。这一步会等到窗口真正切换完成后才结束，"
            + "因此下一步即可按该布局输入。",
        ["system.brightness.name"] = "屏幕亮度",
        ["system.brightness.desc"] = "读取或调整屏幕的亮度。",
        ["system.brightness.what.label"] = "执行",
        ["system.brightness.what.hint"] = "执行哪一项：读取、设为指定值、调亮、调暗，默认「读取」。所有能响应的显示器会一起调整；"
            + "这是通过视频线与显示器直接通信，与显示器上的亮度按钮是同一件事，"
            + "因此笔记本内置屏或没有此功能的显示器可能完全没有响应。",
        ["system.brightness.what.option.get"] = "读取",
        ["system.brightness.what.option.set"] = "设为指定值",
        ["system.brightness.what.option.up"] = "调亮",
        ["system.brightness.what.option.down"] = "调暗",
        ["system.brightness.percent.label"] = "亮度百分比",
        ["system.brightness.percent.hint"] = "要设成的亮度，范围为 0 到 100，默认 50。只在「设为指定值」时使用。",
        ["system.brightness.stepPercent.label"] = "调整幅度",
        ["system.brightness.stepPercent.hint"] = "一次调亮或调暗多少个百分点，范围为 1 到 100，默认 10。只在「调亮」和「调暗」时使用，"
            + "两端都不会超过 0 和 100。",

        // ------------------------------------------------------------------ window
        ["window.exists.name"] = "窗口是否存在",
        ["window.exists.desc"] = "检查当前有没有符合匹配内容的窗口。",
        ["window.exists.window.label"] = WindowMatchTextLabel,
        ["window.exists.window.hint"] = WindowTitleHint,

        ["window.waitFor.name"] = "等待窗口",
        ["window.waitFor.desc"] = "等到符合匹配内容的窗口出现为止，超过给定时间算失败。",
        ["window.waitFor.window.label"] = WindowMatchTextLabel,
        ["window.waitFor.window.hint"] = WindowTitleHint,
        ["window.waitFor.timeoutMs.label"] = "超时",
        ["window.waitFor.timeoutMs.hint"] = "最多等多久，单位是毫秒，默认 10000；到这个时间窗口还没出现就判"
            + "失败。",

        ["window.activate.name"] = "激活窗口",
        ["window.activate.desc"] = "把符合匹配内容的窗口调到最前，已最小化时先恢复。",
        ["window.activate.window.label"] = WindowMatchTextLabel,
        ["window.activate.window.hint"] = WindowTitleHint,

        ["window.minimize.name"] = "最小化窗口",
        ["window.minimize.desc"] = "把符合匹配内容的窗口最小化到任务栏。",
        ["window.minimize.window.label"] = WindowMatchTextLabel,
        ["window.minimize.window.hint"] = WindowTitleHint,

        ["window.maximize.name"] = "最大化窗口",
        ["window.maximize.desc"] = "把符合匹配内容的窗口最大化，铺满整个屏幕。",
        ["window.maximize.window.label"] = WindowMatchTextLabel,
        ["window.maximize.window.hint"] = WindowTitleHint,

        ["window.restore.name"] = "还原窗口",
        ["window.restore.desc"] = "把符合匹配内容的窗口从最小化或最大化恢复成正常大小。",
        ["window.restore.window.label"] = WindowMatchTextLabel,
        ["window.restore.window.hint"] = WindowTitleHint,

        ["window.move.name"] = "移动窗口",
        ["window.move.desc"] = "把符合匹配内容的窗口移到指定位置，并改成指定大小。",
        ["window.move.window.label"] = WindowMatchTextLabel,
        ["window.move.window.hint"] = WindowTitleHint,
        ["window.move.x.label"] = "X",
        ["window.move.x.hint"] = "窗口左边缘的屏幕横坐标，单位像素。按窗口被看见的样子算：Windows 留着"
            + "用来拉伸的那圈边框不画出来也不算；范围 -100000 到 100000，默认 0。window.info 交回来的那组数"
            + "可以原样填在这里。",
        ["window.move.y.label"] = "Y",
        ["window.move.y.hint"] = "窗口上边缘的屏幕纵坐标，单位像素，和 X 一样按窗口被看见的样子算，范围"
            + " -100000 到 100000，默认 0。",
        ["window.move.width.label"] = "宽度",
        ["window.move.width.hint"] = "窗口要改成多宽，单位像素，同样按被看见的样子量，那圈看不见的"
            + "边框不算；范围 1 到 100000，默认 800。",
        ["window.move.height.label"] = "高度",
        ["window.move.height.hint"] = "窗口要改成多高，单位像素，和宽度一样算，范围 1 到 100000，默认 600。",
        ["window.move.useRecord.label"] = WindowRecordSwitchLabel,
        ["window.move.useRecord.hint"] = RecordBoxSwitchHint,
        ["window.move.record.label"] = RecordFieldLabel,
        ["window.move.record.hint"] = RecordBoxFieldHint,

        ["window.close.name"] = "关闭窗口",
        ["window.close.desc"] = "请求符合匹配内容的窗口关闭，相当于点它的关闭按钮。",
        ["window.close.window.label"] = WindowMatchTextLabel,
        ["window.close.window.hint"] = WindowTitleHint,

        ["window.list.name"] = "列出窗口",
        ["window.list.desc"] = "把当前打开的窗口收集成一个列表，每个窗口一条记录，可以只留「匹配内容」认到的那些。",
        ["window.list.window.label"] = WindowMatchTextLabel,
        ["window.list.window.hint"] = WindowListTitleHint,
        ["window.list.details.label"] = "每个窗口都读程序名与类名（慢）",
        ["window.list.details.hint"] =
            "把每个窗口的程序名与窗口类名读进记录的「process」与「class」两格。按标题分辨窗口时用不到它们，"
            + "读一次要给每个窗口问一次系统，所以默认关；要按程序名或按类名分辨窗口时打开它。",

        ["window.info.name"] = "窗口信息",
        ["window.info.desc"] = "读取符合匹配内容的窗口的位置和大小。",
        ["window.info.window.label"] = WindowMatchTextLabel,
        ["window.info.window.hint"] = WindowTitleHint,

        // -------------------------------------------------------------------- input
        ["input.keyPress.name"] = "按键",
        ["input.keyPress.desc"] = "按下并松开一个按键。",
        ["input.keyPress.key.label"] = "按键",
        ["input.keyPress.key.hint"] = "要按下的键，按键盘上印的名字写，例如 F5、Enter 或 A。拿不准拼法时用"
            + "旁边的「键盘」点出来。",
        ["input.keyPress.holdMs.label"] = "按住时长",
        ["input.keyPress.holdMs.hint"] = "按下之后保持多久才松开，单位是毫秒，默认 50。按下就松开、程序来不及"
            + "响应的场合调大。",
        ["input.keyPress.repeat.label"] = "重复次数",
        ["input.keyPress.repeat.hint"] = "这个键一共按几下，从 1 起，默认 1。连着按几下时，两次之间停多久由"
            + "「重复间隔」说。",
        ["input.keyPress.intervalMs.label"] = "重复间隔",
        ["input.keyPress.intervalMs.hint"] = "两下之间停多久，单位是毫秒，默认 0，即一下接一下地按。",

        ["input.keyDown.name"] = "按下按键",
        ["input.keyDown.desc"] = "保持按下某个键，直到对应的“松开按键”步骤。",
        ["input.keyDown.key.label"] = "按键",
        ["input.keyDown.key.hint"] = "要按住的键，写法与「按键」一致。松开它由对应的「松开按键」步骤负责。",

        ["input.keyUp.name"] = "松开按键",
        ["input.keyUp.desc"] = "松开当前处于按下状态的按键。",
        ["input.keyUp.key.label"] = "按键",
        ["input.keyUp.key.hint"] = "要松开的键，写法与「按键」一致；填前面某一个「按下按键」步骤按住的键。",

        ["input.hotkey.name"] = "组合键",
        ["input.hotkey.desc"] = "按下组合键，例如 Ctrl+Shift+S。",
        ["input.hotkey.keys.label"] = "按键组合",
        ["input.hotkey.keys.hint"] = "要同时按下的键，用 + 连接，例如 Ctrl+Shift+S。旁边的「键盘」可以在画出来"
            + "的键盘上逐个点选：左右两个 Ctrl、左右两个 Shift 分开写，机器也分得清它们。",
        ["input.hotkey.holdMs.label"] = "按住时长",
        ["input.hotkey.holdMs.hint"] = "这组键按下之后保持多久才松开，单位是毫秒，默认 50。",
        ["input.hotkey.repeat.label"] = "重复次数",
        ["input.hotkey.repeat.hint"] = "这一组一共发几次，从 1 起，默认 1。",
        ["input.hotkey.intervalMs.label"] = "重复间隔",
        ["input.hotkey.intervalMs.hint"] = "两次之间停多久，单位是毫秒，默认 0。",

        ["input.typeText.name"] = "输入文本",
        ["input.typeText.desc"] = "输入一段文字。",
        ["input.typeText.text.label"] = "文本",
        ["input.typeText.text.hint"] = "要输入的文字，可以写多行，也可以填一个变量或算式。",
        ["input.typeText.intervalMs.label"] = "间隔",
        ["input.typeText.intervalMs.hint"] = "每个字符之间停多久，单位是毫秒，默认 30。输入太快、程序跟不上时"
            + "调大。",

        ["input.keySequence.name"] = "按键序列",
        ["input.keySequence.desc"] = "按顺序发出一串组合键，像游戏里打一套连招那样。",
        ["input.keySequence.keys.label"] = "序列",
        ["input.keySequence.keys.hint"] = "一行一个组合，按列表顺序发送。按键用旁边的「键盘」点选，拼写不会错"
            + "（左右 Ctrl、左右 Shift 名字不同，机器也分得开）。某一行的按住时长或间隔与整串不一样时，"
            + "就在那一行里单独填；留空表示用整串的值。",
        ["input.keySequence.holdMs.label"] = "每个组合按住",
        ["input.keySequence.holdMs.hint"] = "整串里每个组合保持按下多久，单位是毫秒，默认 50；某一行可以在行内"
            + "另填。",
        ["input.keySequence.gapMs.label"] = "组合之间的间隔",
        ["input.keySequence.gapMs.hint"] = "一个组合与下一个之间等多久，单位毫秒，默认 120；某一行可以在行内另填。太短会把几次按键连成一片，"
            + "太长就不像一套连招；想让间隔每次都不同，用这个框旁边的 ± 填每次运行上下浮动的比例。",
        ["input.keySequence.afterMs.label"] = "整串之后停顿",
        ["input.keySequence.afterMs.hint"] = "最后一个组合发完之后再等多久才走下一步，单位是毫秒，默认 0。",
        ["input.keySequence.repeat.label"] = "整串再来几遍",
        ["input.keySequence.repeat.hint"] = "整串连着发几遍，从 1 起，默认 1。",
        ["input.keySequence.intervalMs.label"] = "每遍之间",
        ["input.keySequence.intervalMs.hint"] = "两遍之间停多久，单位是毫秒，默认 0。",

        ["input.mouseMove.name"] = "鼠标移动到",
        ["input.mouseMove.desc"] = "把光标移动到屏幕上的指定位置。",
        ["input.mouseMove.x.label"] = "X",
        ["input.mouseMove.x.hint"] = "目标位置的横坐标，按「坐标基准」量起，单位是像素，默认 0。位置固定时填"
            + "数字；位置会变时改用「坐标基准」，或在这里填一个变量或算式。",
        ["input.mouseMove.y.label"] = "Y",
        ["input.mouseMove.y.hint"] = "目标位置的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["input.mouseMove.useRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseMove.useRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseMove.record.label"] = RecordFieldLabel,
        ["input.mouseMove.record.hint"] = RecordPlaceFieldHint,
        ["input.mouseMove.durationMs.label"] = "用时",
        ["input.mouseMove.durationMs.hint"] = "从指针当前所在处走到目标位置用多久，单位是毫秒，默认 0，即瞬间"
            + "到达。",
        ["input.mouseMove.style.label"] = "移动方式",
        ["input.mouseMove.style.hint"] = "指针怎么走过去，默认直线。弧线：绕开直线走一条曲线。模拟人手：在弧线上再"
            + "加一点抖动，两头慢中间快。走弧线要占时间，所以「用时」填 0 时会按 200 毫秒走。",
        ["input.mouseMove.style.option.direct"] = "直线",
        ["input.mouseMove.style.option.smooth"] = "弧线",
        ["input.mouseMove.style.option.human"] = "模拟人手",

        ["input.mouseMoveRelative.name"] = "鼠标相对移动",
        ["input.mouseMoveRelative.desc"] = "在当前光标位置的基础上偏移一段距离。",
        ["input.mouseMoveRelative.dx.label"] = "横向偏移",
        ["input.mouseMoveRelative.dx.hint"] = "横向移动的像素数，正数向右、负数向左，单位是像素，默认 0，不小于"
            + " -100000。",
        ["input.mouseMoveRelative.dy.label"] = "纵向偏移",
        ["input.mouseMoveRelative.dy.hint"] = "纵向移动的像素数，正数向下、负数向上，单位是像素，默认 0，不小于"
            + " -100000。",
        ["input.mouseMoveRelative.durationMs.label"] = "用时",
        ["input.mouseMoveRelative.durationMs.hint"] = "从指针当前所在处走到偏移后的位置用多久，单位是毫秒，默认"
            + " 0，即瞬间到达。",
        ["input.mouseMoveRelative.style.label"] = "移动方式",
        ["input.mouseMoveRelative.style.hint"] = "指针怎么走过去，默认直线。弧线：绕开直线走一条曲线。模拟人手："
            + "在弧线上再加一点抖动，两头慢中间快。走弧线要占时间，所以「用时」填 0 时会按 200 毫秒走。",
        ["input.mouseMoveRelative.style.option.direct"] = "直线",
        ["input.mouseMoveRelative.style.option.smooth"] = "弧线",
        ["input.mouseMoveRelative.style.option.human"] = "模拟人手",

        ["input.mouseClick.name"] = "鼠标点击",
        ["input.mouseClick.desc"] = "在屏幕指定位置点击鼠标按键。",
        ["input.mouseClick.button.label"] = "按键",
        ["input.mouseClick.button.hint"] = MouseButtonHint,
        ["input.mouseClick.useCursor.label"] = AtPointerLabel,
        ["input.mouseClick.useCursor.hint"] = AtPointerHint,
        ["input.mouseClick.x.label"] = "X",
        ["input.mouseClick.x.hint"] = "点击位置的横坐标，按「坐标基准」量起，单位是像素，默认 0。位置固定时填"
            + "数字；勾上「就在指针当前所在位置」时这一格不写进宏文件。",
        ["input.mouseClick.y.label"] = "Y",
        ["input.mouseClick.y.hint"] = "点击位置的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["input.mouseClick.useRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseClick.useRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseClick.record.label"] = RecordFieldLabel,
        ["input.mouseClick.record.hint"] = RecordPlaceFieldHint,
        ["input.mouseClick.repeat.label"] = "重复次数",
        ["input.mouseClick.repeat.hint"] = "一共点几下，从 1 起，默认 1。点几下时两次之间停多久由「间隔」说。",
        ["input.mouseClick.intervalMs.label"] = "间隔",
        ["input.mouseClick.intervalMs.hint"] = "两下之间停多久，单位是毫秒，默认 0，即一下接一下地点。",
        ["input.mouseClick.holdMs.label"] = "按住时长",
        ["input.mouseClick.holdMs.hint"] = "按下之后保持多久才松开，单位是毫秒，默认 0，即普通的一下快按。对需要"
            + "长按才响应的按钮调大。",
        ["input.mouseClick.variance.label"] = "落点随机范围",
        ["input.mouseClick.variance.hint"] =
            "实际落点与上面写的坐标最多相差多少个像素，默认 0，即正好点在写的位置上。填了数就每次都"
            + "在这个范围内随机取一点，连着点不会落在同一个像素上。数字要小到整个范围仍落在被点的"
            + "东西里面。",
        ["input.mouseClick.moveBack.label"] = "点完把指针移回原处",
        ["input.mouseClick.moveBack.hint"] =
            "点完之后把指针移回点击之前的位置，鼠标就留在用户自己放的地方，默认关。后台模式下屏幕上指针本来"
            + "就没有移动，这个开关不起作用。",

        ["input.mouseDoubleClick.name"] = "鼠标双击",
        ["input.mouseDoubleClick.desc"] = "在屏幕指定位置双击鼠标按键。",
        ["input.mouseDoubleClick.button.label"] = "按键",
        ["input.mouseDoubleClick.button.hint"] = MouseButtonHint,
        ["input.mouseDoubleClick.useCursor.label"] = AtPointerLabel,
        ["input.mouseDoubleClick.useCursor.hint"] = AtPointerHint,
        ["input.mouseDoubleClick.x.label"] = "X",
        ["input.mouseDoubleClick.x.hint"] = "双击位置的横坐标，按「坐标基准」量起，单位是像素，默认 0。位置固定"
            + "时填数字；勾上「就在指针当前所在位置」时这一格不写进宏文件。",
        ["input.mouseDoubleClick.y.label"] = "Y",
        ["input.mouseDoubleClick.y.hint"] = "双击位置的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["input.mouseDoubleClick.useRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseDoubleClick.useRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseDoubleClick.record.label"] = RecordFieldLabel,
        ["input.mouseDoubleClick.record.hint"] = RecordPlaceFieldHint,
        ["input.mouseDoubleClick.repeat.label"] = "重复次数",
        ["input.mouseDoubleClick.repeat.hint"] = "连着双击几次，从 1 起，默认 1，即一次双击。",
        ["input.mouseDoubleClick.intervalMs.label"] = "间隔",
        ["input.mouseDoubleClick.intervalMs.hint"] = "两次双击之间停多久，单位是毫秒，默认 0。",
        ["input.mouseDoubleClick.variance.label"] = "落点随机范围",
        ["input.mouseDoubleClick.variance.hint"] =
            "实际落点与上面写的坐标最多相差多少个像素，默认 0，即正好点在写的位置上。填了数就"
            + "每次双击都在这个范围内随机取一点。",
        ["input.mouseDoubleClick.moveBack.label"] = "点完把指针移回原处",
        ["input.mouseDoubleClick.moveBack.hint"] =
            "点完之后把指针移回双击之前的位置，鼠标就留在用户自己放的地方，默认关。后台模式下屏幕上指针本来"
            + "就没有移动，这个开关不起作用。",

        ["input.mouseDown.name"] = "按下鼠标",
        ["input.mouseDown.desc"] = "保持按住鼠标按键，直到对应的“松开鼠标”步骤。",
        ["input.mouseDown.button.label"] = "按键",
        ["input.mouseDown.button.hint"] = MouseButtonHint,
        ["input.mouseDown.useCursor.label"] = AtPointerLabel,
        ["input.mouseDown.useCursor.hint"] = AtPointerHint,
        ["input.mouseDown.x.label"] = "X",
        ["input.mouseDown.x.hint"] = "按下位置的横坐标，按「坐标基准」量起，单位是像素，默认 0。勾上「就在指针"
            + "当前所在位置」时这一格不写进宏文件。",
        ["input.mouseDown.y.label"] = "Y",
        ["input.mouseDown.y.hint"] = "按下位置的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["input.mouseDown.useRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseDown.useRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseDown.record.label"] = RecordFieldLabel,
        ["input.mouseDown.record.hint"] = RecordPlaceFieldHint,

        ["input.mouseUp.name"] = "松开鼠标",
        ["input.mouseUp.desc"] = "松开当前处于按住状态的鼠标按键。",
        ["input.mouseUp.button.label"] = "按键",
        ["input.mouseUp.button.hint"] = MouseButtonHint,
        ["input.mouseUp.useCursor.label"] = AtPointerLabel,
        ["input.mouseUp.useCursor.hint"] = AtPointerHint,
        ["input.mouseUp.x.label"] = "X",
        ["input.mouseUp.x.hint"] = "松开位置的横坐标，按「坐标基准」量起，单位是像素，默认 0。勾上「就在指针"
            + "当前所在位置」时这一格不写进宏文件。",
        ["input.mouseUp.y.label"] = "Y",
        ["input.mouseUp.y.hint"] = "松开位置的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["input.mouseUp.useRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseUp.useRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseUp.record.label"] = RecordFieldLabel,
        ["input.mouseUp.record.hint"] = RecordPlaceFieldHint,

        ["input.mouseScroll.name"] = "鼠标滚轮",
        ["input.mouseScroll.desc"] = "滚动鼠标滚轮。",
        ["input.mouseScroll.direction.label"] = "方向",
        ["input.mouseScroll.direction.hint"] = "滚轮往哪边滚，默认向下。",
        ["input.mouseScroll.unit.label"] = "滚轮单位",
        ["input.mouseScroll.unit.hint"] = "滚多远按什么计算，默认格。格是滚轮平时一次转动的量；像素把同样的转动量"
            + "分得更细，120 像素等于一格。只看整数格的程序可能会忽略不足一格的部分。",
        ["input.mouseScroll.unit.option.notches"] = "格",
        ["input.mouseScroll.unit.option.pixels"] = "像素",
        ["input.mouseScroll.amount.label"] = "数量",
        ["input.mouseScroll.amount.hint"] = "滚多远，按上面的单位计算，从 1 起，默认 3。",
        ["input.mouseScroll.smoothMs.label"] = "平滑时长",
        ["input.mouseScroll.smoothMs.hint"] = "把这次滚动分摊到这段时间里分小段发送，单位是毫秒，默认 0，即一次"
            + "滚完。程序自己带动画时用它。",
        ["input.mouseScroll.x.label"] = "X",
        ["input.mouseScroll.x.hint"] = "滚动位置的横坐标，按「坐标基准」量起，单位是像素，默认 0。勾上「就在指针"
            + "当前所在位置」时这一格不写进宏文件。",
        ["input.mouseScroll.y.label"] = "Y",
        ["input.mouseScroll.y.hint"] = "滚动位置的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["input.mouseScroll.useRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseScroll.useRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseScroll.record.label"] = RecordFieldLabel,
        ["input.mouseScroll.record.hint"] = RecordPlaceFieldHint,
        ["input.mouseScroll.useCursor.label"] = AtPointerLabel,
        ["input.mouseScroll.useCursor.hint"] = AtPointerHint,
        ["input.mouseScroll.repeat.label"] = "重复次数",
        ["input.mouseScroll.repeat.hint"] = "这么大的滚动量发几次，从 1 起，默认 1。",
        ["input.mouseScroll.intervalMs.label"] = "间隔",
        ["input.mouseScroll.intervalMs.hint"] = "两次之间停多久，单位是毫秒，默认 0。",

        ["input.mouseDrag.name"] = "鼠标拖拽",
        ["input.mouseDrag.desc"] = "在起点按下，拖至终点后松开。",
        ["input.mouseDrag.startX.label"] = "起点 X",
        ["input.mouseDrag.startX.hint"] = "拖拽从哪一列开始，按「坐标基准」量起，单位是像素，默认 0。"
            + "「框选区域」把两端一次填满：拖出来的那个矩形的左上角填在这里。",
        ["input.mouseDrag.startY.label"] = "起点 Y",
        ["input.mouseDrag.startY.hint"] = "拖拽从哪一行开始，和起点的横坐标一样量，单位是像素，默认 0。",
        ["input.mouseDrag.startFromRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseDrag.startFromRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseDrag.startRecord.label"] = "起点记录",
        ["input.mouseDrag.startRecord.hint"] = RecordPlaceFieldHint,
        ["input.mouseDrag.endX.label"] = "终点 X",
        ["input.mouseDrag.endX.hint"] = "拖拽到哪一列结束，和起点一样量，单位是像素，默认 0。"
            + "「框选区域」填的是拖出来的那个矩形的右下角。",
        ["input.mouseDrag.endY.label"] = "终点 Y",
        ["input.mouseDrag.endY.hint"] = "拖拽到哪一行结束，和起点一样量，单位是像素，默认 0。",
        ["input.mouseDrag.endFromRecord.label"] = RecordPlaceSwitchLabel,
        ["input.mouseDrag.endFromRecord.hint"] = RecordPlaceSwitchHint,
        ["input.mouseDrag.endRecord.label"] = "终点记录",
        ["input.mouseDrag.endRecord.hint"] = RecordPlaceFieldHint,
        ["input.mouseDrag.button.label"] = "按键",
        ["input.mouseDrag.button.hint"] = MouseButtonHint,
        ["input.mouseDrag.durationMs.label"] = "用时",
        ["input.mouseDrag.durationMs.hint"] = "整个拖拽用多久，单位是毫秒，默认 300；从起点走到终点就是在这段时间"
            + "里走完的。",
        ["input.mouseDrag.settleMs.label"] = "到终点先停",
        ["input.mouseDrag.settleMs.hint"] =
            "拖到终点后、松开鼠标键之前先按住不动这么多毫秒，默认 0，即一到就松。要拖进格子里的"
            + "东西，只有指针在格子上停过一下才会被放进去；一到就松会变成从上面滑过去。",
        ["input.mouseDrag.steps.label"] = "移动步数",
        ["input.mouseDrag.steps.hint"] = "拖拽过程中发出的中间移动事件数量，从 1 起，默认 20。步数越多，指针"
            + "走出的路径画得越细。",
        ["input.mouseDrag.style.label"] = "移动方式",
        ["input.mouseDrag.style.hint"] = "拖拽过程中指针怎么走，默认直线。弧线：绕开直线走一条曲线。模拟人手：在"
            + "弧线上再加一点抖动，两头慢中间快。",
        ["input.mouseDrag.style.option.direct"] = "直线",
        ["input.mouseDrag.style.option.smooth"] = "弧线",
        ["input.mouseDrag.style.option.human"] = "模拟人手",

        ["gamepad.connect.name"] = "连接虚拟手柄",
        ["gamepad.connect.desc"] = "在本机接上一只虚拟手柄（Xbox 360），后面的按键、摇杆、扳机步骤本来就会"
            + "自己把它接上；这一步用于「游戏只在启动时认一次手柄」的场合。和虚拟键盘一样属于驱动级"
            + "输入，需要 usbip-win2 驱动。",

        ["gamepad.button.name"] = "手柄按键",
        ["gamepad.button.desc"] = "按下虚拟手柄上的一个按键、按住不放，或者松开。",
        ["gamepad.button.button.label"] = "按键",
        ["gamepad.button.button.hint"] = "要按的键，名字和手柄上印的一致，默认 A 键。",
        ["gamepad.button.button.option.a"] = "A 键",
        ["gamepad.button.button.option.b"] = "B 键",
        ["gamepad.button.button.option.x"] = "X 键",
        ["gamepad.button.button.option.y"] = "Y 键",
        ["gamepad.button.button.option.lb"] = "LB",
        ["gamepad.button.button.option.rb"] = "RB",
        ["gamepad.button.button.option.lt"] = "LT",
        ["gamepad.button.button.option.rt"] = "RT",
        ["gamepad.button.button.option.ls"] = "LS",
        ["gamepad.button.button.option.rs"] = "RS",
        ["gamepad.button.button.option.up"] = "方向键上",
        ["gamepad.button.button.option.down"] = "方向键下",
        ["gamepad.button.button.option.left"] = "方向键左",
        ["gamepad.button.button.option.right"] = "方向键右",
        ["gamepad.button.button.option.start"] = "Start",
        ["gamepad.button.button.option.back"] = "Back",
        ["gamepad.button.button.option.guide"] = "Guide",
        ["gamepad.button.mode.label"] = "动作",
        ["gamepad.button.mode.hint"] = "这一步对这个键做什么，默认点按。点按＝按下再松开；按住与松开用于要跨步骤"
            + "保持按下的键。",
        ["gamepad.button.mode.option.tap"] = "点按",
        ["gamepad.button.mode.option.down"] = "按住",
        ["gamepad.button.mode.option.up"] = "松开",
        ["gamepad.button.holdMs.label"] = "按住时长",
        ["gamepad.button.holdMs.hint"] = "点按时按键保持按下多久，单位是毫秒，默认 50；按住与松开两种动作不用"
            + "它。",
        ["gamepad.button.repeat.label"] = "连按次数",
        ["gamepad.button.repeat.hint"] = "点按几下，从 1 起，默认 1；两次之间停多久由「连按间隔」说。",
        ["gamepad.button.intervalMs.label"] = "连按间隔",
        ["gamepad.button.intervalMs.hint"] = "两次之间停多久，单位是毫秒，默认 0。",

        ["gamepad.stick.name"] = "手柄摇杆",
        ["gamepad.stick.desc"] = "把虚拟手柄的一根摇杆拨开，写的是偏离中心百分之多少。",
        ["gamepad.stick.stick.label"] = "摇杆",
        ["gamepad.stick.stick.hint"] = "推哪一根摇杆，默认左摇杆。",
        ["gamepad.stick.stick.option.left"] = "左摇杆",
        ["gamepad.stick.stick.option.right"] = "右摇杆",
        ["gamepad.stick.x.label"] = "X",
        ["gamepad.stick.x.hint"] = "左右拨多少，用偏离中心的百分数写，范围 -100 到 100，默认 0：100 是最右，"
            + "-100 是最左。",
        ["gamepad.stick.y.label"] = "Y",
        ["gamepad.stick.y.hint"] = "上下拨多少，用偏离中心的百分数写，范围 -100 到 100，默认 0：100 是最上，"
            + "-100 是最下。",

        ["gamepad.trigger.name"] = "手柄扳机",
        ["gamepad.trigger.desc"] = "扣下虚拟手柄的一个扳机，或者松开。",
        ["gamepad.trigger.trigger.label"] = "扳机",
        ["gamepad.trigger.trigger.hint"] = "扣哪一个扳机，默认左扳机。",
        ["gamepad.trigger.trigger.option.left"] = "左扳机 (LT)",
        ["gamepad.trigger.trigger.option.right"] = "右扳机 (RT)",
        ["gamepad.trigger.amount.label"] = "扣下程度",
        ["gamepad.trigger.amount.hint"] = "扣下多少，用百分数写，范围 0 到 100，默认 100，即扣到底；松开扳机填 0。",

        ["gamepad.release.name"] = "松开手柄",
        ["gamepad.release.desc"] = "松开虚拟手柄上所有按键、摇杆和扳机。一次运行结束时本来就会自动松开，"
            + "这一步用于宏中途想主动交还手柄。",

        // 输入方式与后台输入的目标窗口，14 个输入动作共用一组文案。
        ["input.keyPress.inputMode.label"] = "输入方式",
        ["input.keyPress.inputMode.hint"] = InputModeHint,
        ["input.keyPress.inputMode.option.foreground"] = "前台",
        ["input.keyPress.inputMode.option.background"] = "后台消息",
        ["input.keyPress.inputMode.option.driver"] = "驱动级",
        ["input.keyPress.targetWindow.label"] = "目标窗口",
        ["input.keyPress.targetWindow.hint"] = TargetWindowHint,

        ["input.keySequence.inputMode.label"] = "输入方式",
        ["input.keySequence.inputMode.hint"] = InputModeHint,
        ["input.keySequence.inputMode.option.foreground"] = "前台",
        ["input.keySequence.inputMode.option.background"] = "后台消息",
        ["input.keySequence.inputMode.option.driver"] = "驱动级",
        ["input.keySequence.targetWindow.label"] = "目标窗口",
        ["input.keySequence.targetWindow.hint"] = TargetWindowHint,

        ["input.keyDown.inputMode.label"] = "输入方式",
        ["input.keyDown.inputMode.hint"] = InputModeHint,
        ["input.keyDown.inputMode.option.foreground"] = "前台",
        ["input.keyDown.inputMode.option.background"] = "后台消息",
        ["input.keyDown.inputMode.option.driver"] = "驱动级",
        ["input.keyDown.targetWindow.label"] = "目标窗口",
        ["input.keyDown.targetWindow.hint"] = TargetWindowHint,

        ["input.keyUp.inputMode.label"] = "输入方式",
        ["input.keyUp.inputMode.hint"] = InputModeHint,
        ["input.keyUp.inputMode.option.foreground"] = "前台",
        ["input.keyUp.inputMode.option.background"] = "后台消息",
        ["input.keyUp.inputMode.option.driver"] = "驱动级",
        ["input.keyUp.targetWindow.label"] = "目标窗口",
        ["input.keyUp.targetWindow.hint"] = TargetWindowHint,

        ["input.hotkey.inputMode.label"] = "输入方式",
        ["input.hotkey.inputMode.hint"] = InputModeHint,
        ["input.hotkey.inputMode.option.foreground"] = "前台",
        ["input.hotkey.inputMode.option.background"] = "后台消息",
        ["input.hotkey.inputMode.option.driver"] = "驱动级",
        ["input.hotkey.targetWindow.label"] = "目标窗口",
        ["input.hotkey.targetWindow.hint"] = TargetWindowHint,

        ["input.typeText.inputMode.label"] = "输入方式",
        ["input.typeText.inputMode.hint"] = InputModeHint,
        ["input.typeText.inputMode.option.foreground"] = "前台",
        ["input.typeText.inputMode.option.background"] = "后台消息",
        ["input.typeText.inputMode.option.driver"] = "驱动级",
        ["input.typeText.targetWindow.label"] = "目标窗口",
        ["input.typeText.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseMove.inputMode.label"] = "输入方式",
        ["input.mouseMove.inputMode.hint"] = InputModeHint
            + "后台方式下屏幕上的鼠标不会移动，只是让窗口以为鼠标到了那里。",
        ["input.mouseMove.inputMode.option.foreground"] = "前台",
        ["input.mouseMove.inputMode.option.background"] = "后台消息",
        ["input.mouseMove.inputMode.option.driver"] = "驱动级",
        ["input.mouseMove.targetWindow.label"] = "目标窗口",
        ["input.mouseMove.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseMoveRelative.inputMode.label"] = "输入方式",
        ["input.mouseMoveRelative.inputMode.hint"] = InputModeHint
            + "后台方式下屏幕上的鼠标不会移动，只是让窗口以为鼠标到了那里。这一步不能用后台消息："
            + "相对移动问的是指针现在在哪儿，而这一档从来没让指针动过。改用前台或驱动级，或者用"
            + "「鼠标移动到」把位置按绝对坐标写全。",
        ["input.mouseMoveRelative.inputMode.option.foreground"] = "前台",
        ["input.mouseMoveRelative.inputMode.option.background"] = "后台消息",
        ["input.mouseMoveRelative.inputMode.option.driver"] = "驱动级",
        ["input.mouseMoveRelative.targetWindow.label"] = "目标窗口",
        ["input.mouseMoveRelative.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseClick.inputMode.label"] = "输入方式",
        ["input.mouseClick.inputMode.hint"] = InputModeHint,
        ["input.mouseClick.inputMode.option.foreground"] = "前台",
        ["input.mouseClick.inputMode.option.background"] = "后台消息",
        ["input.mouseClick.inputMode.option.driver"] = "驱动级",
        ["input.mouseClick.targetWindow.label"] = "目标窗口",
        ["input.mouseClick.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseDoubleClick.inputMode.label"] = "输入方式",
        ["input.mouseDoubleClick.inputMode.hint"] = InputModeHint,
        ["input.mouseDoubleClick.inputMode.option.foreground"] = "前台",
        ["input.mouseDoubleClick.inputMode.option.background"] = "后台消息",
        ["input.mouseDoubleClick.inputMode.option.driver"] = "驱动级",
        ["input.mouseDoubleClick.targetWindow.label"] = "目标窗口",
        ["input.mouseDoubleClick.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseDown.inputMode.label"] = "输入方式",
        ["input.mouseDown.inputMode.hint"] = InputModeHint,
        ["input.mouseDown.inputMode.option.foreground"] = "前台",
        ["input.mouseDown.inputMode.option.background"] = "后台消息",
        ["input.mouseDown.inputMode.option.driver"] = "驱动级",
        ["input.mouseDown.targetWindow.label"] = "目标窗口",
        ["input.mouseDown.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseUp.inputMode.label"] = "输入方式",
        ["input.mouseUp.inputMode.hint"] = InputModeHint,
        ["input.mouseUp.inputMode.option.foreground"] = "前台",
        ["input.mouseUp.inputMode.option.background"] = "后台消息",
        ["input.mouseUp.inputMode.option.driver"] = "驱动级",
        ["input.mouseUp.targetWindow.label"] = "目标窗口",
        ["input.mouseUp.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseScroll.inputMode.label"] = "输入方式",
        ["input.mouseScroll.inputMode.hint"] = InputModeHint,
        ["input.mouseScroll.inputMode.option.foreground"] = "前台",
        ["input.mouseScroll.inputMode.option.background"] = "后台消息",
        ["input.mouseScroll.inputMode.option.driver"] = "驱动级",
        ["input.mouseScroll.targetWindow.label"] = "目标窗口",
        ["input.mouseScroll.targetWindow.hint"] = TargetWindowHint,

        ["input.mouseDrag.inputMode.label"] = "输入方式",
        ["input.mouseDrag.inputMode.hint"] = InputModeHint,
        ["input.mouseDrag.inputMode.option.foreground"] = "前台",
        ["input.mouseDrag.inputMode.option.background"] = "后台消息",
        ["input.mouseDrag.inputMode.option.driver"] = "驱动级",
        ["input.mouseDrag.targetWindow.label"] = "目标窗口",
        ["input.mouseDrag.targetWindow.hint"] = TargetWindowHint,

        // 坐标基准：所有带坐标或矩形的动作共用一组文案。
        ["input.mouseMove.anchorMode.label"] = "坐标基准",
        ["input.mouseMove.anchorMode.hint"] = AnchorModeHint,
        ["input.mouseMove.anchorMode.option.element"] = "界面元素左上角",
        ["input.mouseMove.anchorSelector.label"] = "锚点元素",
        ["input.mouseMove.anchorSelector.hint"] = AnchorElementHint,
        ["input.mouseMove.anchorMode.option.screen"] = "屏幕像素",
        ["input.mouseMove.anchorMode.option.window"] = "窗口左上角",
        ["input.mouseMove.anchorMode.option.client"] = "窗口客户区",
        ["input.mouseMove.anchorWindow.label"] = "基准窗口",
        ["input.mouseMove.anchorWindow.hint"] = AnchorWindowHint,

        ["input.mouseClick.anchorMode.label"] = "坐标基准",
        ["input.mouseClick.anchorMode.hint"] = AnchorModeHint,
        ["input.mouseClick.anchorMode.option.element"] = "界面元素左上角",
        ["input.mouseClick.anchorSelector.label"] = "锚点元素",
        ["input.mouseClick.anchorSelector.hint"] = AnchorElementHint,
        ["input.mouseClick.anchorMode.option.screen"] = "屏幕像素",
        ["input.mouseClick.anchorMode.option.window"] = "窗口左上角",
        ["input.mouseClick.anchorMode.option.client"] = "窗口客户区",
        ["input.mouseClick.anchorWindow.label"] = "基准窗口",
        ["input.mouseClick.anchorWindow.hint"] = AnchorWindowHint,

        ["input.mouseDoubleClick.anchorMode.label"] = "坐标基准",
        ["input.mouseDoubleClick.anchorMode.hint"] = AnchorModeHint,
        ["input.mouseDoubleClick.anchorMode.option.element"] = "界面元素左上角",
        ["input.mouseDoubleClick.anchorSelector.label"] = "锚点元素",
        ["input.mouseDoubleClick.anchorSelector.hint"] = AnchorElementHint,
        ["input.mouseDoubleClick.anchorMode.option.screen"] = "屏幕像素",
        ["input.mouseDoubleClick.anchorMode.option.window"] = "窗口左上角",
        ["input.mouseDoubleClick.anchorMode.option.client"] = "窗口客户区",
        ["input.mouseDoubleClick.anchorWindow.label"] = "基准窗口",
        ["input.mouseDoubleClick.anchorWindow.hint"] = AnchorWindowHint,

        ["input.mouseDown.anchorMode.label"] = "坐标基准",
        ["input.mouseDown.anchorMode.hint"] = AnchorModeHint,
        ["input.mouseDown.anchorMode.option.element"] = "界面元素左上角",
        ["input.mouseDown.anchorSelector.label"] = "锚点元素",
        ["input.mouseDown.anchorSelector.hint"] = AnchorElementHint,
        ["input.mouseDown.anchorMode.option.screen"] = "屏幕像素",
        ["input.mouseDown.anchorMode.option.window"] = "窗口左上角",
        ["input.mouseDown.anchorMode.option.client"] = "窗口客户区",
        ["input.mouseDown.anchorWindow.label"] = "基准窗口",
        ["input.mouseDown.anchorWindow.hint"] = AnchorWindowHint,

        ["input.mouseUp.anchorMode.label"] = "坐标基准",
        ["input.mouseUp.anchorMode.hint"] = AnchorModeHint,
        ["input.mouseUp.anchorMode.option.element"] = "界面元素左上角",
        ["input.mouseUp.anchorSelector.label"] = "锚点元素",
        ["input.mouseUp.anchorSelector.hint"] = AnchorElementHint,
        ["input.mouseUp.anchorMode.option.screen"] = "屏幕像素",
        ["input.mouseUp.anchorMode.option.window"] = "窗口左上角",
        ["input.mouseUp.anchorMode.option.client"] = "窗口客户区",
        ["input.mouseUp.anchorWindow.label"] = "基准窗口",
        ["input.mouseUp.anchorWindow.hint"] = AnchorWindowHint,

        ["input.mouseScroll.anchorMode.label"] = "坐标基准",
        ["input.mouseScroll.anchorMode.hint"] = AnchorModeHint,
        ["input.mouseScroll.anchorMode.option.element"] = "界面元素左上角",
        ["input.mouseScroll.anchorSelector.label"] = "锚点元素",
        ["input.mouseScroll.anchorSelector.hint"] = AnchorElementHint,
        ["input.mouseScroll.anchorMode.option.screen"] = "屏幕像素",
        ["input.mouseScroll.anchorMode.option.window"] = "窗口左上角",
        ["input.mouseScroll.anchorMode.option.client"] = "窗口客户区",
        ["input.mouseScroll.anchorWindow.label"] = "基准窗口",
        ["input.mouseScroll.anchorWindow.hint"] = AnchorWindowHint,

        ["input.mouseDrag.anchorMode.label"] = "坐标基准",
        ["input.mouseDrag.anchorMode.hint"] = AnchorModeHint,
        ["input.mouseDrag.anchorMode.option.element"] = "界面元素左上角",
        ["input.mouseDrag.anchorSelector.label"] = "锚点元素",
        ["input.mouseDrag.anchorSelector.hint"] = AnchorElementHint,
        ["input.mouseDrag.anchorMode.option.screen"] = "屏幕像素",
        ["input.mouseDrag.anchorMode.option.window"] = "窗口左上角",
        ["input.mouseDrag.anchorMode.option.client"] = "窗口客户区",
        ["input.mouseDrag.anchorWindow.label"] = "基准窗口",
        ["input.mouseDrag.anchorWindow.hint"] = AnchorWindowHint,

        ["vision.capture.anchorMode.label"] = "坐标基准",
        ["vision.capture.anchorMode.hint"] = AnchorModeHint,
        ["vision.capture.anchorMode.option.element"] = "界面元素左上角",
        ["vision.capture.anchorSelector.label"] = "锚点元素",
        ["vision.capture.anchorSelector.hint"] = AnchorElementHint,
        ["vision.capture.anchorMode.option.screen"] = "屏幕像素",
        ["vision.capture.anchorMode.option.window"] = "窗口左上角",
        ["vision.capture.anchorMode.option.client"] = "窗口客户区",
        ["vision.capture.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.capture.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["vision.findImage.anchorMode.label"] = "坐标基准",
        ["vision.findImage.anchorMode.hint"] = AnchorModeHint,
        ["vision.findImage.anchorMode.option.screen"] = "屏幕像素",
        ["vision.findImage.anchorMode.option.window"] = "窗口左上角",
        ["vision.findImage.anchorMode.option.client"] = "窗口客户区",
        ["vision.findImage.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.findImage.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["vision.getPixel.anchorMode.label"] = "坐标基准",
        ["vision.getPixel.anchorMode.hint"] = AnchorModeHint,
        ["vision.getPixel.anchorMode.option.screen"] = "屏幕像素",
        ["vision.getPixel.anchorMode.option.window"] = "窗口左上角",
        ["vision.getPixel.anchorMode.option.client"] = "窗口客户区",
        ["vision.getPixel.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.getPixel.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["vision.waitColor.anchorMode.label"] = "坐标基准",
        ["vision.waitColor.anchorMode.hint"] = AnchorModeHint,
        ["vision.waitColor.anchorMode.option.screen"] = "屏幕像素",
        ["vision.waitColor.anchorMode.option.window"] = "窗口左上角",
        ["vision.waitColor.anchorMode.option.client"] = "窗口客户区",
        ["vision.waitColor.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.waitColor.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["ocr.recognize.anchorMode.label"] = "坐标基准",
        ["ocr.recognize.anchorMode.hint"] = AnchorModeHint,
        ["ocr.recognize.anchorMode.option.screen"] = "屏幕像素",
        ["ocr.recognize.anchorMode.option.window"] = "窗口左上角",
        ["ocr.recognize.anchorMode.option.client"] = "窗口客户区",
        ["ocr.recognize.anchorWindow.label"] = WindowMatchTextLabel,
        ["ocr.recognize.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["ocr.findText.anchorMode.label"] = "坐标基准",
        ["ocr.findText.anchorMode.hint"] = AnchorModeHint,
        ["ocr.findText.anchorMode.option.screen"] = "屏幕像素",
        ["ocr.findText.anchorMode.option.window"] = "窗口左上角",
        ["ocr.findText.anchorMode.option.client"] = "窗口客户区",
        ["ocr.findText.anchorWindow.label"] = WindowMatchTextLabel,
        ["ocr.findText.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["condition.imageExists.anchorMode.label"] = "坐标基准",
        ["condition.imageExists.anchorMode.hint"] = AnchorModeHint,
        ["condition.imageExists.anchorMode.option.screen"] = "屏幕像素",
        ["condition.imageExists.anchorMode.option.window"] = "窗口左上角",
        ["condition.imageExists.anchorMode.option.client"] = "窗口客户区",
        ["condition.imageExists.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.imageExists.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["condition.textExists.anchorMode.label"] = "坐标基准",
        ["condition.textExists.anchorMode.hint"] = AnchorModeHint,
        ["condition.textExists.anchorMode.option.screen"] = "屏幕像素",
        ["condition.textExists.anchorMode.option.window"] = "窗口左上角",
        ["condition.textExists.anchorMode.option.client"] = "窗口客户区",
        ["condition.textExists.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.textExists.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["condition.colorEquals.anchorMode.label"] = "坐标基准",
        ["condition.colorEquals.anchorMode.hint"] = AnchorModeHint,
        ["condition.colorEquals.anchorMode.option.screen"] = "屏幕像素",
        ["condition.colorEquals.anchorMode.option.window"] = "窗口左上角",
        ["condition.colorEquals.anchorMode.option.client"] = "窗口客户区",
        ["condition.colorEquals.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.colorEquals.anchorWindow.hint"] = AnchorWindowRuleHint,

        // ------------------------------------- 找图 / 找色做厚：多命中、多区域、多点比色
        // 识别算法：找图那三个动作共用一组文案。
        ["vision.findImage.algorithm.label"] = "识别算法",
        ["vision.findImage.algorithm.hint"] = MatchAlgorithmHint,
        ["vision.findImage.algorithm.option.normed"] = "归一化相关",
        ["vision.findImage.algorithm.option.correlated"] = "相关",
        ["vision.findImage.algorithm.option.difference"] = "逐像素",
        ["vision.findImage.algorithm.option.feature"] = "特征比对",
        ["vision.findImage.ignoreColor.label"] = "忽略的颜色",
        ["vision.findImage.ignoreColor.hint"] = IgnoreColourHint,
        ["vision.findImage.minFeatures.label"] = "最少特征对数",
        ["vision.findImage.minFeatures.hint"] = MinFeaturesHint,
        ["vision.findImage.orderBy.label"] = "命中排序",
        ["vision.findImage.orderBy.hint"] = MatchOrderHint,
        ["vision.findImage.orderBy.option.reading"] = "阅读顺序",
        ["vision.findImage.orderBy.option.score"] = "分数最高",
        ["vision.findImage.orderBy.option.area"] = "面积最大",
        ["vision.findImage.orderBy.option.random"] = "随机",
        ["vision.findImage.matchIndex.label"] = "第几个命中",
        ["vision.findImage.matchIndex.hint"] = MatchIndexHint,
        ["vision.findImage.return.match.label"] = HitRecordLabel,
        ["vision.findImage.return.match.hint"] = HitRecordHintImage,
        ["vision.findImage.return.every.label"] = EveryHitLabel,
        ["vision.findImage.return.every.hint"] = EveryHitHint,
        ["vision.findImage.imageSet.label"] = "认哪一张",
        ["vision.findImage.imageSet.hint"] = ImageSetHint,
        ["vision.findImage.imageSet.option.first"] = "认出的第一张",
        ["vision.findImage.imageSet.option.best"] = "最像的那一张",
        ["vision.findImage.imageSet.option.every"] = "每一张都要",

        ["vision.waitImage.algorithm.label"] = "识别算法",
        ["vision.waitImage.algorithm.hint"] = MatchAlgorithmHint,
        ["vision.waitImage.algorithm.option.normed"] = "归一化相关",
        ["vision.waitImage.algorithm.option.correlated"] = "相关",
        ["vision.waitImage.algorithm.option.difference"] = "逐像素",
        ["vision.waitImage.algorithm.option.feature"] = "特征比对",
        ["vision.waitImage.ignoreColor.label"] = "忽略的颜色",
        ["vision.waitImage.ignoreColor.hint"] = IgnoreColourHint,
        ["vision.waitImage.minFeatures.label"] = "最少特征对数",
        ["vision.waitImage.minFeatures.hint"] = MinFeaturesHint,
        ["vision.waitImage.orderBy.label"] = "命中排序",
        ["vision.waitImage.orderBy.hint"] = MatchOrderHint,
        ["vision.waitImage.orderBy.option.reading"] = "阅读顺序",
        ["vision.waitImage.orderBy.option.score"] = "分数最高",
        ["vision.waitImage.orderBy.option.area"] = "面积最大",
        ["vision.waitImage.orderBy.option.random"] = "随机",
        ["vision.waitImage.region.label"] = "搜索区域",
        ["vision.waitImage.region.hint"] = RegionHintText,
        ["vision.waitImage.matchIndex.label"] = "第几个命中",
        ["vision.waitImage.matchIndex.hint"] = MatchIndexHint,
        ["vision.waitImage.imageSet.label"] = "认哪一张",
        ["vision.waitImage.imageSet.hint"] = ImageSetHint,
        ["vision.waitImage.imageSet.option.first"] = "认出的第一张",
        ["vision.waitImage.imageSet.option.best"] = "最像的那一张",
        ["vision.waitImage.imageSet.option.every"] = "每一张都要",
        ["vision.waitImage.anchorMode.label"] = "坐标基准",
        ["vision.waitImage.anchorMode.hint"] = AnchorModeHint,
        ["vision.waitImage.anchorMode.option.screen"] = "屏幕像素",
        ["vision.waitImage.anchorMode.option.window"] = "窗口左上角",
        ["vision.waitImage.anchorMode.option.client"] = "窗口客户区",
        ["vision.waitImage.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.waitImage.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["vision.clickImage.region.label"] = "搜索区域",
        ["vision.clickImage.region.hint"] = RegionHintText,
        ["vision.clickImage.algorithm.label"] = "识别算法",
        ["vision.clickImage.algorithm.hint"] = MatchAlgorithmHint,
        ["vision.clickImage.algorithm.option.normed"] = "归一化相关",
        ["vision.clickImage.algorithm.option.correlated"] = "相关",
        ["vision.clickImage.algorithm.option.difference"] = "逐像素",
        ["vision.clickImage.algorithm.option.feature"] = "特征比对",
        ["vision.clickImage.ignoreColor.label"] = "忽略的颜色",
        ["vision.clickImage.ignoreColor.hint"] = IgnoreColourHint,
        ["vision.clickImage.minFeatures.label"] = "最少特征对数",
        ["vision.clickImage.minFeatures.hint"] = MinFeaturesHint,
        ["vision.clickImage.orderBy.label"] = "命中排序",
        ["vision.clickImage.orderBy.hint"] = MatchOrderHint,
        ["vision.clickImage.orderBy.option.reading"] = "阅读顺序",
        ["vision.clickImage.orderBy.option.score"] = "分数最高",
        ["vision.clickImage.orderBy.option.area"] = "面积最大",
        ["vision.clickImage.orderBy.option.random"] = "随机",
        ["vision.clickImage.matchIndex.label"] = "第几个命中",
        ["vision.clickImage.matchIndex.hint"] = MatchIndexHint,
        ["vision.clickImage.imageSet.label"] = "认哪一张",
        ["vision.clickImage.imageSet.hint"] = ImageSetHint,
        ["vision.clickImage.imageSet.option.first"] = "认出的第一张",
        ["vision.clickImage.imageSet.option.best"] = "最像的那一张",
        ["vision.clickImage.imageSet.option.every"] = "每一张都要",
        ["vision.clickImage.anchorMode.label"] = "坐标基准",
        ["vision.clickImage.anchorMode.hint"] = AnchorModeHint,
        ["vision.clickImage.anchorMode.option.screen"] = "屏幕像素",
        ["vision.clickImage.anchorMode.option.window"] = "窗口左上角",
        ["vision.clickImage.anchorMode.option.client"] = "窗口客户区",
        ["vision.clickImage.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.clickImage.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["vision.findColor.name"] = "查找颜色",
        ["vision.findColor.desc"] = "在屏幕的一块区域里找某个颜色。",
        ["vision.findColor.color.label"] = "颜色",
        ["vision.findColor.color.hint"] = "要找的颜色，默认 #000000。",
        ["vision.findColor.tolerance.label"] = "容差 %",
        ["vision.findColor.tolerance.hint"] = "允许的颜色偏差，0–100（百分比），默认 5。",
        ["vision.findColor.minCoverage.label"] = "至少占比 %",
        ["vision.findColor.minCoverage.hint"] =
            "搜索区域里至少有多少比例是那个颜色才算找到，0–100（百分比），默认 0，即不看占比。"
            + "血条涨到一半、按钮亮起来、仪表过了刻度这类判断用它，而不是看颜色在哪一点上；"
            + "占比按整个搜索区域算，多行区域合起来算一个。算占比要把整块区域走一遍，"
            + "没问占比的步骤不会因此变慢。",
        ["vision.findColor.matchIndex.label"] = "第几个命中",
        ["vision.findColor.matchIndex.hint"] = MatchIndexHint,
        ["vision.findColor.orderBy.label"] = "命中排序",
        ["vision.findColor.orderBy.hint"] = MatchOrderColourHint,
        ["vision.findColor.orderBy.option.reading"] = "阅读顺序",
        ["vision.findColor.orderBy.option.score"] = "分数最高",
        ["vision.findColor.orderBy.option.random"] = "随机",
        ["vision.findColor.return.match.label"] = HitRecordLabel,
        ["vision.findColor.return.match.hint"] = HitRecordHintColour,
        ["vision.findColor.return.every.label"] = EveryHitLabel,
        ["vision.findColor.return.every.hint"] = EveryHitHint,
        ["vision.findColor.region.label"] = "搜索区域",
        ["vision.findColor.region.hint"] = RegionHintText,
        ["vision.findColor.anchorMode.label"] = "坐标基准",
        ["vision.findColor.anchorMode.hint"] = AnchorModeHint,
        ["vision.findColor.anchorMode.option.screen"] = "屏幕像素",
        ["vision.findColor.anchorMode.option.window"] = "窗口左上角",
        ["vision.findColor.anchorMode.option.client"] = "窗口客户区",
        ["vision.findColor.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.findColor.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["vision.findColors.name"] = "多点找色",
        ["vision.findColors.desc"] = "找一个颜色，并要求它周围指定的位置上也各有指定的颜色。",
        ["vision.findColors.color.label"] = "基准色",
        ["vision.findColors.color.hint"] =
            "图案中间的那个颜色，默认 #000000。交回的位置就是它，其他颜色也都从它这里数。",
        ["vision.findColors.points.label"] = "周围要对的颜色",
        ["vision.findColors.points.hint"] =
            "一行一个，写成相对基准色的偏移与颜色，例如 12,-4,#FF0000，偏移可以是负数。"
            + "每一个都要在写的位置上，整个图案还必须落在搜索区域里面：贴着区域边缘、"
            + "有一半在区域外的图案不算找到。",
        ["vision.findColors.tolerance.label"] = "容差 %",
        ["vision.findColors.tolerance.hint"] = "允许的颜色偏差，0–100（百分比），默认 5。",
        ["vision.findColors.region.label"] = "搜索区域",
        ["vision.findColors.region.hint"] = RegionHintText,
        ["vision.findColors.intervalMs.label"] = LookIntervalLabel,
        ["vision.findColors.intervalMs.hint"] = LookGapHint,
        ["vision.findColors.orderBy.label"] = "命中排序",
        ["vision.findColors.orderBy.hint"] = MatchOrderColourHint,
        ["vision.findColors.orderBy.option.reading"] = "阅读顺序",
        ["vision.findColors.orderBy.option.score"] = "分数最高",
        ["vision.findColors.orderBy.option.random"] = "随机",
        ["vision.findColors.matchIndex.label"] = "第几个命中",
        ["vision.findColors.matchIndex.hint"] = MatchIndexHint,
        ["vision.findColors.return.match.label"] = HitRecordLabel,
        ["vision.findColors.return.match.hint"] = HitRecordHintColour,
        ["vision.findColors.return.every.label"] = EveryHitLabel,
        ["vision.findColors.return.every.hint"] = EveryHitHint,
        ["vision.findColors.anchorMode.label"] = "坐标基准",
        ["vision.findColors.anchorMode.hint"] = AnchorModeHint,
        ["vision.findColors.anchorMode.option.screen"] = "屏幕像素",
        ["vision.findColors.anchorMode.option.window"] = "窗口左上角",
        ["vision.findColors.anchorMode.option.client"] = "窗口客户区",
        ["vision.findColors.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.findColors.anchorWindow.hint"] = AnchorWindowRuleHint,
        ["vision.findColors.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.findColors.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.findColors.returns.label"] = "要留下的结果",
        ["vision.findColors.returns.hint"] =
            "勾上这一步要留下的结果：每一项各自成为一个变量，后面的步骤按名字取用。"
            + "一个都不勾也可以，这一步就不留下任何结果。",

        ["vision.waitStable.name"] = "等画面静止",
        ["vision.waitStable.desc"] =
            "等到画面不再动，让后面的步骤拿到一张不会再变的图，最多等到步骤设置里的「超时」；"
            + "到点这一步算失败。",
        ["vision.waitStable.region.label"] = "搜索区域",
        ["vision.waitStable.region.hint"] = RegionHintText,
        ["vision.waitStable.quietMs.label"] = "静止多久",
        ["vision.waitStable.quietMs.hint"] = "画面一直不变这么久（毫秒）之后才往下走，默认 500。",
        ["vision.waitStable.tolerance.label"] = "容差 %",
        ["vision.waitStable.tolerance.hint"] = "像素颜色差多少以内还算同一个像素，0–100（百分比），默认 5。",
        ["vision.waitStable.changedPercent.label"] = "允许变化 %",
        ["vision.waitStable.changedPercent.hint"] =
            "整块区域里最多有多少比例的像素在动还算静止，0–100（百分比），默认 0.1。"
            + "转动的圆圈、滚动的列表、走动的进度条都会超过这个数。",
        ["vision.waitStable.anchorMode.label"] = "坐标基准",
        ["vision.waitStable.anchorMode.hint"] = AnchorModeHint,
        ["vision.waitStable.anchorMode.option.screen"] = "屏幕像素",
        ["vision.waitStable.anchorMode.option.window"] = "窗口左上角",
        ["vision.waitStable.anchorMode.option.client"] = "窗口客户区",
        ["vision.waitStable.anchorWindow.label"] = WindowMatchTextLabel,
        ["vision.waitStable.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["condition.colorsMatch.name"] = "多点比色",
        ["condition.colorsMatch.desc"] = "当若干个点都显示指定的颜色时为真。",
        ["condition.colorsMatch.points.label"] = "比色点",
        ["condition.colorsMatch.points.hint"] =
            "每个点写成 x,y,#RRGGBB，用分号或换行分开。",
        ["condition.colorsMatch.tolerance.label"] = "容差 %",
        ["condition.colorsMatch.tolerance.hint"] = "允许的颜色偏差，0–100（百分比），默认 5。",
        ["condition.colorsMatch.mode.label"] = "满足方式",
        ["condition.colorsMatch.mode.hint"] = "每个点都要匹配，还是有一个匹配就算满足。默认「每个点都要匹配」。",
        ["condition.colorsMatch.mode.option.all"] = "全部匹配",
        ["condition.colorsMatch.mode.option.any"] = "任意一个匹配",
        ["condition.colorsMatch.anchorMode.label"] = "坐标基准",
        ["condition.colorsMatch.anchorMode.hint"] = AnchorModeHint,
        ["condition.colorsMatch.anchorMode.option.screen"] = "屏幕像素",
        ["condition.colorsMatch.anchorMode.option.window"] = "窗口左上角",
        ["condition.colorsMatch.anchorMode.option.client"] = "窗口客户区",
        ["condition.colorsMatch.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.colorsMatch.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["condition.colorsExist.name"] = "颜色组合存在",
        ["condition.colorsExist.desc"] = "当某个颜色在屏幕上、且它周围指定的位置上也各有指定的颜色时为真。",
        ["condition.colorsExist.color.label"] = "基准色",
        ["condition.colorsExist.color.hint"] =
            "图案中间的那个颜色，默认 #000000。其他颜色都从它这里数。",
        ["condition.colorsExist.points.label"] = "周围要对的颜色",
        ["condition.colorsExist.points.hint"] =
            "一行一个，写成相对基准色的偏移与颜色，例如 12,-4,#FF0000，偏移可以是负数。"
            + "每一个都要在写的位置上，整个图案还必须落在搜索区域里面。",
        ["condition.colorsExist.tolerance.label"] = "容差 %",
        ["condition.colorsExist.tolerance.hint"] = "允许的颜色偏差，0–100（百分比），默认 5。",
        ["condition.colorsExist.region.label"] = "搜索区域",
        ["condition.colorsExist.region.hint"] = RegionHintText,
        ["condition.colorsExist.intervalMs.label"] = LookIntervalLabel,
        ["condition.colorsExist.intervalMs.hint"] = LookGapHint,
        ["condition.colorsExist.anchorMode.label"] = "坐标基准",
        ["condition.colorsExist.anchorMode.hint"] = AnchorModeHint,
        ["condition.colorsExist.anchorMode.option.screen"] = "屏幕像素",
        ["condition.colorsExist.anchorMode.option.window"] = "窗口左上角",
        ["condition.colorsExist.anchorMode.option.client"] = "窗口客户区",
        ["condition.colorsExist.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.colorsExist.anchorWindow.hint"] = AnchorWindowRuleHint,
        ["condition.colorsExist.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.colorsExist.frameMaxAgeMs.hint"] = FrameMaxAgeHint,

        ["condition.colorCoverage.name"] = "颜色占比达到",
        ["condition.colorCoverage.desc"] = "当屏幕的一块区域里，某个颜色的占比达到指定比例时为真。",
        ["condition.colorCoverage.color.label"] = "颜色",
        ["condition.colorCoverage.color.hint"] = "要数的颜色，默认 #000000。",
        ["condition.colorCoverage.minCoverage.label"] = "至少占比 %",
        ["condition.colorCoverage.minCoverage.hint"] =
            "搜索区域里至少要有多少比例是这个颜色才算成立，0–100（百分比），默认 0，即只要区域里出现"
            + "这个颜色就算成立。血条涨到一半、按钮亮起来、仪表过了刻度这类判断给它一个数：占比按整个"
            + "搜索区域算，多行区域合起来算一个。算占比要把整块区域走一遍，所以它是拿来等的"
            + "（放进等待直到、当条件为真期间重复里），不是塞进紧凑循环里反复问的。",
        ["condition.colorCoverage.tolerance.label"] = "容差 %",
        ["condition.colorCoverage.tolerance.hint"] = "允许的颜色偏差，0–100（百分比），默认 5。",
        ["condition.colorCoverage.region.label"] = "搜索区域",
        ["condition.colorCoverage.region.hint"] = RegionHintText,
        ["condition.colorCoverage.intervalMs.label"] = LookIntervalLabel,
        ["condition.colorCoverage.intervalMs.hint"] = LookGapHint,
        ["condition.colorCoverage.anchorMode.label"] = "坐标基准",
        ["condition.colorCoverage.anchorMode.hint"] = AnchorModeHint,
        ["condition.colorCoverage.anchorMode.option.screen"] = "屏幕像素",
        ["condition.colorCoverage.anchorMode.option.window"] = "窗口左上角",
        ["condition.colorCoverage.anchorMode.option.client"] = "窗口客户区",
        ["condition.colorCoverage.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.colorCoverage.anchorWindow.hint"] = AnchorWindowRuleHint,
        ["condition.colorCoverage.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.colorCoverage.frameMaxAgeMs.hint"] = FrameMaxAgeHint,

        // ------------------------------------------------------------------- vision
        ["vision.capture.name"] = "截屏",
        ["vision.capture.desc"] = "把屏幕的一块区域截取到图像变量里；四个数都不填就是整块屏幕。",
        ["vision.capture.x.label"] = "左上角 X",
        ["vision.capture.x.hint"] = "要截取的那块屏幕的左边界距屏幕左边多少像素；坐标基准选"
            + "窗口时，从窗口自己的左边算起。留空就从那条边开始，四个数都留空则读整块屏幕。"
            + "用「框选区域」拖一次会把四个数一起填好。",
        ["vision.capture.y.label"] = "左上角 Y",
        ["vision.capture.y.hint"] = "要截取的那块屏幕的上边界距屏幕上边多少像素；坐标基准选"
            + "窗口时，从窗口自己的上边算起。留空就从那条边开始。",
        ["vision.capture.width.label"] = "向右的宽",
        ["vision.capture.width.hint"] = "要截取的那块屏幕有多宽，单位像素，至少 1；从左上角向"
            + "右量，留空就一直量到屏幕右边缘。",
        ["vision.capture.height.label"] = "向下的高",
        ["vision.capture.height.hint"] = "要截取的那块屏幕有多高，单位像素，至少 1；从左上角向"
            + "下量，留空就一直量到屏幕下边缘。",
        ["vision.capture.useRecord.label"] = RecordBoxSwitchLabel,
        ["vision.capture.useRecord.hint"] = RecordBoxSwitchHint,
        ["vision.capture.record.label"] = RecordFieldLabel,
        ["vision.capture.record.hint"] = RecordBoxFieldHint,

        ["vision.captureWindow.name"] = "截取窗口",
        ["vision.captureWindow.desc"] = "把整个窗口连着标题栏一起截取到图像变量里。",
        ["vision.captureWindow.window.label"] = WindowMatchTextLabel,
        ["vision.captureWindow.window.hint"] = WindowTitleHint,
        ["vision.captureWindow.path.label"] = "存到文件",
        ["vision.captureWindow.path.hint"] = CaptureFileHint,

        ["vision.capture.path.label"] = "存到文件",
        ["vision.capture.path.hint"] = CaptureFileHint,

        ["vision.findImage.name"] = "查找图片",
        ["vision.findImage.desc"] = "在屏幕上寻找一张参考图片。",
        ["vision.findImage.image.label"] = "图片文件",
        ["vision.findImage.image.hint"] = "参考图片：文件路径，或截图动作保存的变量（如 $shot）。",
        ["vision.findImage.confidence.label"] = "相似度 %",
        ["vision.findImage.confidence.hint"] = "要求的相似度下限，写成 0 到 100 的百分数，默认 90；屏幕上的样子画得略有不同时调低。",
        ["vision.findImage.region.label"] = "搜索区域",
        ["vision.findImage.region.hint"] = RegionHintText,

        ["vision.waitImage.name"] = "等待图片",
        ["vision.waitImage.desc"] =
            "等待参考图片在屏幕上出现，最多等到步骤设置里的「超时」；到点这一步算失败。",
        ["vision.waitImage.image.label"] = "图片文件",
        ["vision.waitImage.image.hint"] = "要等待的参考图片：文件路径，或截图动作保存的变量。",
        ["vision.waitImage.confidence.label"] = "相似度 %",
        ["vision.waitImage.confidence.hint"] = "要求的相似度下限，写成 0 到 100 的百分数，默认 90；屏幕上的样子画得略有不同时调低。",

        ["vision.clickImage.name"] = "点击图片",
        ["vision.clickImage.desc"] =
            "在屏幕上找到参考图片并点击它，最多等到步骤设置里的「超时」；到点这一步算失败。",
        ["vision.clickImage.image.label"] = "图片文件",
        ["vision.clickImage.image.hint"] = "图片文件，也可以填前面步骤存下的图片变量。",
        ["vision.clickImage.confidence.label"] = "相似度 %",
        ["vision.clickImage.confidence.hint"] = "要求的相似度下限，写成 0 到 100 的百分数，默认 90；屏幕上的样子画得略有不同时调低。",
        ["vision.clickImage.offsetX.label"] = "横向偏移",
        ["vision.clickImage.offsetX.hint"] = "点击位置在匹配中心基础上水平偏移的像素数，默认 0，即点在匹配中心。",
        ["vision.clickImage.offsetY.label"] = "纵向偏移",
        ["vision.clickImage.offsetY.hint"] = "点击位置在匹配中心基础上垂直偏移的像素数，正数向下，默认 0，即点在匹配中心。",
        ["vision.clickImage.variance.label"] = "落点随机范围",
        ["vision.clickImage.variance.hint"] =
            "落点与匹配中心最多相差多少个像素，默认 0，即正好点在那个点上。填了数就在这个范围内"
            + "随机取一点，连着点两次不会落在同一个像素上。宽按钮上用它避免看起来像机器点的；"
            + "数字要小到整个范围仍落在被点的东西里面。",
        ["vision.clickImage.recheckMs.label"] = "落手前再确认",
        ["vision.clickImage.recheckMs.hint"] =
            "点之前先等这么多毫秒再看一次，单位毫秒，默认 0，即一认出就点。填了数就等这么久再"
            + "看一眼，这次仍认得才点；这次认不得按没找到处理。用于按钮一闪而过、或者画面其实"
            + "已经翻过去的情况。第二次查看只查一次、不再等，所以画面已经变过就直接算没找到。",
        ["vision.clickImage.button.label"] = "按键",
        ["vision.clickImage.button.hint"] = MouseButtonHint,

        ["vision.getPixel.name"] = "取色",
        ["vision.getPixel.desc"] = "读取某个像素的颜色。",
        ["vision.getPixel.x.label"] = "X",
        ["vision.getPixel.x.hint"] = "取样点的横坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["vision.getPixel.y.label"] = "Y",
        ["vision.getPixel.y.hint"] = "取样点的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["vision.getPixel.useRecord.label"] = RecordPlaceSwitchLabel,
        ["vision.getPixel.useRecord.hint"] = RecordPlaceSwitchHint,
        ["vision.getPixel.record.label"] = RecordFieldLabel,
        ["vision.getPixel.record.hint"] = RecordPlaceFieldHint,
        ["vision.getPixel.asHex.label"] = "存为十六进制",
        ["vision.getPixel.asHex.hint"] = "存成 #RRGGBB，而不是原始通道值。默认勾选。",

        ["vision.waitColor.name"] = "等待颜色",
        ["vision.waitColor.desc"] =
            "等待某个像素变成指定颜色，最多等到步骤设置里的「超时」；到点这一步算失败。",
        ["vision.waitColor.x.label"] = "X",
        ["vision.waitColor.x.hint"] = "监视点的横坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["vision.waitColor.y.label"] = "Y",
        ["vision.waitColor.y.hint"] = "监视点的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["vision.waitColor.useRecord.label"] = RecordPlaceSwitchLabel,
        ["vision.waitColor.useRecord.hint"] = RecordPlaceSwitchHint,
        ["vision.waitColor.record.label"] = RecordFieldLabel,
        ["vision.waitColor.record.hint"] = RecordPlaceFieldHint,
        ["vision.waitColor.color.label"] = "颜色",
        ["vision.waitColor.color.hint"] = "像素需要显示的颜色，按 #RRGGBB 写，默认 #000000。用放大镜取色时，取到的那一点同时写进 X、Y。",
        ["vision.waitColor.tolerance.label"] = "容差 %",
        ["vision.waitColor.tolerance.hint"] = "允许的颜色偏差，0–100（百分比），默认 5。",

        // ---------------------------------------------------------------------- ocr
        ["ocr.recognize.name"] = "识别文字",
        ["ocr.recognize.desc"] = "识别屏幕某块区域里的文字；四个数都不填就是整块屏幕。",
        ["ocr.recognize.x.label"] = "左上角 X",
        ["ocr.recognize.x.hint"] = "要读的那块屏幕的左边界距屏幕左边多少像素；坐标基准选窗口"
            + "时，从窗口自己的左边算起。留空就从那条边开始，四个数都留空则读整块屏幕。"
            + "用「框选区域」拖一次会把四个数一起填好。",
        ["ocr.recognize.y.label"] = "左上角 Y",
        ["ocr.recognize.y.hint"] = "要读的那块屏幕的上边界距屏幕上边多少像素；坐标基准选窗口"
            + "时，从窗口自己的上边算起。留空就从那条边开始。",
        ["ocr.recognize.width.label"] = "向右的宽",
        ["ocr.recognize.width.hint"] = "要读的那块屏幕有多宽，单位像素，至少 1；从左上角向右"
            + "量，留空就一直量到屏幕右边缘。",
        ["ocr.recognize.height.label"] = "向下的高",
        ["ocr.recognize.height.hint"] = "要读的那块屏幕有多高，单位像素，至少 1；从左上角向下"
            + "量，留空就一直量到屏幕下边缘。",
        ["ocr.recognize.useRecord.label"] = RecordBoxSwitchLabel,
        ["ocr.recognize.useRecord.hint"] = RecordBoxSwitchHint,
        ["ocr.recognize.record.label"] = RecordFieldLabel,
        ["ocr.recognize.record.hint"] = RecordBoxFieldHint,
        ["ocr.recognize.content.label"] = "读取内容",
        ["ocr.recognize.content.hint"] = ContentHint,
        ["ocr.recognize.contentPattern.label"] = ContentPatternLabel,
        ["ocr.recognize.contentPattern.hint"] = ContentPatternHint,
        ["ocr.recognize.table.label"] = "按表格读",
        ["ocr.recognize.table.hint"] = "按屏幕上的位置分行分列：$变量是表格（每行一项、每项是一条"
            + "记录，键是格子的列号 A、B、C），$变量.text 是同样的内容按行拼成的文本（行与行之间换行、"
            + "单元格之间制表符）。没有表格线也能用，靠的是文字的位置。",
        ["ocr.recognize.preprocess.label"] = "画面预处理",
        ["ocr.recognize.preprocess.hint"] = "识别前的画面处理：灰度化去掉颜色；黑白在灰度化后"
            + "再拉开明暗对比（分界值取整幅图的平均亮度，因此深色与浅色主题均可使用）；"
            + "放大两倍用于较小的文字；最后一项同时执行前两项。"
            + "文字清晰且大小合适时无需处理，只会略增加耗时。",
        ["ocr.recognize.colorFilter.label"] = "文字颜色",
        ["ocr.recognize.colorFilter.hint"] = ColourFilterHint,
        ["ocr.recognize.colorTolerance.label"] = "颜色容差 %",
        ["ocr.recognize.colorTolerance.hint"] = ColourToleranceHint,
        ["ocr.recognize.fixText.label"] = "纠错表",
        ["ocr.recognize.fixText.hint"] = FixTextHint,

        ["ocr.findText.name"] = "查找文字",
        ["ocr.findText.desc"] = "在屏幕上搜索指定文字。",
        ["ocr.findText.text.label"] = "要查找的文字",
        ["ocr.findText.text.hint"] = "要搜索的文字。按一个值读取，变量或表达式都可以写成它；怎么比由下面的「匹配方式」决定。",
        ["ocr.findText.region.label"] = "搜索区域",
        ["ocr.findText.region.hint"] = RegionHintText,
        ["ocr.findText.matchMode.label"] = "匹配方式",
        ["ocr.findText.matchMode.hint"] = TextMatchHint,
        ["ocr.findText.expected.label"] = "整段须匹配",
        ["ocr.findText.expected.hint"] = ExpectedHint,
        ["ocr.findText.return.match.label"] = HitRecordLabel,
        ["ocr.findText.return.match.hint"] = HitRecordHintText,
        ["ocr.findText.return.every.label"] = EveryHitLabel,
        ["ocr.findText.return.every.hint"] = EveryHitHint,
        ["ocr.findText.minScore.label"] = "最低识别分",
        ["ocr.findText.minScore.hint"] = MinScoreHint,
        ["ocr.findText.orderBy.label"] = "命中排序",
        ["ocr.findText.orderBy.hint"] = MatchOrderHint,
        ["ocr.findText.orderBy.option.reading"] = "阅读顺序",
        ["ocr.findText.orderBy.option.score"] = "分数最高",
        ["ocr.findText.orderBy.option.area"] = "面积最大",
        ["ocr.findText.orderBy.option.random"] = "随机",
        ["ocr.findText.content.label"] = "读取内容",
        ["ocr.findText.content.hint"] = ContentHint,
        ["ocr.findText.contentPattern.label"] = ContentPatternLabel,
        ["ocr.findText.contentPattern.hint"] = ContentPatternHint,
        ["ocr.findText.preprocess.label"] = "画面预处理",
        ["ocr.findText.preprocess.hint"] = "识别前的画面处理：灰度 / 黑白 / 放大两倍 / "
            + "放大并黑白。黑白的分界取整幅图的平均亮度；放大后读到的位置会按比例换算回"
            + "屏幕像素，因此找到的位置仍可直接点击。",
        ["ocr.findText.colorFilter.label"] = "文字颜色",
        ["ocr.findText.colorFilter.hint"] = ColourFilterHint,
        ["ocr.findText.colorTolerance.label"] = "颜色容差 %",
        ["ocr.findText.colorTolerance.hint"] = ColourToleranceHint,
        ["ocr.findText.fixText.label"] = "纠错表",
        ["ocr.findText.fixText.hint"] = FixTextHint,

        ["ocr.clickText.name"] = "点击文字",
        ["ocr.clickText.desc"] =
            "在屏幕上找到文字并点击它，最多等到步骤设置里的「超时」；到点这一步算失败。",
        ["ocr.clickText.text.label"] = "要查找的文字",
        ["ocr.clickText.text.hint"] = "要查找的文字，例如 Save，默认空着要自己填。整段按一个值读，所以变量或算式都能放在这里。",
        ["ocr.clickText.region.label"] = "搜索区域",
        ["ocr.clickText.region.hint"] = RegionHintText,
        ["ocr.clickText.anchorMode.label"] = "坐标基准",
        ["ocr.clickText.anchorMode.hint"] = AnchorModeHint,
        ["ocr.clickText.anchorMode.option.screen"] = "屏幕像素",
        ["ocr.clickText.anchorMode.option.window"] = "窗口左上角",
        ["ocr.clickText.anchorMode.option.client"] = "窗口客户区",
        ["ocr.clickText.anchorWindow.label"] = WindowMatchTextLabel,
        ["ocr.clickText.anchorWindow.hint"] = AnchorWindowRuleHint,
        ["ocr.clickText.matchMode.label"] = "匹配方式",
        ["ocr.clickText.matchMode.hint"] = TextMatchHint,
        ["ocr.clickText.expected.label"] = "整段须匹配",
        ["ocr.clickText.expected.hint"] = ExpectedHint,
        ["ocr.clickText.offsetX.label"] = "横向偏移",
        ["ocr.clickText.offsetX.hint"] = "点击位置在匹配中心基础上水平偏移的像素数，默认 0，即点在匹配中心。",
        ["ocr.clickText.offsetY.label"] = "纵向偏移",
        ["ocr.clickText.offsetY.hint"] = "点击位置在匹配中心基础上垂直偏移的像素数，正数向下，默认 0，即点在匹配中心。",
        ["ocr.clickText.variance.label"] = "落点随机范围",
        ["ocr.clickText.variance.hint"] =
            "落点与读到的文字中心最多相差多少个像素，默认 0，即正好点在那个点上。填了数就在"
            + "这个范围内随机取一点，连着点两次不会落在同一个像素上。",
        ["ocr.clickText.recheckMs.label"] = "落手前再确认",
        ["ocr.clickText.recheckMs.hint"] =
            "点之前先等这么多毫秒再看一次，单位毫秒，默认 0，即一认出就点。填了数就等这么久再"
            + "看一眼，这次仍读得到才点；这次读不到按没找到处理。用于那一行文字已经被替掉的情"
            + "况。第二次查看只查一次、不再等，所以文字已经没了就直接算没找到。",
        ["ocr.clickText.minScore.label"] = "最低识别分",
        ["ocr.clickText.minScore.hint"] = MinScoreHint,
        ["ocr.clickText.orderBy.label"] = "命中排序",
        ["ocr.clickText.orderBy.hint"] = MatchOrderHint,
        ["ocr.clickText.orderBy.option.reading"] = "阅读顺序",
        ["ocr.clickText.orderBy.option.score"] = "分数最高",
        ["ocr.clickText.orderBy.option.area"] = "面积最大",
        ["ocr.clickText.orderBy.option.random"] = "随机",
        ["ocr.clickText.content.label"] = "读取内容",
        ["ocr.clickText.content.hint"] = ContentHint,
        ["ocr.clickText.contentPattern.label"] = ContentPatternLabel,
        ["ocr.clickText.contentPattern.hint"] = ContentPatternHint,
        ["ocr.clickText.preprocess.label"] = "画面预处理",
        ["ocr.clickText.preprocess.hint"] = "识别前的画面处理：灰度 / 黑白 / 放大两倍 / "
            + "放大并黑白。黑白的分界取整幅图的平均亮度。",
        ["ocr.clickText.colorFilter.label"] = "文字颜色",
        ["ocr.clickText.colorFilter.hint"] = ColourFilterHint,
        ["ocr.clickText.colorTolerance.label"] = "颜色容差 %",
        ["ocr.clickText.colorTolerance.hint"] = ColourToleranceHint,
        ["ocr.clickText.fixText.label"] = "纠错表",
        ["ocr.clickText.fixText.hint"] = FixTextHint,
        ["ocr.clickText.button.label"] = "按键",
        ["ocr.clickText.button.hint"] = MouseButtonHint,

        // ---------------------------------------------------------------------- uia
        ["uia.exists.name"] = "元素是否存在",
        ["uia.exists.desc"] = "判断某个 UI Automation 元素是否存在。",
        ["uia.exists.window.label"] = WindowMatchTextLabel,
        ["uia.exists.selector.label"] = "选择器",
        ["uia.exists.selector.hint"] = "元素选择器，例如 Button[name='Save']。" + SelectorPickHint,
        ["uia.exists.matchIndex.label"] = "第几个命中",
        ["uia.exists.matchIndex.hint"] = "选择器可能匹配到多个元素，这里填要的是第几个。" + MatchIndexHint,
        ["uia.exists.timeoutMs.label"] = "超时",
        ["uia.exists.timeoutMs.hint"] = "等这个元素出现多久，单位是毫秒，默认 0：0 表示只检查一次并立即返回。",

        ["uia.find.name"] = "查找元素",
        ["uia.find.desc"] = "查找一个 UI Automation 元素，并记住它在屏幕上的位置。",
        ["uia.find.window.label"] = WindowMatchTextLabel,
        ["uia.find.selector.label"] = "选择器",
        ["uia.find.selector.hint"] = "元素选择器，例如 Button[name='Save']。" + SelectorPickHint,
        ["uia.find.matchIndex.label"] = "第几个命中",
        ["uia.find.matchIndex.hint"] = MatchIndexHint,
        ["uia.find.return.match.label"] = HitRecordLabel,
        ["uia.find.return.match.hint"] = HitRecordHintElement,
        ["uia.find.return.every.label"] = EveryHitLabel,
        ["uia.find.return.every.hint"] = EveryHitHint,

        ["uia.waitElement.name"] = "等待元素",
        ["uia.waitElement.desc"] = "等待某个 UI Automation 元素出现。",
        ["uia.waitElement.window.label"] = WindowMatchTextLabel,
        ["uia.waitElement.selector.label"] = "选择器",
        ["uia.waitElement.selector.hint"] = "元素选择器，例如 Button[name='Save']。" + SelectorPickHint,
        ["uia.waitElement.matchIndex.label"] = "第几个命中",
        ["uia.waitElement.matchIndex.hint"] =
            "选择器可能匹配到多个元素，这里填要等的是第几个。" + MatchIndexHint,
        ["uia.waitElement.timeoutMs.label"] = "超时",
        ["uia.waitElement.timeoutMs.hint"] = "最多等多久，单位是毫秒，默认 10000；到这个时间元素还没出现就放弃。",
        ["uia.waitElement.pollMs.label"] = "轮询间隔",
        ["uia.waitElement.pollMs.hint"] = "两次检查之间等多久，单位是毫秒，默认 200。",

        ["uia.click.name"] = "点击元素",
        ["uia.click.desc"] = "点击一个 UI Automation 元素。",
        ["uia.click.window.label"] = WindowMatchTextLabel,
        ["uia.click.selector.label"] = "选择器",
        ["uia.click.selector.hint"] = "元素选择器，例如 Button[name='Save']。" + SelectorPickHint,
        ["uia.click.matchIndex.label"] = "第几个命中",
        ["uia.click.matchIndex.hint"] = "选择器可能匹配到多个元素，这里填要点击的是第几个。" + MatchIndexHint,
        ["uia.click.timeoutMs.label"] = "超时",
        ["uia.click.timeoutMs.hint"] = "点击前等这个元素出现多久，单位是毫秒，默认 5000。",
        ["uia.click.button.label"] = "按键",
        ["uia.click.button.hint"] = MouseButtonHint,

        ["uia.invoke.name"] = "按下元素",
        ["uia.invoke.desc"] = "用元素自己的方式完成它的默认动作，不移动鼠标。",
        ["uia.invoke.window.label"] = WindowMatchTextLabel,
        ["uia.invoke.selector.label"] = "选择器",
        ["uia.invoke.selector.hint"] = "元素选择器，例如 Button[name='Save']。" + SelectorPickHint,
        ["uia.invoke.matchIndex.label"] = "第几个命中",
        ["uia.invoke.matchIndex.hint"] = "选择器可能匹配到多个元素，这里填要按下的是第几个。" + MatchIndexHint,
        ["uia.invoke.timeoutMs.label"] = "超时",
        ["uia.invoke.timeoutMs.hint"] = "按下前等这个元素出现多久，单位是毫秒，默认 5000。",

        ["uia.setText.name"] = "设置元素文本",
        ["uia.setText.desc"] = "向 UI Automation 元素输入文本。",
        ["uia.setText.window.label"] = WindowMatchTextLabel,
        ["uia.setText.selector.label"] = "选择器",
        ["uia.setText.selector.hint"] = "元素选择器，例如 Button[name='Save']。" + SelectorPickHint,
        ["uia.setText.matchIndex.label"] = "第几个命中",
        ["uia.setText.matchIndex.hint"] = "选择器可能匹配到多个元素，这里填要写入的是第几个。" + MatchIndexHint,
        ["uia.setText.text.label"] = "文本",
        ["uia.setText.text.hint"] = "写进这个元素的文本，可以填变量或算式。",
        ["uia.setText.clearFirst.label"] = "先清空",
        ["uia.setText.clearFirst.hint"] = "写入前先清空元素里已有的内容，默认开；要接着原有内容往下写时关掉它。",

        ["uia.read.name"] = "读取元素",
        ["uia.read.desc"] = "读取一个 UI Automation 元素能说出的全部内容。",
        ["uia.read.window.label"] = WindowMatchTextLabel,
        ["uia.read.selector.label"] = "选择器",
        ["uia.read.selector.hint"] = "元素选择器，例如 Button[name='Save']。" + SelectorPickHint,
        ["uia.read.matchIndex.label"] = "第几个命中",
        ["uia.read.matchIndex.hint"] = "选择器可能匹配到多个元素，这里填要读取的是第几个。" + MatchIndexHint,
        ["uia.read.return.element.label"] = "元素",
        ["uia.read.return.element.hint"] = "这个元素自己说出的内容，一条记录：enabled 与 visible、"
            + "value、name、automationId、controlType，然后是 x、y（它的中心），以及 left、top、"
            + "width、height 与 box。"
            + "取其中一项用 field($变量, \"value\")。value 是元素持有的内容，输入框里的文字、"
            + "按钮上的标题，元素本身不持有内容时为空。",

        ["uia.select.name"] = "选择列表项",
        ["uia.select.desc"] = "在下拉框、列表或选项卡里选中一项。",
        ["uia.select.window.label"] = WindowMatchTextLabel,
        ["uia.select.selector.label"] = "选择器",
        ["uia.select.selector.hint"] = "下拉框、列表或选项卡的选择器。" + SelectorPickHint,
        ["uia.select.matchIndex.label"] = "第几个命中",
        ["uia.select.matchIndex.hint"] = "选择器可能匹配到多个控件，这里填要操作的是第几个。" + MatchIndexHint,
        ["uia.select.item.label"] = "选项文字",
        ["uia.select.item.hint"] = "要选中的那一项显示的文字，也可以由 $变量 提供（比如刚从别处读回来的值）。留空就填下面的序号。",
        ["uia.select.itemIndex.label"] = "选项序号",
        ["uia.select.itemIndex.hint"] = "要选第几项，从 1 开始数；默认 0，表示用上面写的文字来选。",

        ["uia.check.name"] = "勾选框",
        ["uia.check.desc"] = "把勾选框、开关或单选框打开或关掉。",
        ["uia.check.window.label"] = WindowMatchTextLabel,
        ["uia.check.selector.label"] = "选择器",
        ["uia.check.selector.hint"] = "勾选框、开关或单选框的选择器。" + SelectorPickHint,
        ["uia.check.matchIndex.label"] = "第几个命中",
        ["uia.check.matchIndex.hint"] = "选择器可能匹配到多个控件，这里填要操作的是第几个。" + MatchIndexHint,
        ["uia.check.state.label"] = "状态",
        ["uia.check.state.hint"] = "要把它怎么样，默认打开。“反过来”是不管现在是什么状态都切成另一种，宏不知道"
            + "开始时是什么状态时用这个。",
        ["uia.check.state.option.on"] = "打开",
        ["uia.check.state.option.off"] = "关闭",
        ["uia.check.state.option.toggle"] = "反过来",

        ["uia.expand.name"] = "展开/收起",
        ["uia.expand.desc"] = "展开或收起树节点、菜单、折叠面板。",
        ["uia.expand.window.label"] = WindowMatchTextLabel,
        ["uia.expand.selector.label"] = "选择器",
        ["uia.expand.selector.hint"] = "要展开或收起的节点选择器。" + SelectorPickHint,
        ["uia.expand.matchIndex.label"] = "第几个命中",
        ["uia.expand.matchIndex.hint"] = "选择器可能匹配到多个控件，这里填要操作的是第几个。" + MatchIndexHint,
        ["uia.expand.state.label"] = "状态",
        ["uia.expand.state.hint"] = "要展开、收起，还是不管现在是哪种都切成另一种，默认展开。",
        ["uia.expand.state.option.expand"] = "展开",
        ["uia.expand.state.option.collapse"] = "收起",
        ["uia.expand.state.option.toggle"] = "反过来",

        ["uia.scrollIntoView.name"] = "滚动到可见",
        ["uia.scrollIntoView.desc"] = "把列表或面板里的某个元素滚动到看得见的位置。",
        ["uia.scrollIntoView.window.label"] = WindowMatchTextLabel,
        ["uia.scrollIntoView.selector.label"] = "选择器",
        ["uia.scrollIntoView.selector.hint"] = "要滚动到可见的元素选择器。" + SelectorPickHint,
        ["uia.scrollIntoView.matchIndex.label"] = "第几个命中",
        ["uia.scrollIntoView.matchIndex.hint"] = "选择器可能匹配到多个控件，这里填要操作的是第几个。" + MatchIndexHint,

        ["uia.readTable.name"] = "读取表格",
        ["uia.readTable.desc"] = "把表格或网格读成一个按行存放的列表。",
        ["uia.readTable.window.label"] = WindowMatchTextLabel,
        ["uia.readTable.selector.label"] = "选择器",
        ["uia.readTable.selector.hint"] = "表格或网格的选择器。" + SelectorPickHint,
        ["uia.readTable.matchIndex.label"] = "第几个命中",
        ["uia.readTable.matchIndex.hint"] = "选择器可能匹配到多个控件，这里填要读取的是第几个。" + MatchIndexHint,
        ["uia.readTable.maxRows.label"] = "读取行数",
        ["uia.readTable.maxRows.hint"] = "最多读取前多少行数据，范围为 1 到 10000，默认 100。整行都是空的会被跳过："
            + "放列标题的那一行、有些表格底部留着用来输入新记录的空白行，都不是数据。",

        ["uia.focusWindow.name"] = "窗口置前",
        ["uia.focusWindow.desc"] = "把某个窗口切换到最前面。",
        ["uia.focusWindow.window.label"] = WindowMatchTextLabel,
        ["uia.focusWindow.window.hint"] = "窗口标题，或保存标题的 $变量。可以用“拾取窗口”从当前打开的窗口里选一个。",

        // ---------------------------------------------------------------- browser
        ["browser.open.name"] = "打开浏览器",
        ["browser.open.desc"] = "启动浏览器并打开一个网址。",
        ["browser.open.browser.label"] = "浏览器",
        ["browser.open.browser.hint"] = "驱动哪个浏览器，默认 Edge。Edge 是 Windows 自带的，不用装任何东西；"
            + "Chrome 和 Playwright 自己下载的那几个内核，机器上没有时会给出安装命令。",
        ["browser.open.browser.option.edge"] = "Edge（Windows 自带）",
        ["browser.open.browser.option.chrome"] = "Chrome",
        ["browser.open.browser.option.chromium"] = "Chromium",
        ["browser.open.browser.option.firefox"] = "Firefox",
        ["browser.open.browser.option.webkit"] = "WebKit",
        ["browser.open.url.label"] = "网址",
        ["browser.open.url.hint"] = "要打开的页面。默认留空，表示只开浏览器，不打开页面。按一个值读取，变量或表达式都可以写成它。",
        ["browser.open.headless.label"] = "不显示窗口",
        ["browser.open.headless.hint"] = "不显示浏览器窗口。默认不勾选；勾选后速度更快，但运行过程中看不到页面。",
        ["browser.open.profile.label"] = "档案",
        ["browser.open.profile.hint"] = "给这个浏览器自己的登录状态起一个名字，以后用这个名字打开的就是"
            + "已经登录过的那一份：登录状态与 Cookie 都留在里面。默认留空，表示每次都是全新的浏览器。名字是"
            + "程序旁边 browsers 文件夹里的一格（写不进去时改用 %LOCALAPPDATA%\\WhaleGenie\\browsers，"
            + "见「程序自己的文件放哪儿」），其中的 $变量 会替换成它的值。同一时刻只开一份浏览器，所以"
            + "这一步写了别的档案名会先关掉前一份再打开新的。",

        ["browser.goTo.name"] = "转到网址",
        ["browser.goTo.desc"] = "让浏览器当前页面转到另一个网址。",
        ["browser.goTo.url.label"] = "网址",
        ["browser.goTo.url.hint"] = "要让当前页面转到的网址。按一个值读取，变量或表达式都可以写成它。"
            + "等页面加载完要多久由这一步设置里的「超时」说，默认 15000 毫秒。",

        ["browser.click.name"] = "点击元素",
        ["browser.click.desc"] = "点击选择器指定的元素。",
        ["browser.click.target.label"] = "选择器",
        ["browser.click.target.hint"] = PageTargetHint + "元素在哪，写法是 Playwright 的选择器。"
            + "选择器像 CSS，也能按文字找：「text=登录」认的是显示这段文字的元素。",
        ["browser.click.how.label"] = "怎么点",
        ["browser.click.how.hint"] = "怎么点这个元素。左键单击是默认，接下来要去别处或按下按钮时用它；"
            + "左键双击用在点一下就变成输入框的地方，例如表格里的一行；右键单击弹出页面自己的菜单，"
            + "菜单里的东西只有它能打开。",
        ["browser.click.how.option.single"] = "左键单击",
        ["browser.click.how.option.double"] = "左键双击",
        ["browser.click.how.option.right"] = "右键单击",
        ["browser.click.force.label"] = "硬点",
        ["browser.click.force.hint"] = "元素被挡住、被禁用或还在移动时也点下去。默认不勾选，也建议不勾："
            + "页面挡住不让点的按钮，多半是这一步点下去也达不到作者想要的结果。页面用一层看不见的东西盖住"
            + "自己的按钮时，才需要勾上它。",
        ["browser.click.matchIndex.label"] = "第几个命中",
        ["browser.click.narrow.label"] = "限定",
        ["browser.click.narrow.hint"] = "要点击的那个元素" + NarrowHint,
        ["browser.click.matchIndex.hint"] = PageMatchIndexHint("要点击的"),

        ["browser.fill.name"] = "填写",
        ["browser.fill.desc"] = "往选择器指定的元素里输入文本。",
        ["browser.fill.target.label"] = "选择器",
        ["browser.fill.target.hint"] = PageTargetHint + "要输入到哪个元素，写法与「点击元素」相同。",
        ["browser.fill.text.label"] = "文本",
        ["browser.fill.text.hint"] = "要输入的内容。按一个值读取，变量或表达式都可以写成它，所以可以把别处读到的值输入进来。",
        ["browser.fill.how.label"] = "怎么填",
        ["browser.fill.how.hint"] = "文字怎么进到这个元素里。「一次填好」更快，普通输入框用它就够了；"
            + "「逐键输入」是一个键一个键地发，页面自己要盯着输入过程时才用它，例如边打边给出候选的搜索框、"
            + "离开每个格子就检查的表单。",
        ["browser.fill.how.option.fill"] = "一次填好",
        ["browser.fill.how.option.type"] = "逐键输入",
        ["browser.fill.intervalMs.label"] = "按键间隔",
        ["browser.fill.intervalMs.hint"] = "两个键之间停多久，0 到 10000 毫秒。默认 0，也就是不停，"
            + "按页面能跟上的最快速度发；只有在「逐键输入」时才读它，页面嫌来得太快而漏掉按键时可以调大。",
        ["browser.fill.matchIndex.label"] = "第几个命中",
        ["browser.fill.narrow.label"] = "限定",
        ["browser.fill.narrow.hint"] = "要填写的那个元素" + NarrowHint,
        ["browser.fill.matchIndex.hint"] = PageMatchIndexHint("要填写的"),

        ["browser.readText.name"] = "读取文本",
        ["browser.readText.desc"] = "读取某个元素的一部分、或整个页面的文本。",
        ["browser.readText.target.label"] = "选择器",
        ["browser.readText.target.hint"] = PageTargetHint + "要读取哪个元素，写法与「点击元素」相同。"
            + "一行都不写，也就是默认，读的是整个页面的文本。",
        ["browser.readText.what.label"] = "读什么",
        ["browser.readText.what.hint"] = "读元素的哪一部分。「显示的文字」是页面上看得见的那段字，"
            + "在没得选之前这一步读的就是它；「填写的值」是输入框、下拉框真正带着的值，密码框显示的是点、"
            + "带着的是密码，下拉框显示的是选项文字、带着的是它后面的值；「属性」是元素标记里的某一项，"
            + "比如链接指向的网址。",
        ["browser.readText.what.option.text"] = "显示的文字",
        ["browser.readText.what.option.value"] = "填写的值",
        ["browser.readText.what.option.attribute"] = "属性",
        ["browser.readText.attribute.label"] = "属性名",
        ["browser.readText.attribute.hint"] = "读哪个属性，按页面标记里的写法写：链接的地址是 href，"
            + "图片的出处是 src，标记里写的值是 value。元素没有这个属性时读回来是空的，不算找不到。",
        ["browser.readText.matchIndex.label"] = "第几个命中",
        ["browser.readText.narrow.label"] = "限定",
        ["browser.readText.narrow.hint"] = "要读取的那个元素" + NarrowHint,
        ["browser.readText.matchIndex.hint"] = PageMatchIndexHint("要读取的"),

        ["browser.readAll.name"] = "读取全部",
        ["browser.readAll.desc"] = "把选择器认到的每个元素各读一条，组成一个列表。",
        ["browser.readAll.target.label"] = "选择器",
        ["browser.readAll.target.hint"] = PageTargetHint + "要读取哪些元素，写法与「点击元素」相同。"
            + "选择器认到几个就读几个，所以只认到一个时读到的也是一条。",
        ["browser.readAll.what.label"] = "读什么",
        ["browser.readAll.what.hint"] = "每个元素读它的哪一部分，与「读取文本」那一格相同：显示的文字、"
            + "填写的值，或者标记里的某一项属性。",
        ["browser.readAll.what.option.text"] = "显示的文字",
        ["browser.readAll.what.option.value"] = "填写的值",
        ["browser.readAll.what.option.attribute"] = "属性",
        ["browser.readAll.attribute.label"] = "属性名",
        ["browser.readAll.attribute.hint"] = "读哪个属性，按页面标记里的写法写，例如 href、src、value。"
            + "元素没有这个属性时读回来是空的，不算找不到。",
        ["browser.readAll.matchIndex.label"] = "第几个命中",
        ["browser.readAll.narrow.label"] = "限定",
        ["browser.readAll.narrow.hint"] = "要读取的那些元素" + NarrowHint,
        ["browser.readAll.matchIndex.hint"] = "从第几个命中开始读：选择器可能同时对上多个元素，这里说从它们"
            + "中的第几个开始读，按在页面上出现的顺序从 1 数起，它后面的也照读，直到读满「最多几条」。"
            + "默认 1，也就是从第一个开始读整个列表。",
        ["browser.readAll.maxItems.label"] = "最多几条",
        ["browser.readAll.maxItems.hint"] = "最多读几条，1 到 10000。默认 100，与程序里其它读列表的地方"
            + "同一个上限，免得页面一边读一边变长时读不完。",

        ["browser.readTable.name"] = "读取表格",
        ["browser.readTable.desc"] = "把页面上的一个表格读成一行一条的表格。",
        ["browser.readTable.target.label"] = "选择器",
        ["browser.readTable.target.hint"] = PageTargetHint + "要读哪个表格，写法与「点击元素」相同。"
            + "默认写的是 table，也就是页面自己的表格元素。",
        ["browser.readTable.matchIndex.label"] = "第几个命中",
        ["browser.readTable.narrow.label"] = "限定",
        ["browser.readTable.narrow.hint"] = "要读取的那张表格" + NarrowHint,
        ["browser.readTable.matchIndex.hint"] = PageMatchIndexHint("要读取的"),
        ["browser.readTable.maxRows.label"] = "最多几行",
        ["browser.readTable.maxRows.hint"] = "最多读几行，1 到 10000。默认 100，与程序里其它读表格的地方"
            + "同一个上限。",

        ["browser.readPage.name"] = "读取页面信息",
        ["browser.readPage.desc"] = "读取当前页面在哪、叫什么名字。",

        ["browser.switchTab.name"] = "切换标签页",
        ["browser.switchTab.desc"] = "让后面的步骤改用浏览器里的另一个标签页。",
        ["browser.switchTab.how.label"] = "哪个标签页",
        ["browser.switchTab.how.hint"] = "后面的步骤在哪个标签页上操作，默认「最新打开的那个」。「最新打开的那个」是最后出现的页面，"
            + "点击后新开标签页时留下的就是它；标签页要等点击返回后一小会儿才到浏览器，所以这一步会等它几秒，"
            + "等不到会报错，而不是留在原地：在长得很像的站点上，留在原地是看不出问题的。",
        ["browser.switchTab.how.option.newest"] = "最新打开的那个",
        ["browser.switchTab.how.option.index"] = "第几个",
        ["browser.switchTab.how.option.title"] = "按标题",
        ["browser.switchTab.how.option.url"] = "按网址",
        ["browser.switchTab.index.label"] = "第几个标签页",
        ["browser.switchTab.index.hint"] = "从标签栏最左边数起，第一个是 1，范围为 1 到 50，默认 1，配合「第几个」使用。",
        ["browser.switchTab.match.label"] = "标题或网址里包含",
        ["browser.switchTab.match.hint"] = "标签页的标题或网址里要包含的文字，配合「按标题」「按网址」使用。",

        ["browser.closeTab.name"] = "关闭标签页",
        ["browser.closeTab.desc"] = "关闭浏览器当前所在的标签页，并转到另一个标签页。",

        ["browser.close.name"] = "关闭浏览器",
        ["browser.close.desc"] = "关闭宏打开的浏览器。",

        ["browser.select.name"] = "下拉选择",
        ["browser.select.desc"] = "在页面上的下拉框里选中一个选项。",
        ["browser.select.target.label"] = "选择器",
        ["browser.select.target.hint"] = PageTargetHint + "要操作哪个下拉框，写法与「点击元素」相同。",
        ["browser.select.value.label"] = "选项",
        ["browser.select.value.hint"] = "要选中哪个选项：值和可见文字正好是这段文字的那一个，或者"
            + "配合下面的「按序号」写选项的位置；整段按一个值读取，因此可以写变量或算式。",
        ["browser.select.by.label"] = "按什么选",
        ["browser.select.by.hint"] = "上面那格怎么匹配。「按值或文字」取第一个值和可见文字正好是"
            + "所写内容的选项；「按序号」取那个位置上的选项，从上往下数，第一个是 1。页面自己用"
            + "别的元素画出来的下拉框不属于这一类，那种要按人操作的方式点开再点选。",
        ["browser.select.by.option.value"] = "按值或文字",
        ["browser.select.by.option.index"] = "按序号",
        ["browser.select.matchIndex.label"] = "第几个命中",
        ["browser.select.narrow.label"] = "限定",
        ["browser.select.narrow.hint"] = "要选的那个下拉框" + NarrowHint,
        ["browser.select.matchIndex.hint"] = PageMatchIndexHint("要选中的"),

        ["browser.check.name"] = "勾选",
        ["browser.check.desc"] = "把复选框或单选框留在勾上或没勾的状态。",
        ["browser.check.target.label"] = "选择器",
        ["browser.check.target.hint"] = PageTargetHint + "要让哪个框留在该状态，写法与「点击元素」相同。",
        ["browser.check.checked.label"] = "勾上",
        ["browser.check.checked.hint"] = "这个框最后是不是要勾上，默认勾上；关掉它表示把已经"
            + "勾上的框取消勾选。要求的状态与当前相同就什么都不做，不像点击那样把勾着的框点掉。",
        ["browser.check.matchIndex.label"] = "第几个命中",
        ["browser.check.narrow.label"] = "限定",
        ["browser.check.narrow.hint"] = "要勾选的那个方框" + NarrowHint,
        ["browser.check.matchIndex.hint"] = PageMatchIndexHint("要勾选或取消勾选的"),

        ["browser.hover.name"] = "悬停",
        ["browser.hover.desc"] = "把指针移到某个元素上并停在那里。",
        ["browser.hover.target.label"] = "选择器",
        ["browser.hover.target.hint"] = PageTargetHint + "要移到哪个元素上，写法与「点击元素」相同。"
            + "菜单、提示框、整排按钮都是这么出现的；这一步不点击任何东西。",
        ["browser.hover.matchIndex.label"] = "第几个命中",
        ["browser.hover.narrow.label"] = "限定",
        ["browser.hover.narrow.hint"] = "要把指针移上去的那个元素" + NarrowHint,
        ["browser.hover.matchIndex.hint"] = PageMatchIndexHint("要悬停的"),

        ["browser.press.name"] = "页面按键",
        ["browser.press.desc"] = "向页面里发送一个按键或一个组合键。",
        ["browser.press.target.label"] = "选择器",
        ["browser.press.target.hint"] = PageTargetHint + "发送按键前由哪个元素取得焦点，写法与"
            + "「点击元素」相同。一行都不写，也就是默认，把按键发给页面本身。",
        ["browser.press.key.label"] = "按键",
        ["browser.press.key.hint"] = "要发送哪个键：键盘选择器里列出的名字（Enter、Esc、F5、"
            + "Down），或者用「+」连起来的组合键（Ctrl+S）。键盘里没有的名字在运行前就会被拒绝。",
        ["browser.press.matchIndex.label"] = "第几个命中",
        ["browser.press.narrow.label"] = "限定",
        ["browser.press.narrow.hint"] = "接按键的那个元素" + NarrowHint,
        ["browser.press.matchIndex.hint"] = PageMatchIndexHint("按键前要取得焦点的"),

        ["browser.waitFor.name"] = "等待元素",
        ["browser.waitFor.desc"] = "等待页面上的某个元素出现或消失。",
        ["browser.waitFor.target.label"] = "选择器",
        ["browser.waitFor.target.hint"] = PageTargetHint + "要等哪个元素，写法与「点击元素」相同。"
            + "页面自己去取内容时要用这一步，而不是固定停一会儿：状态一到，这一步立刻结束。"
            + "等多久由这一步设置里的「超时」说，默认 15000 毫秒。",
        ["browser.waitFor.state.label"] = "等到它",
        ["browser.waitFor.state.hint"] = "等哪一种状态。「可见」是接下来要点击某处时用的；"
            + "「不可见」用于等一个转圈的进度提示消失；「在页面上」等的是存在但可以没有大小的"
            + "元素；「不在页面上」等的是已经被拿掉的元素。",
        ["browser.waitFor.state.option.visible"] = "可见",
        ["browser.waitFor.state.option.hidden"] = "不可见",
        ["browser.waitFor.state.option.attached"] = "在页面上",
        ["browser.waitFor.state.option.detached"] = "不在页面上",
        ["browser.waitFor.matchIndex.label"] = "第几个命中",
        ["browser.waitFor.narrow.label"] = "限定",
        ["browser.waitFor.narrow.hint"] = "要等到的那个元素" + NarrowHint,
        ["browser.waitFor.matchIndex.hint"] = PageMatchIndexHint("要等的"),

        ["browser.waitForUrl.name"] = "等待网址",
        ["browser.waitForUrl.desc"] = "等到浏览器当前页面转到某个网址。",
        ["browser.waitForUrl.match.label"] = "网址里含有",
        ["browser.waitForUrl.match.hint"] = "网址里要有这段文字才算等到。按一个值读取，变量或表达式都可以"
            + "写成它。点击之后要去的是另一个页面时，该等的是网址而不是元素：元素在点击到达的那一页上，"
            + "太早去找会找到正要离开的那一页上的同名元素。",
        ["browser.waitForUrl.how.label"] = "比对方式",
        ["browser.waitForUrl.how.hint"] = "当前网址与上面那格怎么比：包含（默认）、以…开头、以…结尾、"
            + "等于、正则匹配。默认的「包含」通常就够用，因为网址里真正说清是哪一页的那一段后面还跟着"
            + "查询串和锚点。认不出的词会停下这一步并说明，正则读不出来在开始等之前就报错。",
        ["browser.waitForUrl.how.option.contains"] = "包含",
        ["browser.waitForUrl.how.option.startsWith"] = "以…开头",
        ["browser.waitForUrl.how.option.endsWith"] = "以…结尾",
        ["browser.waitForUrl.how.option.equals"] = "等于",
        ["browser.waitForUrl.how.option.regex"] = "正则匹配",

        ["browser.waitForLoad.name"] = "等待加载",
        ["browser.waitForLoad.desc"] = "等到页面加载到指定的那一步。",
        ["browser.waitForLoad.state.label"] = "等到",
        ["browser.waitForLoad.state.hint"] = "等到页面加载的哪一步。「加载完成」是默认，一次把内容全部画出来"
            + "的页面就停在这里；「文档就绪」是页面自己的脚本已经跑过、文字已经在那里的时候，图片和样式表还没"
            + "到，只要文字时它更快；「网络空闲」是页面自己发起的请求都已经回来的时候，一次取一点的页面要用它，"
            + "它也是三档里最慢的，因为它要等到页面不再要东西为止。",
        ["browser.waitForLoad.state.option.ready"] = "文档就绪",
        ["browser.waitForLoad.state.option.loaded"] = "加载完成",
        ["browser.waitForLoad.state.option.idle"] = "网络空闲",

        ["browser.scroll.name"] = "滚动页面",
        ["browser.scroll.desc"] = "把页面滚动一下，让屏幕外的东西露出或加载出来。",
        ["browser.scroll.target.label"] = "选择器",
        ["browser.scroll.target.hint"] = PageTargetHint + "要把哪个元素滚到看得见，写法与「点击元素」相同；"
            + "只有「滚到元素」这一档要用它。一行都不写，也就是默认，滚的是页面本身，往下滚一屏。",
        ["browser.scroll.how.label"] = "怎么滚",
        ["browser.scroll.how.hint"] = "页面往哪儿滚。「往下滚一屏」是默认，一次一点加内容的页面就靠它一条条"
            + "走完；「滚到元素」要用上面那格写选择器，图片或数据等到快滚到才加载的页面靠它把内容要出来；"
            + "「到页面底部」是一下子跳到底，一直在往下加内容的页面要连用好几次；「到页面顶部」用于接下来"
            + "从头读一遍页面的步骤。",
        ["browser.scroll.how.option.screen"] = "往下滚一屏",
        ["browser.scroll.how.option.element"] = "滚到元素",
        ["browser.scroll.how.option.bottom"] = "到页面底部",
        ["browser.scroll.how.option.top"] = "到页面顶部",
        ["browser.scroll.matchIndex.label"] = "第几个命中",
        ["browser.scroll.narrow.label"] = "限定",
        ["browser.scroll.narrow.hint"] = "要滚到眼前的那个元素" + NarrowHint,
        ["browser.scroll.matchIndex.hint"] = PageMatchIndexHint("要滚到眼前的"),

        ["browser.download.name"] = "下载文件",
        ["browser.download.desc"] = "把页面接下来交出的文件保存到磁盘上。",
        ["browser.download.target.label"] = "选择器",
        ["browser.download.target.hint"] = PageTargetHint + "点哪个元素开始下载，写法与「点击元素」相同，"
            + "这一步替你把这一下点了。一行都不写，也就是默认，等的是页面接下来自己开始的下载，"
            + "页面自己判断某个东西是文件时要用这一档。",
        ["browser.download.folder.label"] = "保存到",
        ["browser.download.folder.hint"] = "文件存到哪个文件夹；留空就是宏文件所在的那个文件夹。按一个值读取，"
            + "变量或表达式都可以写成它。同名的文件会被覆盖，与别的程序里保存一次一样。",
        ["browser.download.matchIndex.label"] = "第几个命中",
        ["browser.download.narrow.label"] = "限定",
        ["browser.download.narrow.hint"] = "要点击以开始下载的那个元素" + NarrowHint,
        ["browser.download.matchIndex.hint"] = PageMatchIndexHint("要点击开始下载的"),

        ["browser.dialog.name"] = "回答对话框",
        ["browser.dialog.desc"] = "回答页面接下来弹出的对话框，把触发它的步骤包在里面。",
        ["browser.dialog.how.label"] = "怎么回答",
        ["browser.dialog.how.hint"] = "对话框是答应还是取消。想要接着往下走就答应，下面那格填的字也由它带过去；"
            + "想留在原处就取消。一个盒子里只有第一个对话框按这里说的回答，它之后弹出的按浏览器的默认处理，"
            + "也就是没人应答时的自动关掉。",
        ["browser.dialog.how.option.accept"] = "答应",
        ["browser.dialog.how.option.dismiss"] = "取消",
        ["browser.dialog.text.label"] = "要输入的文字",
        ["browser.dialog.text.hint"] = "要输入到对话框里的文字，例如页面在保存前问的文件名。留空也就是默认，"
            + "表示不作答；不问话的对话框用不到这一格。按一个值读取，变量或表达式都可以写成它。",
        ["browser.dialog.body.label"] = "会弹出对话框的步骤",
        ["browser.dialog.body.hint"] = "让对话框弹出来的那些步骤，例如点击那个会提问的按钮。它们之所以写在盒子"
            + "里，是因为对话框没人回答时页面就停在那里：触发它的那一步要等到被回答之后才会返回。",

        ["browser.evaluate.name"] = "在页面里跑脚本",
        ["browser.evaluate.desc"] = "在页面里跑一小段脚本，并把它交回的结果拿出来。",
        ["browser.evaluate.script.label"] = "脚本",
        ["browser.evaluate.script.hint"] = "要跑的脚本，写成页面会调用的一段函数，例如 "
            + "() => { return document.title; }。它在页面自己里面跑，所以页面读得到的东西它都读得到；"
            + "用 return 交回什么，这一步的结果就是什么。上面这些动作都没覆盖到的事情由它兜底，代价是这个宏"
            + "依赖页面自己的实现。写 {{变量名}} 的地方会在运行前换成那个变量的值。",
        ["browser.evaluate.script.default"] =
            "// 脚本交回什么，这一步就交出什么。\n"
            + "// return document.title; 读页面自己写的名字。\n"
            + "// return { rows: 3, first: '甲' }; 交回一条记录，结果里也还是一条记录。\n"
            + "return document.querySelectorAll('a').length;\n",

        ["browser.upload.name"] = "上传文件",
        ["browser.upload.desc"] = "把文件交给页面上的文件选择框。",
        ["browser.upload.target.label"] = "选择器",
        ["browser.upload.target.hint"] = PageTargetHint + "要交给哪个文件选择框，写法与「点击元素」相同。",
        ["browser.upload.files.label"] = "文件",
        ["browser.upload.files.hint"] = "一行一个文件的路径，空行会被跳过；相对路径从宏文件所在"
            + "的文件夹算起，与别的步骤里的路径同一条规则。浏览器的规矩是不许脚本告诉页面一个"
            + "路径，这一步是绕开这条规矩的唯一办法。",
        ["browser.upload.matchIndex.label"] = "第几个命中",
        ["browser.upload.narrow.label"] = "限定",
        ["browser.upload.narrow.hint"] = "要交给文件的那个文件框" + NarrowHint,
        ["browser.upload.matchIndex.hint"] = PageMatchIndexHint("要交给的"),

        // ------------------------------------------------------------------ script
        ["script.run.name"] = "运行脚本",
        ["script.run.desc"] = "运行一段命令、批处理、PowerShell 或 AutoHotkey 脚本，也可以交给机器上别的程序运行。",
        ["script.run.kind.label"] = "脚本类型",
        ["script.run.kind.hint"] = "这一段脚本交给谁运行。三种命令写法都先把正文写成批处理文件再交给 cmd.exe："
            + "第一种在命令跑完后把窗口留着，好让人看输出；第二种在命令跑完时把窗口关掉；第三种不显示窗口。"
            + "两种文件类型与 PowerShell、AutoHotkey 都不显示窗口；自选程序用于机器上装的其他解释器。默认 PowerShell。",
        ["script.run.kind.option.cmdKeep"] = "CMD命令(完成保留窗口)",
        ["script.run.kind.option.cmdClose"] = "CMD命令(完成后关闭窗口)",
        ["script.run.kind.option.cmdHidden"] = "CMD命令(隐藏命令行窗口)",
        ["script.run.kind.option.bat"] = "BAT批处理文件(.bat)",
        ["script.run.kind.option.cmdFile"] = "CMD批处理脚本(.cmd)",
        ["script.run.kind.option.ps1"] = "PowerShell脚本(.ps1)",
        ["script.run.kind.option.ahk"] = "AutoHotKey脚本(.ahk)",
        ["script.run.kind.option.custom"] = "自定义脚本类型",
        ["script.run.interpreter.label"] = "解释器命令行",
        ["script.run.interpreter.hint"] = "运行这段脚本的程序，它自己的开关写在前面，配合「自定义脚本类型」使用。脚本的路径会接在它后面，"
            + "与手打命令行时一样；路径里有空格时用引号括起来。",
        ["script.run.extension.label"] = "脚本后缀",
        ["script.run.extension.hint"] = "那个程序认识的脚本后缀，例如 dotnet-script 用 .csx、cscript 用 .vbs。只在「自定义脚本类型」时用得上。",
        ["script.run.encoding.label"] = "脚本文件编码",
        ["script.run.encoding.hint"] = "交给程序的那份脚本按什么编码写，默认「按这类脚本的习惯」。按习惯指批处理与命令用本机代码页、"
            + "PowerShell 与 AutoHotkey 带 BOM、自选程序用 UTF-8，各自都能正确读出中文；像 cscript 这种按系统代码页读文件的程序，要选 GBK。",
        ["script.run.encoding.option.auto"] = "按这类脚本的习惯",
        ["script.run.encoding.option.utf8"] = "UTF-8",
        ["script.run.encoding.option.utf8bom"] = "UTF-8 带 BOM",
        ["script.run.encoding.option.gbk"] = "GBK（中文）",
        ["script.run.encoding.option.utf16"] = "UTF-16",
        ["script.run.script.label"] = "脚本",
        ["script.run.script.hint"] = "脚本正文，交给上面那个程序运行。写 {{变量名}} 的地方会在运行前换成那个变量的值，这就是宏的值传进脚本的方式；"
            + "脚本打印出来的内容在能回收的那几种类型上回到「输出」里。",
        ["script.run.source.label"] = "脚本来源",
        ["script.run.source.hint"] = "这一步跑什么：写在下面的一段脚本，还是这台机器上已有的一个脚本文件。"
            + "选文件时那个文件就地运行，它旁边读什么、里面 include 什么、它自己的目录是哪里都不变，"
            + "里面的 {{变量名}} 也不会替换。默认「写在下面」。",
        ["script.run.source.option.text"] = "写在下面",
        ["script.run.source.option.file"] = "一个脚本文件",
        ["script.run.file.label"] = "脚本文件",
        ["script.run.file.hint"] = "要运行的那个脚本文件，写它现在所在的位置；谁运行它由上面的「脚本类型」说了算，"
            + "不再看文件自己的后缀。写相对路径时从宏文件所在的目录算起。要跑的是别人一直在维护的脚本、"
            + "或者太长不方便写进步骤里的脚本时用它。",
        ["script.run.arguments.label"] = "参数",
        ["script.run.arguments.hint"] = "附加在脚本命令行后面的参数，其中的 {{变量名}} 也会替换成变量的值。默认留空，表示不传参数。",
        ["script.run.folder.label"] = "工作目录",
        ["script.run.folder.hint"] = "脚本在哪个目录中运行。默认留空，表示宏所在的目录；该目录不存在时，改用鲸灵自身的工作目录。",
        ["script.run.timeoutMs.label"] = "超时",
        ["script.run.timeoutMs.hint"] = "超过这个时长就结束脚本并判这一步失败，单位毫秒，默认 60000。可能会跑更久的脚本，"
            + "或者要把窗口留着慢慢看的脚本，填 0 表示不限时。",
        ["script.python.name"] = "运行 Python 脚本",
        ["script.python.desc"] = "运行一段 Python 脚本，运行期间脚本能读写宏的变量、调用宏的步骤。",
        ["script.python.script.label"] = "脚本",
        ["script.python.script.hint"] = "脚本本身。脚本打印的内容会逐行写进运行日志；运行期间宏就在它旁边："
            + "WhaleGenie.GetProperty(\"名字\") 读一个宏变量，SetProperty(\"名字\", 值) 写一个，"
            + "StepExecute(\"标识\") 跑本宏里的某一步，Print(文字) 往运行日志写一行，"
            + "SetResult(值) 交回这一步要留下的结果。",
        ["script.python.script.default"] =
            "# 读一个宏变量的值\n"
            + "# text = WhaleGenie.GetProperty(\"变量名\")\n\n"
            + "# 写一个宏变量，后面的步骤读得到\n"
            + "# WhaleGenie.SetProperty(\"变量名\", 新值)\n\n"
            + "# 调用本宏的某一步，写它的四字符标识\n"
            + "# WhaleGenie.StepExecute(\"a7k2\")\n\n"
            + "# 往运行日志写一行\n"
            + "WhaleGenie.Print(\"Hello World\")\n\n"
            + "# 交回这一步要留下的结果\n"
            + "# WhaleGenie.SetResult(text)\n",
        ["script.python.arguments.label"] = "参数",
        ["script.python.arguments.hint"] = "附加在脚本命令行后面的参数，脚本在 sys.argv 里读到它们，"
            + "与从命令行启动一个程序时一样。默认留空，表示不传参数。",
        ["script.python.python.label"] = "Python 命令",
        ["script.python.python.hint"] = "这一步用哪个 Python 运行，它自己的开关写在前面；路径里有空格时用引号括起来。"
            + "默认留空，表示用本机搜索路径上的 python。",
        ["script.python.timeoutMs.label"] = "超时",
        ["script.python.timeoutMs.hint"] = "超过这个时长就结束脚本并判这一步失败，单位毫秒，默认 60000；"
            + "可能会跑更久的脚本填 0，表示不限时。",
        ["script.python.folder.label"] = "工作目录",
        ["script.python.folder.hint"] = "脚本在哪个目录中运行。默认留空，表示宏所在的目录；该目录不存在时，"
            + "改用鲸灵自身的工作目录。",
        ["script.pip.name"] = "安装 PIP 包",
        ["script.pip.desc"] = "看一下要用的 Python 包在不在，缺的下下来，好让 Python 脚本能 import 它们。",
        ["script.pip.packages.label"] = "包",
        ["script.pip.packages.hint"] = "Python 脚本要用到的包，一行一个，写法与 pip 一致：一个包名，"
            + "或者包名后面跟两个等号与版本号。以 # 开头的行会被跳过。包装在鲸灵自己的目录里，"
            + "不装进这台机器的 Python，每个 Python 脚本步骤都能 import 到它。",
        ["script.pip.python.label"] = "Python 命令",
        ["script.pip.python.hint"] = "用哪个 Python 的 pip 下载这些包，它自己的开关写在前面。"
            + "默认留空，表示用本机搜索路径上的 python。",
        ["script.csharp.name"] = "运行 C# 脚本",
        ["script.csharp.desc"] = "把一段 C# 编译后运行，运行期间脚本能读写宏的变量、调用宏的步骤。",
        ["script.csharp.script.label"] = "脚本",
        ["script.csharp.script.hint"] = "脚本本身。它在鲸灵自己的子进程里编译并运行，"
            + "所以脚本对自己做什么都伤不到宏；运行期间宏就在它旁边：__InjectedServices.DynamicObject 就是宏，"
            + "GetProperty(\"名字\") 读一个宏变量，SetProperty(\"名字\", 值) 写一个，"
            + "StepExecute(\"标识\") 跑本宏里的某一步，SetResult(值) 交回这一步要留下的结果，"
            + "Console.WriteLine 写的内容逐行进运行日志。",
        ["script.csharp.script.default"] = """
            using System;

            class Program
            {
                static void Main()
                {
                    var WhaleGenie = __InjectedServices.DynamicObject;

                    //获取变量值
                    //dynamic text = WhaleGenie.GetProperty("变量名");

                    //设置变量值
                    //WhaleGenie.SetProperty("变量名", 新值);

                    //调用步骤功能
                    //WhaleGenie.StepExecute("a7k2");

                    //交回这一步要留下的结果
                    //WhaleGenie.SetResult(text);

                    //打印日志到控制台
                    Console.WriteLine("Hello World");
                }
            }
            """,
        ["script.csharp.arguments.label"] = "参数",
        ["script.csharp.arguments.hint"] = "附加在脚本命令行后面的参数，入口点从调用它时拿到的那个数组里读到它们。"
            + "默认留空，表示不传参数。",
        ["script.csharp.timeoutMs.label"] = "超时",
        ["script.csharp.timeoutMs.hint"] = "超过这个时长就结束脚本并判这一步失败，单位毫秒，默认 60000；"
            + "可能会跑更久的脚本填 0，表示不限时。",
        ["script.csharp.folder.label"] = "工作目录",
        ["script.csharp.folder.hint"] = "脚本在哪个目录中运行。默认留空，表示宏所在的目录；该目录不存在时，"
            + "改用鲸灵自身的工作目录。",
        ["script.ahk.name"] = "运行 AHK 脚本",
        ["script.ahk.desc"] = "运行一段 AutoHotkey v2 脚本，运行期间脚本能读写宏的变量、调用宏的步骤。",
        ["script.ahk.script.label"] = "脚本",
        ["script.ahk.script.hint"] = "脚本本身，按 AutoHotkey v2 写。脚本打印的内容会逐行写进运行日志；"
            + "运行期间宏就在它旁边：GetProperty(\"名字\") 读一个宏变量，SetProperty(\"名字\", 值) 写一个，"
            + "StepExecute(\"标识\") 跑本宏里的某一步，Print(文字) 往运行日志写一行，"
            + "SetResult(值) 交回这一步要留下的结果。宏里的列表与记录在这边是它们的 JSON 文字。",
        ["script.ahk.script.default"] =
            "; 读一个宏变量的值\n"
            + "; variable := GetProperty(\"变量名\")\n\n"
            + "; 写一个宏变量，后面的步骤读得到\n"
            + "; SetProperty(\"变量名\", \"新值\")\n\n"
            + "; 调用本宏的某一步，写它的四字符标识\n"
            + "; StepExecute(\"a7k2\")\n\n"
            + "; 往运行日志写一行\n"
            + "Print(\"Hello World\")\n\n"
            + "; 交回这一步要留下的结果\n"
            + "; SetResult(\"结果\")\n",
        ["script.ahk.arguments.label"] = "参数",
        ["script.ahk.arguments.hint"] = "附加在脚本命令行后面的参数，脚本在 A_Args 里读到它们，"
            + "与从命令行启动一个程序时一样。默认留空，表示不传参数。",
        ["script.ahk.ahk.label"] = "AutoHotkey 命令",
        ["script.ahk.ahk.hint"] = "这一步用哪个 AutoHotkey 运行，它自己的开关写在前面；路径里有空格时用引号括起来。"
            + "默认留空，表示用这台机器上找到的那个 AutoHotkey。",
        ["script.ahk.timeoutMs.label"] = "超时",
        ["script.ahk.timeoutMs.hint"] = "超过这个时长就结束脚本并判这一步失败，单位毫秒，默认 60000；"
            + "可能会跑更久的脚本填 0，表示不限时。",
        ["script.ahk.folder.label"] = "工作目录",
        ["script.ahk.folder.hint"] = "脚本在哪个目录中运行。默认留空，表示宏所在的目录；该目录不存在时，"
            + "改用鲸灵自身的工作目录。",
        ["script.nuget.name"] = "下载 NuGet 包",
        ["script.nuget.desc"] = "看一下要用的 NuGet 包在不在，缺的连同它们自己依赖的包一起下下来，"
            + "好让 C# 脚本能编译着用。",
        ["script.nuget.packages.label"] = "包",
        ["script.nuget.packages.hint"] = "C# 脚本要用到的包，一行一个：一个包名，或者包名后面隔一个空格跟上版本号。"
            + "只写包名取最新的稳定版。以 # 开头的行会被跳过。包下在鲸灵自己的目录里，"
            + "连带它们依赖的包，每个 C# 脚本都按这个目录里的东西编译。",

        // ---------------------------------------------------------------- condition
        ["condition.imageExists.name"] = "图片存在",
        ["condition.imageExists.desc"] = "当参考图片出现在屏幕上时为真。",
        ["condition.imageExists.image.label"] = "图片文件",
        ["condition.imageExists.image.hint"] = "要找的图片，一行一张，按列出的顺序试；只要有一张出现在画面上，这个条件就成立。",
        ["condition.imageExists.confidence.label"] = "相似度 %",
        ["condition.imageExists.confidence.hint"] = "要求的相似度下限，写成 0 到 100 的百分数，默认 90；把参考图"
            + "的写法放宽一点就调低。",
        ["condition.imageExists.region.label"] = "搜索区域",
        ["condition.imageExists.region.hint"] = RegionHintText,
        ["condition.imageExists.imageSet.label"] = "认哪一张",
        ["condition.imageExists.imageSet.hint"] = ImageSetHint,
        ["condition.imageExists.imageSet.option.first"] = "认出的第一张",
        ["condition.imageExists.imageSet.option.best"] = "最像的那一张",
        ["condition.imageExists.imageSet.option.every"] = "每一张都要",

        ["condition.textExists.name"] = "文字存在",
        ["condition.textExists.desc"] = "当屏幕上出现指定文字时为真。",
        ["condition.textExists.text.label"] = "文字",
        ["condition.textExists.text.hint"] = "要在屏幕上找的文字，例如 Ready，默认空着要自己填。整段按一个值读，所以变量或算式都能放在这里；画面上出现这段文字时条件成立。",
        ["condition.textExists.region.label"] = "搜索区域",
        ["condition.textExists.region.hint"] = RegionHintText,
        ["condition.textExists.matchMode.label"] = "匹配方式",
        ["condition.textExists.matchMode.hint"] = TextMatchHint,

        ["condition.uiaExists.name"] = "UI 元素存在",
        ["condition.uiaExists.desc"] = "当 UI Automation 元素存在时为真。",
        ["condition.uiaExists.window.label"] = WindowMatchTextLabel,
        ["condition.uiaExists.window.hint"] = "可选：只在标题里含这段文字的窗口里找；留空表示在所有窗口里找。",
        ["condition.uiaExists.selector.label"] = "选择器",
        ["condition.uiaExists.selector.hint"] = "元素选择器，例如 Button[name='Save']。也可用“拾取元素”直接在屏幕"
            + "上拾取控件；每次判断都会重新找一遍，元素挪了位置也找得到。",

        ["condition.colorEquals.name"] = "颜色相同",
        ["condition.colorEquals.desc"] = "当某个像素的颜色匹配时为真。",
        ["condition.colorEquals.x.label"] = "X",
        ["condition.colorEquals.x.hint"] = "要比较的像素的横坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["condition.colorEquals.y.label"] = "Y",
        ["condition.colorEquals.y.hint"] = "要比较的像素的纵坐标，按「坐标基准」量起，单位是像素，默认 0。",
        ["condition.colorEquals.color.label"] = "颜色",
        ["condition.colorEquals.color.hint"] = "拿来和这个像素比较的颜色，写成 #RRGGBB，默认黑色。用放大镜取色时，取到的那一点同时写进 X、Y。",
        ["condition.colorEquals.tolerance.label"] = "容差 %",
        ["condition.colorEquals.tolerance.hint"] = "允许的颜色偏差，写成 0 到 100 的百分数，默认 5；画面有压缩"
            + "痕迹或做过缩放时调大。",

        // ------------------------------------------ 否定条件与布尔表达式（条件也能自己写公式）
        ["condition.imageNotExists.name"] = "图片不存在",
        ["condition.imageNotExists.desc"] = "当参考图片不在屏幕上时为真。",
        ["condition.imageNotExists.image.label"] = "图片文件",
        ["condition.imageNotExists.image.hint"] = "要找的图片，一行一张，按列出的顺序试；一张都没出现，这个条件才成立。",
        ["condition.imageNotExists.confidence.label"] = "相似度 %",
        ["condition.imageNotExists.confidence.hint"] = "要求的相似度下限，写成 0 到 100 的百分数，默认 90；把"
            + "参考图的写法放宽一点就调低。",
        ["condition.imageNotExists.region.label"] = "搜索区域",
        ["condition.imageNotExists.region.hint"] = RegionHintText,
        ["condition.imageNotExists.imageSet.label"] = "认哪一张",
        ["condition.imageNotExists.imageSet.hint"] = ImageSetHint,
        ["condition.imageNotExists.imageSet.option.first"] = "认出的第一张",
        ["condition.imageNotExists.imageSet.option.best"] = "最像的那一张",
        ["condition.imageNotExists.imageSet.option.every"] = "每一张都要",
        ["condition.imageNotExists.anchorMode.label"] = "坐标基准",
        ["condition.imageNotExists.anchorMode.hint"] = AnchorModeHint,
        ["condition.imageNotExists.anchorMode.option.screen"] = "屏幕像素",
        ["condition.imageNotExists.anchorMode.option.window"] = "窗口左上角",
        ["condition.imageNotExists.anchorMode.option.client"] = "窗口客户区",
        ["condition.imageNotExists.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.imageNotExists.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["condition.textNotExists.name"] = "文字不存在",
        ["condition.textNotExists.desc"] = "当屏幕上没有指定文字时为真。",
        ["condition.textNotExists.text.label"] = "文字",
        ["condition.textNotExists.text.hint"] = "要在屏幕上确认没有的文字，例如 Ready，默认空着要自己填。整段按一个值读，所以变量或算式都能放在这里；画面上没有它时条件成立。",
        ["condition.textNotExists.region.label"] = "搜索区域",
        ["condition.textNotExists.region.hint"] = RegionHintText,
        ["condition.textNotExists.matchMode.label"] = "匹配方式",
        ["condition.textNotExists.matchMode.hint"] = TextMatchHint,
        ["condition.textNotExists.matchMode.option.contains"] = "包含",
        ["condition.textNotExists.matchMode.option.startsWith"] = "以…开头",
        ["condition.textNotExists.matchMode.option.endsWith"] = "以…结尾",
        ["condition.textNotExists.matchMode.option.equals"] = "等于",
        ["condition.textNotExists.matchMode.option.regex"] = "正则匹配",
        ["condition.textNotExists.anchorMode.label"] = "坐标基准",
        ["condition.textNotExists.anchorMode.hint"] = AnchorModeHint,
        ["condition.textNotExists.anchorMode.option.screen"] = "屏幕像素",
        ["condition.textNotExists.anchorMode.option.window"] = "窗口左上角",
        ["condition.textNotExists.anchorMode.option.client"] = "窗口客户区",
        ["condition.textNotExists.anchorWindow.label"] = WindowMatchTextLabel,
        ["condition.textNotExists.anchorWindow.hint"] = AnchorWindowRuleHint,

        ["condition.uiaNotExists.name"] = "UI 元素不存在",
        ["condition.uiaNotExists.desc"] = "当某个 UI Automation 元素不存在时为真。",
        ["condition.uiaNotExists.window.label"] = WindowMatchTextLabel,
        ["condition.uiaNotExists.window.hint"] = "可选：只在标题里含这段文字的窗口里找；留空表示在所有窗口里找。",
        ["condition.uiaNotExists.selector.label"] = "选择器",
        ["condition.uiaNotExists.selector.hint"] =
            "元素选择器，例如 Button[name='Save']。也可用“拾取元素”直接在屏幕上拾取控件；每次判断都会重新找"
            + "一遍，元素挪了位置也找得到。",

        ["condition.browserExists.name"] = "页面元素存在",
        ["condition.browserExists.desc"] = "当页面上的某个元素处在指定的状态时为真。",
        ["condition.browserExists.target.label"] = "选择器",
        ["condition.browserExists.target.hint"] = PageTargetHint + "要判断页面上的哪个元素，写法与「点击元素」"
            + "相同；问的是页面现在有没有这样的元素，所以它不会等。",
        ["condition.browserExists.state.label"] = "看它是不是",
        ["condition.browserExists.state.hint"] = "问的是元素的哪一种状态。「看得见」是默认，接下来要操作这个"
            + "元素时问的就是它；「看不见」是元素在那里但不占地方；「在页面上」是无论看得见看不见都在；"
            + "「不在页面上」是已经被拿掉。这是一眼的结果，不等待；要等用「等待元素」或包一层等待块。",
        ["condition.browserExists.state.option.visible"] = "看得见",
        ["condition.browserExists.state.option.hidden"] = "看不见",
        ["condition.browserExists.state.option.attached"] = "在页面上",
        ["condition.browserExists.state.option.detached"] = "不在页面上",
        ["condition.browserExists.matchIndex.label"] = "第几个命中",
        ["condition.browserExists.narrow.label"] = "限定",
        ["condition.browserExists.narrow.hint"] = "要判断的那个元素" + NarrowHint,
        ["condition.browserExists.matchIndex.hint"] = PageMatchIndexHint("要判断的"),

        ["condition.browserNotExists.name"] = "页面元素不存在",
        ["condition.browserNotExists.desc"] = "当页面上的某个元素不在指定的状态时为真。",
        ["condition.browserNotExists.target.label"] = "选择器",
        ["condition.browserNotExists.target.hint"] = PageTargetHint + "要判断页面上的哪个元素，写法与"
            + "「点击元素」相同；问的是页面现在有没有这样的元素，所以它不会等。",
        ["condition.browserNotExists.state.label"] = "看它是不是",
        ["condition.browserNotExists.state.hint"] = "问的是元素的哪一种状态，与「页面元素存在」那一格相同。"
            + "要等一个转圈的提示消失，用它加上「看不见」再包一层等待块。",
        ["condition.browserNotExists.state.option.visible"] = "看得见",
        ["condition.browserNotExists.state.option.hidden"] = "看不见",
        ["condition.browserNotExists.state.option.attached"] = "在页面上",
        ["condition.browserNotExists.state.option.detached"] = "不在页面上",
        ["condition.browserNotExists.matchIndex.label"] = "第几个命中",
        ["condition.browserNotExists.narrow.label"] = "限定",
        ["condition.browserNotExists.narrow.hint"] = "要判断的那个元素" + NarrowHint,
        ["condition.browserNotExists.matchIndex.hint"] = PageMatchIndexHint("要判断的"),

        ["condition.expression.name"] = "布尔表达式",
        ["condition.expression.desc"] = "自己写一段表达式，结果为真时条件成立。",
        ["condition.expression.expression.label"] = "表达式",
        ["condition.expression.expression.hint"] =
            "用 $变量、运算符和内置函数拼出一个真 / 假结果，例如 $count > 3 and contains($name, \"ok\")。"
            + "每次判断都会重新求值，所以在等待里它跟着变量一起变。",

        ["condition.compare.name"] = "条件判断",
        ["condition.compare.desc"] = "把一个变量与另一个值或变量进行比较。",
        ["condition.compare.variable.label"] = "变量",
        ["condition.compare.variable.hint"] = "要判断的变量，下拉里的候选项来自前面步骤创建过的变量。",
        ["condition.compare.operator.label"] = "判断方式",
        ["condition.compare.operator.hint"] = "变量与比较值怎么比，默认等于。",
        ["condition.compare.value.label"] = "变量或值",
        ["condition.compare.value.hint"] = "拿来做比较的另一个值：从下拉里选已有变量，或直接输入数字或文本；"
            + "判断方式选“变量存在”时可以留空，选“正则匹配”时这里填的就是正则表达式。",
        ["condition.compare.operator.option.equals"] = "等于",
        ["condition.compare.operator.option.notEquals"] = "不等于",
        ["condition.compare.operator.option.greaterThan"] = "大于",
        ["condition.compare.operator.option.greaterOrEqual"] = "大于等于",
        ["condition.compare.operator.option.lessThan"] = "小于",
        ["condition.compare.operator.option.lessOrEqual"] = "小于等于",
        ["condition.compare.operator.option.contains"] = "包含",
        ["condition.compare.operator.option.notContains"] = "不包含",
        ["condition.compare.operator.option.exists"] = "变量存在",
        ["condition.compare.operator.option.regex"] = "正则匹配",

        ["condition.stepResult.name"] = "步骤结果",
        ["condition.stepResult.desc"] = "判断本宏里某一步跑完的结果。",
        ["condition.stepResult.step.label"] = "步骤",
        ["condition.stepResult.step.hint"] =
            "要问的那一步；候选项是本宏里的每一步，按备注、动作和步骤名称列出。还没轮到的那一步"
            + "答的是“没跑到”，不是“否”。",
        ["condition.stepResult.expected.label"] = "结果应当是",
        ["condition.stepResult.expected.hint"] = "这一步跑完应当是什么结果，默认「成功」：做成了、失败了，还是"
            + "根本没轮到。",
        ["condition.stepResult.expected.option.ok"] = "成功",
        ["condition.stepResult.expected.option.failed"] = "失败",
        ["condition.stepResult.expected.option.skipped"] = "没跑到",

        ["condition.listContains.name"] = "列表包含",
        ["condition.listContains.desc"] = "列表里有某个值时成立。",
        ["condition.listContains.list.label"] = "列表",
        ["condition.listContains.list.hint"] =
            "要查找的列表：直接写成 a;b;c，或者用 $items 指定一个装着列表的变量。",
        ["condition.listContains.value.label"] = "要找的值",
        ["condition.listContains.value.hint"] =
            "必须出现在列表里的值。数字按数字比，1 能找到 1.0；文本比较不分大小写。",

        ["condition.pathExists.name"] = "文件或文件夹存在",
        ["condition.pathExists.desc"] = "路径上有文件或文件夹时成立。",
        ["condition.pathExists.path.label"] = "路径",
        ["condition.pathExists.path.hint"] =
            "必须存在的文件或文件夹。这里只看那个位置上有没有东西，不看它是什么。",

        ["condition.processRunning.name"] = "进程正在运行",
        ["condition.processRunning.desc"] = "某个程序正在运行时成立。",
        ["condition.processRunning.name.label"] = "程序",
        ["condition.processRunning.name.hint"] =
            "程序名，按任务管理器里的写法且不带 .exe：notepad、chrome、excel。",

        ["condition.windowExists.name"] = "窗口存在",
        ["condition.windowExists.desc"] = "某个窗口打开着时成立。",
        ["condition.windowExists.window.label"] = WindowMatchTextLabel,
        ["condition.windowExists.window.hint"] =
            "窗口靠这段文字认出：它和「匹配方式」里选的那一种比，不区分大小写。留空表示只要有一个窗口"
            + "开着就成立。",

        ["condition.valueInRange.name"] = "值在区间内",
        ["condition.valueInRange.desc"] = "一个值落在另外两个值之间时成立。",
        ["condition.valueInRange.value.label"] = "值",
        ["condition.valueInRange.value.hint"] = "要放到数轴上的值，可以填变量或算式。",
        ["condition.valueInRange.min.label"] = "下限",
        ["condition.valueInRange.min.hint"] = "区间的一端，例如 1。两端谁先写都可以。",
        ["condition.valueInRange.max.label"] = "上限",
        ["condition.valueInRange.max.hint"] = "区间的另一端，例如 10。两端都算在内，所以 1 到 10 里包含 1 和 10。",

        ["condition.dateCompare.name"] = "日期比较",
        ["condition.dateCompare.desc"] = "比较两个日期、时间或两者。",
        ["condition.dateCompare.left.label"] = "日期",
        ["condition.dateCompare.left.hint"] =
            "要摆在前面的日期或时间：填变量名如 $sys.dateTime，直接写 2026-01-31 或 09:30，"
            + "也可以用 today() 现算一个。写出来的日期按字面理解，不会当成减法。",
        ["condition.dateCompare.operator.label"] = "比较方式",
        ["condition.dateCompare.operator.hint"] =
            "第一个日期相对第二个日期的位置，默认「晚于」。选“同一天”时不看具体时刻，用来问某件事今天做过"
            + "没有。",
        ["condition.dateCompare.operator.option.before"] = "早于",
        ["condition.dateCompare.operator.option.after"] = "晚于",
        ["condition.dateCompare.operator.option.same"] = "正好等于",
        ["condition.dateCompare.operator.option.sameDay"] = "是同一天",
        ["condition.dateCompare.right.label"] = "比较对象",
        ["condition.dateCompare.right.hint"] = "拿来做比较的日期，写法与上面一样，例如 2026-01-01。",

        ["condition.group.name"] = "条件组",
        ["condition.group.desc"] = "用“与 / 或 / 非”把多个条件组合在一起：与＝全部满足，或＝任一满足，非＝全部不满足。",
        ["condition.group.op.label"] = "逻辑",
        ["condition.group.op.hint"] = "子条件怎么组合，默认「与」；有两个以上子条件时才会生效。",
        ["condition.group.op.option.and"] = "与（全部满足）",
        ["condition.group.op.option.or"] = "或（任一满足）",
        ["condition.group.op.option.none"] = "非（全部不满足）",
        ["condition.group.conditions.label"] = "子条件",
        ["condition.group.conditions.hint"] = "加两个以上条件后，可用上面的逻辑组合它们；条件组可以继续嵌套。",

        ["condition.randomChance.name"] = "随机概率",
        ["condition.randomChance.desc"] = "按固定概率随机为真。",
        ["condition.randomChance.percent.label"] = "概率 %",
        ["condition.randomChance.percent.hint"] = "条件为真的概率，0–100（百分比），默认 50。",

        // --------------------------------------------------- 抓屏方式（看屏幕的步骤都有这一个字段）

        // --------------------------------------------------- 画面复用（看屏幕的步骤也都有这一个字段）
        ["vision.capture.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.capture.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.captureWindow.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.captureWindow.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.findImage.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.findImage.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.waitImage.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.waitImage.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.clickImage.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.clickImage.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.getPixel.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.getPixel.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.waitColor.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.waitColor.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.findColor.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.findColor.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["vision.waitStable.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["vision.waitStable.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["ocr.recognize.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["ocr.recognize.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["ocr.findText.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["ocr.findText.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["ocr.clickText.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["ocr.clickText.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["condition.imageExists.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.imageExists.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["condition.imageNotExists.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.imageNotExists.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["condition.textExists.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.textExists.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["condition.textNotExists.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.textNotExists.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["condition.colorEquals.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.colorEquals.frameMaxAgeMs.hint"] = FrameMaxAgeHint,
        ["condition.colorsMatch.frameMaxAgeMs.label"] = FrameMaxAgeLabel,
        ["condition.colorsMatch.frameMaxAgeMs.hint"] = FrameMaxAgeHint,

        // ------------------------------------------- 查看间隔（等待类拿它当轮询，其余的当下限）
        ["vision.capture.intervalMs.label"] = LookIntervalLabel,
        ["vision.capture.intervalMs.hint"] = LookGapHint,
        ["vision.captureWindow.intervalMs.label"] = LookIntervalLabel,
        ["vision.captureWindow.intervalMs.hint"] = LookGapHint,
        ["vision.findImage.intervalMs.label"] = LookIntervalLabel,
        ["vision.findImage.intervalMs.hint"] = LookGapHint,
        ["vision.waitImage.intervalMs.label"] = LookIntervalLabel,
        ["vision.waitImage.intervalMs.hint"] = LookPollHint,
        ["vision.clickImage.intervalMs.label"] = LookIntervalLabel,
        ["vision.clickImage.intervalMs.hint"] = LookPollHint,
        ["vision.getPixel.intervalMs.label"] = LookIntervalLabel,
        ["vision.getPixel.intervalMs.hint"] = LookGapHint,
        ["vision.waitColor.intervalMs.label"] = LookIntervalLabel,
        ["vision.waitColor.intervalMs.hint"] = LookPollHint,
        ["vision.findColor.intervalMs.label"] = LookIntervalLabel,
        ["vision.findColor.intervalMs.hint"] = LookGapHint,
        ["vision.waitStable.intervalMs.label"] = LookIntervalLabel,
        ["vision.waitStable.intervalMs.hint"] = LookPollHint,
        ["ocr.recognize.intervalMs.label"] = LookIntervalLabel,
        ["ocr.recognize.intervalMs.hint"] = LookGapHint,
        ["ocr.findText.intervalMs.label"] = LookIntervalLabel,
        ["ocr.findText.intervalMs.hint"] = LookGapHint,
        ["ocr.clickText.intervalMs.label"] = LookIntervalLabel,
        ["ocr.clickText.intervalMs.hint"] = LookPollHint,
        ["condition.imageExists.intervalMs.label"] = LookIntervalLabel,
        ["condition.imageExists.intervalMs.hint"] = LookGapHint,
        ["condition.imageNotExists.intervalMs.label"] = LookIntervalLabel,
        ["condition.imageNotExists.intervalMs.hint"] = LookGapHint,
        ["condition.textExists.intervalMs.label"] = LookIntervalLabel,
        ["condition.textExists.intervalMs.hint"] = LookGapHint,
        ["condition.textNotExists.intervalMs.label"] = LookIntervalLabel,
        ["condition.textNotExists.intervalMs.hint"] = LookGapHint,
        ["condition.colorEquals.intervalMs.label"] = LookIntervalLabel,
        ["condition.colorEquals.intervalMs.hint"] = LookGapHint,
        ["condition.colorsMatch.intervalMs.label"] = LookIntervalLabel,
        ["condition.colorsMatch.intervalMs.hint"] = LookGapHint,

        // ------------------------------------------------- 下拉选项（每个动作各一份键）
        // 鼠标按键在八个动作里出现，措辞必须一致，否则同一个下拉框换个动作就变样。
        ["input.mouseClick.button.option.left"] = "左键",
        ["input.mouseClick.button.option.right"] = "右键",
        ["input.mouseClick.button.option.middle"] = "中键",
        ["input.mouseClick.button.option.back"] = "后退键",
        ["input.mouseClick.button.option.forward"] = "前进键",
        ["input.mouseDoubleClick.button.option.left"] = "左键",
        ["input.mouseDoubleClick.button.option.right"] = "右键",
        ["input.mouseDoubleClick.button.option.middle"] = "中键",
        ["input.mouseDoubleClick.button.option.back"] = "后退键",
        ["input.mouseDoubleClick.button.option.forward"] = "前进键",
        ["input.mouseDown.button.option.left"] = "左键",
        ["input.mouseDown.button.option.right"] = "右键",
        ["input.mouseDown.button.option.middle"] = "中键",
        ["input.mouseDown.button.option.back"] = "后退键",
        ["input.mouseDown.button.option.forward"] = "前进键",
        ["input.mouseUp.button.option.left"] = "左键",
        ["input.mouseUp.button.option.right"] = "右键",
        ["input.mouseUp.button.option.middle"] = "中键",
        ["input.mouseUp.button.option.back"] = "后退键",
        ["input.mouseUp.button.option.forward"] = "前进键",
        ["input.mouseDrag.button.option.left"] = "左键",
        ["input.mouseDrag.button.option.right"] = "右键",
        ["input.mouseDrag.button.option.middle"] = "中键",
        ["input.mouseDrag.button.option.back"] = "后退键",
        ["input.mouseDrag.button.option.forward"] = "前进键",
        ["vision.clickImage.button.option.left"] = "左键",
        ["vision.clickImage.button.option.right"] = "右键",
        ["vision.clickImage.button.option.middle"] = "中键",
        ["vision.clickImage.button.option.back"] = "后退键",
        ["vision.clickImage.button.option.forward"] = "前进键",
        ["ocr.clickText.button.option.left"] = "左键",
        ["ocr.clickText.button.option.right"] = "右键",
        ["ocr.clickText.button.option.middle"] = "中键",
        ["ocr.clickText.button.option.back"] = "后退键",
        ["ocr.clickText.button.option.forward"] = "前进键",
        ["uia.click.button.option.left"] = "左键",
        ["uia.click.button.option.right"] = "右键",
        ["uia.click.button.option.middle"] = "中键",
        ["uia.click.button.option.back"] = "后退键",
        ["uia.click.button.option.forward"] = "前进键",

        ["input.mouseScroll.direction.option.up"] = "向上",
        ["input.mouseScroll.direction.option.down"] = "向下",
        ["input.mouseScroll.direction.option.left"] = "向左",
        ["input.mouseScroll.direction.option.right"] = "向右",

        ["ocr.recognize.content.option.text"] = ContentAll,
        ["ocr.recognize.content.option.digits"] = ContentDigits,
        ["ocr.recognize.content.option.letters"] = ContentLetters,
        ["ocr.recognize.content.option.chinese"] = ContentChinese,
        ["ocr.recognize.content.option.lettersAndChinese"] = ContentLettersAndChinese,
        ["ocr.recognize.content.option.regex"] = ContentByPattern,
        ["ocr.findText.content.option.text"] = ContentAll,
        ["ocr.findText.content.option.digits"] = ContentDigits,
        ["ocr.findText.content.option.letters"] = ContentLetters,
        ["ocr.findText.content.option.chinese"] = ContentChinese,
        ["ocr.findText.content.option.lettersAndChinese"] = ContentLettersAndChinese,
        ["ocr.findText.content.option.regex"] = ContentByPattern,
        ["ocr.clickText.content.option.text"] = ContentAll,
        ["ocr.clickText.content.option.digits"] = ContentDigits,
        ["ocr.clickText.content.option.letters"] = ContentLetters,
        ["ocr.clickText.content.option.chinese"] = ContentChinese,
        ["ocr.clickText.content.option.lettersAndChinese"] = ContentLettersAndChinese,
        ["ocr.clickText.content.option.regex"] = ContentByPattern,
        ["ocr.recognize.preprocess.option.none"] = "原样",
        ["ocr.recognize.preprocess.option.greyscale"] = "灰度",
        ["ocr.recognize.preprocess.option.binarize"] = "黑白",
        ["ocr.recognize.preprocess.option.upscale"] = "放大两倍",
        ["ocr.recognize.preprocess.option.upscaleBinarize"] = "放大两倍并黑白",
        ["ocr.findText.preprocess.option.none"] = "原样",
        ["ocr.findText.preprocess.option.greyscale"] = "灰度",
        ["ocr.findText.preprocess.option.binarize"] = "黑白",
        ["ocr.findText.preprocess.option.upscale"] = "放大两倍",
        ["ocr.findText.preprocess.option.upscaleBinarize"] = "放大两倍并黑白",
        ["ocr.clickText.preprocess.option.none"] = "原样",
        ["ocr.clickText.preprocess.option.greyscale"] = "灰度",
        ["ocr.clickText.preprocess.option.binarize"] = "黑白",
        ["ocr.clickText.preprocess.option.upscale"] = "放大两倍",
        ["ocr.clickText.preprocess.option.upscaleBinarize"] = "放大两倍并黑白",

        ["ocr.findText.matchMode.option.contains"] = "包含",
        ["ocr.findText.matchMode.option.startsWith"] = "以…开头",
        ["ocr.findText.matchMode.option.endsWith"] = "以…结尾",
        ["ocr.findText.matchMode.option.equals"] = "等于",
        ["ocr.findText.matchMode.option.regex"] = "正则匹配",
        ["ocr.clickText.matchMode.option.contains"] = "包含",
        ["ocr.clickText.matchMode.option.startsWith"] = "以…开头",
        ["ocr.clickText.matchMode.option.endsWith"] = "以…结尾",
        ["ocr.clickText.matchMode.option.equals"] = "等于",
        ["ocr.clickText.matchMode.option.regex"] = "正则匹配",
        ["condition.textExists.matchMode.option.contains"] = "包含",
        ["condition.textExists.matchMode.option.startsWith"] = "以…开头",
        ["condition.textExists.matchMode.option.endsWith"] = "以…结尾",
        ["condition.textExists.matchMode.option.equals"] = "等于",
        ["condition.textExists.matchMode.option.regex"] = "正则匹配",

        ["control.log.level.option.info"] = "信息",
        ["control.log.level.option.warn"] = "警告",
        ["control.log.level.option.error"] = "错误",
        ["control.log.level.option.debug"] = "调试",

        // control.ask
        ["control.ask.returns.label"] = "要留下的结果",
        ["control.ask.returns.hint"] = ReturnsHintText,

        // control.input
        ["control.input.returns.label"] = "要留下的结果",
        ["control.input.returns.hint"] = ReturnsHintText,

        // control.waitUntil
        ["control.waitUntil.return.elapsed.label"] = "用时",
        ["control.waitUntil.return.elapsed.hint"] = "这次等待实际花了多少毫秒，等到和超时都会写。",
        ["control.waitUntil.returns.label"] = "要留下的结果",
        ["control.waitUntil.returns.hint"] = ReturnsHintText,

        // control.try
        ["control.try.return.error.label"] = "错误信息",
        ["control.try.return.error.hint"] = "尝试失败的原因；没有失败时为空。",
        ["control.try.returns.label"] = "要留下的结果",
        ["control.try.returns.hint"] = ReturnsHintText,

        // file.readText
        ["file.readText.return.text.label"] = "文本",
        ["file.readText.return.text.hint"] = "文件里的内容；按“每行一项”读出来时是一个列表。",
        ["file.readText.returns.label"] = "要留下的结果",
        ["file.readText.returns.hint"] = ReturnsHintText,

        // file.exists
        ["file.exists.return.exists.label"] = "存在",
        ["file.exists.return.exists.hint"] = "文件或文件夹在不在。",
        ["file.exists.returns.label"] = "要留下的结果",
        ["file.exists.returns.hint"] = ReturnsHintText,

        // file.path
        ["file.path.return.path.label"] = "路径",
        ["file.path.return.path.hint"] = "算出来的路径。",
        ["file.path.returns.label"] = "要留下的结果",
        ["file.path.returns.hint"] = ReturnsHintText,

        // file.listFiles
        ["file.listFiles.return.files.label"] = "文件",
        ["file.listFiles.return.files.hint"] = "收集到的文件完整路径。",
        ["file.listFiles.returns.label"] = "要留下的结果",
        ["file.listFiles.returns.hint"] = ReturnsHintText,

        // file.readJson
        ["file.readJson.return.value.label"] = "值",
        ["file.readJson.return.value.hint"] = "读到的值；取不到时为空。",
        ["file.readJson.returns.label"] = "要留下的结果",
        ["file.readJson.returns.hint"] = ReturnsHintText,

        // file.readCsv
        ["file.readCsv.return.rows.label"] = "行",
        ["file.readCsv.return.rows.hint"] = "读到的行，每行是一条记录：键就是表头那一行里的列名，用 field(行, \"金额\") 按名字取一个单元格；第一行不是表头时列名是 A、B、C。",
        ["file.readCsv.returns.label"] = "要留下的结果",
        ["file.readCsv.returns.hint"] = ReturnsHintText,

        // data.base64Encode
        ["data.base64Encode.return.encoded.label"] = "编码结果",
        ["data.base64Encode.return.encoded.hint"] = "写成 Base64 之后的文本。",
        ["data.base64Encode.returns.label"] = "要留下的结果",
        ["data.base64Encode.returns.hint"] = "勾上这一步要留下的结果：每一项各自成为一个变量，后面的步骤按名字取用。一个都不勾也可以，这一步就不留下任何结果。",

        // data.base64Decode
        ["data.base64Decode.return.decoded.label"] = "解码文本",
        ["data.base64Decode.return.decoded.hint"] = "Base64 里藏的文本，按 UTF-8 读出来。",
        ["data.base64Decode.returns.label"] = "要留下的结果",
        ["data.base64Decode.returns.hint"] = "勾上这一步要留下的结果：每一项各自成为一个变量，后面的步骤按名字取用。一个都不勾也可以，这一步就不留下任何结果。",

        // data.hash
        ["data.hash.return.digest.label"] = "校验和",
        ["data.hash.return.digest.hint"] = "用小写字母和数字写出来的校验和。",
        ["data.hash.returns.label"] = "要留下的结果",
        ["data.hash.returns.hint"] = ReturnsHintText,

        // clipboard.readText
        ["clipboard.readText.return.text.label"] = "文本",
        ["clipboard.readText.return.text.hint"] = "剪贴板上的文字。",
        ["clipboard.readText.returns.label"] = "要留下的结果",
        ["clipboard.readText.returns.hint"] = ReturnsHintText,

        // clipboard.readImage
        ["clipboard.readImage.return.image.label"] = "图片",
        ["clipboard.readImage.return.image.hint"] = "剪贴板上的图片，可以交给找图步骤在屏幕上找；它没有屏幕位置，所以没有坐标。",
        ["clipboard.readImage.returns.label"] = "要留下的结果",
        ["clipboard.readImage.returns.hint"] = ReturnsHintText,

        // clipboard.readFiles
        ["clipboard.readFiles.return.files.label"] = "文件",
        ["clipboard.readFiles.return.files.hint"] = "剪贴板上文件的完整路径；一个文件都没有时是空列表。",
        ["clipboard.readFiles.returns.label"] = "要留下的结果",
        ["clipboard.readFiles.returns.hint"] = ReturnsHintText,

        // clipboard.waitChange
        ["clipboard.waitChange.return.text.label"] = "文本",
        ["clipboard.waitChange.return.text.hint"] = "新复制上来的文字；只等待不保存时不用勾。",
        ["clipboard.waitChange.returns.label"] = "要留下的结果",
        ["clipboard.waitChange.returns.hint"] = ReturnsHintText,

        // clipboard.copy
        ["clipboard.copy.return.text.label"] = "文本",
        ["clipboard.copy.return.text.hint"] = "复制到剪贴板上的内容。",
        ["clipboard.copy.returns.label"] = "要留下的结果",
        ["clipboard.copy.returns.hint"] = ReturnsHintText,

        // process.start
        ["process.start.return.processId.label"] = "进程号",
        ["process.start.return.processId.hint"] = "程序的编号。",
        ["process.start.returns.label"] = "要留下的结果",
        ["process.start.returns.hint"] = ReturnsHintText,

        // process.waitFor
        ["process.waitFor.return.process.label"] = "进程号",
        ["process.waitFor.return.process.hint"] = "出现的那个程序的编号。",
        ["process.waitFor.returns.label"] = "要留下的结果",
        ["process.waitFor.returns.hint"] = ReturnsHintText,

        // process.waitExit
        ["process.waitExit.return.exitCode.label"] = "退出码",
        ["process.waitExit.return.exitCode.hint"] = "结束时返回的代码，0 通常表示正常。",
        ["process.waitExit.returns.label"] = "要留下的结果",
        ["process.waitExit.returns.hint"] = ReturnsHintText,

        // process.exists
        ["process.exists.return.running.label"] = "在运行",
        ["process.exists.return.running.hint"] = "程序在不在运行。",
        ["process.exists.returns.label"] = "要留下的结果",
        ["process.exists.returns.hint"] = ReturnsHintText,

        // process.list
        ["process.list.return.processes.label"] = "进程列表",
        ["process.list.return.processes.hint"] = "正在运行的程序的名称。",
        ["process.list.returns.label"] = "要留下的结果",
        ["process.list.returns.hint"] = ReturnsHintText,

        // process.info
        ["process.info.return.program.label"] = "程序",
        ["process.info.return.program.hint"] =
            "这个程序，一条记录：「info」（程序所在的文件，Windows 不肯说时为空）、「id」（进程号）、"
            + "「name」（名称）、「memoryMb」（占用的内存，单位是兆字节）、「cpuSeconds」（累计用掉的处理器"
            + "时间，单位是秒）。取其中一项用 field($变量, \"id\")。",
        ["process.info.returns.label"] = "要留下的结果",
        ["process.info.returns.hint"] = ReturnsHintText,

        // process.kill
        ["process.kill.return.stopped.label"] = "已结束",
        ["process.kill.return.stopped.hint"] = "一共关掉了几个程序。",
        ["process.kill.returns.label"] = "要留下的结果",
        ["process.kill.returns.hint"] = ReturnsHintText,

        // command.run
        ["command.run.return.output.label"] = "输出",
        ["command.run.return.output.hint"] = "命令打印到标准输出的内容。",
        ["command.run.return.error.label"] = "错误输出",
        ["command.run.return.error.hint"] = "命令打印到错误流的内容。",
        ["command.run.return.exitCode.label"] = "退出码",
        ["command.run.return.exitCode.hint"] = "命令结束时返回的代码，0 通常表示成功。",
        ["command.run.returns.label"] = "要留下的结果",
        ["command.run.returns.hint"] = ReturnsHintText,

        // system.info
        ["system.info.return.value.label"] = "值",
        ["system.info.return.value.hint"] = "读到的这项系统信息。",
        ["system.info.returns.label"] = "要留下的结果",
        ["system.info.returns.hint"] = ReturnsHintText,

        // system.environment
        ["system.environment.return.value.label"] = "值",
        ["system.environment.return.value.hint"] = "环境变量的内容；没有设置时为空。",
        ["system.environment.returns.label"] = "要留下的结果",
        ["system.environment.returns.hint"] = ReturnsHintText,

        // system.volume
        ["system.volume.return.volume.label"] = "音量",
        ["system.volume.return.volume.hint"] = "机器最后停在的音量，0 到 100；只执行静音的步骤也会报它停在哪一级。",
        ["system.volume.returns.label"] = "要留下的结果",
        ["system.volume.returns.hint"] = ReturnsHintText,

        // system.ime
        ["system.ime.return.ime.label"] = "输入法",
        ["system.ime.return.ime.hint"] = "正在使用的键盘布局，或者可以切换过去的布局列表。",
        ["system.ime.returns.label"] = "要留下的结果",
        ["system.ime.returns.hint"] = ReturnsHintText,

        // system.brightness
        ["system.brightness.return.brightness.label"] = "亮度",
        ["system.brightness.return.brightness.hint"] = "屏幕最后停在的亮度，0 到 100。",
        ["system.brightness.returns.label"] = "要留下的结果",
        ["system.brightness.returns.hint"] = ReturnsHintText,

        // window.exists
        ["window.exists.return.found.label"] = "找到",
        ["window.exists.return.found.hint"] = "有匹配的窗口开着时为真。",
        ["window.exists.returns.label"] = "要留下的结果",
        ["window.exists.returns.hint"] = ReturnsHintText,

        // window.waitFor
        ["window.waitFor.return.title.label"] = "标题",
        ["window.waitFor.return.title.hint"] = "窗口标题栏上的文字。",
        ["window.waitFor.returns.label"] = "要留下的结果",
        ["window.waitFor.returns.hint"] = ReturnsHintText,

        // window.list
        ["window.list.return.windows.label"] = "窗口列表",
        ["window.list.return.windows.hint"] =
            "匹配到的窗口，一个窗口一条记录，按系统列出的顺序排。同名窗口是两个窗口，各占一条。每条记录与"
            + "window.info 给的那一条同形（少一个客户区）：「title」（标题）、「handle」（句柄，标题相同时"
            + "用它区分）、「minimized」「maximized」（是否收进任务栏、是否铺满屏幕）、「x」「y」（窗口"
            + "矩形的中心，也就是动作要点的那一点）、「left」「top」"
            + "「width」「height」（矩形四个数）与「box」（同一个矩形，写成 左,上,宽,高，填进搜索区域那一"
            + "行的变量格里，下一步就在那个窗口里找），再加上「process」（程序名，不含 .exe）与「class」"
            + "（窗口类名，说明窗口的种类而不是里面的文档，记事本改了名也还是 Notepad），这两格要勾上"
            + "「每个窗口都读程序名与类名（慢）」才有，没读的留空。读其中一条的一个字段用 "
            + "field(get($变量, 0), \"title\")，逐个走一遍用 forEach。",
        ["window.list.returns.label"] = "要留下的结果",
        ["window.list.returns.hint"] = ReturnsHintText,

        // window.info
        ["window.info.return.window.label"] = "窗口",
        ["window.info.return.window.hint"] =
            "这个窗口，一条记录（与窗口列表里的一条同形，另加一个客户区）：「title」（标题）、「process」"
            + "（程序名，不含 .exe，也就是按进程名筛选时比较的那一项）、「class」（窗口类名，例如 Notepad，"
            + "说明的是窗口的种类而不是里面的文档）、「handle」（句柄，标题相同时用它区分）、「minimized」"
            + "「maximized」（是否收进任务栏、是否铺满屏幕）、「x」「y」（窗口矩形的中心，也就是动作要点"
            + "的那一点）、「left」「top」「width」「height」（矩形四个"
            + "数）、「box」（同一个矩形，写成 左,上,宽,高，整条记录直接填进搜索区域那一行的变量格即可看同一"
            + "块）与「clientBox」（窗口边框以内、标题栏以下的那一块，写成 左,上,宽,高，坐标基准选「窗口客户"
            + "区」的这一步看的就是它）。取其中一项用 field($变量, \"left\")。给的是窗口被看见的那个矩形："
            + "Windows 留着用来拉伸的那圈边框不画出来也不算，最大化的窗口按它在屏幕上的样子量。",
        ["window.info.returns.label"] = "要留下的结果",
        ["window.info.returns.hint"] = ReturnsHintText,

        // vision.capture
        ["vision.capture.return.image.label"] = "图片",
        ["vision.capture.return.image.hint"] = "截到的图片，可以拿去搜索，也可以写进文件。",
        ["vision.capture.return.place.label"] = "位置",
        ["vision.capture.return.place.hint"] =
            "截到的那块在哪里，一条记录：「x」「y」（这块画面的中心，也就是动作要点的那一点）、「left」"
            + "「top」（左上角在屏幕上的位置，单位是屏幕像素，和搜索区域"
            + "的「左上角 X」「左上角 Y」同一个口径）、「width」「height」（大小，单位是像素；给的是图片自己"
            + "的大小，贴着屏幕边缘截时可能比要的小）、「box」（整块写成 左,上,宽,高，填进搜索区域那一行的"
            + "变量格即可看同一块）。取其中一项用 field($变量, \"left\")。",
        ["vision.capture.returns.label"] = "要留下的结果",
        ["vision.capture.returns.hint"] = ReturnsHintText,

        // vision.captureWindow
        ["vision.captureWindow.return.image.label"] = "图片",
        ["vision.captureWindow.return.image.hint"] = "截到的整窗图片，连标题栏一起。",
        ["vision.captureWindow.return.place.label"] = "位置",
        ["vision.captureWindow.return.place.hint"] =
            "窗口在哪里，一条记录：「x」「y」（窗口矩形的中心，也就是动作要点的那一点）、「left」「top」"
            + "（窗口左上角在屏幕上的位置，单位是屏幕像素）、「width」"
            + "「height」（窗口的大小，单位是像素）、「box」（整块写成 左,上,宽,高，填进搜索区域那一行的"
            + "变量格即可在窗口里找）。取其中一项用 field($变量, \"left\")。",
        ["vision.captureWindow.returns.label"] = "要留下的结果",
        ["vision.captureWindow.returns.hint"] = ReturnsHintText,

        // vision.findImage
        ["vision.findImage.returns.label"] = "要留下的结果",
        ["vision.findImage.returns.hint"] = ReturnsHintText,

        // vision.waitImage
        ["vision.waitImage.return.match.label"] = HitRecordLabel,
        ["vision.waitImage.return.match.hint"] = HitRecordHintImage,
        ["vision.waitImage.return.every.label"] = EveryHitLabel,
        ["vision.waitImage.return.every.hint"] = EveryHitHint,
        ["vision.waitImage.returns.label"] = "要留下的结果",
        ["vision.waitImage.returns.hint"] = ReturnsHintText,

        // vision.clickImage
        ["vision.clickImage.return.match.label"] = HitRecordLabel,
        ["vision.clickImage.return.match.hint"] = HitRecordHintImage,
        ["vision.clickImage.return.every.label"] = EveryHitLabel,
        ["vision.clickImage.return.every.hint"] = EveryHitHint,
        ["vision.clickImage.returns.label"] = "要留下的结果",
        ["vision.clickImage.returns.hint"] = ReturnsHintText,

        // vision.getPixel
        ["vision.getPixel.return.color.label"] = "颜色",
        ["vision.getPixel.return.color.hint"] = "这个像素的颜色。",
        ["vision.getPixel.returns.label"] = "要留下的结果",
        ["vision.getPixel.returns.hint"] = ReturnsHintText,

        // vision.findColor
        ["vision.findColor.return.coverage.label"] = "占比 %",
        ["vision.findColor.return.coverage.hint"] =
            "整个搜索区域里有多少比例是那个颜色，0–100 保留一位小数，多行区域合起来算一个。"
            + "找没找到都会写，所以占比不够时能读出来看差多少。",
        ["vision.findColor.returns.label"] = "要留下的结果",
        ["vision.findColor.returns.hint"] = ReturnsHintText,

        // ocr.recognize
        ["ocr.recognize.return.text.label"] = "文本",
        ["ocr.recognize.return.text.hint"] = "识别出来的文字。",
        ["ocr.recognize.return.table.label"] = "表格",
        ["ocr.recognize.return.table.hint"] = "把内容按行列留成表格，每项是一条记录，键是格子的列号"
            + "（A、B、C），与没有表头的文件同一套叫法；只有勾上“按表格读”才有。",
        ["ocr.recognize.returns.label"] = "要留下的结果",
        ["ocr.recognize.returns.hint"] = ReturnsHintText,

        // ocr.findText
        ["ocr.findText.returns.label"] = "要留下的结果",
        ["ocr.findText.returns.hint"] = ReturnsHintText,

        // ocr.clickText
        ["ocr.clickText.return.match.label"] = HitRecordLabel,
        ["ocr.clickText.return.match.hint"] = HitRecordHintText,
        ["ocr.clickText.return.every.label"] = EveryHitLabel,
        ["ocr.clickText.return.every.hint"] = EveryHitHint,
        ["ocr.clickText.returns.label"] = "要留下的结果",
        ["ocr.clickText.returns.hint"] = ReturnsHintText,

        // uia.exists
        ["uia.exists.return.exists.label"] = "存在",
        ["uia.exists.return.exists.hint"] = "元素在不在。",
        ["uia.exists.returns.label"] = "要留下的结果",
        ["uia.exists.returns.hint"] = ReturnsHintText,

        // uia.find
        ["uia.find.returns.label"] = "要留下的结果",
        ["uia.find.returns.hint"] = ReturnsHintText,

        // uia.read
        ["uia.read.returns.label"] = "要留下的结果",
        ["uia.read.returns.hint"] = ReturnsHintText,

        // uia.readTable
        ["uia.readTable.return.table.label"] = "表格",
        ["uia.readTable.return.table.hint"] = "读到的行，每行是一条记录：键就是表格列标题那一行写的"
            + "列名，用 field(get($变量, 0), \"金额\") 按名字取一个单元格；没有列标题时列名是 A、B、C，"
            + "列名为空或与前面重名的列也用它的列号。",
        ["uia.readTable.returns.label"] = "要留下的结果",
        ["uia.readTable.returns.hint"] = ReturnsHintText,

        // browser.readText
        ["browser.readText.return.text.label"] = "文本",
        ["browser.readText.return.text.hint"] = "读到的内容：元素显示的文字、它带着的值，或者它的某个"
            + "属性。",
        ["browser.readText.returns.label"] = "要留下的结果",
        ["browser.readText.returns.hint"] = ReturnsHintText,

        // browser.readAll
        ["browser.readAll.return.items.label"] = "列表",
        ["browser.readAll.return.items.hint"] = "读到的全部内容，一条一项，按它们在页面上出现的顺序排列。"
            + "用 forEach 逐条走，或用 count() 数一数。",
        ["browser.readAll.returns.label"] = "要留下的结果",
        ["browser.readAll.returns.hint"] = ReturnsHintText,

        // browser.readTable
        ["browser.readTable.return.table.label"] = "表格",
        ["browser.readTable.return.table.hint"] = "读到的行，每行是一条记录：键就是表格列标题那一行写的"
            + "列名。页面把列名写在 thead 或第一行的 th 里才算列标题，否则列名是 A、B、C；"
            + "用 field(get($变量, 0), \"金额\") 按名字取一个单元格。",
        ["browser.readTable.returns.label"] = "要留下的结果",
        ["browser.readTable.returns.hint"] = ReturnsHintText,

        // browser.readPage
        ["browser.readPage.return.url.label"] = "网址",
        ["browser.readPage.return.url.hint"] = "当前页面的地址，用来判断点击之后浏览器停在哪一页。",
        ["browser.readPage.return.title.label"] = "标题",
        ["browser.readPage.return.title.hint"] = "当前页面自己写的名字，也就是标签页上显示的那段字。",
        ["browser.readPage.returns.label"] = "要留下的结果",
        ["browser.readPage.returns.hint"] = ReturnsHintText,

        // browser.download
        ["browser.download.return.path.label"] = "保存为",
        ["browser.download.return.path.hint"] = "文件保存到了哪里，路径按程序读路径的规矩写；"
            + "把它交给后面的步骤，就会存在同一个地方。",
        ["browser.download.return.name.label"] = "文件名",
        ["browser.download.return.name.hint"] = "页面给这个文件起的名字。",
        ["browser.download.returns.label"] = "要留下的结果",
        ["browser.download.returns.hint"] = ReturnsHintText,

        // browser.dialog
        ["browser.dialog.return.message.label"] = "说了什么",
        ["browser.dialog.return.message.hint"] = "被回答的那个对话框说的话，例如页面在继续之前问的那一句。",
        ["browser.dialog.returns.label"] = "要留下的结果",
        ["browser.dialog.returns.hint"] = ReturnsHintText,

        // browser.evaluate
        ["browser.evaluate.return.result.label"] = "结果",
        ["browser.evaluate.return.result.hint"] = "脚本交回的东西，按 JSON 写出来：文字带引号，数字还是数字，"
            + "记录和列表保持原样，用 field() 取其中的一项。脚本什么都没交回时这一项是空的。",
        ["browser.evaluate.returns.label"] = "要留下的结果",
        ["browser.evaluate.returns.hint"] = ReturnsHintText,

        // script.run
        ["script.run.return.output.label"] = "输出",
        ["script.run.return.output.hint"] = "脚本打印的内容，去掉了前后的空行。只有不显示窗口的类型收得到；"
            + "在窗口里跑的命令把输出写在那个窗口上，这一项是空的。",
        ["script.run.return.error.label"] = "错误消息",
        ["script.run.return.error.hint"] = "脚本在错误输出上写的内容，或解释器自己报的错。脚本正常结束时为空；"
            + "在窗口里跑的命令没有地方写给它，这一项也是空的。",
        ["script.run.return.success.label"] = "是否执行成功",
        ["script.run.return.success.hint"] = "脚本以退出码 0 结束为真，否则为假。退出码不是 0 时这一步也会判失败，"
            + "所以这一项是在失败策略放行之后读的。",
        ["script.run.returns.label"] = "要留下的结果",
        ["script.run.returns.hint"] = ReturnsHintText,

        // script.python
        ["script.python.return.value.label"] = "返回值",
        ["script.python.return.value.hint"] = "脚本用 WhaleGenie.SetResult 交回的那个值，原样保留："
            + "数字、文字、真假、列表，或者一条带字段的记录。脚本没交过就是空的。",
        ["script.python.return.error.label"] = "错误消息",
        ["script.python.return.error.hint"] = "脚本在错误输出上写的内容，Python 报的错就写在这里，"
            + "带着出错的行号。脚本顺利跑完时为空。",
        ["script.python.return.success.label"] = "是否执行成功",
        ["script.python.return.success.hint"] = "脚本没有出错并跑完为真，否则为假。脚本失败时这一步也会判失败，"
            + "所以这一项是在失败策略放行之后读的。",
        ["script.python.returns.label"] = "要留下的结果",
        ["script.python.returns.hint"] = ReturnsHintText,

        // script.pip
        ["script.pip.return.packages.label"] = "装上的包",
        ["script.pip.return.packages.hint"] = "这一次真的装进去的包，一项一个。要的包都已经在时这一项为空。",
        ["script.pip.return.error.label"] = "错误消息",
        ["script.pip.return.error.hint"] = "pip 没能做成这件事时写在错误输出上的内容。顺利时为空。",
        ["script.pip.return.success.label"] = "是否执行成功",
        ["script.pip.return.success.hint"] = "pip 顺利结束为真，否则为假；失败时这一步也会判失败。",
        ["script.pip.returns.label"] = "要留下的结果",
        ["script.pip.returns.hint"] = ReturnsHintText,

        // script.csharp
        ["script.csharp.return.value.label"] = "返回值",
        ["script.csharp.return.value.hint"] = "脚本用 WhaleGenie.SetResult 交回的那个值，原样保留："
            + "数字、文字、真假、列表，或者一条带字段的记录。脚本没交过就是空的。",
        ["script.csharp.return.error.label"] = "错误消息",
        ["script.csharp.return.error.hint"] = "脚本编译不过时编译器说的话，或者运行中出的错，"
            + "都带着出错的行号。脚本顺利跑完时为空。",
        ["script.csharp.return.success.label"] = "是否执行成功",
        ["script.csharp.return.success.hint"] = "脚本编译通过并跑完为真，否则为假。脚本失败时这一步也会判失败，"
            + "所以这一项是在失败策略放行之后读的。",
        ["script.csharp.returns.label"] = "要留下的结果",
        ["script.csharp.returns.hint"] = ReturnsHintText,

        // script.ahk
        ["script.ahk.return.value.label"] = "返回值",
        ["script.ahk.return.value.hint"] = "脚本用 SetResult 交回的那个值，原样保留：数字、文字、真假、列表，"
            + "或者一条带字段的记录。脚本没交过就是空的。",
        ["script.ahk.return.error.label"] = "错误消息",
        ["script.ahk.return.error.hint"] = "脚本在错误输出上写的内容，AutoHotkey 报的错就写在这里，"
            + "带着出错的行号。脚本顺利跑完时为空。",
        ["script.ahk.return.success.label"] = "是否执行成功",
        ["script.ahk.return.success.hint"] = "脚本没有出错并跑完为真，否则为假。脚本失败时这一步也会判失败，"
            + "所以这一项是在失败策略放行之后读的。",
        ["script.ahk.returns.label"] = "要留下的结果",
        ["script.ahk.returns.hint"] = ReturnsHintText,

        // script.nuget
        ["script.nuget.return.packages.label"] = "下载的包",
        ["script.nuget.return.packages.hint"] = "这一次真的下下来的包，一项一个，连着取到的是哪个版本。"
            + "要的包都已经在时这一项为空。",
        ["script.nuget.return.error.label"] = "错误消息",
        ["script.nuget.return.error.hint"] = "包没能下下来时说清是哪一处：找不到这个包、连不上源，"
            + "或者 NuGet 自己报的错。顺利时为空。",
        ["script.nuget.return.success.label"] = "是否执行成功",
        ["script.nuget.return.success.hint"] = "要的包都就绪为真，否则为假；失败时这一步也会判失败。",
        ["script.nuget.returns.label"] = "要留下的结果",
        ["script.nuget.returns.hint"] = ReturnsHintText,

        // vision.waitImage

        // vision.clickImage

        // ocr.clickText

        // control.runMacro
        ["control.runMacro.returns.label"] = "要带回的结果",
        ["control.runMacro.returns.hint"] = "勾上被调用的宏声明过、这一步要取回的结果："
            + "每一项在本宏里各自成为一个变量，名字由这一行决定。一个都不勾也可以，"
            + "这一步就不带回任何值。",
    };

    /// <summary>
    /// The one choice every window rule asks is the same five ways wherever a window is recognised,
    /// so it is worded once and written into the table for each action that asks it — sixty fields
    /// with the same five lines written out is sixty chances for the wording to drift apart. What
    /// the settings beside the field do differs from one family to the next, which is the sentence
    /// each of them adds.
    /// </summary>
    static ActionStrings()
    {
        WindowHowFill(Chinese, "how", WindowActions, string.Empty);
        WindowHowFill(Chinese, "anchorHow", AnchorActions, "坐标基准选屏幕像素或界面元素时用不到它。");
        WindowHowFill(Chinese, "targetHow", TargetActions, "只有输入方式选后台消息时用得到它。");

        // The element actions recognise the window they look in with the same two fields the window
        // actions use, and they all say the same thing about an empty one. Bringing a window to the
        // front is the one of them with nothing to do without a match text, so its own line says so.
        WindowHowFill(Chinese, "how", [.. ElementActions, "uia.focusWindow"], ElementLookInExtra);
        foreach (var action in ElementActions)
        {
            Chinese[$"{action}.window.label"] = WindowMatchTextLabel;
            Chinese[$"{action}.window.hint"] = ElementWindowHint;
        }

        WindowHowFill(Chinese, "how", ["condition.windowExists"],
            "不填「匹配内容」时问的是有没有窗口开着。");
    }

    private static void WindowHowFill(Dictionary<string, string> table, string name,
        string[] actions, string extra)
    {
        var hint = extra.Length == 0 ? WindowHowHint : WindowHowHint + extra;
        foreach (var action in actions)
        {
            table[$"{action}.{name}.label"] = WindowHowLabel;
            table[$"{action}.{name}.hint"] = hint;
            table[$"{action}.{name}.option.titleContains"] = WindowHowTitleContains;
            table[$"{action}.{name}.option.titleStartsWith"] = WindowHowTitleStartsWith;
            table[$"{action}.{name}.option.titleEndsWith"] = WindowHowTitleEndsWith;
            table[$"{action}.{name}.option.titleEquals"] = WindowHowTitleEquals;
            table[$"{action}.{name}.option.titleRegex"] = WindowHowTitleRegex;
            table[$"{action}.{name}.option.process"] = WindowHowProcess;
            table[$"{action}.{name}.option.processEquals"] = WindowHowProcessEquals;
            table[$"{action}.{name}.option.class"] = WindowHowClass;
            table[$"{action}.{name}.option.classEquals"] = WindowHowClassEquals;
        }
    }

    /// <summary>The actions that name the one window they act on.</summary>
    private static readonly string[] WindowActions =
    [
        "window.exists", "window.waitFor", "window.activate", "window.minimize", "window.maximize",
        "window.restore", "window.move", "window.close", "window.list", "window.info",
        "vision.captureWindow",
    ];

    /// <summary>
    /// The steps that name the window their coordinates are counted from, which is also the window
    /// their pixels come out of.
    /// </summary>
    private static readonly string[] AnchorActions =
    [
        "vision.capture", "vision.findImage", "vision.waitImage", "vision.clickImage",
        "vision.getPixel", "vision.waitColor", "vision.findColor", "vision.findColors",
        "vision.waitStable", "ocr.recognize", "ocr.findText", "ocr.clickText",
        "condition.imageExists", "condition.imageNotExists", "condition.textExists",
        "condition.textNotExists", "condition.colorEquals", "condition.colorsMatch",
        "condition.colorsExist", "condition.colorCoverage",
        "input.mouseMove", "input.mouseClick", "input.mouseDoubleClick", "input.mouseDown",
        "input.mouseUp", "input.mouseScroll", "input.mouseDrag",
    ];

    /// <summary>
    /// The element actions look inside a window of their own, named with the same two fields the
    /// window actions use. Bringing a window to the front asks the same choice but cannot be left
    /// without a match text, so it is worded apart from these.
    /// </summary>
    private static readonly string[] ElementActions =
    [
        "uia.exists", "uia.find", "uia.waitElement", "uia.click", "uia.invoke", "uia.setText",
        "uia.read", "uia.select", "uia.check", "uia.expand", "uia.scrollIntoView",
        "uia.readTable", "condition.uiaExists", "condition.uiaNotExists",
    ];

    /// <summary>The actions that post their input at a window of their own.</summary>
    private static readonly string[] TargetActions =
    [
        "input.keyPress", "input.keyDown", "input.keyUp", "input.keySequence", "input.hotkey",
        "input.typeText", "input.mouseMove", "input.mouseMoveRelative", "input.mouseClick",
        "input.mouseDoubleClick", "input.mouseDown", "input.mouseUp", "input.mouseScroll",
        "input.mouseDrag",
    ];
}
