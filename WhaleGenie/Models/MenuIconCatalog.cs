using System.Collections.Generic;
using System.Linq;
using Lucide.Avalonia;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

public sealed record MenuIconOption(string Key, LucideIconKind Icon, string LabelKey)
{
    public string Label => Strings.Get(LabelKey);
}

/// <summary>The stable names saved by menu cells and the line icons those names draw.</summary>
public static class MenuIconCatalog
{
    public static IReadOnlyList<MenuIconOption> Options { get; } =
    [
        new("play", AppIcons.Play, "MenuIcon.Play"),
        new("file", AppIcons.File, "MenuIcon.File"),
        new("folder", AppIcons.Folder, "MenuIcon.Folder"),
        new("keyboard", AppIcons.Keyboard, "MenuIcon.Keyboard"),
        new("mouse", AppIcons.Mouse, "MenuIcon.Mouse"),
        new("window", AppIcons.AppWindow, "MenuIcon.Window"),
        new("browser", AppIcons.Browser, "MenuIcon.Browser"),
        new("clipboard", AppIcons.Clipboard, "MenuIcon.Clipboard"),
        new("code", AppIcons.Code, "MenuIcon.Code"),
        new("eye", AppIcons.Eye, "MenuIcon.Eye"),
        new("scan", AppIcons.TextScan, "MenuIcon.Scan"),
        new("settings", AppIcons.Settings, "MenuIcon.Settings"),
        new("search", AppIcons.Search, "MenuIcon.Search"),
        new("save", AppIcons.Save, "MenuIcon.Save"),
        new("download", AppIcons.Download, "MenuIcon.Download"),
        new("workflow", AppIcons.Workflow, "MenuIcon.Workflow"),
        new("group", AppIcons.Group, "MenuIcon.Group"),
        new("variable", AppIcons.Variable, "MenuIcon.Variable"),
        new("clock", AppIcons.Clock, "MenuIcon.Clock"),
        new("sparkle", AppIcons.Sparkle, "MenuIcon.Sparkle"),
        new("cursor", AppIcons.CursorClick, "MenuIcon.Cursor"),
        new("move", AppIcons.Move, "MenuIcon.Move"),
    ];

    public static MenuIconOption? Find(string? key)
        => Options.FirstOrDefault(option => option.Key == key);
}
