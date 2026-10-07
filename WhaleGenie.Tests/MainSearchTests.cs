using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The search box on the main window used to be bound to nothing: what was typed went into a
/// property no list read, so the list never changed. These read what it narrows now.
/// </summary>
public class MainSearchTests
{
    [Fact]
    public void The_search_box_narrows_the_macro_list()
    {
        var viewModel = new MainViewModel();
        viewModel.AddMacro(new MacroItem { Name = "登录" });
        viewModel.AddMacro(new MacroItem { Name = "导出表格" });

        Assert.Equal(2, viewModel.VisibleMacros.Count);

        viewModel.SearchText = "表";

        Assert.Equal("导出表格", Assert.Single(viewModel.VisibleMacros).Name);
        Assert.True(viewModel.HasMacros);
        Assert.False(viewModel.HasNoMatch);

        // The macros themselves are untouched: what was typed narrows the view, not the project.
        Assert.Equal(2, viewModel.Macros.Count);
    }

    [Fact]
    public void A_search_that_matches_nothing_says_so()
    {
        var viewModel = new MainViewModel();
        viewModel.AddMacro(new MacroItem { Name = "登录" });

        viewModel.SearchText = "找不到的宏";

        Assert.Empty(viewModel.VisibleMacros);
        Assert.True(viewModel.HasNoMatch);

        viewModel.SearchText = string.Empty;

        Assert.Single(viewModel.VisibleMacros);
        Assert.False(viewModel.HasNoMatch);
    }

    [Fact]
    public void A_macro_added_while_a_search_is_on_is_kept_only_when_it_matches()
    {
        var viewModel = new MainViewModel { SearchText = "log" };
        viewModel.AddMacro(new MacroItem { Name = "login" });
        viewModel.AddMacro(new MacroItem { Name = "logout" });
        viewModel.AddMacro(new MacroItem { Name = "save" });

        Assert.Equal(["login", "logout"],
            viewModel.VisibleMacros.Select(macro => macro.Name).ToList());
    }

    [Fact]
    public void Removing_a_macro_takes_it_out_of_the_narrowed_list_too()
    {
        var viewModel = new MainViewModel();
        var login = new MacroItem { Name = "login" };
        viewModel.AddMacro(login);
        viewModel.AddMacro(new MacroItem { Name = "save" });

        viewModel.SearchText = "log";
        Assert.Single(viewModel.VisibleMacros);

        viewModel.RemoveMacro(login);

        Assert.Empty(viewModel.VisibleMacros);
        Assert.True(viewModel.HasNoMatch);
    }
}
