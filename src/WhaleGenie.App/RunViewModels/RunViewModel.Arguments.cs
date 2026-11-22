using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// The arguments a direct run asks for: building the rows, and what a changed one does.
public partial class RunViewModel
{
    /// <summary>
    /// Rebuilds the parameter rows for the macro that is loaded. A window opened on steps alone has
    /// no declaration to ask about; a macro loaded from the list or the editor does.
    /// </summary>
    private void BuildArguments()
    {
        foreach (var row in Arguments)
        {
            row.Changed -= OnArgumentChanged;
        }

        Arguments.Clear();
        if (_callable is not null)
        {
            foreach (var parameter in _callable.Parameters)
            {
                var name = parameter.Name.Trim();
                if (!VariableName.IsName(name))
                {
                    continue;
                }

                var row = new RunArgumentViewModel
                {
                    Name = name,
                    TypeLabel = Strings.Get("ActionType." + parameter.Type,
                        parameter.Type.ToString()),
                    Required = parameter.Required,
                    Default = ExpressionPhrase.Display(parameter.Default),
                    Description = parameter.Description,
                };
                row.Changed += OnArgumentChanged;
                Arguments.Add(row);
            }
        }

        OnPropertyChanged(nameof(HasArguments));
        OnArgumentChanged();
    }

    /// <summary>Raised when an argument row changes: the required ones decide whether a run can start.</summary>
    private void OnArgumentChanged()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStep));
        RunCommand.NotifyCanExecuteChanged();
        StepCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The values a direct run hands to the macro's declaration.</summary>
    private IReadOnlyList<MacroStartArgument> StartArguments()
        => [.. Arguments.Select(row => new MacroStartArgument(row.Name, row.Value.Trim(), row.Given))];
}
