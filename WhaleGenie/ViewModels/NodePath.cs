using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Where one piece stands in a value's tree. The editor used to keep a caret as a count of
/// characters into a line; a value is a shape now, so a place is the walk from the root to the
/// piece instead. Two places that read the same are the same place, which lets a caret, a drop
/// target and a removal all be spoken about in one way.
/// </summary>
public sealed record NodePath(IReadOnlyList<int> Indices)
{
    public static NodePath Root { get; } = new([]);

    public bool IsRoot => Indices.Count == 0;

    public NodePath Append(int index) => new([.. Indices, index]);

    public NodePath? Parent => IsRoot ? null : new([.. Indices.Take(Indices.Count - 1)]);

    public int Leaf => Indices.Count == 0 ? -1 : Indices[^1];
}
