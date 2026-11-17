using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// What a press and a release mean for each way of repeating. A key and a watched colour answer to
/// the same four words, so they answer them here rather than each keeping a copy.
/// </summary>
internal static class TriggerLoop
{
    public static void Pressed(TriggerRound round, ArmedTrigger trigger, MacroLoop loop)
    {
        switch (loop)
        {
            case MacroLoop.Press:
                round.Start(trigger, repeating: false);
                break;

            case MacroLoop.Hold:
                round.Start(trigger, repeating: true);
                break;

            case MacroLoop.Toggle:
                if (round.IsRunning(trigger))
                {
                    round.Stop(trigger);
                }
                else
                {
                    round.Start(trigger, repeating: true);
                }

                break;
        }
    }

    public static void Released(TriggerRound round, ArmedTrigger trigger, MacroLoop loop)
    {
        switch (loop)
        {
            case MacroLoop.Hold:
                round.Stop(trigger);
                break;

            case MacroLoop.Release:
                round.Start(trigger, repeating: false);
                break;
        }
    }
}
