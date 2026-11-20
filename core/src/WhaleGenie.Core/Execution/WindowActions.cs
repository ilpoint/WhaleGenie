using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The window actions: whether one is open, waiting for it, bringing it forward or putting it away,
/// moving it, closing it, and reading what it is and where it sits. Which window a step means is
/// read by the runner's one window rule, so a listing and an action name a window the same way.
/// </summary>
internal static class WindowActions
{
    /// <summary>
    /// The window actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["window.exists"] = (run, step, depth, _) =>
            {
                WindowExists(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["window.waitFor"] = async (run, step, depth, token) =>
            {
                await WaitForWindow(run, step, depth, token);
                return Signal.Normal;
            },

            ["window.activate"] = (run, step, depth, _) =>
            {
                Act(run, step, depth, LogKeys.ActivatedWindow,
                    window => run.Devices.Windows.Activate(window.Handle));
                return Task.FromResult(Signal.Normal);
            },

            ["window.minimize"] = (run, step, depth, _) =>
            {
                Act(run, step, depth, LogKeys.MinimizedWindow,
                    window => run.Devices.Windows.Minimize(window.Handle));
                return Task.FromResult(Signal.Normal);
            },

            ["window.maximize"] = (run, step, depth, _) =>
            {
                Act(run, step, depth, LogKeys.MaximizedWindow,
                    window => run.Devices.Windows.Maximize(window.Handle));
                return Task.FromResult(Signal.Normal);
            },

            ["window.restore"] = (run, step, depth, _) =>
            {
                Act(run, step, depth, LogKeys.RestoredWindow,
                    window => run.Devices.Windows.Restore(window.Handle));
                return Task.FromResult(Signal.Normal);
            },

            ["window.move"] = (run, step, depth, _) =>
            {
                if (MoveTargetWritten(step))
                {
                    Act(run, step, depth, LogKeys.MovedWindow, window =>
                    {
                        var (x, y, width, height) = MoveTarget(run, step, window);
                        return run.Devices.Windows.Move(window.Handle, x, y, width, height);
                    });
                }

                return Task.FromResult(Signal.Normal);
            },

            ["window.close"] = (run, step, depth, _) =>
            {
                Act(run, step, depth, LogKeys.ClosedWindow,
                    window => run.Devices.Windows.Close(window.Handle));
                return Task.FromResult(Signal.Normal);
            },

            ["window.list"] = (run, step, depth, _) =>
            {
                ListWindows(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["window.info"] = (run, step, depth, _) =>
            {
                WindowBox(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    /// <summary>Leaves true or false behind, depending on whether the window is open.</summary>
    private static void WindowExists(IRunContext run, ExecutableStep step, int depth)
    {
        var found = run.Lookup(step) is not null;
        run.Store(step, "found", Value.FromBool(found), depth);
    }

    /// <summary>Waits until a window the step names appears.</summary>
    private static async Task WaitForWindow(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var timeout = step.Meta.TimeoutMs;

        var window = await Pacing.WaitForValueAsync(() => run.Lookup(step), timeout, 100, token);
        if (window is null)
        {
            throw new StepFailure(RunKeys.WindowTimeout, WindowText(run, step));
        }

        run.Give(step, "title", Value.FromText(window.Title));

        if (run.Wants(step, "window"))
        {
            // The whole record costs a look at the program, the class and the client area; a step
            // that only wants the title should not pay for them.
            var client = run.Devices.Windows.ClientOrigin(window.Handle);
            var inner = run.Devices.Windows.ClientSize(window.Handle);
            run.Give(step, "window", WindowRecord(window,
                run.Devices.Windows.ProcessOf(window.Handle),
                run.Devices.Windows.ClassOf(window.Handle),
                $"{client.X},{client.Y},{inner.Width},{inner.Height}",
                run.WindowRuleOf(step)));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.WindowAppeared, window.Title);
    }

    /// <summary>
    /// Collects the open windows into a list, narrowed down to the ones a step names, each window as
    /// a record of its own: its title, where it sits, what the system knows it by, and — when the
    /// step asks for them — the program and the class behind it. Two windows with the same title are
    /// two windows, so each gets a record of its own. A text nobody wrote means every window here,
    /// where it means the window in front to a step that acts on one of them.
    /// </summary>
    private static void ListWindows(IRunContext run, ExecutableStep step, int depth)
    {
        var windows = Matching(run, step);
        var rule = run.WindowRuleOf(step);

        // The program and the class of a window are read only when the step keeps the list and asks
        // for them, because looking either of them up costs a look at the window. Every entry is
        // then the whole of one window, the same record window.info hands over for a single one; a
        // step that leaves them out gets two empty fields rather than missing ones.
        if (run.Wants(step, "windows"))
        {
            var details = run.Flag(step, "details", false);
            run.Store(step, "windows", Value.FromList(windows.Select(window => WindowRecord(window,
                details ? run.Devices.Windows.ProcessOf(window.Handle) : string.Empty,
                details ? run.Devices.Windows.ClassOf(window.Handle) : string.Empty,
                string.Empty, rule))), depth);
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ListedWindows, windows.Count);
    }

    /// <summary>
    /// The windows a listing hands over, in the order the system gives them: the ones the step's
    /// text picks out, or every window there is when it names none.
    /// </summary>
    private static IReadOnlyList<WindowInfo> Matching(IRunContext run, ExecutableStep step)
    {
        var rule = run.WindowRuleOf(step);
        return run.Devices.Windows.Filter(rule.MatchValue, rule.Reading.Match,
            rule.Reading.Compare, everyWhenEmpty: true, options: rule.Options);
    }

    /// <summary>Does something to the window a step names, or fails the step when it is not open.</summary>
    private static void Act(IRunContext run, ExecutableStep step, int depth, string message,
        Func<WindowInfo, bool> action)
    {
        var window = run.Locate(step);

        if (!action(window))
        {
            throw new StepFailure(RunKeys.WindowFailed, window.Title);
        }

        run.Log(LogLevel.Info, depth, step.Type, message, window.Title);
    }

    /// <summary>
    /// Where <c>window.move</c> sends a window: the rectangle a record says, or the sides the step
    /// wrote, with the ones it left empty staying where they are, which is what its empty cells
    /// mean. A window is never told to be zero pixels wide.
    /// </summary>
    private static (int X, int Y, int Width, int Height) MoveTarget(IRunContext run,
        ExecutableStep step, WindowInfo window)
    {
        if (run.RectOf(step, "rect") is { } rect)
        {
            return (rect.Left, rect.Top, rect.Width, rect.Height);
        }

        int Side(string name, int current, int least)
            => run.Args(step).Given(name) ? Math.Max(least, run.Args(step).Number(name)) : current;

        return (Side("x", window.Location.X, int.MinValue),
            Side("y", window.Location.Y, int.MinValue),
            Side(FieldNames.Width, window.Size.Width, 1),
            Side(FieldNames.Height, window.Size.Height, 1));
    }

    /// <summary>Whether the step says enough about a window's rectangle to move it at all.</summary>
    private static bool MoveTargetWritten(ExecutableStep step)
        => step.Text("rect").Trim().Length > 0
           || new[] { "x", "y", FieldNames.Width, FieldNames.Height }
               .Any(name => step.Text(name).Trim().Length > 0);

    /// <summary>
    /// Where a window sits and what it is, handed over as one record — the same shape a listing
    /// gives each window it finds, so the rectangle goes into a search region as it stands and one
    /// part of it is read by name.
    /// </summary>
    private static void WindowBox(IRunContext run, ExecutableStep step, int depth)
    {
        var window = run.Locate(step);
        var box = $"{window.Location.X},{window.Location.Y},{window.Size.Width},{window.Size.Height}";

        if (run.Wants(step, "window"))
        {
            // The program, the class and the part inside the border cost a look at the window each,
            // so they are worked out only when the record is kept at all.
            var client = run.Devices.Windows.ClientOrigin(window.Handle);
            var inner = run.Devices.Windows.ClientSize(window.Handle);
            run.Give(step, "window", WindowRecord(window,
                run.Devices.Windows.ProcessOf(window.Handle),
                run.Devices.Windows.ClassOf(window.Handle),
                $"{client.X},{client.Y},{inner.Width},{inner.Height}",
                run.WindowRuleOf(step)));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.WindowBox, window.Title, box);
    }

    /// <summary>
    /// One window as a record: what it is — the title, the program and the class behind it, what the
    /// system knows it by, whether it is shrunk or spread — and where it sits, both as the four
    /// numbers of its rectangle, as the middle of that rectangle under x and y, and as the text a
    /// search region is spelled with. The part inside the border is always there as a field; a
    /// listing leaves it empty, because reading it costs a look at every window found.
    /// </summary>
    private static Value WindowRecord(WindowInfo window, string program, string kind,
        string clientBox, WindowRule rule)
    {
        var left = window.Location.X;
        var top = window.Location.Y;
        var width = window.Size.Width;
        var height = window.Size.Height;
        var middle = ScreenMath.Middle(window.Location, window.Size);

        return Value.FromRecord(
        [
            (FieldNames.Title, Value.FromText(window.Title)),
            (FieldNames.Process, Value.FromText(program)),
            (FieldNames.Class, Value.FromText(kind)),
            (FieldNames.Handle, Value.FromNumber(window.Handle)),
            (FieldNames.Minimized, Value.FromBool(window.Minimized)),
            (FieldNames.Maximized, Value.FromBool(window.Maximized)),
            (FieldNames.X, Value.FromNumber(middle.X)),
            (FieldNames.Y, Value.FromNumber(middle.Y)),
            (FieldNames.Left, Value.FromNumber(left)),
            (FieldNames.Top, Value.FromNumber(top)),
            (FieldNames.Width, Value.FromNumber(width)),
            (FieldNames.Height, Value.FromNumber(height)),
            (FieldNames.Box, Value.FromText($"{left},{top},{width},{height}")),
            (FieldNames.ClientBox, Value.FromText(clientBox)),
            (FieldNames.How, Value.FromText(rule.How)),
            (FieldNames.CaseSensitive, Value.FromBool(rule.CaseSensitive)),
            (FieldNames.Trim, Value.FromBool(rule.Trim)),
            (FieldNames.Multiline, Value.FromBool(rule.Multiline)),
        ]);
    }

    /// <summary>What a step wrote into its window field, for the failure that names it.</summary>
    private static string WindowText(IRunContext run, ExecutableStep step)
        => run.WindowRuleOf(step).MatchValue.Trim();
}
