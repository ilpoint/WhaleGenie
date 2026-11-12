using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Models;

/// <summary>
/// One value a macro asks whoever calls it to fill in. A macro that expects a count and a name says
/// so here, and every call to it is then a form of exactly those fields rather than a list of
/// <c>NAME=value</c> lines nobody checks.
/// </summary>
public sealed class MacroParameter
{
    /// <summary>The stable identity the calls and the body bind to; empty means the name alone.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The name the called macro reads this value by.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What is used when the call leaves this value out.</summary>
    public string Default { get; set; } = string.Empty;

    /// <summary>True when a call has to fill this value in.</summary>
    public bool Required { get; set; } = true;

    /// <summary>What the value is for, shown under the field at the call site.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>What kind of value the call is asked for, so a mismatch can be seen before the run.</summary>
    public ValueKind Type { get; set; } = ValueKind.Text;

    /// <summary>
    /// What a record of this kind holds. Empty for every other kind, and empty for a record whose
    /// author has not said — a record without it is a bag of names nobody can check, which is why it
    /// is worth writing down when the macro is written rather than found out at the run.
    /// </summary>
    public List<MacroField> Fields { get; set; } = [];
}

/// <summary>One field of a record a macro declares: the name it is read by and what it holds.</summary>
public sealed class MacroField
{
    /// <summary>The name `$record.name` reads.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What kind of value the field holds.</summary>
    public ValueKind Type { get; set; } = ValueKind.Text;
}

/// <summary>
/// One name a macro offers to hand back to whoever called it. A call may ask for any of these; a
/// name the macro does not offer is a mistake at the call site rather than a value that quietly
/// arrives empty, which is what a free list of names could not tell apart.
/// </summary>
public sealed class MacroResult
{
    /// <summary>The stable identity the body and the call site bind to; empty means the name alone.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The name the value goes by inside the called macro.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What the value is, shown beside the box at the call site.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>What kind of value it is, so the variable pickers can say what a name is good for.</summary>
    public ValueKind Type { get; set; } = ValueKind.Text;

    /// <summary>What a record result holds, field by field; empty for every other kind.</summary>
    public List<MacroField> Fields { get; set; } = [];
}

/// <summary>
/// One value a macro keeps for itself. It is declared here rather than left to whatever a step
/// happens to write, so a macro that counts up can say where the count starts and be read back
/// that way. Every run writes the initial value into the macro's own table before the first step,
/// which is why it is an initial value and not a default: it does not wait for the name to be
/// missing, it puts the name back where it started.
/// </summary>
public sealed class MacroLocalVariable
{
    /// <summary>The stable identity the body binds to; empty means the name alone.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The name the macro reads it by.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What kind of value it holds, which is what decides how the initial value is read.</summary>
    public ValueKind Type { get; set; } = ValueKind.Text;

    /// <summary>
    /// What it starts every run at: a written value or an expression, read the way any other value
    /// field is. Left empty it starts empty, not at the kind's empty value written into the file.
    /// </summary>
    public string Initial { get; set; } = string.Empty;

    /// <summary>What it is for, shown on the variable wall and under the field that declares it.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// What one macro in the project asks of a call to it, in the shape the call site and the
/// design-time check work with.
/// </summary>
public sealed record MacroCallable(
    string Name, IReadOnlyList<MacroParameter> Parameters, IReadOnlyList<MacroResult> Results,
    IReadOnlyList<MacroStep>? Steps = null, IReadOnlyList<MacroLocalVariable>? Locals = null)
{
    /// <summary>The steps the macro runs, for the checks that follow a call into it.</summary>
    public IReadOnlyList<MacroStep> OwnSteps => Steps ?? [];

    /// <summary>The values the macro declares for itself, which its own steps read as locals.</summary>
    public IReadOnlyList<MacroLocalVariable> OwnLocals => Locals ?? [];

    /// <summary>
    /// The same, in the shape the engine calls macros by. Empty rows are kept: the engine has to
    /// see them to refuse a call with <c>Run.BadDeclaration</c>, and the editor has to see them to
    /// show what is wrong with the declaration.
    /// </summary>
    public MacroSignature Signature => new(
        [.. Parameters.Select(parameter => new MacroArgument(parameter.Name.Trim(),
            parameter.Default, parameter.Required, parameter.Type, parameter.Id))],
        [.. Results.Select(result => new MacroReturn(result.Name.Trim(), result.Type, result.Id))],
        [.. OwnLocals.Select(local => new MacroLocal(local.Name.Trim(), local.Initial, local.Type,
            local.Id))]);

    /// <summary>
    /// Every declaration name that cannot be used: one left empty, one the macro language cannot
    /// read, one that collides with the supplied-empty marker, one repeated, one the run keeps
    /// for itself, or a required argument that also has a default. The editor shows these even
    /// when no call reaches the macro.
    /// </summary>
    public IReadOnlyList<(MacroSignatureFault Fault, string Name)> Problems()
        => Signature.Problems();
    /// <summary>The declared parameter of this name, or null when the macro declares none.</summary>
    public MacroParameter? Parameter(string name)
        => Parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the macro offers to hand back a value of this name.</summary>
    public bool Offers(string name)
        => Results.Any(result =>
            string.Equals(result.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
