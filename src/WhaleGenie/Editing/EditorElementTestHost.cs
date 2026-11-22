using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Editing;
using WhaleGenie.Views;

namespace WhaleGenie.Editing;

/// <summary>The native element flash behind the editor bridge's test port.</summary>
internal sealed class EditorElementTestHost(Func<Window?> owner) : IEditorElementTestHost
{
    public async Task<bool> FlashAsync(IDeviceLayer devices, string selector, WindowRule rule)
    {
        var found = ElementFlash.Locate(devices.Ui, selector, rule);
        if (found is null)
        {
            return false;
        }

        using var aside = owner() is { } window ? AppWindows.StepAside(window) : null;
        if (aside is not null)
        {
            await AppWindows.SettleAsync(devices.Screen);
        }

        var frame = ElementFlash.Show(found);
        if (frame is not null)
        {
            await frame.Finished;
        }

        return true;
    }
}
