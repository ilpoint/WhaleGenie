using System;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The catalogue and the engine have to name the same actions. The source ledger follows which
/// results a path writes by reading the handler tables; this asks the built table itself, so a
/// change in how the tables are written cannot make the two agree by parsing nothing.
/// </summary>
public class ActionRoutingTests
{
    private static MacroRunner Engine() => new(new VariableStore());

    [Fact]
    public void Every_action_the_catalogue_offers_is_answered_by_the_engine()
    {
        var runner = Engine();

        var missing = ActionSemanticsCatalog.All
            // A case is a label inside a switch step rather than something a run carries out.
            .Where(action => !action.Hidden)
            .Where(action => !runner.HandledActions.Contains(action.Key))
            .Select(action => action.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
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
