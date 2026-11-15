using System;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The catalogue and the engine have to name the same actions. A key the catalogue offers and the
/// engine cannot answer reaches the user as a step that fails only when it is run, which is the
/// kind of mistake a table makes findable without running anything.
/// </summary>
public sealed class ActionDispatchTests
{
    private static MacroRunner Engine() => new(new VariableStore());

    [Fact]
    public void Every_action_the_catalogue_offers_is_answered_by_the_engine()
    {
        var runner = Engine();

        var missing = ActionSemanticsCatalog.All
            // A case is a label inside a switch step rather than something a run carries out.
            .Where(action => action.Key != "control.case")
            .Where(action => !runner.HandledActions.Contains(action.Key))
            .Select(action => action.Key)
            .ToList();

        Assert.True(missing.Count == 0,
            "the catalogue offers these actions and the engine answers none of them: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void Every_action_the_engine_answers_is_one_the_catalogue_offers()
    {
        var runner = Engine();

        var unknown = runner.HandledActions
            .Where(key => ActionSemanticsCatalog.Find(key) is null)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0,
            "the engine answers these actions and the catalogue does not know them: "
            + string.Join(", ", unknown));
    }
}
