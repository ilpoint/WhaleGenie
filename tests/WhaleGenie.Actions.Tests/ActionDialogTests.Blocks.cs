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
using WhaleGenie.Core.Expressions;
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

// Writing the steps that hold other steps: blocks, cases, variables, loops and try.
public partial class ActionDialogTests
{
    [Fact]
    public void A_variable_calculation_is_one_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.setVariable");
            var value = viewModel.Parameters.First(parameter => parameter.Definition.Name == "value");

            // The operand-and-operator pieces are gone: the whole value is one expression, offered
            // the live checking and suggestions that storing a value gets, so nothing has to be
            // assembled by hand.
            Assert.True(value.IsExpression);
            Assert.NotEmpty(value.ExpressionSuggestions);
            Assert.DoesNotContain(viewModel.Parameters,
                parameter => parameter.Definition.Name is "left" or "right" or "operator");
        });
    }

    [Fact]
    public void Nothing_can_be_stored_into_a_variable_WhaleGenie_owns()
    {
        Ui.Run(() =>
        {
            // The variables WhaleGenie provides are read-only, and the rule belongs to storing a value
            // under a name rather than to one action, so a calculation is refused the same way
            // "Set Variable" is. The clipboard is written by the clipboard action, never by
            // storing into sys.clipboard.
            foreach (var key in new[] { "control.setVariable" })
            {
                var viewModel = Open(key);
                viewModel.Parameters.First(parameter => parameter.Definition.Name == "name").Text =
                    "sys.clipboard";

                Assert.False(viewModel.CanSave);
                Assert.Equal(Strings.Format("Add.SystemReadOnly", "sys.clipboard"),
                    viewModel.ValidationMessage);
            }
        });
    }

    [Fact]
    public void The_steps_of_a_block_are_edited_in_the_editor_and_not_in_the_dialog()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.repeat");
            var body = viewModel.Parameters.First(parameter => parameter.Definition.Name == "body");

            // The child list remains in the model so the form can round-trip it, but no line or
            // add control for it reaches the form.
            Assert.NotNull(body.List);
            Assert.DoesNotContain(viewModel.Rows.SelectMany(Names), name => name == "body");

            // A condition is a Boolean expression inside its block, not another action list.
            var test = Open("control.if");
            var condition = test.Parameters.First(parameter => parameter.Definition.Name == "condition");

            Assert.True(condition.IsCondition);
            Assert.Null(condition.List);
        });
    }

    [Fact]
    public void A_loop_asks_its_pace_on_the_first_page()
    {
        Ui.Run(() =>
        {
            foreach (var (key, blocks) in new[]
                     {
                         ("control.repeat", new[] { "rounds", "pacing" }),
                         ("control.while", new[] { "condition", "guard", "pacing" }),
                         ("control.waitUntil", new[] { "condition", "pacing" }),
                         ("control.forEach", new[] { "items", "each", "pacing" }),
                         ("control.for", new[] { "range", "each", "pacing" }),
                     })
            {
                var viewModel = Open(key);

                // Each loop asks its questions in the order a person asks them: what it goes
                // through, what each round is called, then how fast. A block per question, and no
                // more than three of them.
                Assert.Equal(blocks,
                    viewModel.Rows.Where(row => row.GroupKey.Length > 0)
                        .Select(row => row.GroupKey).Distinct());

                // The pace is not an advanced option: once it moved back to the first page, this
                // action has nothing left to put there, and a page with no fields is not drawn.
                Assert.DoesNotContain(viewModel.Pages,
                    page => page.IsPresent && page.Key == AddActionViewModel.AdvancedPage);
            }
        });
    }

    /// <summary>
    /// 一个问题的两部分可以分在两页上：找什么，以及查找本身怎么调。块名与编号跟着问题走、不跟着页走——
    /// 每页各自从 ① 数起时，同一块在两页上各有一份同名的标题，各挂一个 ①，读起来像两个不同的问题。
    /// </summary>
    [Fact]
    public void A_question_split_over_two_pages_keeps_one_number()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");

            var basic = viewModel.Rows.Where(row => row.GroupKey == "target").ToList();
            var advanced = viewModel.AdvancedRows.Where(row => row.GroupKey == "target").ToList();
            Assert.NotEmpty(basic);
            Assert.NotEmpty(advanced);

            // The same question on both pages carries the same number, and says so on both.
            Assert.Equal(basic[0].GroupIndex, advanced[0].GroupIndex);
            Assert.True(basic[0].IsGroupStart);
            Assert.True(advanced[0].IsGroupStart);

            // No two questions are numbered the same, whichever page they sit on.
            var numbers = viewModel.Rows.Concat(viewModel.AdvancedRows)
                .Where(row => row.GroupKey.Length > 0)
                .GroupBy(row => row.GroupKey)
                .Select(rows => rows.First().GroupIndex)
                .ToList();
            Assert.Equal(numbers.Count, numbers.Distinct().Count());
        });
    }

    /// <summary>
    /// A percentage is one number said two ways: the box for the exact figure, the slider for about
    /// this much. Written as an expression, the field is neither, so the slider has to go with the
    /// box. Both read "is this a plain number", which is computed rather than stored, and a computed
    /// answer that nobody announces leaves a slider on screen writing into a field that has none.
    /// </summary>
    [Fact]
    public void A_percentage_gives_up_its_slider_when_it_is_written_as_an_expression()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("vision.findImage");
            var confidence = viewModel.Parameters.First(parameter => parameter.Definition.Name == "confidence");
            Assert.True(confidence.IsPercent);

            var announced = new List<string>();
            confidence.PropertyChanged += (_, arguments) => announced.Add(arguments.PropertyName ?? string.Empty);
            confidence.UseFormula = true;

            Assert.False(confidence.IsPercent);
            Assert.False(confidence.IsNumberValue);
            Assert.Contains(nameof(StepParameterViewModel.IsPercent), announced);
            Assert.Contains(nameof(StepParameterViewModel.IsNumberValue), announced);
        });
    }

    [Fact]
    public void A_try_block_says_how_much_sits_in_each_of_its_lists()
    {
        Ui.Run(() =>
        {
            var kept = new MacroStep
            {
                Type = "control.try",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "body",
                        Kind = ActionParameterKind.Steps,
                        Steps =
                        [
                            new MacroStep { Type = "control.log" },
                            new MacroStep { Type = "control.delay" },
                        ],
                    },
                    new StepParameter
                    {
                        Name = "catch",
                        Kind = ActionParameterKind.Steps,
                        Steps = [new MacroStep { Type = "control.log" }],
                    },
                    new StepParameter { Name = "finally", Kind = ActionParameterKind.Steps },
                ],
            };

            var window = new ActionEditorWindow(kept, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // The three lists are steps, not fields, so the page cannot show their contents. One
            // line saying how much sits in each is what keeps the page from reading as "a condition
            // and nothing else"; the same sentence a switch gets for its branches.
            Assert.True(viewModel.ShowsAttempt);
            Assert.Equal(Strings.Format("Add.AttemptSummary", 2, 1, 0), viewModel.AttemptSummary);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == viewModel.AttemptSummary);

            // Only the try block has them; the sibling block must not grow the same line.
            Assert.False(Open("control.if").ShowsAttempt);

            window.Close();
        });
    }

    [Fact]
    public void The_settings_of_a_block_say_they_are_about_the_block()
    {
        Ui.Run(() =>
        {
            var window = new ActionEditorWindow(
                new MacroStep { Type = "control.repeat" }, ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;

            // Retries, timeouts and "when it fails" mean something bigger on a block: a failure
            // inside it that no step answered is the block failing. Its explanation belongs with
            // those controls, not among the fields that say what the block examines.
            Assert.True(viewModel.IsBlock);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible
                    && text.Text == Strings.Get("Add.BlockStepsInList"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible
                    && text.Text == Strings.Get("Add.BlockSettings"));

            viewModel.Select(viewModel.Pages.Single(page => page.Key == AddActionViewModel.OtherPage));
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible
                    && text.Text == Strings.Get("Add.BlockSettings"));

            // A step that does one thing is not a block, and the line is not about it.
            Assert.False(Open("control.delay").IsBlock);
        });
    }

    [Fact]
    public void A_block_form_keeps_children_without_showing_a_second_editor()
    {
        Ui.Run(() =>
        {
            var child = Open("control.delay").BuildStep();
            var window = new ActionEditorWindow(
                new MacroStep
                {
                    Type = "control.repeat",
                    Parameters =
                    [
                        new StepParameter
                        {
                            Name = "body",
                            Kind = ActionParameterKind.Steps,
                            Steps = [child],
                        },
                    ],
                },
                ActionCatalog.Definitions, [], []);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (AddActionViewModel)window.DataContext!;
            var body = viewModel.Parameters.First(parameter => parameter.Definition.Name == "body");

            // One explanation replaces one repeated note per child list, and no child editor is
            // present. Saving still returns the child unchanged.
            Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible
                    && text.Text == Strings.Get("Add.BlockStepsInList"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ItemsControl>(),
                control => ReferenceEquals(control.ItemsSource, body.List!.Steps));
            Assert.Empty(window.OwnedWindows);

            MacroStep? saved = null;
            viewModel.CloseRequested += step => saved = step;
            viewModel.SaveCommand.Execute(null);

            Assert.Equal("control.repeat", saved!.Type);
            Assert.Equal("control.delay", Assert.Single(saved.StepLists.Single().Steps).Type);
        });
    }

    /// <summary>
    /// 计数循环写在外层循环里时，默认的计数器名字换成外层没在用的那个。内层用外层的名字，内层结束
    /// 时外层的值就没有了；那正是编辑器事后要标出来的问题，而在这里改掉它比事后提示更有用。
    ///
    /// 表单上写的就是保存下来的：名字以形状写进字段，而不是只改框里那行字。
    /// </summary>
    [Fact]
    public void A_counting_loop_is_named_around_the_loops_it_goes_into()
    {
        Ui.Run(() =>
        {
            var inner = Named("control.for", newStepId: "abcd", loopNames: ["i"]);
            Assert.Equal("i2", Value(inner, "name"));
            Assert.Equal("i2", inner.BuildStep().Text("name"));

            // 顶层没有外层循环：字段仍是目录里的起点，名字跟着它读的那一层走。
            Assert.Equal("i", Value(Named("control.for", newStepId: "efgh"), "name"));

            // 已有的步骤不改名：名字是作者写的，遮蔽仍由检查去提示，不在这里静默改掉。
            Assert.Equal("i", Value(Named("control.for", existing: Counter("i"), loopNames: ["i"]),
                "name"));
        });
    }

    /// <summary>The form on a counting loop: a new step of the editor's, an existing one, or neither.</summary>
    private static AddActionViewModel Named(string key, string? newStepId = null,
        MacroStep? existing = null, IReadOnlyList<string>? loopNames = null)
    {
        var window = new ActionEditorWindow(existing, ActionCatalog.Definitions, [], [],
            newStepId: newStepId, loopNames: loopNames);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var viewModel = (AddActionViewModel)window.DataContext!;
        if (existing is null)
        {
            viewModel.SelectAction(key);
        }

        Dispatcher.UIThread.RunJobs();
        return viewModel;
    }

    /// <summary>A counting loop that already counts in one name.</summary>
    private static MacroStep Counter(string name)
        => new()
        {
            Type = "control.for",
            Parameters =
            [
                new StepParameter
                {
                    Name = "name",
                    Kind = ActionParameterKind.Text,
                    Written = Formula.Name(name),
                },
            ],
        };
}
