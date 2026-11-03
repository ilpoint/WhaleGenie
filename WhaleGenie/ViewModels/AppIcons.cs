using Lucide.Avalonia;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Semantic names used by the interface, mapped to the Lucide icon set. Keeping the names here
/// means call sites describe what an icon means rather than which artwork it happens to use.
/// </summary>
public static class AppIcons
{
    public static LucideIconKind Workflow => LucideIconKind.Workflow;
    public static LucideIconKind File => LucideIconKind.File;
    public static LucideIconKind Variable => LucideIconKind.Variable;
    public static LucideIconKind Clipboard => LucideIconKind.Clipboard;
    public static LucideIconKind Cpu => LucideIconKind.Cpu;
    public static LucideIconKind Settings => LucideIconKind.Settings;
    public static LucideIconKind AppWindow => LucideIconKind.AppWindow;
    public static LucideIconKind Keyboard => LucideIconKind.Keyboard;
    public static LucideIconKind Eye => LucideIconKind.Eye;
    public static LucideIconKind TextScan => LucideIconKind.ScanText;
    public static LucideIconKind CursorClick => LucideIconKind.MousePointerClick;
    public static LucideIconKind Browser => LucideIconKind.Globe;
    public static LucideIconKind Code => LucideIconKind.Code;
    public static LucideIconKind Split => LucideIconKind.Split;
    public static LucideIconKind Undo => LucideIconKind.Undo2;
    public static LucideIconKind Redo => LucideIconKind.Redo2;
    public static LucideIconKind Pencil => LucideIconKind.Pencil;
    public static LucideIconKind Toggle => LucideIconKind.ToggleLeft;
    public static LucideIconKind Copy => LucideIconKind.Copy;
    public static LucideIconKind Scissors => LucideIconKind.Scissors;
    public static LucideIconKind Paste => LucideIconKind.ClipboardPaste;
    public static LucideIconKind Trash => LucideIconKind.Trash;
    public static LucideIconKind Group => LucideIconKind.Group;
    public static LucideIconKind ExternalLink => LucideIconKind.ExternalLink;
    public static LucideIconKind ArrowUp => LucideIconKind.ArrowUp;
    public static LucideIconKind ArrowDown => LucideIconKind.ArrowDown;
    public static LucideIconKind X => LucideIconKind.X;
    public static LucideIconKind Clock => LucideIconKind.Clock;
    public static LucideIconKind Play => LucideIconKind.Play;
    public static LucideIconKind BugPlay => LucideIconKind.BugPlay;
    public static LucideIconKind Pause => LucideIconKind.Pause;
    public static LucideIconKind GridFour => LucideIconKind.Grid2x2;
    public static LucideIconKind Minus => LucideIconKind.Minus;
    public static LucideIconKind Maximize => LucideIconKind.Maximize;
    public static LucideIconKind Restore => LucideIconKind.Copy;
    public static LucideIconKind Sun => LucideIconKind.Sun;
    public static LucideIconKind Pin => LucideIconKind.Pin;
    public static LucideIconKind PinOff => LucideIconKind.PinOff;
    public static LucideIconKind Ban => LucideIconKind.Ban;
    public static LucideIconKind Moon => LucideIconKind.Moon;
    public static LucideIconKind Folder => LucideIconKind.Folder;
    public static LucideIconKind FolderOpen => LucideIconKind.FolderOpen;
    public static LucideIconKind Save => LucideIconKind.Save;
    public static LucideIconKind FilePlus => LucideIconKind.FilePlus;
    public static LucideIconKind Download => LucideIconKind.Download;
    public static LucideIconKind Search => LucideIconKind.Search;
    public static LucideIconKind Reload => LucideIconKind.RefreshCw;
    public static LucideIconKind Sparkle => LucideIconKind.Sparkles;
    public static LucideIconKind Message => LucideIconKind.MessageCircle;
    public static LucideIconKind QuestionMark => LucideIconKind.CircleQuestionMark;

