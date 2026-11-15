using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What one action family may do besides its own work: read a step's fields, hand a result over,
/// write a log line, reach the devices, and take part in what the run is watching. It is the
/// runner seen from a family's side, so the family can be read — and tried — without the engine.
/// </summary>
internal interface IRunContext
{
    /// <summary>The machine the steps act on.</summary>
    IDeviceLayer Devices { get; }

    /// <summary>Pictures the run has taken, under the name the macro saved them by.</summary>
    IDictionary<string, ImageFrame> Images { get; }

    /// <summary>One step's fields, read the way the catalogue says they are written.</summary>
    StepArguments Args(ExecutableStep step);

    /// <summary>A yes/no field, with the answer a blank cell means.</summary>
    bool Flag(ExecutableStep step, string name, bool fallback);

    /// <summary>Whether the step asked for one of its results, by giving it a name.</summary>
    bool Wants(ExecutableStep step, string key);

    /// <summary>The window rule a step wrote, resolved the same way every family resolves it.</summary>
    WindowRule WindowRuleOf(ExecutableStep step);

    /// <summary>Hands one result over under the name the step gave it.</summary>
    string Give(ExecutableStep step, string key, Value value);

    /// <summary>Hands a result over and writes the line that says a variable was set.</summary>
    void Store(ExecutableStep step, string key, Value value, int depth);

    /// <summary>Adds a line to the run's log.</summary>
    void Log(LogLevel level, int depth, string type, string key, params object[] arguments);

    /// <summary>Hands the picture a step looked at to whoever is watching, if anybody is.</summary>
    void Saw(ExecutableStep step, LookKind kind, ImageFrame frame);

    /// <summary>Runs a body with the step's picture cleared, keeping whatever it saw.</summary>
    void Watching(Action body);

    /// <summary>The input device this step's mode routes to.</summary>
    IInputDevice Input(ExecutableStep step);

    /// <summary>The keys of the combination a shortcut field names.</summary>
    IReadOnlyList<string> Shortcut(ExecutableStep step);

    /// <summary>The one reference picture a step names, read from the run or from disk.</summary>
    ImageFrame Reference(ExecutableStep step);
}
