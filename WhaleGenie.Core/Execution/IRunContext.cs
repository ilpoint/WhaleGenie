using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
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

    /// <summary>The variables the run works with, for the step that writes them all down.</summary>
    VariableStore Variables { get; }

    /// <summary>A path a step named, with any name in it already worked out.</summary>
    string PathOf(ExecutableStep step, string parameter = "path");

    /// <summary>One step's fields, read the way the catalogue says they are written.</summary>
    StepArguments Args(ExecutableStep step);

    /// <summary>A yes/no field, with the answer a blank cell means.</summary>
    bool Flag(ExecutableStep step, string name, bool fallback);

    /// <summary>
    /// Whether one value answers another the way a filter asked: the same comparison a switch uses,
    /// so "keep the rows matching this" and "run this branch" mean the same thing by it.
    /// </summary>
    bool CaseMatches(ExecutableStep step, Value value, string wanted, string mode);

    /// <summary>Whether the step asked for one of its results, by giving it a name.</summary>
    bool Wants(ExecutableStep step, string key);

    /// <summary>The window rule a step wrote, resolved the same way every family resolves it.</summary>
    WindowRule WindowRuleOf(ExecutableStep step);

    /// <summary>
    /// The window a step is about: the one its rule finds, or the one in front when it named none.
    /// </summary>
    WindowInfo? Lookup(ExecutableStep step);

    /// <summary>The same, as a failure naming the window that could not be found.</summary>
    WindowInfo Locate(ExecutableStep step);

    /// <summary>The rectangle a step's record field names, or null when it named none.</summary>
    (int Left, int Top, int Width, int Height)? RectOf(ExecutableStep step, string name);

    /// <summary>Which of the hits a step means, counted from one and held to the search limit.</summary>
    int Index(ExecutableStep step, string name, int fallback);

    /// <summary>The mouse button a step names, or the left one when it names none.</summary>
    string Button(ExecutableStep step);

    /// <summary>The keys of the combination the step's own key field names.</summary>
    IReadOnlyList<string> Keys(ExecutableStep step);

    /// <summary>A pause the way the run's speed setting scales it.</summary>
    int Pace(int milliseconds);

    /// <summary>One number of a row that may be left empty, where empty means the run's value.</summary>
    int Number(IReadOnlyDictionary<string, ExpressionNode> row, string column, int fallback);

    /// <summary>Remembers that a controller was driven, so the run lets go of it at the end.</summary>
    void MarkGamepadUsed();

    /// <summary>One column of one row of a step's table, worked out where it stands.</summary>
    string Text(IReadOnlyDictionary<string, ExpressionNode> row, string column);

    /// <summary>One column of one row read as the word it names rather than as a value.</summary>
    string Word(IReadOnlyDictionary<string, ExpressionNode> row, string column);

    /// <summary>Hands one result over under the name the step gave it.</summary>
    string Give(ExecutableStep step, string key, Value value);

    /// <summary>Hands a result over and writes the line that says a variable was set.</summary>
    void Store(ExecutableStep step, string key, Value value, int depth);

    /// <summary>Hands over what a search found: where to aim, and what was read with it.</summary>
    void GiveMatch(ExecutableStep step, ScreenPoint corner, ScreenSize size, double score,
        string text = "", string label = "", int classId = -1);

    /// <summary>Empties the results a search would have handed over after finding nothing.</summary>
    void GiveNothing(ExecutableStep step);

    /// <summary>Hands over every hit there was, for the steps that asked for the list.</summary>
    void GiveEvery(ExecutableStep step, IEnumerable<Value> hits);

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
