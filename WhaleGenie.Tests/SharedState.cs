using System;
using System.Reflection;
using WhaleGenie.Localization;
using Xunit.Sdk;

namespace WhaleGenie.Tests;

/// <summary>
/// The state a check leaves behind when it does not put it back. What is left here is static for
/// the whole process: the interface language and where settings are read from. A check that changes
/// either and fails part-way — or whose work finishes after it has already been judged — leaves the
/// next check reading a world it did not set up.
/// </summary>
/// <remarks>
/// The shared variables used to be on this list. They are not any more: a project owns its own
/// table, so a check that opens one is the only one that sees what it wrote.
/// </remarks>
/// <remarks>
/// Applied as <see cref="RestoreSharedStateAttribute"/>. The point is not to hide a leak but to make
/// a leak fail locally: a check that depends on what ran before it now fails on its own instead of
/// taking an unrelated check down three minutes later.
/// </remarks>
internal sealed record SharedState(Language Language, string SettingsPath)
{
    public static SharedState Capture() => new(Strings.Current.Language, LocalSettings.FilePath);

    public void Restore()
    {
        Strings.Current.Language = Language;
        LocalSettings.FilePath = SettingsPath;
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
