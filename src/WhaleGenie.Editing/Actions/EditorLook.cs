using System.Collections.Generic;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

internal enum EditorLookMode
{
    Offset,
    Colours,
}

/// <summary>What a look picker needs: the frozen picture, its origin, and the hit it counts from.</summary>
internal sealed record EditorLookRequest(EditorLookMode Mode, StepLook Look, ImageFrame Area,
    ScreenPoint Origin, ScreenPoint Centre, IReadOnlyList<string> Entries,
    IReadOnlyList<VariableChoice> Variables);

internal sealed record EditorLookResult(bool Chosen, int X, int Y,
    IReadOnlyList<string> Entries)
{
    public static EditorLookResult Cancelled { get; } = new(false, 0, 0, []);
}

internal interface IEditorLookHost
{
    Task<EditorLookResult> PickLookAsync(EditorLookRequest request);
}
