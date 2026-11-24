using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Views;

namespace WhaleGenie.Views.Pickers;

/// <summary>The native window overlay behind the bridge's window pick.</summary>
internal sealed class WindowPickHost(Func<Window?> owner, IDeviceLayer devices) : IWindowPickHost
{
    public async Task<WindowInfo?> PickWindowAsync()
    {
        if (owner() is not { } window)
        {
            return null;
        }

        using var aside = AppWindows.StepAside(window);
        await AppWindows.SettleAsync();
        return await WindowPickWindow.PickAsync(devices.Windows, devices.Screen, devices.Hook);
    }
}
