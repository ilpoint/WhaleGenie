using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Bridge;

/// <summary>The native pickers the settings page still opens: a menu icon and a desktop window.</summary>
public interface ISettingsPickerHost
{
    Task<MenuIconReference?> PickMenuIconAsync();

    Task<WindowInfo?> PickWindowAsync();
}