    /// <summary>
    /// 动作表单里每一块问的是什么。块头原来是一个圈里的序号，序号只说得清"第几块"，说不出
    /// "这一块问什么"；换成图标以后，扫一眼就知道该往哪儿看。没有对应的块返回 null，那里仍然
    /// 显示序号，不至于空着一个圈。
    /// </summary>
    public static LucideIconKind? Block(string group) => group switch
    {
        // 找东西：在哪儿、看哪一点、找什么、什么颜色、怎样算找到、按内容挑
        "area" => LucideIconKind.Crop,
        "point" => LucideIconKind.Crosshair,
        "target" => LucideIconKind.ScanSearch,
        "colour" => LucideIconKind.Palette,
        "content" => LucideIconKind.ListFilter,
        "found" => LucideIconKind.CheckCheck,
        "stable" => LucideIconKind.Focus,

        // 动作本身：怎么点、发给谁、按什么、点哪儿、滚多少、从哪儿到哪儿
        "press" => LucideIconKind.MousePointerClick,
        "sender" => LucideIconKind.Send,
        "keys" => LucideIconKind.Keyboard,
        "where" => LucideIconKind.MapPin,
        "wheel" => LucideIconKind.ArrowDownToLine,
        "from" => LucideIconKind.CircleDot,
        "to" => LucideIconKind.Target,
        "axis" => LucideIconKind.Joystick,

        // 节奏、调参、取画面、存文件、识别前后
        "pacing" => LucideIconKind.Timer,
        "tuning" => LucideIconKind.SlidersHorizontal,
        "capture" => LucideIconKind.Frame,
        "save" => LucideIconKind.Save,
        "read" => LucideIconKind.TextQuote,
        "before" => LucideIconKind.WandSparkles,

        // 元素与网页
        "narrowing" => LucideIconKind.ListFilter,
        "taking" => LucideIconKind.TextQuote,
        "entry" => LucideIconKind.TextCursorInput,
        "state" => LucideIconKind.ToggleLeft,
        "limit" => LucideIconKind.ListOrdered,
        "address" => LucideIconKind.Globe,
        "tab" => LucideIconKind.PanelsTopLeft,

        // 变量、文件与剪贴板
        "variable" => LucideIconKind.Variable,
        "source" => LucideIconKind.FileInput,
        "clash" => LucideIconKind.TriangleAlert,
        "doing" => LucideIconKind.Wrench,
        "reading" => LucideIconKind.BookOpenText,
        "writing" => LucideIconKind.PencilLine,
        "picking" => LucideIconKind.Funnel,
        "order" => LucideIconKind.ArrowUpDown,

        // 脚本：跑什么、交给谁跑、传什么给它
        "code" => LucideIconKind.FileCode,
        "runner" => LucideIconKind.SquareTerminal,
        "passing" => LucideIconKind.ArrowRightToLine,

        // 程序与窗口
        "program" => LucideIconKind.Boxes,
        "options" => LucideIconKind.Settings2,
        "place" => LucideIconKind.Move,
        _ => null,
    };
    public static LucideIconKind Info => LucideIconKind.Info;
    public static LucideIconKind List => LucideIconKind.List;
    public static LucideIconKind Mouse => LucideIconKind.Mouse;
    public static LucideIconKind GameController => LucideIconKind.Gamepad2;
    public static LucideIconKind Dropper => LucideIconKind.Pipette;
    public static LucideIconKind Cursor => LucideIconKind.MousePointer2;
    public static LucideIconKind Filter => LucideIconKind.ListFilter;
    public static LucideIconKind Record => LucideIconKind.CircleDot;
    public static LucideIconKind Sliders => LucideIconKind.SlidersHorizontal;
    public static LucideIconKind Plus => LucideIconKind.Plus;
    public static LucideIconKind Move => LucideIconKind.Move;
    public static LucideIconKind SelectionPlus => LucideIconKind.SquarePlus;
    public static LucideIconKind ChevronDown => LucideIconKind.ChevronDown;
    public static LucideIconKind ChevronRight => LucideIconKind.ChevronRight;
    public static LucideIconKind WarningTriangle => LucideIconKind.TriangleAlert;
}
