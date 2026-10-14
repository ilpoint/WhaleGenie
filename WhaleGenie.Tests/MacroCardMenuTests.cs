using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// What a right click on a macro card offers. Editing and deleting already have a button each, so
/// the menu is another way to the same three things those buttons now do. Which macro it is about is
/// the card that was right-clicked, and the menu is a popup, so that answer has to travel out of the
/// list with it.
/// </summary>
public class MacroCardMenuTests
{
    [Fact]
    public void The_card_offers_the_three_things_a_right_click_asks_for()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));

        var menu = markup[markup.IndexOf("<Border.ContextMenu>", StringComparison.Ordinal)..];
        menu = menu[..menu.IndexOf("</Border.ContextMenu>", StringComparison.Ordinal)];

        // Each line is the same thing the card's own buttons do, or the one thing they do not.
        Assert.Contains("Click=\"OnDebugMacroClicked\"", menu, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnEditMacroClicked\"", menu, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnDeleteMacroClicked\"", menu, StringComparison.Ordinal);
        Assert.Contains("[Main.RunDebug]", menu, StringComparison.Ordinal);
    }

    /// <summary>
    /// 卡片自己那排按钮也把「运行调试」画出来了，接的是与右键菜单同一段代码：两处进的必须是同一扇窗、
    /// 同一个宏，所以它们点名的是同一个处理函数。
    /// </summary>
    [Fact]
    public void The_card_has_a_run_button_that_goes_where_the_menu_goes()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "MainWindow.axaml"));

        var buttons = markup[markup.IndexOf("<StackPanel Grid.Column=\"1\" Orientation=\"Horizontal\"",
            StringComparison.Ordinal)..];
        buttons = buttons[..buttons.IndexOf("</StackPanel>", StringComparison.Ordinal)];

        Assert.Contains("Click=\"OnDebugMacroClicked\"", buttons, StringComparison.Ordinal);
        Assert.Contains("[Main.RunDebug]", buttons, StringComparison.Ordinal);
    }

    [Fact]
    public void A_menu_opened_on_a_card_answers_with_that_card()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var item = new MenuItem { Header = "debug" };
            var card = new Border
            {
                DataContext = macro,
                ContextMenu = new ContextMenu { ItemsSource = new[] { item } },
            };

            var window = new Window { Content = card };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // A click on one of the items is answered with the item's own data context, which is
            // what says which macro the menu belongs to.
            card.ContextMenu!.Open(card);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(macro, item.DataContext);

            card.ContextMenu.Close();
            window.Close();
        });
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
