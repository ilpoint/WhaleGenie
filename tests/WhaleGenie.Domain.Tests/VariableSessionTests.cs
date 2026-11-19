using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// The one table a project shares with its runs. What the checks are about is the part that used to
/// be impossible to ask: two projects are two tables, and neither can see the other's values.
/// </summary>
public class VariableSessionTests
{
    [Fact]
    public void Two_projects_do_not_see_each_others_variables()
    {
        var first = new VariableSession();
        var second = new VariableSession();
        first.Adopt(new VariableDefinition
        {
            Name = "count",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            Default = Formula.Text("1"),
        });
        first.Values.SetNumber("count", 5);

        Assert.True(first.Values.TryGet("count", out var mine));
        Assert.Equal(5, mine.AsNumber());
        Assert.Null(second.FindGlobal("count"));
        Assert.False(second.Values.TryGet("count", out _));
    }

    /// <summary>
    /// A starting value is read against the session's own table, so a declaration that names
    /// another global shows what it would start at rather than an unreadable-name problem.
    /// </summary>
    [Fact]
    public void An_adopted_declaration_reads_its_default_against_the_session()
    {
        var session = new VariableSession();
        session.Adopt(new VariableDefinition
        {
            Name = "base",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            Default = Formula.Text("4"),
        });
        session.Values.SetNumber("base", 4);
        var derived = new VariableDefinition
        {
            Name = "derived",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            Default = Formula.Binary("+", Formula.Name("base"), Formula.Number(1)),
        };
        session.Adopt(derived);

        Assert.False(derived.DefaultHasError);
        Assert.Equal("5", derived.DefaultPreview);
    }

    /// <summary>
    /// A window that has to write while it reads gets its own copy: what it works out on the way
    /// must not become a value a macro reads.
    /// </summary>
    [Fact]
    public void A_working_copy_takes_the_values_but_keeps_its_own_writes()
    {
        var session = new VariableSession();
        session.Values.SetNumber("count", 2);

        var context = session.Context();
        Assert.True(context.Global.TryGet("count", out var copied));
        Assert.Equal(2, copied.AsNumber());

        context.Global.SetNumber("scratch", 9);
        Assert.False(session.Values.TryGet("scratch", out _));

        // The built-in values are seeded in the copy, so a default that names one is readable.
        Assert.Contains(context.System.Values.Keys,
            name => name.StartsWith(VariableName.SystemPrefix));
    }
}
