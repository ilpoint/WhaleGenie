using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// Groups are the second layer beside the fixed views: the view says how a macro starts, the group
/// says which part of someone's work it belongs to. Both filters apply at once, and a group only
/// exists while a macro carries its name.
/// </summary>
public class MainGroupFilterTests
{
    [Fact]
    public void Choosing_a_group_narrows_the_list_to_it()
    {
        var viewModel = new MainViewModel { SelectedView = MacroView.All };
        viewModel.AddMacro(new MacroItem { Name = "raid", Group = "游戏" });
        viewModel.AddMacro(new MacroItem { Name = "report", Group = "办公" });
        viewModel.AddMacro(new MacroItem { Name = "loose" });

        // The filter opens on everything.
        Assert.Equal(["raid", "report", "loose"],
            viewModel.VisibleMacros.Select(macro => macro.Name));

        viewModel.SelectedGroup = viewModel.Groups.Single(choice => choice.Name == "游戏");
        Assert.Equal(["raid"], viewModel.VisibleMacros.Select(macro => macro.Name));

        viewModel.SelectedGroup = viewModel.Groups.Single(choice => choice.Name is null);
        Assert.Equal(["loose"], viewModel.VisibleMacros.Select(macro => macro.Name));

        viewModel.SelectedGroup = viewModel.Groups[0];
        Assert.Equal(3, viewModel.VisibleMacros.Count);
    }

    [Fact]
    public void A_group_leaves_the_filter_when_its_last_macro_does()
    {
        var viewModel = new MainViewModel { SelectedView = MacroView.All };
        var only = new MacroItem { Name = "raid", Group = "游戏" };
        viewModel.AddMacro(only);
        Assert.Contains(viewModel.Groups, choice => choice.Name == "游戏");

        viewModel.RemoveMacro(only);

        Assert.DoesNotContain(viewModel.Groups, choice => choice.Name == "游戏");
        Assert.Equal(2, viewModel.Groups.Count);
    }

    [Fact]
    public void A_group_survives_the_file_and_is_not_written_when_empty()
    {
        var macro = new MacroItem { Name = "raid", Group = "  游戏 " };
        Assert.Equal("游戏", MacroItem.FromJson(macro.ToJson()).Group);

        var ungrouped = new MacroItem { Name = "loose" };
        Assert.False(ungrouped.ToJson().ContainsKey("group"));
    }

    [Fact]
    public void The_editor_carries_the_group_in_and_out()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "raid", Group = "游戏" };
            var editor = new MacroEditorViewModel();
            editor.LoadFrom(macro);
            Assert.Equal("游戏", editor.Group);

            editor.Group = " 办公 ";
            MacroItem? saved = null;
            editor.CloseRequested += built => saved = built;
            editor.SaveCommand.Execute(null);

            Assert.Equal("办公", saved?.Group);
        });
    }
}
