using System.Threading.Tasks;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Editing;

/// <summary>The native flash that shows where a test found an element; false means it was not found.</summary>
public interface IEditorElementTestHost
{
    Task<bool> FlashAsync(IDeviceLayer devices, string selector, WindowRule rule);
}
