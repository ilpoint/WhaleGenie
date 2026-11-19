using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

// What a Fail step is asked to report: the engine's own key, or one the macro wrote for itself.
public sealed partial class MacroRunner
{
    /// <summary>The key a Fail step reports, either the engine's default or the macro's own.</summary>
    private string FailureKeyOf(ExecutableStep step)
    {
        var key = Args(step).Text("failureKey").Trim();
        if (key.Length == 0)
        {
            return RunKeys.StepFailedByMacro;
        }

        if (!UserFailureKeys.IsValid(key))
        {
            throw new StepFailure(RunKeys.BadUserFailureKey, key);
        }

        return key;
    }
}
