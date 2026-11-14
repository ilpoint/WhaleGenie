using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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

// The variables panel: the values a run holds, where they come from and what they are.
/// <summary>
/// One variable the macro uses, as the watch list shows it: its name, where it lives, what it holds
/// and what it holds right now. It is listed from the moment the macro is loaded, so the list says
/// what the run works with rather than only what it has reached.
/// </summary>
public sealed record WatchedVariable(string Name, string Scope, ValueKind? Type, string Value)
{
    /// <summary>How it is named on the panel: the Chinese name beside the code name where it has one.</summary>
    public string Display => VariableCatalog.Display(Name);

    /// <summary>
    /// What it holds, as one word of the interface language. A variable nothing has written yet has
    /// no type to show, and a dash says that rather than leaving the column looking broken.
    /// </summary>
    public string LocalType => Type is { } kind
        ? Strings.Get($"Variable.Type.{VariableTypes.Name(kind)}", VariableTypes.Name(kind))
        : "—";
}

/// <summary>The three pages the variables half is divided into.</summary>
public enum VariableTab
{
    /// <summary>Everything the macros in the list can use right now.</summary>
    Current,

    /// <summary>Only the variables a macro created for itself.</summary>
    Local,

    /// <summary>Only the shared variables, which are edited here.</summary>
    Global,

    /// <summary>Only the variables WhaleGenie provides.</summary>
    System,
}

/// <summary>The local variables one macro created, grouped under the macro's name.</summary>
public sealed class MacroVariableGroup
{
    public required string MacroName { get; init; }

    public required IReadOnlyList<VariableDefinition> Variables { get; init; }
}

/// <summary>A global declaration and the value this run would read, without exposing an editor.</summary>
public sealed class RunGlobalVariable
{
    public required string Name { get; init; }
    public required ValueKind Type { get; init; }
    public required string DefaultValue { get; init; }
    public required string Description { get; init; }
    public required string CurrentValue { get; init; }
    public required bool PersistValue { get; init; }

    public string Display => VariableCatalog.Display(Name);
    public string LocalType => Strings.Get($"Variable.Type.{VariableTypes.Name(Type)}",
        VariableTypes.Name(Type));
    public string PersistenceText => PersistValue ? Strings.Get("VarWall.Persistent") : string.Empty;
}
