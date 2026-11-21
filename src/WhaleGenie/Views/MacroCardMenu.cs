using System.Linq;
using Avalonia.Controls;

namespace WhaleGenie.Views;

/// <summary>
/// A card's right-click menu carries no data of its own: its items are written out in XAML and used
/// to inherit the card's macro from the logical tree. Under a theme that presents the menu inside a
/// popup, that tree stops at the menu and every item arrives with a null DataContext. Stamping the
/// value on each item keeps their one rule — the sender's DataContext is the macro — true.
/// </summary>
internal static class MacroCardMenu
{
    internal static void Stamp(ContextMenu menu, object? data)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.DataContext = data;
        }
    }
}
