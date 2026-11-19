using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Key = Avalonia.Input.Key;
using KeyModifiers = Avalonia.Input.KeyModifiers;

namespace WhaleGenie.Tests;

/// <summary>
/// What the "Add action" dialog does when it is driven the way a user drives it: the window is
/// built for real, the controls are found, and the values are read back through the view model.
/// </summary>
public partial class ActionDialogTests
{
    /// <summary>The markup of one page of the form: its marker up to the end of its box.</summary>
    private static string Page(string markup, string page)
    {
        var start = markup.IndexOf($"IsVisible=\"{{Binding {page}}}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the form has no {page} page");
        return markup[start..markup.IndexOf("</Border>", start, StringComparison.Ordinal)];
    }

    private static AddActionViewModel Open(string key)
        => OpenWindow(key).ViewModel;

    /// <summary>
    /// The same dialog, window and all, for a check that looks at what the form draws rather than
    /// only at what the view model holds.
    /// </summary>
    /// <summary>
    /// 表单开在一个新步骤前，可选用"落点之上已经有窗口"这一条件：动作对话框因此会把窗口格默认成
    /// 「沿用上一次」。
    /// </summary>
    private static (ActionEditorWindow Window, AddActionViewModel ViewModel) OpenWindow(
        string key, bool followsWindowAbove)
        => OpenWindow(key, WindowRuleChoices.Of([StepOnNotepad()]), followsWindowAbove,
            followsWindowAbove ? NotepadRule : null);

    private static (ActionEditorWindow Window, AddActionViewModel ViewModel) OpenWindow(
        string key, IReadOnlyList<WindowRuleChoice> used, bool followsWindowAbove = false,
        string? windowAbove = null)
    {
        // 新步骤有自己的名字，编辑器在真实的流程里也是这么给的：有了它，这一步才算"正在写的"，
        // 窗口格才会默认沿用上面那个窗口。
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("match.x", "match.y", "spot"), [],
            newStepId: followsWindowAbove ? "new01" : null, windowRules: used,
            followsWindowAbove: followsWindowAbove, windowAbove: windowAbove);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    /// <summary>记事本那条规则的原文，测试里反复要用。</summary>
    private static string NotepadRule => new WindowRule(Title: "Notepad").ToJson().ToJsonString();

    /// <summary>一步指着记事本的窗口，这样表单就有"上面已经用过的窗口"可列、可沿用。</summary>
    private static MacroStep StepOnNotepad() => OnWindow(NotepadRule, "prev");

    private static (ActionEditorWindow Window, AddActionViewModel ViewModel) OpenWindow(string key)
    {
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("match.x", "match.y", "spot"), []);

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        return (window, viewModel);
    }

    private static string Value(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name).CurrentText;
}
