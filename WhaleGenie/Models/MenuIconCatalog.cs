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
        new("copy", LucideIconKind.Copy, "MenuIcon.Copy"),
        new("paste", LucideIconKind.ClipboardPaste, "MenuIcon.Paste"),
        new("cut", LucideIconKind.Scissors, "MenuIcon.Cut"),
        new("delete", LucideIconKind.Trash, "MenuIcon.Delete"),
        new("edit", LucideIconKind.Pencil, "MenuIcon.Edit"),
        new("add", LucideIconKind.Plus, "MenuIcon.Add"),
        new("remove", LucideIconKind.Minus, "MenuIcon.Remove"),
        new("check", LucideIconKind.Check, "MenuIcon.Check"),
        new("close", LucideIconKind.X, "MenuIcon.Close"),
        new("checklist", LucideIconKind.ListChecks, "MenuIcon.Checklist"),
        new("calendar", LucideIconKind.CalendarDays, "MenuIcon.Calendar"),
        new("schedule", LucideIconKind.Clock3, "MenuIcon.Schedule"),
        new("timer", LucideIconKind.Timer, "MenuIcon.Timer"),
        new("wait", LucideIconKind.Hourglass, "MenuIcon.Wait"),
        new("reminder", LucideIconKind.BellRing, "MenuIcon.Reminder"),
        new("mail", LucideIconKind.Mail, "MenuIcon.Mail"),
        new("message", LucideIconKind.MessageSquare, "MenuIcon.Message"),
        new("phone", LucideIconKind.Phone, "MenuIcon.Phone"),
        new("camera", LucideIconKind.Camera, "MenuIcon.Camera"),
        new("image", LucideIconKind.Image, "MenuIcon.Image"),
        new("video", LucideIconKind.Video, "MenuIcon.Video"),
        new("audio", LucideIconKind.AudioLines, "MenuIcon.Audio"),
        new("volume", LucideIconKind.Volume2, "MenuIcon.Volume"),
        new("microphone", LucideIconKind.Mic, "MenuIcon.Microphone"),
        new("wifi", LucideIconKind.Wifi, "MenuIcon.Wifi"),
        new("cloud", LucideIconKind.Cloud, "MenuIcon.Cloud"),
        new("database", LucideIconKind.Database, "MenuIcon.Database"),
        new("server", LucideIconKind.Server, "MenuIcon.Server"),
        new("processor", LucideIconKind.Cpu, "MenuIcon.Processor"),
        new("drive", LucideIconKind.HardDrive, "MenuIcon.Drive"),
        new("terminal", LucideIconKind.Terminal, "MenuIcon.Terminal"),
        new("command", LucideIconKind.Command, "MenuIcon.Command"),
        new("branch", LucideIconKind.GitBranch, "MenuIcon.Branch"),
        new("lock", LucideIconKind.Lock, "MenuIcon.Lock"),
        new("key", LucideIconKind.KeyRound, "MenuIcon.Key"),
        new("shield", LucideIconKind.Shield, "MenuIcon.Shield"),
        new("user", LucideIconKind.User, "MenuIcon.User"),
        new("users", LucideIconKind.Users, "MenuIcon.Users"),
        new("book", LucideIconKind.BookOpen, "MenuIcon.Book"),
        new("bookmark", LucideIconKind.Bookmark, "MenuIcon.Bookmark"),
        new("star", LucideIconKind.Star, "MenuIcon.Star"),
        new("heart", LucideIconKind.Heart, "MenuIcon.Heart"),
        new("home", LucideIconKind.House, "MenuIcon.Home"),
        new("location", LucideIconKind.MapPin, "MenuIcon.Location"),
        new("navigation", LucideIconKind.Navigation, "MenuIcon.Navigation"),
        new("send", LucideIconKind.Send, "MenuIcon.Send"),
        new("upload", LucideIconKind.Upload, "MenuIcon.Upload"),
        new("refresh", LucideIconKind.RefreshCw, "MenuIcon.Refresh"),
        new("reset", LucideIconKind.RotateCcw, "MenuIcon.Reset"),
        new("grid", LucideIconKind.LayoutGrid, "MenuIcon.Grid"),
    ];

    public static MenuIconOption? Find(string? key)
        => Options.FirstOrDefault(option => option.Key == key);
}
