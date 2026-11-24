using System.Threading.Tasks;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Bridge;

/// <summary>The desktop overlay that takes one window by pointing at it.</summary>
public interface IWindowPickHost
{
    Task<WindowInfo?> PickWindowAsync();
}
