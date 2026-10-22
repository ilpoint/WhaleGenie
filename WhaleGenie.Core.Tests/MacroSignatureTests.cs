using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What a macro may declare: a call can only use a name the macro language reads back, one that
/// does not collide with the call's own supplied-empty marker, and a parameter that is either
/// required or has a default rather than both.
/// </summary>
public class MacroSignatureTests
{
    private static MacroSignature Signature(params MacroArgument[] arguments)
        => new(arguments, []);

    private static MacroSignature WithResult(params MacroReturn[] results)
        => new([], results);

    [Theory]
    [InlineData("有 空格")]
    [InlineData(".count")]
    [InlineData("a-b")]
    [InlineData("$name")]
    public void A_declared_name_has_to_be_readable_as_a_variable(string name)
    {
        var problem = Assert.Single(Signature(new MacroArgument(name, string.Empty, false)).Problems());

        Assert.Equal(MacroSignatureFault.InvalidName, problem.Fault);
        Assert.Equal(name, problem.Name);
    }

    [Fact]
    public void Dots_and_chinese_are_part_of_a_readable_name()
    {
        Assert.Empty(Signature(new MacroArgument("a.b", string.Empty, false)).Problems());
        Assert.Empty(Signature(new MacroArgument("次数", string.Empty, false)).Problems());
        Assert.Empty(Signature(new MacroArgument("_start", string.Empty, false)).Problems());
        Assert.Empty(Signature(new MacroArgument("1count", string.Empty, false)).Problems());
        Assert.Empty(Signature(new MacroArgument("123", string.Empty, false)).Problems());
    }

    [Fact]
    public void A_parameter_may_not_end_with_the_supplied_empty_suffix()
    {
        var problem = Assert.Single(Signature(new MacroArgument("x.given", string.Empty, false)).Problems());

        Assert.Equal(MacroSignatureFault.ConflictingName, problem.Fault);
        Assert.Equal("x.given", problem.Name);
    }

    [Fact]
    public void A_result_may_end_with_the_supplied_empty_suffix()
    {
        // Only a parameter has an argument field and a marker; a result is just a variable.
        Assert.Empty(WithResult(new MacroReturn("x.given")).Problems());
    }

    [Fact]
    public void A_required_argument_may_not_have_a_default()
    {
        var problem = Assert.Single(Signature(
            new MacroArgument("次数", "3", Required: true)).Problems());

        Assert.Equal(MacroSignatureFault.RequiredWithDefault, problem.Fault);
        Assert.Equal("次数", problem.Name);
        Assert.Empty(Signature(new MacroArgument("次数", "3", Required: false)).Problems());
    }

    [Fact]
    public void An_empty_name_is_reported_before_its_other_faults()
    {
        var problem = Assert.Single(Signature(new MacroArgument("", "3", Required: true)).Problems());

        Assert.Equal(MacroSignatureFault.EmptyName, problem.Fault);
    }

    [Fact]
    public void Duplicate_names_are_read_without_caring_about_case()
    {
        var problems = Signature(
            new MacroArgument("Name", string.Empty, false),
            new MacroArgument("name", string.Empty, false)).Problems();

        var problem = Assert.Single(problems);
        Assert.Equal(MacroSignatureFault.DuplicateName, problem.Fault);
    }

    [Fact]
    public void The_run_keeps_a_parameter_and_a_result_from_sharing_a_name()
    {
        // The same name on both sides is an in-out value: the call hands it in and takes it back.
        var problems = new MacroSignature(
            [new MacroArgument("n", string.Empty, false)],
            [new MacroReturn("n")]).Problems();

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("sys.date")]
    [InlineData("sys.macroName")]
    [InlineData("step.abcd.outcome")]
    [InlineData("step.abcd.error")]
    public void A_name_the_run_keeps_is_refused(string name)
    {
        var problem = Assert.Single(Signature(new MacroArgument(name, string.Empty, false)).Problems());

        Assert.Equal(MacroSignatureFault.ReservedName, problem.Fault);
    }

    [Fact]
    public void Problems_are_kept_in_the_order_the_declarations_are_read()
    {
        var problems = Signature(
            new MacroArgument("", string.Empty, false),
            new MacroArgument(".bad", string.Empty, false),
            new MacroArgument("a b", string.Empty, false)).Problems();

        Assert.Equal(
            [MacroSignatureFault.EmptyName, MacroSignatureFault.InvalidName,
                MacroSignatureFault.InvalidName],
            problems.Select(problem => problem.Fault));
    }
}
