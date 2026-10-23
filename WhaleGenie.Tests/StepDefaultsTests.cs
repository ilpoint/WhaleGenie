using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The settings a step starts with when the editor made it rather than the user writing it. A step
/// written in the add-action dialog opens with "ask me"; the block Ctrl + G throws around a
/// selection and a step that was just recorded are made by the editor itself, and they have to come
/// out the same way, or one macro behaves differently depending on how each of its steps got there.
/// </summary>
public class StepDefaultsTests
{
    private static AddActionViewModel Open(string key)
    {
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions,
            VariableChoicesForChecks.Named(), []);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        viewModel.SelectAction(key);
        Dispatcher.UIThread.RunJobs();
        window.Close();
        return viewModel;
    }

    [Fact]
    public void A_group_thrown_around_a_selection_asks_when_it_fails()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var first = new MacroStep { Type = "control.log" };
            var second = new MacroStep { Type = "control.delay" };
            editor.AddStep(first);
            editor.AddStep(second);
            editor.SetSelection([first, second]);

            editor.GroupSelectedCommand.Execute(null);

            var group = Assert.Single(editor.Steps);
            Assert.Equal("control.sequence", group.Type);
            Assert.Equal(StepErrorAction.AskUser, group.Meta.OnError);

            // The step list has to say so as well: a step that would bring the whole macro down on
            // its own is what the user has to be able to see before running it.
            Assert.Contains(Strings.Get("Editor.Meta.OnErrorAsk"), group.MetaSummary);

            // And saving the macro does not quietly turn it back into "stop the macro".
            Assert.Equal(StepErrorAction.AskUser,
                MacroStep.FromJson(group.ToJson()).Meta.OnError);
        });
    }

    [Fact]
    public void A_recorded_step_asks_when_it_fails()
        => Assert.Equal(StepErrorAction.AskUser,
            new RecordedAction(RecordedActionKind.KeyPress, Key: "Ctrl+C").ToStep().Meta.OnError);

    [Fact]
    public void A_recorded_mouse_click_names_only_fields_the_action_still_has()
    {
        var step = new RecordedAction(RecordedActionKind.MouseClick,
            Button: "left", X: 10, Y: 20, Amount: 1).ToStep();
        var fields = ActionCatalog.Find(step.Type)!.Parameters
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(["button", "x", "y", "intervalMs"],
            step.Parameters.Select(parameter => parameter.Name));
        Assert.All(step.Parameters, parameter => Assert.Contains(parameter.Name, fields));
        Assert.DoesNotContain(step.Parameters, parameter => parameter.Name == "clicks");
    }

    /// <summary>
    /// How long a browser step is allowed to take is the step's own kind of answer: one that waits
    /// for the page to put an element up needs a clock, one that waits for the page itself needs a
    /// longer one, and one that only opens, closes or says which page comes next needs none at all.
    /// </summary>
    [Fact]
    public void A_browser_step_carries_the_clock_its_kind_of_step_wants()
    {
        // 碰元素与读一次的动作：页面把元素放出来要多久，这一步就等多久。
        string[] acting =
        [
            "browser.click", "browser.fill", "browser.readText", "browser.readAll",
            "browser.readTable", "browser.readPage", "browser.select", "browser.check",
            "browser.hover", "browser.press", "browser.upload", "browser.scroll",
            "browser.evaluate",
        ];

        // 会停在原地等的动作：等页面自己走到某一步，比等一个元素要久；下载也在这一档，因为文件
        // 从页面那边到磁盘上要多久不由这一步说了算。
        string[] waiting =
        [
            "browser.goTo", "browser.waitFor", "browser.waitForUrl", "browser.waitForLoad",
            "browser.download",
        ];

        // 不等任何东西的动作：设一个也用不到，所以不设。
        string[] instant =
        [
            "browser.open", "browser.close", "browser.closeTab", "browser.switchTab",
            // 回答对话框的块也不设：盒子里每一步有自己的时钟，块本身不等。
            "browser.dialog",
        ];

        Ui.Run(() =>
        {
            foreach (var key in acting)
            {
                Assert.Equal(10000, ActionCatalog.Find(key)!.StepTimeoutMs);
            }

            foreach (var key in waiting)
            {
                Assert.Equal(15000, ActionCatalog.Find(key)!.StepTimeoutMs);
            }

            foreach (var key in instant)
            {
                Assert.Equal(0, ActionCatalog.Find(key)!.StepTimeoutMs);
            }
        });
    }

    /// <summary>
    /// Every action that used to keep a timeout field of its own now starts with that length in the
    /// step's one timeout, while `uia.exists` starts with none because it only looks once.
    /// </summary>
    [Fact]
    public void Every_waiting_action_starts_with_its_own_length_in_the_step_timeout()
    {
        (string Key, int Milliseconds)[] waiting =
        [
            ("clipboard.waitChange", 5000), ("clipboard.copy", 1500),
            ("process.waitFor", 10000), ("process.waitExit", 60000),
            ("command.run", 30000), ("window.waitFor", 10000),
            ("uia.waitElement", 10000), ("uia.click", 5000), ("uia.invoke", 5000),
            ("script.run", 60000), ("script.python", 60000),
            ("script.csharp", 60000), ("script.ahk", 60000),
        ];

        Ui.Run(() =>
        {
            foreach (var (key, milliseconds) in waiting)
            {
                Assert.Equal(milliseconds, ActionCatalog.Find(key)!.StepTimeoutMs);
                Assert.Equal(milliseconds, Open(key).MetaTimeout.Milliseconds);
            }

            Assert.Equal(0, ActionCatalog.Find("uia.exists")!.StepTimeoutMs);
            Assert.Equal(0, Open("uia.exists").MetaTimeout.Milliseconds);
        });
    }

    /// <summary>
    /// The field the user reads it in. The step holding "ask" is no good if the dialog that opens
    /// on it shows "stop the macro" — that is the thing Ctrl + G was reported about.
    /// </summary>
    [Fact]
    public void The_dialog_opens_a_new_group_on_asking_when_it_fails()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var editorWindow = new MacroEditorWindow(macro, [macro]);
            editorWindow.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = (MacroEditorViewModel)editorWindow.DataContext!;

            var first = new MacroStep { Type = "control.log" };
            var second = new MacroStep { Type = "control.delay" };
            editor.AddStep(first);
            editor.AddStep(second);
            editor.SetSelection([first, second]);
            editor.GroupSelectedCommand.Execute(null);

            var group = Assert.Single(editor.Steps);
            var dialog = new ActionEditorWindow(group, ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("wanted"), []);
            Dispatcher.UIThread.RunJobs();

            var form = (AddActionViewModel)dialog.DataContext!;
            Assert.Equal("ask", form.MetaOnError.Value);
            Assert.Equal(Strings.Get("Add.OnError.AskUser"), form.MetaOnError.Display);
        });
    }
}
