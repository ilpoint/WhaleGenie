using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

// Trying a look or a question without carrying anything out: what the dialog shows for a step,
// and what a breakpoint asks before the run goes on.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Whether this kind of step is one that looks at the screen, and so is one whose looking can
    /// be tried out on its own. It is what the dialog offers "try looking" for, and the only thing
    /// <see cref="LookOnce"/> will look at the screen for.
    /// </summary>
    /// <summary>
    /// Whether a trial can look at the screen for this kind of step. It is read off the catalogue
    /// rather than kept as a list here: the list that used to live here had drifted, and a step the
    /// catalogue says looks at the screen was told it does not.
    /// </summary>
    public static bool CanLook(string stepType)
        => ActionSemanticsCatalog.Find(stepType)?.LooksAtScreen == true;

    /// <summary>
    /// Looks at the screen the way a step would and hands back what it saw, without doing anything
    /// about it: no key is pressed, no mouse button goes down, no variable is written. It answers
    /// "what does this step actually see", where running the step would answer it by clicking on
    /// the user's screen. Null when this kind of step does not look at the screen at all.
    /// <paramref name="watch"/> is told which window is being read as it happens, if anybody wants
    /// to be: it is the same callback a run hands over, so a trial can show what a run shows.
    /// </summary>
    public static LookOutcome LookOnce(ExecutableStep step, IDeviceLayer devices,
        IRunLooks? watch = null)
    {
        if (!CanLook(step.Type))
        {
            return new LookOutcome(null, RunKeys.NotALookingStep);
        }

        var asked = new Asked(watch);
        try
        {
            new MacroRunner(new VariableStore(), devices: devices, looks: asked).SeeOnce(step);
        }
        catch (ExpressionException failure)
        {
            return new LookOutcome(null, RunKeys.BadExpression, failure.Detail);
        }
        catch (RegexTimeoutException failure)
        {
            return new LookOutcome(null, RunKeys.RegexTimeout, failure.Pattern);
        }
        catch (DeviceUnavailableException failure)
        {
            return new LookOutcome(null, RunKeys.NoDevice, failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while looking, the answer is a message rather than
            // an exception in the dialog's face.
            return new LookOutcome(null, RunKeys.DeviceFailed, failure.Message);
        }

        return asked.Seen is { } look
            ? new LookOutcome(look)
            : new LookOutcome(null, RunKeys.NotALookingStep);
    }

    /// <summary>
    /// Answers one condition the way a run would, on the values in play at that moment and without
    /// carrying anything out. This is what a breakpoint's own condition is asked with: the debugger
    /// has the values the run has just written, and none of the run's own machinery. A condition
    /// that cannot be answered at all comes back saying why rather than throwing, so the caller can
    /// tell "no" apart from "could not be asked".
    /// </summary>
    public static ConditionOutcome AskCondition(ExecutableStep condition, VariableStore variables,
        IDeviceLayer devices, IMacroLibrary? macros = null)
    {
        try
        {
            return new ConditionOutcome(new MacroRunner(variables, devices: devices, macros: macros)
                .Check(condition, "condition", CancellationToken.None));
        }
        catch (ExpressionException failure)
        {
            return new ConditionOutcome(false, RunKeys.BadExpression, failure.Detail);
        }
        catch (RegexTimeoutException failure)
        {
            return new ConditionOutcome(false, RunKeys.RegexTimeout, failure.Pattern);
        }
        catch (DeviceUnavailableException failure)
        {
            return new ConditionOutcome(false, RunKeys.NoDevice, failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while a condition is asked, the answer is a message
            // rather than an exception in the debugger's face.
            return new ConditionOutcome(false, RunKeys.DeviceFailed, failure.Message);
        }
    }

    /// <summary>Where a look goes when the caller only wanted the one.</summary>
    private sealed class Asked(IRunLooks? watch) : IRunLooks
    {
        public StepLook? Seen { get; private set; }

        public void Look(StepLook look) => Seen = look;

        /// <summary>Nothing is read from what a step came to; only the one look was wanted.</summary>
        public void Ran(StepRun run)
        {
        }

        public void Reading(WindowInfo? window) => watch?.Reading(window);
    }

}
