using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Runtime;
using WhaleGenie.Views;

namespace WhaleGenie.Web;

/// <summary>The page's edit/debug/icon commands, carried out by the shell window and its pickers.</summary>
internal sealed class MenuSurfaceActions(Func<WebShellWindow?> owner) : IMenuSurfaceActions
{
    public void OpenEditor(string macroName) => owner()?.OpenEditor(macroName);

    public void OpenRun(string macroName) => owner()?.OpenRun(macroName);

    public async Task<(string Kind, string Value)?> PickIconAsync()
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var icon = await MenuIconPickerWindow.ShowAsync(window);
        return icon is null
            ? null
            : (icon.Kind.ToString().ToLowerInvariant(), icon.Value);
    }
}
