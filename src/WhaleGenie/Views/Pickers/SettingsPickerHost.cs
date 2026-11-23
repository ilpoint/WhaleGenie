using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Views;

namespace WhaleGenie.Views.Pickers;

/// <summary>The native menu-icon and window pickers behind the settings bridge's port.</summary>
internal sealed class SettingsPickerHost(Func<Window?> owner, IDeviceLayer devices) : ISettingsPickerHost
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
