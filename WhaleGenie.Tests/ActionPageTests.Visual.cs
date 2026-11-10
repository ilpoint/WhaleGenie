using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The visual actions: recognition, the beat, and what a model looks for.
public partial class ActionPageTests
{
    /// <summary>
    /// A listing names its windows with the same captured rule the other window actions use, and that
    /// rule stands on the basic page next to what the step is; whether every program and class is
    /// read as well is tuning.
    /// </summary>
    [Fact]
    public void A_listing_names_its_windows_the_way_the_window_actions_do()
    {
        var list = Ui.Run(() => ActionCatalog.Find("window.list")!);

        var basic = On(list, viewModel => viewModel.Rows);
        Assert.Contains("window", basic);
        Assert.DoesNotContain("how", basic);

        var advanced = On(list, viewModel => viewModel.AdvancedRows);
        Assert.DoesNotContain("how", advanced);
        Assert.Contains("details", advanced);
    }

    /// <summary>
    /// Visual actions call the same field "recognition source"; it belongs on the basic page
    /// because it decides which picture is read. The window-only capture has no such choice.
    /// </summary>
    [Fact]
    public void Visual_actions_keep_recognition_source_on_the_basic_page()
    {
        var actions = Ui.Run(() => ActionCatalog.Definitions
            .Where(action => action.Parameters.Any(parameter => parameter.Name == "anchorMode"))
            .ToList());
        var problems = new List<string>();
        foreach (var action in actions)
        {
            var basic = On(action, viewModel => viewModel.Rows);
            if (!basic.Contains("anchorMode", StringComparer.Ordinal))
            {
                problems.Add($"{action.Key}: anchorMode is not on the basic page");
            }
            else if (basic[0] != "anchorMode" || basic[1] != "window")
            {
                problems.Add($"{action.Key}: recognition source is not the first row");
            }
        }

        if (Ui.Run(() => ActionCatalog.Find("vision.captureWindow")!.Parameters
                .Any(parameter => parameter.Name == "anchorMode")))
        {
            problems.Add("vision.captureWindow has an anchorMode");
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// A step that looks at the screen waits between two looks, and that number is not the same
    /// question as the beat of a step that presses a key: one is how much a loop may cost, the other
    /// is how the presses read. The first is tuning and the second is part of the action.
    /// </summary>
    /// <summary>
    /// 只有循环与输入还带自己的节拍：它们之间隔多久是这一步要回答的问题。看屏幕的节拍已经
    /// 搬到设置里，视觉与识别的步骤不再有这个字段，所以这里只剩发输入的那几个。
    /// </summary>
    [Theory]
    [InlineData("input.keyPress", "base")]
    [InlineData("input.mouseClick", "base")]
    [InlineData("input.mouseScroll", "base")]
    public void A_step_that_sends_input_keeps_its_beat_on_the_page_the_step_is(
        string action, string page)
    {
        Ui.Run(() =>
        {
            var viewModel = Open(action);
            var names = page == "advanced"
                ? viewModel.AdvancedRows.SelectMany(Names).ToList()
                : viewModel.Rows.SelectMany(Names).ToList();

            Assert.Contains("intervalMs", names);
        });
    }

    /// <summary>
    /// 每秒看几次屏幕是调参，隔多久跑下一轮不是：后者决定这个循环跑起来是什么手感，与次数、
    /// 遍历对象、什么时候停一样，是这一步要回答的问题，所以它和这些字段同处基础设置，在
    /// 「节奏」那一块里。看屏幕的轮询仍留在高级设置，两件事因此没有混成一条规则。
    /// </summary>
    [Fact]
    public void A_loop_waits_between_rounds_beside_what_it_runs()
    {
        Ui.Run(() =>
        {
            var viewModel = Open("control.forEach");
            var basic = viewModel.Rows.SelectMany(Names).ToList();

            Assert.Contains("intervalMs", basic);
            Assert.DoesNotContain("intervalMs", viewModel.AdvancedRows.SelectMany(Names));

            var field = ActionCatalog.Find("control.forEach")!.Parameters
                .First(parameter => parameter.Name == "intervalMs");
            Assert.Equal("0", field.DefaultValue);
            Assert.Equal(0m, field.Minimum);
            Assert.Equal(165600000m, field.Maximum);
        });
    }

    /// <summary>
    /// How a picture is compared — the way of recognising it, how many feature pairs have to line
    /// up, which colour to ignore, and in what order the hits are counted — is the same kind of
    /// answer as the rest of the tuning, and is asked for on the advanced page.
    /// </summary>
    [Theory]
    [InlineData("vision.findImage")]
    [InlineData("vision.waitImage")]
    [InlineData("vision.clickImage")]
    public void How_a_picture_is_recognised_is_asked_for_on_the_advanced_page(string action)
    {
        var finder = Ui.Run(() => ActionCatalog.Find(action)!);
        var advanced = On(finder, viewModel => viewModel.AdvancedRows);

        Assert.Contains("detectAlgorithm", advanced);
        Assert.Contains("minFeatures", advanced);
        Assert.Contains("transparentColor", advanced);
        Assert.Contains("orderBy", advanced);
    }

    /// <summary>
    /// 用模型找东西的那几步：模型、标签文件与要认的类是要什么，检测范围是在哪，置信度是多严，
    /// 这几样都在基础页上挨着；要换个做法才会动的两个数（模型输入尺寸、允许重叠）在高级页。
    /// </summary>
    [Theory]
    [InlineData("vision.detect")]
    [InlineData("vision.waitDetect")]
    [InlineData("vision.clickDetect")]
    public void What_a_model_looks_for_is_on_the_basic_page_and_how_it_is_run_is_tuning(
        string action)
    {
        var detector = Ui.Run(() => ActionCatalog.Find(action)!);

        var basic = On(detector, viewModel => viewModel.Rows);
        Assert.Contains("model", basic);
        Assert.Contains("labelFile", basic);
        Assert.Contains("labels", basic);
        Assert.Contains("region", basic);
        Assert.Contains("confidence", basic);
        Assert.DoesNotContain("inputSize", basic);
        Assert.DoesNotContain("iou", basic);

        var advanced = On(detector, viewModel => viewModel.AdvancedRows);
        Assert.Contains("inputSize", advanced);
        Assert.Contains("iou", advanced);
        Assert.Contains("orderBy", advanced);
        Assert.Contains("matchIndex", advanced);
    }
}
