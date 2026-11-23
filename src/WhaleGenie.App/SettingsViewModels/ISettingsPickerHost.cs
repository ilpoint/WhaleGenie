using System.Threading.Tasks;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Bridge;

/// <summary>The native picker the settings page still opens: a desktop window.</summary>
public interface ISettingsPickerHost
{
    Task<WindowInfo?> PickWindowAsync();
}
