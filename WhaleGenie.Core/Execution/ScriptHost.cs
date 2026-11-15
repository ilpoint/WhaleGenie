using System.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The macro as a script sees it: the values it may read and write, the steps it may run, and
/// somewhere to say something. It is the run the script is happening inside, so what a script
/// reads is what the macro holds at that moment and what it writes is what the next step sees.
/// </summary>
internal sealed class ScriptHost(IRunContext run, string type, int depth,
    CancellationToken token) : IScriptHost
{
    public Value? Read(string name) => run.Variables.TryGet(name, out var value) ? value : null;

    /// <summary>
    /// A script writes the macro's own values, never the program's. A write to a built-in name is
    /// dropped rather than failing the script: the script asked for a value it cannot have, and
    /// what it reads next is still the clock or the clipboard it wrote over.
    /// </summary>
    public void Write(string name, Value value)
    {
        if (VariableName.IsSystem(name))
        {
            return;
        }

        run.Variables.Set(name, value);
    }

    public bool RunStep(string id) => run.RunStepById(id, type, depth, token);

    public void Print(string text) => run.Log(LogLevel.Info, depth, type, LogKeys.ScriptSaid, text);
}
