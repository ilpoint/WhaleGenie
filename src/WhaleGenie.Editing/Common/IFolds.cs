namespace WhaleGenie.ViewModels;

/// <summary>
/// What a row says about whether it is folded by a box of its own: only a node that holds other
/// nodes inside it is. A row that names what a block runs is part of that block, not a block itself,
/// and a box on it would claim a fold that does not exist.
/// </summary>
public interface IFolds
{
    /// <summary>True on a node that can be opened and closed on its own.</summary>
    bool CanFold { get; }
}
