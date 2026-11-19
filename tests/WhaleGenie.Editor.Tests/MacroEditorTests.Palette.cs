using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The left palette: its sources, the one-line rows, and the id badge.
public partial class MacroEditorTests
{
    [Fact]
    public void The_left_palette_has_four_sources()
    {
        Ui.Run(() =>
        {
            var (window, _) = Open();
            var palette = Assert.IsType<ActionPaletteView>(
                window.FindControl<ActionPaletteView>("ActionPalette"));
            var model = Assert.IsType<ActionPaletteViewModel>(palette.DataContext);

            Assert.Equal(4, model.Tabs.Count);
            model.SelectDock(ActionPaletteDock.Basic);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(model.Cards, card => card.Key == "input.keyPress");

            // 日常那一栏里没有“流程”那一类的动作。判的是分类，不是名字前缀：变量与列表那几个
            // 动作虽然还叫 control.*，分类早已归到「数据与变量」，它们本来就该在这一栏里。
            var flow = ActionCatalog.Definitions
                .Where(action => action.Category == ActionCategory.Control)
                .Select(action => action.Key)
                .ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(flow);
            Assert.DoesNotContain(model.Cards, card => flow.Contains(card.Key));

            model.SelectDock(ActionPaletteDock.Flow);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(model.Cards, card => card.Key == "control.repeat");
            Assert.All(model.Cards, card => Assert.StartsWith("control.", card.Key));
        });
    }

    /// <summary>
    /// 步骤行平时是**一行**（下限 30）：说明截断、备注与执行节奏不占地方。点中的那一行才在
    /// 原处展开，把完整说明、备注与节奏露出来；别的行仍旧一行，列表不因为看一步而整体变高。
    /// </summary>
    [Fact]
    public void A_step_row_is_one_line_until_it_is_the_one_that_was_clicked()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            var first = new MacroStep { Type = "control.delay" };
            var second = new MacroStep
            {
                Type = "input.keyPress",
                Meta = new StepMeta { Comment = new string('长', 160) },
            };
            viewModel.AddStep(first);
            viewModel.AddStep(second);
            viewModel.SetSelection([]);
            Dispatcher.UIThread.RunJobs();

            var rows = window.FindControl<TreeView>("StepList")!
                .GetVisualDescendants().OfType<TreeViewItem>().ToList();
            Assert.Equal(2, rows.Count);

            Border RowLine(TreeViewItem item) => item.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("StepLine"));

            foreach (var item in rows)
            {
                Assert.True(item.Bounds.Height >= 30, $"步骤行只有 {item.Bounds.Height:0} 高");
                Assert.True(item.Bounds.Height + 0.5 >= RowLine(item).Bounds.Height,
                    $"行高 {item.Bounds.Height:0} 装不下卡片 {RowLine(item).Bounds.Height:0}");
                Assert.True(RowLine(item).Bounds.Height <= 40,
                    $"没点中的卡片长到了 {RowLine(item).Bounds.Height:0}，说明被折成了多行");
            }

            // 点中带长备注的那一步：这一行在原处长高，另一行不动。选的是行上那一步本身，
            // 因为加进列表的步骤未必是同一个对象。
            viewModel.SetSelection([((StepRow)rows[1].DataContext!).Step]);
            Dispatcher.UIThread.RunJobs();

            Assert.True(((StepRow)rows[1].DataContext!).ShowsDetail, "行没有标记为展开");
            Assert.True(RowLine(rows[1]).Bounds.Height > 40,
                $"点中的那一行没有展开：{RowLine(rows[1]).Bounds.Height:0}");
            Assert.True(RowLine(rows[0]).Bounds.Height <= 40,
                $"没点中的那一行跟着长到了 {RowLine(rows[0]).Bounds.Height:0}");

            Ui.CloseAndDiscard(window);
        });
    }

    /// <summary>
    /// 步骤 ID 的提示只挂在 ID 那枚徽标上。整张卡片都提示，等于每一步随手一指都弹出同一句，
    /// 挡住本来要看的两行字。
    /// </summary>
    [Fact]
    public void Only_the_id_badge_explains_the_step_id()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();
            viewModel.AddStep(new MacroStep { Type = "control.delay" });
            Dispatcher.UIThread.RunJobs();

            var item = window.FindControl<TreeView>("StepList")!
                .GetVisualDescendants().OfType<TreeViewItem>().First();
            var card = item.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("StepLine"));
            var badge = card.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("Accent"));

            var row = viewModel.Rows.First(step => step.IsStep);
            Assert.Null(ToolTip.GetTip(card));
            Assert.Equal(row.IdHint, ToolTip.GetTip(badge) as string);
            Assert.Contains(row.Id, row.IdHint, StringComparison.Ordinal);
            Ui.CloseAndDiscard(window);
        });
    }
}
