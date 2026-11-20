using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The detection family on its own: the fake model answers with the boxes a test wrote down, so a
/// step that looks, waits or clicks can be checked without a model file or a screen.
/// </summary>
public class DetectionActionTests
{
    [Fact]
    public async Task Looking_hands_over_each_box_and_the_one_the_step_went_with()
    {
        var context = new FakeRunContext();
        context.Machine.Detector.Detections.Add(
            new DetectionHit(0.8, new ScreenPoint(10, 20), new ScreenSize(40, 30), 0, "person"));

        await Run(context, "vision.detect", Step("vision.detect",
            Fields.Text("model", "model.onnx"),
            SampleFiles.Returns("found", "match", "every")));

        Assert.True(context.Written["found"].AsBool());
        Assert.Equal(30, Field(context.Written["match"], FieldNames.X).AsNumber());
        Assert.Equal("person", Field(context.Written["match"], FieldNames.Label).AsText());
        Assert.Single(context.Written["every"].Items);
    }

    [Fact]
    public async Task Clicking_a_box_clicks_the_middle_of_it()
    {
        var context = new FakeRunContext();
        context.Machine.Detector.Detections.Add(
            new DetectionHit(0.8, new ScreenPoint(10, 20), new ScreenSize(40, 30), 0, "person"));

        await Run(context, "vision.clickDetect", Step("vision.clickDetect",
            Fields.Text("model", "model.onnx"),
            Fields.Text("offsetX", "5"),
            SampleFiles.Returns("match", "every")));

        Assert.Contains("click left 35 35 1 0", context.Machine.Desk.Calls);
    }

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => DetectionActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
