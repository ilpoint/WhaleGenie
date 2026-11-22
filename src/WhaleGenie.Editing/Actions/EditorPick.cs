using System.Collections.Generic;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Editing;

/// <summary>Which desktop picker an action field asks for.</summary>
internal enum EditorPickKind
{
    Point,
    Region,
    Drag,
    Capture,
    Color,
    WindowBounds,
    Window,
    WindowTitle,
    Element,
    Browser,
    Path,
}

/// <summary>
/// What a picker is asked for: the field's own basis (a window rule, a rectangle), where a picture
/// captured for it belongs, and the page a browser element is picked on. It is plain data so the
/// session can build it without knowing a window, and a check can hand a stand-in to the host.
/// </summary>
internal sealed record EditorPickRequest(
    EditorPickKind Kind,
    string Name,
    int Index,
    string Mode,
    int X,
    int Y,
    int Width,
    int Height,
    long Handle,
    string WindowRuleJson,
    string WindowMatch,
    string AssetFolder,
    string Url,
    string Profile,
    string PathIntent,
    string PathFilter,
    string PathLabel,
    bool Multiple,
    bool WantsWords,
    IReadOnlyList<string> Entries);

/// <summary>What a picker came back with, whether it was taken or dismissed.</summary>
internal sealed record EditorPickResult(
    bool Chosen,
    int X,
    int Y,
    int Width,
    int Height,
    int EndX,
    int EndY,
    string Colour,
    string Path,
    string Selector,
    string WindowTitle,
    string WindowRuleJson,
    IReadOnlyList<PickCandidate> Candidates,
    IReadOnlyList<string> Entries,
    string Notice,
    bool NoticeIsWarning)
{
    public static EditorPickResult Cancelled { get; } = new(false, 0, 0, 0, 0, 0, 0,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, [], [], string.Empty, false);
}

/// <summary>The one door the bridge uses to put a desktop overlay up, so the session stays headless.</summary>
internal interface IEditorPickerHost
{
    Task<EditorPickResult> PickAsync(EditorPickRequest request);
}
