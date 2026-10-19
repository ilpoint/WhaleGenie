using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Execution;

/// <summary>
/// The one device layer this program uses. Every device in a layer is made the first time it is
/// asked for, and the ones that cost something — the text recognition models, the graphics device
/// behind a window capture — keep that cost until the layer is let go. A layer per window would pay
/// it again for each window and throw it away when that window closed, so the program holds one and
/// hands it to the editor, to the runs a trigger starts, and to the trials in a dialog. The windows
/// do not release what they were handed; the program releases it as it exits.
/// </summary>
public static class SharedDevices
{
    private static WindowsDeviceLayer? _layer;

    /// <summary>The layer every window, trial and trigger run in this program shares.</summary>
    public static IDeviceLayer Current => Layer;

    /// <summary>
    /// Loads the text recognition models, which is what 「预加载文字识别」 asks for: the first text
    /// step of a run otherwise waits for them. Called on the thread that does the loading rather
    /// than on the interface thread, because it takes a moment.
    /// </summary>
    public static void WarmUpOcr()
    {
        if (Layer.Ocr is PaddleOcrDevice reader)
        {
            reader.WarmUp();
        }
    }

    /// <summary>
    /// Lets go of everything the layer opened, on the way out of the program. The models and the
    /// capture sessions are the reason this exists: leaving them behind would hold the graphics
    /// device and the loaded models after the last window is gone.
    /// </summary>
    public static void Release()
    {
        // The layer is forgotten even when one of its devices cannot be let go of: the next call
        // must not hand the same half-closed layer out again, and the way out still has the virtual
        // devices to take off the machine.
        var layer = _layer;
        _layer = null;
        layer?.Dispose();
    }

    /// <summary>The layer itself, made the first time anything asks for it.</summary>
    private static WindowsDeviceLayer Layer => _layer ??= new WindowsDeviceLayer();
}
