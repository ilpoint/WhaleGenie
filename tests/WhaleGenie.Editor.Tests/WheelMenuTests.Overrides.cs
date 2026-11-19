using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Editing an inherited position, and clearing an action without touching another scene.
public partial class WheelMenuTests
{
    [Fact]
    public void Editing_an_inherited_position_writes_a_program_override_and_clearing_empties_it()
    {
        InOwnFiles(() =>
        {
            var project = new MainViewModel();
            var settings = new SettingsViewModel(() => DriverInputState.Ready, project: project);
            var scene = project.Triggers.Wheel.AddScene(
                new WindowRule(Process: "notepad", How: WindowHow.ProcessEquals));
            project.Triggers.Wheel.Global.At(Top).Macro = "copy";
            settings.RefreshWheelScenes();
            settings.SelectedWheelScene =
                settings.WheelScenes.First(row => row.Key == scene.Key);

            // The editor starts from the global action the position is drawn with, and saving it
            // writes that action into this program's own scene.
            Assert.True(settings.IsWheelSlotInherited(Top));
            var draft = Assert.IsType<WheelSlot>(settings.WheelSlotForEdit(Top));
            Assert.Equal("copy", draft.Macro);
            draft.Note = "Copy the text";
            settings.SetWheelAction(Top, draft);

            Assert.False(settings.IsWheelSlotInherited(Top));
            Assert.Equal("copy", scene.At(Top).Macro);
            Assert.Equal("Copy the text", scene.At(Top).Note);

            // Clearing through the same path leaves nothing behind.
            settings.SetWheelAction(Top, new WheelSlot());
            Assert.True(scene.At(Top).IsEmpty);
            Assert.Equal(string.Empty, scene.At(Top).Note);
            Assert.Null(scene.At(Top).Icon);

            // The global scene still holds the action this program was inheriting.
            Assert.Equal("copy", project.Triggers.Wheel.Global.At(Top).Macro);
        });
    }

    [Fact]
    public void Clearing_the_global_action_does_not_touch_another_scene()
    {
        var rules = new WheelMenuRules();
        var scene = rules.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Top).Macro = "cut";
        rules.Find(WheelScene.GlobalKey)!.At(Top).Macro = "copy";

        Assert.Equal("copy", rules.Global.At(Top).Macro);
        rules.Global.At(Top).Macro = string.Empty;
        Assert.Equal("cut", scene.At(Top).Macro);
    }


    [Fact]
    public void The_action_dialog_opens_on_the_action_it_was_given()
    {
        Ui.Run(() =>
        {
            var slot = new WheelSlot { Macro = "copy", Note = "Copy the text" };
            var macros = new List<MacroItem>
            {
                new() { Name = "copy" },
                new() { Name = "paste" },
            };

            var window = new WheelActionWindow(slot, macros,
                "Inner circle, position 1", inherited: false);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var controls = window.GetVisualDescendants().OfType<Control>().ToList();
            Assert.Equal("copy", controls.OfType<TextBlock>()
                .Single(control => control.Name == "ActionMacroName").Text);
            Assert.Equal("Copy the text", controls.OfType<TextBox>()
                .Single(control => control.Name == "ActionNote").Text);
            // An empty program position inherits the global action again, so "clear this position"
            // is not offered where clearing would leave the wheel showing the same action.
            var clear = controls.OfType<Button>().Single(button =>
                Equals(button.Content, Strings.Get("Wheel.ActionClearSlot")));
            Assert.True(clear.IsVisible);
            window.Close();

            var inherited = new WheelActionWindow(slot, macros,
                "Inner circle, position 1", inherited: true);
            inherited.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var inheritedClear = inherited.GetVisualDescendants().OfType<Button>().Single(button =>
                Equals(button.Content, Strings.Get("Wheel.ActionClearSlot")));
            Assert.False(inheritedClear.IsVisible);
            inherited.Close();
        });
    }
}
