using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Which macros an export is to hold, out of the ones a project has.
/// </summary>
/// <remarks>
/// The tree is a macro and the submacros written inside it. Ticking a macro covers them: they travel
/// inside it, so they cannot be taken or left on their own. Ticking a submacro instead takes that
/// one alone, which is how a single piece of a long macro is handed over. Both are decided here
/// rather than in the window, because this is the rule the file ends up depending on.
/// </remarks>
public sealed class MacroExportSelection
{
    public sealed class Node
    {
        internal Node(MacroItem macro, Node? parent)
        {
            Macro = macro;
            Parent = parent;
        }

        public MacroItem Macro { get; }

        public Node? Parent { get; }

        public List<Node> Children { get; } = [];

        public bool IsSub => Parent is not null;

        /// <summary>Ticked by the person, or ticked because a ticked macro above it covers it.</summary>
        public bool IsChecked { get; internal set; }

        /// <summary>Inside a ticked macro, so this row cannot be changed on its own.</summary>
        public bool IsCovered { get; internal set; }

        /// <summary>Counts as a macro of its own: ticked and not already inside one.</summary>
        public bool TakesPart => IsChecked && !IsCovered;
    }

    private readonly List<Node> _roots = [];

    public MacroExportSelection(IReadOnlyList<MacroItem> macros,
        IEnumerable<MacroItem>? preselected = null)
    {
        var wanted = (preselected ?? [])
            .Select(macro => macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in macros)
        {
            _roots.Add(Add(macro, parent: null, wanted));
        }

        foreach (var root in _roots.Where(root => root.IsChecked))
        {
            Cover(root, taken: true);
        }
    }

    public IReadOnlyList<Node> Roots => _roots;

    /// <summary>Every row, parents before the submacros written inside them.</summary>
    public IEnumerable<Node> All => _roots.SelectMany(Flatten);

    /// <summary>The macros that leave, each with whatever is written inside it.</summary>
    public IReadOnlyList<MacroItem> Chosen
        => [.. All.Where(node => node.TakesPart).Select(node => node.Macro)];

    /// <summary>
    /// Ticks or unticks one row. A macro takes its submacros with it in either direction; a submacro
    /// changes nothing but itself.
    /// </summary>
    public void SetChecked(Node node, bool value)
    {
        node.IsChecked = value;
        if (node.Parent is null)
        {
            Cover(node, value);
        }
    }

    /// <summary>Ticks or unticks every macro. Submacros follow from their parents.</summary>
    public void SetAll(bool value)
    {
        foreach (var root in _roots)
        {
            root.IsChecked = value;
        }

        foreach (var root in _roots)
        {
            Cover(root, value);
        }
    }

    private static Node Add(MacroItem macro, Node? parent, IReadOnlySet<string> wanted)
    {
        var node = new Node(macro, parent) { IsChecked = wanted.Contains(macro.Name.Trim()) };
        parent?.Children.Add(node);
        foreach (var sub in macro.SubMacros)
        {
            Add(sub, node, wanted);
        }

        return node;
    }

    private static void Cover(Node node, bool taken)
    {
        foreach (var child in node.Children)
        {
            child.IsChecked = taken;
            child.IsCovered = taken;
            Cover(child, taken);
        }
    }

    private static IEnumerable<Node> Flatten(Node node)
    {
        yield return node;
        foreach (var nested in node.Children.SelectMany(Flatten))
        {
            yield return nested;
        }
    }
}
