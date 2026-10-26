using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// What a submacro is called, decided in one place: the picker beside a call, the check that reads a
/// written name and the run that looks one up all take their names from here, so a name that is
/// offered is a name that runs.
/// </summary>
public class CallableMacrosTests
{
    private static MacroItem Macro(string name, params MacroItem[] children)
    {
        var macro = new MacroItem { Name = name };
        macro.SubMacros.AddRange(children);
        return macro;
    }

    /// <summary>
    /// A submacro keeps its own name when that name is unambiguous, and is always reachable as
    /// 父/子 — the form that still says which one is meant when two parents each hold one.
    /// </summary>
    [Fact]
    public void A_submacro_is_offered_by_its_name_and_by_its_path()
    {
        var parent = Macro("外面", Macro("里面"));

        var names = CallableMacros.Callables([parent]).Select(callable => callable.Name).ToList();

        Assert.Contains("外面", names);
        Assert.Contains("里面", names);
        Assert.Contains("外面/里面", names);
    }

    /// <summary>
    /// Two submacros of the same name are not offered by that name: a call that says "收尾" would
    /// have to guess which one. Their paths are, and those do not guess.
    /// </summary>
    [Fact]
    public void A_name_two_submacros_share_is_only_offered_as_a_path()
    {
        var first = Macro("甲", Macro("收尾"));
        var second = Macro("乙", Macro("收尾"));

        var names = CallableMacros
            .Callables([first, second]).Select(callable => callable.Name).ToList();

        Assert.DoesNotContain("收尾", names);
        Assert.Contains("甲/收尾", names);
        Assert.Contains("乙/收尾", names);
    }

    /// <summary>
    /// What a run is handed is what the picker offered: the same entries, the same names, with the
    /// steps of the macro the name belongs to.
    /// </summary>
    [Fact]
    public void The_run_is_handed_the_same_names_the_editor_offers()
    {
        var parent = Macro("外面", Macro("里面"));

        var offered = CallableMacros.Callables([parent]).Select(callable => callable.Name).ToList();
        var runnable = CallableMacros.Entries([parent]).Select(entry => entry.Name).ToList();

        Assert.Equal(offered, runnable);
    }
}
