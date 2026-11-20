using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;


namespace WhaleGenie.Core.Tests;

// What a detection step saw, box by box, and how a trial of one behaves.
public partial class RunLookTests
{
    /// <summary>
    /// 用模型找东西的那几步交出的样子：看的是哪些块、模型认出的每一处（框连同它说是哪一类）、以及
    /// 这一步取的是哪一处。分是按"有多有把握"读数的那一档，与其他找东西的步骤一样。
    /// </summary>
    [Fact]
    public async Task What_a_detection_step_saw_is_handed_over_box_by_box()
    {
        var devices = Screen(width: 400, height: 300);
        devices.Detector.Detections.Add(
            new DetectionHit(0.62, new ScreenPoint(20, 30), new ScreenSize(40, 50), 0, "person"));
        devices.Detector.Detections.Add(
            new DetectionHit(0.91, new ScreenPoint(120, 60), new ScreenSize(30, 20), 3, "car"));
        var looks = new Watched();

        // 这一步只要第一处（没有勾「全部命中」）：交出来的仍然是模型认出的每一处，窗口里看得见的
        // 也是每一处，只有取走的那一处带着 Hit。
        await Run(Step("vision.detect", Param("model", @"C:\models\yolo.onnx"),
            Param("confidence", "25"), Hit("match", "spot")), devices, looks);

        var look = Assert.Single(looks.Seen);
        Assert.Equal("vision.detect", look.StepType);
        Assert.Equal(LookKind.Detection, look.Kind);
        Assert.Equal(0.25, look.Minimum!.Value, 6);

        // 看的那一块，然后模型认出的两处：都是一样的框，各自带着它说的那一类。
        Assert.Equal(3, look.Boxes.Count);
        Assert.Equal(LookRole.Area, look.Boxes[0].Role);
        Assert.Equal("person", look.Boxes[1].Label);
        Assert.Equal("car", look.Boxes[2].Label);
        Assert.Equal(new ScreenPoint(120, 60), look.Boxes[2].Match.Location);

        // 「阅读顺序」是默认的排序：先上后下，所以取的是上面那一处（第 1 处），另一处是候选。
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.Equal(LookRole.Candidate, look.Boxes[2].Role);
        Assert.Equal(2, look.ChosenIndex);
    }

    /// <summary>
    /// 等待与点击检测的「试找一下」只找一次：认出东西就交出来，认不出就交一份空的，且绝不点下去。
    /// </summary>
    [Fact]
    public void Trying_the_looking_of_a_detection_click_clicks_nothing()
    {
        var devices = Screen(width: 200, height: 100);
        devices.Detector.Detections.Add(
            new DetectionHit(0.7, new ScreenPoint(30, 40), new ScreenSize(10, 10), 0, "person"));
        var step = Step("vision.clickDetect", Param("model", "yolo.onnx"),
            Param("offsetX", "3"), Param("offsetY", "-2"), Param("button", "left"),
            Hit("match", "match"));

        var outcome = MacroRunner.LookOnce(step, devices);

        var look = Assert.IsType<StepLook>(outcome.Look);
        Assert.Equal(LookKind.Detection, look.Kind);
        Assert.Equal(LookRole.Hit, look.Boxes[1].Role);
        Assert.DoesNotContain(devices.Calls,
            call => call.StartsWith("click ", StringComparison.Ordinal));

        // No aiming mark: nothing was acted on, so there is nowhere the step went.
        Assert.DoesNotContain(look.Boxes, box => box.Role == LookRole.Target);
    }

    [Fact]
    public void Trying_the_looking_of_a_wait_does_not_wait()
    {
        var devices = Screen();
        var step = Step("vision.waitImage", Pictures(@"C:\images\ok.png"),
            Param("confidence", "90"), Hit("match", "match"));

        var outcome = MacroRunner.LookOnce(step, devices);

        Assert.NotNull(outcome.Look);
        var look = outcome.Look!;
        Assert.Equal(0, look.ChosenIndex);
        Assert.Equal(1, devices.Vision.Searches);
    }
}
