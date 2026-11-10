using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The step lists this step carries, and making sure the ones its action declares exist.
public partial class MacroStep : INotifyPropertyChanged
{
    /// <summary>The parameters of this step that hold steps, in the order they were written.</summary>
    public IEnumerable<StepParameter> StepLists
        => Parameters.Where(parameter => parameter.Kind == ActionParameterKind.Steps);

    /// <summary>
    /// Restores the empty child lists the action catalogue declares but the file or form omitted.
    /// An empty block still has a place for its first child; that place is part of the action's
    /// shape, not data that only exists after a child has been added.
    /// </summary>
    public void EnsureDeclaredLists()
    {
        if (Definition is { } definition)
        {
            foreach (var declared in definition.Parameters
                         .Where(parameter => parameter.Kind == ActionParameterKind.Steps))
            {
                if (!Parameters.Any(parameter => parameter.Name == declared.Name))
                {
                    Parameters.Add(new StepParameter
                    {
                        Name = declared.Name,
                        Kind = ActionParameterKind.Steps,
                    });
                }
            }
        }

        foreach (var child in Parameters.SelectMany(parameter => parameter.Steps))
        {
            child.EnsureDeclaredLists();
        }
    }

    /// <summary>How many steps are written inside this one, all of its lists together.</summary>
    public int ChildCount => StepLists.Sum(list => list.Steps.Count);

    /// <summary>True when there are steps inside this one, so it can be folded open and shut.</summary>
    public bool HasChildren => ChildCount > 0;

    private StepMeta _meta = StepMeta.Empty;
}
