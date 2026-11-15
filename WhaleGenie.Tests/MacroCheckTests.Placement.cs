using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Where a step may stand: a case outside a switch, a break where no loop holds it, and input another macro claims.
public partial class MacroCheckTests
{
    /// <summary>
    /// A call to a submacro is a call to a macro of this document. With the submacro in the list
    /// the check has nothing to say about it; with nothing in the list the step is a call into
    /// thin air, which is what it used to say about a submacro that was right there in the file.
    /// </summary>
    [Fact]
    public void A_call_to_a_submacro_is_known_when_the_submacro_is()
    {
        var call = Step("control.runMacro", Text("macro", "里面"));

        Assert.DoesNotContain(Against("外面", [Callable("里面")], call), problem =>
            problem.Code == "MacroNotFound");
        Assert.Contains(Against("外面", [], call), problem =>
            problem.Code == "MacroNotFound");
    }

    /// <summary>
    /// A step that sends keys, written the way the file writes one. What it sends does not matter to
    /// the check — that it sends anything at all is the whole of the question.
    /// </summary>
    private static MacroStep Pressing()
        => Step("input.keyPress", Text("key", "F5"));

    [Fact]
    public void A_case_outside_a_switch_is_marked_on_the_case()
    {
        var misplaced = Case("a");

        var problem = Assert.Single(Inspect(misplaced));

        Assert.Same(misplaced, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.CaseOutsideSwitch"), problem.Message);
    }

    [Fact]
    public void An_ordinary_step_inside_a_case_list_is_marked_where_it_sits()
    {
        var misplaced = Step("control.log", Text("message", "x"), Text("level", "info"));
        var choice = Step("control.switch",
            Text("switchValue", "a"),
            Text("switchMode", "equals"),
            Steps("cases", Case("a"), misplaced),
            Steps("otherwise", Step("control.log", Text("message", "y"), Text("level", "info"))));

        var problem = Assert.Single(Inspect(choice));

        Assert.Same(misplaced, problem.Step);
        Assert.Equal(Strings.Format("Editor.Problem.WrongStepKind",
            ActionCatalog.Find("control.case")!.LocalName), problem.Message);
    }

    [Fact]
    public void A_case_in_the_list_that_declares_it_is_not_marked()
    {
        var choice = Step("control.switch",
            Text("switchValue", "a"),
            Text("switchMode", "equals"),
            Steps("cases", Case("a")),
            Steps("otherwise"));

        Assert.Empty(Inspect(choice));
    }

    /// <summary>
    /// 这个项目里还有别的宏用浏览器时，说一次：整个程序只有一份浏览器与当前页，两条宏同时跑时
    /// 一条切页会改变另一条读到的页面。
    /// </summary>
    [Fact]
    public void Using_the_browser_next_to_another_macro_that_does_is_said_once()
    {
        var first = Step("browser.switchTab", Text("tabHow", "newest"));
        var second = Step("browser.goTo", Text("url", "https://example.com"));

        var problems = Ui.Run(() => MacroCheck.Inspect([first, second], null, [], [],
            browserShared: true));

        var problem = Assert.Single(problems);
        Assert.Same(first, problem.Step);
        Assert.Equal(Strings.Get("Editor.Problem.SharedBrowser"), problem.Message);

        Assert.Empty(Ui.Run(() => MacroCheck.Inspect([first, second], null, [], [])));
    }

    /// <summary>
    /// 脚本说它会自己发键鼠时提醒一句：这条输入不经过互斥门，另一条宏仍可能和它插队。
    /// </summary>
    [Fact]
    public void A_script_that_declares_it_sends_input_is_marked()
    {
        var sending = Step("script.ahk",
            Text("script", "Print(\"hi\")"), Flag("sendsInput", true));

        var problem = Assert.Single(Inspect(sending));
        Assert.Equal(Strings.Get("Editor.Problem.ScriptInput"), problem.Message);

        var quiet = Step("script.ahk",
            Text("script", "Print(\"hi\")"), Flag("sendsInput", false));
        Assert.Empty(Inspect(quiet));
    }

    [Fact]
    public void A_catch_condition_may_read_the_failure_it_was_given()
    {
        var trying = Step("control.try",
            Steps("body", Pressing()),
            When("catchWhen", Formula.Binary("==", Formula.Name("caught.key"), Formula.Text("Run.ElementNotFound"))),
            Steps("catch"),
            Steps("finally"));

        Assert.Empty(Inspect(trying));
    }

    /// <summary>A relative move offers foreground and driver only, so background is marked as an
    /// option the action does not have.</summary>
    [Fact]
    public void A_relative_move_has_no_background_option()
    {
        var posted = Step("input.mouseMoveRelative",
            Text("dx", "200"), Text("inputMode", "background"));

        var problem = Assert.Single(Inspect(posted));

        Assert.Same(posted, problem.Step);
        Assert.Contains("background", problem.Message, StringComparison.Ordinal);

        // 前台与驱动级各自都是从指针当前位置往前走的，不标；别的动手的事也问不到这一条。
        Assert.Empty(Inspect(Step("input.mouseMoveRelative", Text("dx", "200"))));
        Assert.Empty(Inspect(Step("input.mouseMoveRelative",
            Text("dx", "200"), Text("inputMode", "driver"))));
        Assert.Empty(Inspect(Step("input.mouseMove",
            Text("x", "10"), Text("y", "20"), Text("inputMode", "background"),
            Text("window", "Notepad"))));
    }
}
