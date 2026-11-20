using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What a function takes, told argument by argument. The editor names its lines after these, so a
/// function whose arguments are not spelled out is a function whose lines have nothing to say.
/// </summary>
public class ExpressionArgumentTests
{
    [Fact]
    public void Every_argument_a_function_takes_is_spelled_out()
    {
        foreach (var function in Expression.Functions)
        {
            var names = function.Parameters.Select(parameter => parameter.Name).ToList();

            // The names come from the signature, so a function whose signature says one thing and
            // whose arguments say another cannot get past this.
            Assert.Equal(names.Count, names.Distinct(System.StringComparer.Ordinal).Count());
            Assert.All(names, name => Assert.Contains(name, function.Signature));
            Assert.All(function.Parameters, parameter =>
            {
                Assert.False(string.IsNullOrWhiteSpace(parameter.Label));
                Assert.False(string.IsNullOrWhiteSpace(parameter.Hint));
            });

            // The first however many are the ones that cannot be left out, and a function that
            // takes more than its signature names is one whose last argument repeats.
            Assert.InRange(function.MinArguments, 0, names.Count);
            Assert.True(function.MaxArguments == int.MaxValue
                || function.MaxArguments == names.Count,
                $"{function.Name} takes {function.MaxArguments} arguments but names {names.Count}");
        }
    }

    [Fact]
    public void An_argument_that_means_something_narrower_says_so()
    {
        var field = Expression.Functions.First(function => function.Name == "field");
        var name = field.Parameters.First(parameter => parameter.Name == "name");

        // Read a field of a record, "name" is the field to read, and nothing else means quite that.
        Assert.Contains("field to read", name.Hint);

        // Where the shared table does say it, the same sentence is handed to every function that
        // calls its argument that: substring and slice both ask where to start.
        var substring = Expression.Functions.First(function => function.Name == "substring");
        var slice = Expression.Functions.First(function => function.Name == "slice");
        Assert.Equal(substring.Parameters.Single(parameter => parameter.Name == "start").Hint,
            slice.Parameters.Single(parameter => parameter.Name == "start").Hint);
    }

    [Theory]
    [InlineData("upper", "text")]
    [InlineData("substring", "text,start,length")]
    [InlineData("concat", "value")]
    [InlineData("jsonOf", "name,value")]
    [InlineData("random", "min,max")]
    [InlineData("now", "")]
    public void The_arguments_come_from_the_signature(string function, string expected)
    {
        var names = Expression.Functions.First(entry => entry.Name == function).Parameters
            .Select(parameter => parameter.Name);

        Assert.Equal(expected, string.Join(",", names));
    }
}
