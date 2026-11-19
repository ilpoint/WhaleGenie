using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lucide.Avalonia;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// What the menu window is given and what it can ask the rest of the program to do.
/// <summary>One editable or runnable position on a common or context page.</summary>
public sealed record MenuSlotRef(
    string PageId,
    int Row,
    int Column,
    MenuRuntimeCell? Cell)
{
    public string Key => $"{PageId}:{Row}:{Column}";
}

public sealed record MenuWindowActions(
    Action<MacroItem> OpenMacroEditor,
    Action<MacroItem> DebugMacro,
    Action OpenSettings,
    Action RestoreForeground,
    Func<Task<MenuRuntimeLayout?>> AddCurrentWindowRule,
    Func<Task<bool>> BlockCurrentWindow,
    Func<Task<MenuRuntimeLayout?>> AddCommonPage,
    Func<string, Task<MenuRuntimeLayout?>> RemoveCommonPage,
    Func<string, Task<MenuRuntimeLayout?>> AddContextPage,
    Func<string, Task<MenuRuntimeLayout?>> RemoveContextPage,
    Func<MenuSlotRef, Task<MenuRuntimeLayout?>> ChooseMacro,
    Func<MenuSlotRef, Task<MenuRuntimeLayout?>> ChooseIcon,
    Func<MenuSlotRef, MenuSlotRef, Task<MenuRuntimeLayout?>> Move);
