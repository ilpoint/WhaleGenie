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

// The recording panel and the line that names the key it stops on.
public partial class MacroEditorTests
{

    /// <summary>The warning mark every row carries, whether or not it is showing.</summary>
    private static List<Control> Marks(Window window)
        => [.. window.GetVisualDescendants().OfType<Control>()
            .Where(control => control.Classes.Contains("ProblemMark"))];

    /// <summary>
    /// 独占输入是「独占输入」块的性质，不是宏的：宏文件里没有这一格，旧包里还留着的按未知字段读过去，
    /// 读得开、读回来也没有，再存一次就没了。
    /// </summary>
    [Fact]
    public void A_macro_carries_no_input_door_of_its_own()
    {
        Ui.Run(() =>
        {
            var (window, viewModel) = Open();

            viewModel.SaveCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var saved = window.Result;
            Assert.NotNull(saved);
            Assert.False(saved!.ToJson().ContainsKey("exclusiveInput"));
            Assert.False(saved.ToSubMacroJson().ContainsKey("exclusiveInput"));

            // 旧包：多出来的那一格不该让这个包读不开，也不该被照原样写回去。
            var older = saved.ToJson();
            older["exclusiveInput"] = true;
            Assert.False(MacroItem.FromJson(older).ToJson().ContainsKey("exclusiveInput"));
        });
    }

    /// <summary>
    /// 录制面板那一格不是宏的一部分：打开宏时取的是设置里的「忽略自身输入」，保存时也不写进文件 ——
    /// 它只对这一次录制说话。
    /// </summary>
    [Fact]
    public void The_recording_panel_starts_from_the_setting_and_saves_nothing()
    {
        var was = WhaleGenie.Core.Devices.WhaleGenieInputGate.IgnoreOwnInput;
        try
        {
            Ui.Run(() =>
            {
                // 进去再改：无头会话第一次启动时，程序按设置文件把自己那一份答案读一遍，
                // 在外面改的值会被它盖掉。
                WhaleGenie.Core.Devices.WhaleGenieInputGate.IgnoreOwnInput = false;

                var (window, viewModel) = Open();
                Assert.False(viewModel.RecordIgnoreInjected);

                viewModel.SaveCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                var saved = window.Result;
                Assert.NotNull(saved);
                Assert.False(saved!.ToJson()["recorder"]!.AsObject().ContainsKey("ignoreInjected"));
            });
        }
        finally
        {
            WhaleGenie.Core.Devices.WhaleGenieInputGate.IgnoreOwnInput = was;
        }
    }

    /// <summary>
    /// 录制面板上那行字写的是设置里那个键，不是写死的 F10 —— 改了停止键却还写着旧键，等于教人按错。
    /// </summary>
    [Fact]
    public void The_recording_line_names_the_key_the_settings_kept()
    {
        var stored = WhaleGenie.Localization.LocalSettings.FilePath;
        var folder = Path.Combine(Path.GetTempPath(), "wg-record-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            WhaleGenie.Localization.LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            WhaleGenie.Localization.LocalSettings.StoreRecordStopKey("Ctrl+Alt+F12");

            Ui.Run(() =>
            {
                var (_, viewModel) = Open();
                Assert.Contains("Ctrl+Alt+F12", viewModel.RecordStatusText, StringComparison.Ordinal);
                Assert.Contains("Ctrl+Alt+F12", viewModel.RecordStartTip, StringComparison.Ordinal);
            });
        }
        finally
        {
            WhaleGenie.Localization.LocalSettings.FilePath = stored;
            Directory.Delete(folder, true);
        }
    }
}
