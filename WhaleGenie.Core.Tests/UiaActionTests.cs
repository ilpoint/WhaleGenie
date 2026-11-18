using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The element family on its own: the fake element device answers what it is asked and records the
/// query it was handed, so a step's search and what it hands over can be checked without a desktop.
/// </summary>
public class UiaActionTests
{
    [Fact]
    public async Task Existence_is_answered_as_a_yes_or_no()
    {
        var context = new FakeRunContext();
        context.Machine.Ui.ElementExists = false;

        await Run(context, "uia.exists",
            Step("uia.exists", Fields.Text("uiSelector", "Button[name='Save']")));

        Assert.False(context.Written["exists"].AsBool());
    }

    [Fact]
    public async Task Finding_an_element_hands_over_the_hit_and_the_list()
    {
        var context = new FakeRunContext();
        context.Machine.Ui.Elements.Add(new UiElementInfo("Save", "save", "Button", string.Empty,
            new ScreenPoint(10, 20), new ScreenSize(30, 10), "Document"));
        context.Machine.Window.Open.Add(new WindowInfo(1, "Document",
            new ScreenPoint(0, 0), new ScreenSize(800, 600), false, false));

        await Run(context, "uia.find", Step("uia.find",
            Fields.Text("uiSelector", "Button[name='Save']"),
            SampleFiles.Returns("found", "match", "window", "every")));

        Assert.True(context.Written["found"].AsBool());
        var match = context.Written["match"];
        Assert.Equal(25, Field(match, FieldNames.X).AsNumber());
        Assert.Equal(25, Field(match, FieldNames.Y).AsNumber());
        Assert.Equal("10,20,30,10", Field(match, FieldNames.Box).AsText());
        Assert.Equal("Document", context.Written["window"].AsText());

        var every = context.Written["every"].Items;
        var only = Assert.Single(every);
        Assert.Equal(25, Field(only, FieldNames.X).AsNumber());
    }

    [Fact]
    public async Task Reading_an_element_hands_over_what_it_says_about_itself()
    {
        var context = new FakeRunContext();
        context.Machine.Ui.ElementText = "typed words";
        context.Machine.Ui.Elements.Add(new UiElementInfo("Box", "box", "Edit", string.Empty,
            new ScreenPoint(5, 7), new ScreenSize(100, 20), "Document"));

        await Run(context, "uia.read", Step("uia.read",
            Fields.Text("uiSelector", "Edit[automationId='box']"),
            SampleFiles.Returns("element")));

        var element = context.Written["element"];
        Assert.Equal("typed words", Field(element, FieldNames.Value).AsText());
        Assert.Equal("Box", Field(element, FieldNames.Name).AsText());
        Assert.Equal("box", Field(element, FieldNames.AutomationId).AsText());
        Assert.Equal("5,7,100,20", Field(element, FieldNames.Box).AsText());
    }

    [Fact]
    public async Task A_narrow_row_reaches_the_device_as_a_condition()
    {
        var context = new FakeRunContext();

        await Run(context, "uia.exists", Step("uia.exists",
            Fields.Text("uiSelector", "Button"),
            Fields.Rows("narrow",
                Cells.Of(("how", "visible"), ("value", string.Empty)),
                Cells.Of(("how", string.Empty), ("value", "a row nobody filled in")),
                Cells.Of(("how", "nameHas"), ("value", "Save")))));

        var conditions = context.Machine.Ui.LastQuery!.Conditions;
        Assert.Collection(conditions,
            condition => Assert.Equal(UiConditionHow.Visible, condition.How),
            condition =>
            {
                Assert.Equal(UiConditionHow.NameHas, condition.How);
                Assert.Equal("Save", condition.Value);
            });
    }

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => UiaActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
