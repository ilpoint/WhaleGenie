using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// What the detection model found, and how a step picks one of its boxes.
public partial class DeviceActionTests
{
    /// <summary>
    /// 检测到的每一处交出的是一条与其他找东西的步骤同样形状的记录：框在哪、有多大、模型有多有把握，
    /// 另外多出模型给的类序号与标签文件给这个名字。
    /// </summary>
    [Fact]
    public async Task A_detected_thing_is_handed_over_with_where_it_is_and_what_it_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Detector.Detections.Add(
            new DetectionHit(0.87, new ScreenPoint(30, 40), new ScreenSize(10, 20), 2, "car"));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.detect",
                Param("model", @"models\yolo.onnx"),
                Param("labelFile", "classes.txt"),
                Param("labels", "car\nbus"),
                Param("confidence", "40"),
                Hit("match", "spot", every: true)),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 框的正中（点击动作要点的那个点）、左上角与整块矩形，与找图那一步一样。
        Assert.Equal("35,50", Centre(store, "spot"));
        Assert.Equal(30, Field(store, "spot", "left").AsNumber());
        Assert.Equal(40, Field(store, "spot", "top").AsNumber());
        Assert.Equal("30,40,10,20", Field(store, "spot", "box").AsText());
        Assert.Equal(0.87, Field(store, "spot", "score").AsNumber(), 3);

        // 多出来的两项：类序号与标签文件给它的名字。
        Assert.Equal(2, Field(store, "spot", "class").AsNumber());
        Assert.Equal("car", Field(store, "spot", "label").AsText());

        // 「全部命中」也是一样的记录，一条命中一条。
        Assert.Single(store.Local.Values["spot.every"].AsList());

        // 模型与标签文件的路径都按宏目录解析，交给设备的是解出来的全路径；要认的类一行一个，
        // 置信度写多少就是多少。
        Assert.Equal(@"G:\fake\models\yolo.onnx", Assert.Single(devices.Detector.Models));
        var query = Assert.Single(devices.Detector.DetectionQueries);
        Assert.Equal(40, query.ConfidencePercent);
        Assert.Equal(["car", "bus"], query.Labels);
        Assert.Equal(@"G:\fake\classes.txt", query.LabelFile);
    }

    /// <summary>
    /// 检测范围里报出来的框按屏幕像素算：设备从交给它的那块画面的角落数起，这一步把区域自己的位置
    /// 加上去，后面的步骤拿到的就是屏幕上的位置。
    /// </summary>
    [Fact]
    public async Task A_detection_in_a_region_counts_from_the_corner_of_that_region()
    {
        var devices = new FakeDeviceLayer();
        devices.Detector.Detections.Add(
            new DetectionHit(0.9, new ScreenPoint(5, 6), new ScreenSize(10, 10), 0, "person"));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.detect", Param("model", "yolo.onnx"),
                Region((100, 200, 300, 400)), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("110,211", Centre(store, "where"));
        Assert.Contains("capture 100 200 300 400", devices.Calls);
    }

    /// <summary>
    /// 等待检测到为止：没认出东西就再看一遍，认出为止；等多久由这一步自己的超时说了算。
    /// </summary>
    [Fact]
    public async Task Waiting_for_a_detection_looks_again_until_the_model_finds_something()
    {
        var devices = new FakeDeviceLayer();
        devices.Detector.DetectAfter = 3;
        devices.Detector.Detections.Add(
            new DetectionHit(0.8, new ScreenPoint(7, 9), new ScreenSize(4, 4), 1, "bicycle"));

        var (result, _, store) = await RunAsync(
        [
            Within(5000, Step("vision.waitDetect", Param("model", "yolo.onnx"),
                Hit("match", "spot"))),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(devices.Detector.Looks >= 3);
        Assert.Equal("bicycle", Field(store, "spot", "label").AsText());
        Assert.Equal("9,11", Centre(store, "spot"));
    }

    /// <summary>
    /// 一直没有认出东西时，失败说的是"始终没有检测到"，并带上找的是什么 —— 模型，以及要认的类。
    /// </summary>
    [Fact]
    public async Task A_detection_that_never_turns_up_fails_the_step_that_waited_for_it()
    {
        var devices = new FakeDeviceLayer();

        var (result, _, _) = await RunAsync(
        [
            Within(40, Step("vision.waitDetect", Param("model", @"models\enemy.onnx"),
                Param("labels", "enemy"))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.DetectNotFound", result.Key);
        Assert.Contains("enemy.onnx", result.Detail);
        Assert.Contains("enemy", result.Detail);
    }

    /// <summary>
    /// 模型一次就答出全部框，所以这一步不管自己只要第几个，都先把它们全收下来，再按自己说的顺序数
    /// 「第几个命中」。只问一个的话，设备按分数截断，挑中的就不是这一步排序里的第一个。
    /// </summary>
    [Fact]
    public async Task Every_box_the_model_found_is_collected_before_the_step_picks_one()
    {
        var devices = new FakeDeviceLayer();

        // 分数最高的那一处在右下角，阅读顺序的第一个在左上角：只要阅读顺序的第一个，就该拿到左上的。
        devices.Detector.Detections.Add(
            new DetectionHit(0.95, new ScreenPoint(900, 700), new ScreenSize(20, 20), 0, "person"));
        devices.Detector.Detections.Add(
            new DetectionHit(0.6, new ScreenPoint(10, 12), new ScreenSize(20, 20), 5, "bus"));

        var (result, _, store) = await RunAsync(
        [
            Step("vision.detect", Param("model", "yolo.onnx"), Param("matchIndex", "1"),
                Hit("match", "spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("bus", Field(store, "spot", "label").AsText());
        Assert.Equal("20,22", Centre(store, "spot"));
        Assert.True(Assert.Single(devices.Detector.DetectionQueries).Limit > 1);
    }

    /// <summary>
    /// 点击检测到的物体落在这个框的正中，再加上这一步自己写的两个偏移。
    /// </summary>
    [Fact]
    public async Task Clicking_a_detected_thing_lands_on_the_middle_of_its_box_and_the_offset()
    {
        var devices = new FakeDeviceLayer();
        devices.Detector.Detections.Add(
            new DetectionHit(0.95, new ScreenPoint(30, 40), new ScreenSize(10, 20), 0, "person"));

        var (result, _, store) = await RunAsync(
        [
            Within(5000, Step("vision.clickDetect", Param("model", "yolo.onnx"),
                Param("offsetX", "10"), Param("offsetY", "-5"), Returns("match=spot"))),
        ], devices);

        Assert.True(result.Succeeded, result.Key);

        // 框的中心是 (35,50)，正 10、负 5 之后落在 (45,45)。
        var click = Assert.Single(devices.Calls,
            call => call.StartsWith("click ", StringComparison.Ordinal));
        Assert.Equal("click left 45 45 1 0", click);

        // 点到的那一处照旧交出去，位置是框自己的，不是落点。
        Assert.Equal("35,50", Centre(store, "spot"));
        Assert.Equal("person", Field(store, "spot", "label").AsText());
    }

    /// <summary>
    /// 模型读不出来、或者答出的形状这一步读不了时，设备给出的原因原样报出来：写宏的人要的是"哪个
    /// 模型、什么形状"，而不是"某一步失败了"。
    /// </summary>
    [Fact]
    public async Task A_model_the_device_refuses_keeps_its_own_reason()
    {
        var devices = new FakeDeviceLayer();
        devices.Detector.DetectionAnswers = (_, _) =>
                throw new DeviceActionException("Run.MissingModel", @"models\ghost.onnx");

        var (result, _, _) = await RunAsync(
            [Step("vision.detect", Param("model", @"models\ghost.onnx"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingModel", result.Key);
        Assert.Equal(@"models\ghost.onnx", result.Detail);

        devices.Detector.DetectionAnswers = (_, _) =>
            throw new DeviceActionException("Run.BadModelShape", "1x25200x85");

        var (shaped, _, _) = await RunAsync(
            [Step("vision.detect", Param("model", "old.onnx"))], devices);

        Assert.False(shaped.Succeeded);
        Assert.Equal("Run.BadModelShape", shaped.Key);
        Assert.Equal("1x25200x85", shaped.Detail);
    }

    [Fact]
    public async Task A_search_region_is_copied_before_it_is_searched()
    {
        var devices = new FakeDeviceLayer();
        devices.Vision.Loaded = new ImageFrame(2, 2, new byte[16]);
        devices.Vision.Match = new ImageMatch(0.99, new ScreenPoint(0, 0), new ScreenSize(2, 2));

        var (_, _, store) = await RunAsync(
        [
            Step("vision.findImage", Pictures("a.png"), Param("confidence", "90"),
                Region((10, 20, 30, 40)), Hit("match", "where")),
        ], devices);

        // The match is reported in screen coordinates, not in region coordinates.
        Assert.Contains("capture 10 20 30 40", devices.Calls);
        Assert.Equal("11,21", Centre(store, "where"));
    }

    [Fact]
    public async Task A_missing_device_is_reported_as_such()
    {
        var devices = new FakeDeviceLayer { Unavailable = true };
        var (result, _, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "A"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.NoDevice", result.Key);
    }

    [Fact]
    public async Task A_refused_action_keeps_its_reason()
    {
        var devices = new FakeDeviceLayer { Refusal = new DeviceActionException("Run.UnknownKey", "QQQ") };
        var (result, _, _) = await RunAsync(
            [Step("input.keyPress", Param("key", "QQQ"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.UnknownKey", result.Key);
        Assert.Equal("QQQ", result.Detail);
    }

    [Fact]
    public async Task The_engine_still_runs_without_any_devices()
    {
        // The stand-in for "no devices at all" is the engine's own default.
        var store = new VariableStore();
        var result = await new MacroRunner(store).RunAsync(
            [Step("control.setVariable", Param("name", "a"), Param("value", "1"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.Local.Values["a"].AsNumber());
    }
}
