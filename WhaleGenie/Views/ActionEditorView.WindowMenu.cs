using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// The window a step names: the menu of open windows, and the ways to fill its rule in.
public partial class ActionEditorView : UserControl
{
    /// <summary>Picks one of the windows that are open and writes its title into the field.</summary>
    internal async void OnPickWindow(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        // The picker takes the part this step compares its text with, so what it hands back can be
        // written straight into the field.
        var match = (DataContext as AddActionViewModel)?.WindowMatchFor(parameter) ?? WindowMatch.Title;
        if (await WindowPickerWindow.PickAsync(HostWindow!, match, Devices.Windows) is { Length: > 0 } picked)
        {
            parameter.Text = picked;
        }
    }

    /// <summary>
    /// 窗口那一格的唯一入口：把"用哪个窗口"的几种走法收在一处。以前是三颗按钮，其中捕获那一颗
    /// 还要先按 F1 再对准窗口点一下；同一个窗口在一页宏里要用二十次时，这一格是最费手的地方。
    /// </summary>
    internal void OnChooseWindow(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StepParameterViewModel parameter } button)
        {
            BuildWindowMenu(parameter).ShowAt(button);
        }
    }

    /// <summary>
    /// 窗口那一格的菜单：前台、沿用、本宏用过的、已打开的、捕获、变量。建菜单与显示分开，是因为
    /// 菜单里列出什么、勾在哪一项，是可以单独核对的东西。
    /// </summary>
    internal MenuFlyout BuildWindowMenu(StepParameterViewModel parameter)
    {
        if (DataContext is not AddActionViewModel form)
        {
            return new MenuFlyout();
        }

        var menu = new MenuFlyout();

        // 不指定就是前台窗口，与从前空着那一格的意思一样。
        menu.Items.Add(Choice(Strings.Get("WindowRule.Menu.Foreground"),
            parameter.ClearWindowRule, !parameter.HasWindowRule));

        // 沿用：不写死窗口，跟前面那个走。新步骤默认就是它，所以这一项平时常常是勾上的。
        menu.Items.Add(Choice(Strings.Get("WindowRule.Following"),
            parameter.FollowWindowRule, parameter.IsFollowingWindow));

        if (form.WindowRules.Count > 0)
        {
            menu.Items.Add(new Separator());

            var used = new MenuItem { Header = Strings.Get("WindowRule.Menu.Used") };
            foreach (var choice in form.WindowRules)
            {
                if (!WindowRule.TryParse(choice.Rule, out var rule))
                {
                    continue;
                }

                var label = choice.Count > 1
                    ? $"{choice.Summary} {Strings.Get("WindowRule.UsedTimes", choice.Count.ToString(CultureInfo.CurrentCulture))}"
                    : choice.Summary;
                used.Items.Add(Choice(label, () => parameter.ApplyWindowRule(rule),
                    string.Equals(parameter.Text.Trim(), choice.Rule, StringComparison.Ordinal)));
            }

            menu.Items.Add(used);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(OpenWindows(parameter));

        menu.Items.Add(new Separator());
        menu.Items.Add(Choice(Strings.Get("WindowRule.Menu.Capture"),
            () => _ = CaptureWindowRule(parameter)));
        menu.Items.Add(Choice(Strings.Get("WindowRule.Menu.Variable"),
            () => _ = UseWindowVariable(parameter)));

        return menu;
    }

    /// <summary>The windows that are open right now, ready to be named in one click.</summary>
    private MenuItem OpenWindows(StepParameterViewModel parameter)
    {
        var menu = new MenuItem { Header = Strings.Get("WindowRule.Menu.Open") };
        IReadOnlyList<WindowInfo> windows;
        try
        {
            windows = Devices.Windows.List();
        }
        catch (Exception)
        {
            // 列不出来就没有这一项：读窗口的设备不在时，别的走法仍旧可用。
            menu.IsEnabled = false;
            return menu;
        }

        foreach (var window in windows.Take(OpenWindowLimit))
        {
            var named = window;
            menu.Items.Add(Choice(Label(named), () =>
            {
                // 记下的是整条规则，程序名与窗口类也一起：改天标题变了，还能按它们找回同一个窗口。
                parameter.ApplyWindowRule(WindowRule.From(named,
                    Devices.Windows.ProcessOf(named.Handle), Devices.Windows.ClassOf(named.Handle)));
            }));
        }

        if (windows.Count > OpenWindowLimit)
        {
            menu.Items.Add(Choice(Strings.Get("WindowRule.Menu.More"),
                () => _ = PickWindowByTitle(parameter)));
        }

        if (menu.Items.Count == 0)
        {
            menu.IsEnabled = false;
        }

        return menu;
    }

    /// <summary>A row for one window: its title, then the program it belongs to.</summary>
    private string Label(WindowInfo window)
    {
        if (window.Title.Length == 0)
        {
            return window.Handle.ToString(CultureInfo.CurrentCulture);
        }

        var program = Devices.Windows.ProcessOf(window.Handle);
        return program.Length == 0 ? window.Title : $"{window.Title} · {program}";
    }

    /// <summary>捕获窗口：按 F1 对准窗口点一下，再改匹配方式。仍然是最精确的那条路。</summary>
    private async Task CaptureWindowRule(StepParameterViewModel parameter)
    {
        WindowRule.TryParse(parameter.Text, out var current);
        if (await WindowRuleWindow.PickAsync(HostWindow!, Devices.Windows, Devices.Hook, current) is { } picked)
        {
            parameter.ApplyWindowRule(picked);
        }
    }

    /// <summary>从窗口列表里选一个，只留下标题那一条规则。</summary>
    private async Task PickWindowByTitle(StepParameterViewModel parameter)
    {
        if (await WindowPickerWindow.PickAsync(HostWindow!, WindowMatch.Title, Devices.Windows)
            is { Length: > 0 } picked)
        {
            parameter.ApplyWindowRule(new WindowRule(Title: picked));
        }
    }

    /// <summary>Uses a window record a previous step handed over.</summary>
    private async Task UseWindowVariable(StepParameterViewModel parameter)
    {
        if (DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        if (await ValuePickerWindow.ShowFor(HostWindow!, null, viewModel.CollectVariables(),
                ActionValueForm.Name) is not { Chosen: true } picked)
        {
            return;
        }

        parameter.UseWindowVariable(Formula.NameOf(picked.Value).Name);
    }

    /// <summary>One line of the window menu: what it says, what it does, and whether it is the one in force.</summary>
    private static MenuItem Choice(string text, Action chosen, bool inForce = false)
    {
        var item = new MenuItem
        {
            Header = text,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = inForce
        };
        item.Click += (_, _) => chosen();
        return item;
    }

    internal void OnClearWindowRule(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.ClearWindowRule();
        }
    }
}
