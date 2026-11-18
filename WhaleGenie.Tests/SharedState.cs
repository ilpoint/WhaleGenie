using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using Xunit.Sdk;

namespace WhaleGenie.Tests;

/// <summary>
/// The state a check leaves behind when it does not put it back. Everything here is a static shared
/// by the whole process: the interface language, where settings are read from, the global variable
/// catalogue and the bag of values a run writes into. A check that changes any of them and fails
/// part-way — or whose work finishes after it has already been judged — leaves the next check
/// reading a world it did not set up.
/// </summary>
/// <remarks>
/// Applied as <see cref="RestoreSharedStateAttribute"/>. The point is not to hide a leak but to make
/// a leak fail locally: a check that depends on what ran before it now fails on its own instead of
/// taking an unrelated check down three minutes later.
/// </remarks>
internal sealed record SharedState(
    Language Language,
    string SettingsPath,
    IReadOnlyList<VariableDefinition> Globals)
{
    public static SharedState Capture() => new(
        Strings.Current.Language,
        LocalSettings.FilePath,
        [.. VariableCatalog.Globals]);

    public void Restore()
    {
        Strings.Current.Language = Language;
        LocalSettings.FilePath = SettingsPath;

        if (!VariableCatalog.Globals.SequenceEqual(Globals))
        {
            VariableCatalog.Globals.Clear();
            foreach (var definition in Globals)
            {
                VariableCatalog.Globals.Add(definition);
            }
        }
    }
}

/// <summary>Puts <see cref="SharedState"/> back the way it was found, before and after every check.</summary>
/// <remarks>
/// Both halves run on the user-interface thread. That is not tidiness: changing the language raises
/// the event a window redraws its text from, and a window can only be touched by the thread that
/// owns it — restoring from the runner's thread throws "The calling thread cannot access this
/// object", which is what this attribute did before it was written this way.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RestoreSharedStateAttribute : BeforeAfterTestAttribute
{
    private SharedState? _before;

    public override void Before(MethodInfo methodUnderTest) => _before = Ui.Run(SharedState.Capture);

    public override void After(MethodInfo methodUnderTest)
    {
        var before = _before;
        if (before is not null)
        {
            Ui.Run(before.Restore);
        }
    }
}
