using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The step list shows the macro the way it is written: a block opens its steps underneath,
/// indented, and closes them with a line of its own. Reading is the point of it — a macro whose
/// shape is hidden is a macro nobody can check — so most of these cases read the rows back, and
/// the rest make sure editing works at any depth.
/// </summary>
public partial class StepTreeTests
{
    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static StepParameter Body(string name, params MacroStep[] steps)
        => new() { Name = name, Kind = ActionParameterKind.Steps, Steps = [.. steps] };

    private static StepParameter Text(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Text, Written = Formula.FromLine(value) };
}
