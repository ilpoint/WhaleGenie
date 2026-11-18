using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A record is several answers about one thing — where a hit was, how big it was, how sure the
/// reading was — kept together under names instead of spread over a variable each. Its fields are
/// read with <c>field</c>, and it is written as a JSON object so it survives a file.
/// </summary>
public class RecordValueTests
{
    /// <summary>One hit, shaped the way the finders hand one over.</summary>
    private static Value Hit(string text = "") => Value.FromRecord(text.Length == 0
        ?
        [
            ("x", Value.FromNumber(30)),
            ("y", Value.FromNumber(40)),
            ("left", Value.FromNumber(20)),
            ("top", Value.FromNumber(30)),
            ("width", Value.FromNumber(20)),
            ("height", Value.FromNumber(20)),
            ("score", Value.FromNumber(0.93)),
            ("box", Value.FromText("20,30,20,20")),
        ]
        :
        [
            ("text", Value.FromText(text)),
        ]);

    private static VariableBag Bag(params (string Name, Value Value)[] values)
    {
        var bag = new VariableBag();
        foreach (var (name, value) in values)
        {
            bag.Set(name, value);
        }

        return bag;
    }

    private static ExpressionException Failure(string source, IVariableResolver variables)
    {
        Assert.False(Expression.TryEvaluate(source, variables, out _, out var error));
        return Assert.IsType<ExpressionException>(error);
    }

    [Fact]
    public void A_record_is_read_field_by_field()
    {
        var bag = Bag(("hit", Hit()));

        Assert.True(Expression.Evaluate("$hit", bag).IsRecord);
        Assert.Equal("30", Expression.Evaluate("field($hit, \"x\")", bag).AsText());
        Assert.Equal("20,30,20,20", Expression.Evaluate("field($hit, \"box\")", bag).AsText());

        // Names are read without caring about case, which is how every other name in a macro is read.
        Assert.Equal("0.93", Expression.Evaluate("field($hit, \"SCORE\")", bag).AsText());

        // A record counts its fields, so length says how much there is to read.
        Assert.Equal("8", Expression.Evaluate("length($hit)", bag).AsText());

        // An object read out of JSON is the same kind of value, so its fields are read the same way.
        Assert.Equal("2", Expression.Evaluate(
            "field(jsonGet(jsonOf(\"a\", 1, \"b\", 2), \"\"), \"b\")").AsText());
    }

    [Fact]
    public void A_field_the_record_does_not_have_is_reported_with_the_ones_it_has()
    {
        var bag = Bag(("hit", Hit()), ("plain", Value.FromText("hello")));

        var missing = Failure("field($hit, \"score2\")", bag);
        Assert.Equal(ExpressionErrorCode.TypeMismatch, missing.Code);
        Assert.Contains("score2", missing.Detail, System.StringComparison.Ordinal);
        foreach (var name in new[] { "x", "y", "left", "top", "width", "height", "score", "box" })
        {
            Assert.Contains(name, missing.Detail, System.StringComparison.Ordinal);
        }

        // A value that is not a record at all says so rather than quietly reading as nothing.
        Assert.Equal(ExpressionErrorCode.TypeMismatch, Failure("field($plain, \"x\")", bag).Code);
        Assert.Equal(ExpressionErrorCode.TypeMismatch, Failure("field(1, \"x\")", bag).Code);
    }

    [Fact]
    public void A_record_is_written_as_one_json_object()
    {
        var bag = Bag(("hit", Value.FromRecord(
        [
            ("x", Value.FromNumber(30)),
            ("text", Value.FromText("say \"hi\", then go")),
            ("sure", Value.FromBool(true)),
            ("box", Value.FromText("1,2,3,4")),
        ])));

        var written = Expression.Evaluate("$hit", bag).AsText();
        Assert.StartsWith("{\"x\":30,\"text\":\"", written, System.StringComparison.Ordinal);
        Assert.EndsWith("\",\"sure\":true,\"box\":\"1,2,3,4\"}", written, System.StringComparison.Ordinal);

        // Fields keep the order they were written in, which is what a reader of the file sees.
        Assert.Equal("{\"x\":30,\"y\":40}", Value.FromRecord(
            [("x", Value.FromNumber(30)), ("y", Value.FromNumber(40))]).AsText());

        // Text read back out of a record is the text that went in: the commas and the quotes of it
        // are the JSON's business, not the macro's.
        Assert.Equal("say \"hi\", then go",
            Expression.Evaluate("field(jsonGet($hit, \"\"), \"text\")", bag).AsText());
    }

    [Fact]
    public void A_record_read_back_from_its_own_text_is_the_record_that_went_out()
    {
        var nested = Value.FromRecord([("title", Value.FromText("记事本"))]);
        var hit = Value.FromRecord(
        [
            ("x", Value.FromNumber(30)),
            ("score", Value.FromNumber(0.93)),
            ("width", Value.FromNumber(20)),
            ("text", Value.FromText("Save")),
            ("box", Value.FromText("20,30,20,20")),
            ("nested", nested),
            ("tags", Value.FromList([Value.FromNumber(1), Value.FromText("a")])),
        ]);
        var bag = Bag(("hit", hit));

        var back = Expression.Evaluate("jsonGet(toJson($hit), \"\")", bag);

        Assert.True(back.IsRecord);
        Assert.Equal(hit, back);
        Assert.Equal(hit.AsText(), back.AsText());

        // Each field keeps the kind of value it had, so a number read back is still a number.
        var reading = Bag(("back", back), ("tags", back.Field("tags")!));
        Assert.Equal(30, Expression.Evaluate("field($back, \"x\")", reading).AsNumber());
        Assert.Equal("记事本",
            Expression.Evaluate("field(field($back, \"nested\"), \"title\")", reading).AsText());
        Assert.Equal("1, a", Expression.Evaluate("$tags", reading).AsText());
        Assert.Equal(2, Expression.Evaluate("count($tags)", reading).AsNumber());
    }

    [Fact]
    public void A_record_of_several_fields_and_a_list_of_them_are_told_apart()
    {
        var bag = Bag(("hit", Hit("Save")),
            ("hits", Value.FromList([Hit("Save"), Hit("Cancel")])));

        Assert.True(Expression.Evaluate("$hits", bag).IsList);
        Assert.Equal(2, Expression.Evaluate("$hits", bag).AsList().Count);
        Assert.Equal("Cancel",
            Expression.Evaluate("field(get($hits, 1), \"text\")", bag).AsText());

        // Written into a file, a list of records is an array of objects.
        Assert.Equal("[{\"text\":\"Save\"},{\"text\":\"Cancel\"}]",
            Expression.Evaluate("toJson($hits)", bag).AsText());
        Assert.True(Expression.Evaluate("jsonGet(toJson($hits), \"\")", bag).IsList);
        Assert.Equal(Expression.Evaluate("$hits", bag),
            Expression.Evaluate("jsonGet(toJson($hits), \"\")", bag));
    }
}
