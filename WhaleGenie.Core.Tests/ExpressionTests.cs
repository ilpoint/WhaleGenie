using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

public class ExpressionTests
{
    private static string Text(string source) => Expression.Evaluate(source).AsText();

    private static bool Flag(string source) => Expression.Evaluate(source).AsBool();

    private static ExpressionException Failure(string source)
    {
        Assert.False(Expression.TryEvaluate(source, null, out _, out var error));
        return Assert.IsType<ExpressionException>(error);
    }

    [Theory]
    [InlineData("1 + 2", "3")]
    [InlineData("2 * 3 + 1", "7")]
    [InlineData("2 * (3 + 1)", "8")]
    [InlineData("10 / 4", "2.5")]
    [InlineData("7 % 3", "1")]
    [InlineData("-5 + 2", "-3")]
    [InlineData("1.5 + 1.5", "3")]
    [InlineData("2 + 3 * 4", "14")]
    public void Arithmetic_follows_the_usual_precedence(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Theory]
    [InlineData("\"a\" + \"b\"", "ab")]
    [InlineData("\"count=\" + 3", "count=3")]
    [InlineData("1 + 2 + \"x\"", "3x")]
    public void Plus_glues_text_together(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Theory]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 2", true)]
    [InlineData("3 > 4", false)]
    [InlineData("\"10\" > \"9\"", true)]
    [InlineData("\"abc\" < \"abd\"", true)]
    [InlineData("2 == 2.0", true)]
    [InlineData("1 != 2", true)]
    [InlineData("\"a\" == \"A\"", true)]
    public void Comparisons_work_on_numbers_and_text(string source, bool expected)
        => Assert.Equal(expected, Flag(source));

    [Theory]
    [InlineData("true and true", true)]
    [InlineData("true and false", false)]
    [InlineData("false or true", true)]
    [InlineData("not false", true)]
    [InlineData("!0", true)]
    [InlineData("1 == 1 and 2 > 1", true)]
    public void Logic_combines_flags(string source, bool expected)
        => Assert.Equal(expected, Flag(source));

    [Fact]
    public void And_skips_the_second_side_when_the_first_is_false()
        => Assert.False(Flag("false and 1 / 0 == 0"));

    [Fact]
    public void Or_skips_the_second_side_when_the_first_is_true()
        => Assert.True(Flag("true or 1 / 0 == 0"));

    [Fact]
    public void If_only_runs_the_branch_it_picks()
    {
        Assert.Equal("1", Text("if(true, 1, 1 / 0)"));
        Assert.Equal("2", Text("if(false, 1 / 0, 2)"));
    }

    [Theory]
    [InlineData("upper(\"ada\")", "ADA")]
    [InlineData("lower(\"ADA\")", "ada")]
    [InlineData("trim(\"  ada  \")", "ada")]
    [InlineData("length(\"hello\")", "5")]
    [InlineData("substring(\"abcdef\", 1, 3)", "bcd")]
    [InlineData("substring(\"abcdef\", -2)", "ef")]
    [InlineData("replace(\"a-b-c\", \"-\", \"+\")", "a+b+c")]
    [InlineData("replaceRegex(\"a1 b22\", \"[0-9]+\", \"#\")", "a# b#")]
    [InlineData("replaceRegex(\"2024-03-01\", \"([0-9]+)-([0-9]+)-([0-9]+)\", \"$3/$2/$1\")", "01/03/2024")]
    [InlineData("replaceRegex(\"AbC\", \"(?i)b\", \"x\")", "AxC")]
    [InlineData("join(split(\"a,b\", \",\"), \"-\")", "a-b")]
    [InlineData("concat(\"a\", 1, true)", "a1true")]
    [InlineData("repeat(\"ab\", 3)", "ababab")]
    [InlineData("format(\"{0}-{1}\", \"a\", 2)", "a-2")]
    [InlineData("extract(\"abc123\", \"[0-9]+\")", "123")]
    [InlineData("length(list(1, 2, 3))", "3")]
    public void Text_functions_shape_values(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Theory]
    [InlineData("contains(\"hello\", \"ELL\")", true)]
    [InlineData("contains(list(\"a\", \"b\"), \"B\")", true)]
    [InlineData("startsWith(\"Hello\", \"he\")", true)]
    [InlineData("endsWith(\"Hello\", \"LO\")", true)]
    [InlineData("match(\"abc123\", \"[0-9]+\")", true)]
    [InlineData("match(\"abc\", \"[0-9]+\")", false)]
    [InlineData("indexOf(\"hello\", \"ll\") == 2", true)]
    [InlineData("indexOf(list(1, 2, 3), \"3\") == 2", true)]
    [InlineData("indexOf(\"hello\", \"z\") == -1", true)]
    public void Search_functions_answer_questions(string source, bool expected)
        => Assert.Equal(expected, Flag(source));

    [Theory]
    [InlineData("abs(-3)", "3")]
    [InlineData("round(2.5)", "3")]
    [InlineData("round(3.14159, 2)", "3.14")]
    [InlineData("floor(2.9)", "2")]
    [InlineData("ceil(2.1)", "3")]
    [InlineData("min(3, 1, 2)", "1")]
    [InlineData("max(3, 1, 2)", "3")]
    [InlineData("pow(2, 10)", "1024")]
    [InlineData("sqrt(9)", "3")]
    [InlineData("sum(list(1, 2, 3))", "6")]
    public void Maths_functions_compute(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Fact]
    public void Random_between_bounds_stays_inside_them()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var value = Expression.Evaluate("random(5, 8)").AsNumber();
            Assert.InRange(value, 5.0, 8.0);
            Assert.Equal(value, System.Math.Floor(value));
        }
    }

    [Theory]
    [InlineData("list(1, 2, 3)", "1, 2, 3")]
    [InlineData("[1, 2, 3]", "1, 2, 3")]
    [InlineData("append(list(1, 2), 3, 4)", "1, 2, 3, 4")]
    [InlineData("insertAt(list(1, 3), 1, 2)", "1, 2, 3")]
    [InlineData("removeAt(list(1, 2, 3), 0)", "2, 3")]
    [InlineData("get(list(\"a\", \"b\"), 1)", "b")]
    [InlineData("get(list(\"a\", \"b\"), -1)", "b")]
    [InlineData("first(list(1, 2, 3))", "1")]
    [InlineData("last(list(1, 2, 3))", "3")]
    [InlineData("sort(list(3, 1, 2))", "1, 2, 3")]
    [InlineData("sort(list(\"b\", \"a\"))", "a, b")]
    [InlineData("sortDesc(list(3, 1, 2))", "3, 2, 1")]
    [InlineData("reverse(list(1, 2, 3))", "3, 2, 1")]
    [InlineData("unique(list(1, 1, 2))", "1, 2")]
    [InlineData("slice(list(1, 2, 3, 4), 1, 2)", "2, 3")]
    [InlineData("count(list(1, 2, 3))", "3")]
    public void List_functions_build_and_reshape_lists(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Theory]
    [InlineData("number(\"42\")", "42")]
    [InlineData("text(42)", "42")]
    [InlineData("bool(\"\")", "false")]
    [InlineData("bool(\"0\")", "false")]
    [InlineData("bool(\"yes\")", "true")]
    [InlineData("bool(list(1))", "true")]
    [InlineData("bool(list())", "false")]
    public void Conversion_functions_switch_kind(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Fact]
    public void Empty_text_stays_empty_instead_of_failing()
    {
        Assert.True(Expression.TryEvaluate("   ", null, out var value, out var error));
        Assert.Null(error);
        Assert.Equal(string.Empty, value.AsText());
    }

    [Fact]
    public void Unknown_variable_is_reported()
        => Assert.Equal(ExpressionErrorCode.UnknownVariable, Failure("$missing + 1").Code);

    [Fact]
    public void Unknown_function_is_reported()
        => Assert.Equal(ExpressionErrorCode.UnknownFunction, Failure("nope(1)").Code);

    [Fact]
    public void Wrong_argument_count_is_reported()
        => Assert.Equal(ExpressionErrorCode.ArgumentCount, Failure("upper()").Code);

    [Fact]
    public void Divide_by_zero_is_reported()
        => Assert.Equal(ExpressionErrorCode.DivideByZero, Failure("1 / 0").Code);

    [Fact]
    public void A_missing_bracket_is_reported()
        => Assert.Equal(ExpressionErrorCode.Syntax, Failure("(1 + 2").Code);

    [Fact]
    public void A_lone_equals_sign_is_reported()
        => Assert.Equal(ExpressionErrorCode.Syntax, Failure("1 = 1").Code);

    [Fact]
    public void An_index_off_the_end_is_reported()
        => Assert.Equal(ExpressionErrorCode.IndexOutOfRange, Failure("get(list(1), 5)").Code);

    [Fact]
    public void Variables_are_read_by_name()
    {
        var bag = new VariableBag();
        bag.SetText("name", "ada");
        bag.SetNumber("count", 3);

        Assert.Equal("ADA-3", Expression.Evaluate("upper($name) + \"-\" + $count", bag).AsText());
        Assert.True(Expression.Evaluate("$count >= 3 and $name == \"ADA\"", bag).AsBool());
    }

    [Fact]
    public void List_variables_can_be_grown_by_an_expression()
    {
        var bag = new VariableBag();
        bag.Set("names", Value.FromList([Value.FromText("Ada"), Value.FromText("Grace")]));

        var grown = Expression.Evaluate("append($names, \"Alan\")", bag);
        Assert.Equal(new[] { "Ada", "Grace", "Alan" }, grown.Items.Select(item => item.AsText()));
    }

    [Fact]
    public void The_function_table_is_unique_and_documented()
    {
        var names = Expression.Functions.Select(function => function.Name).ToList();
        Assert.True(names.Count > 30);
        Assert.Equal(names.Count, names.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Expression.Functions, function =>
        {
            Assert.False(string.IsNullOrWhiteSpace(function.Signature));
            Assert.False(string.IsNullOrWhiteSpace(function.Description));
        });
    }

    [Theory]
    [InlineData("year(\"2024-03-01\")", "2024")]
    [InlineData("month(\"2024-03-01\")", "3")]
    [InlineData("day(\"2024-03-01\")", "1")]
    [InlineData("hour(\"2024-03-01 09:30:15\")", "9")]
    [InlineData("minute(\"2024-03-01 09:30:15\")", "30")]
    [InlineData("second(\"2024-03-01 09:30:15\")", "15")]
    [InlineData("weekday(\"2024-03-01\")", "5")]
    [InlineData("weekday(\"2024-03-03\")", "7")]
    public void A_date_is_read_apart_piece_by_piece(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Theory]
    [InlineData("dateAdd(\"2024-03-01\", 45, \"days\")", "2024-04-15 00:00:00")]
    [InlineData("dateAdd(\"2024-01-31\", 1, \"months\")", "2024-02-29 00:00:00")]
    [InlineData("dateAdd(\"2024-03-01 09:00:00\", -90, \"minutes\")", "2024-03-01 07:30:00")]
    [InlineData("dateAdd(\"2024-03-01\", 2, \"weeks\")", "2024-03-15 00:00:00")]
    public void A_date_can_be_moved_forward_or_back(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Theory]
    [InlineData("dateDiff(\"2024-01-01\", \"2024-03-01\", \"days\")", "60")]
    [InlineData("dateDiff(\"2024-01-01\", \"2025-01-01\", \"years\")", "1")]
    [InlineData("dateDiff(\"2024-03-01\", \"2024-01-01\", \"days\")", "-60")]
    [InlineData("dateDiff(\"2024-01-06\", \"2024-01-01\", \"weeks\")", "-0.714286")]
    public void The_gap_between_two_dates_is_counted_in_the_unit(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Fact]
    public void A_date_is_written_out_in_a_pattern()
        => Assert.Equal("2024/03/01 09:30",
            Text("formatDate(\"2024-03-01 09:30:00\", \"yyyy/MM/dd HH:mm\")"));

    [Fact]
    public void A_time_on_its_own_counts_as_today()
    {
        var bag = new VariableBag();
        bag.SetText("clock", "09:30:00");

        var today = Expression.Evaluate("today()").AsText();
        Assert.Equal(today + " 09:30:00",
            Expression.Evaluate("formatDate($clock, \"yyyy-MM-dd HH:mm:ss\")", bag).AsText());
    }

    [Theory]
    [InlineData("parseDate(\"01/03/2024\", \"dd/MM/yyyy\")", "2024-03-01 00:00:00")]
    [InlineData("parseDate(\"2024.03.01 09:30\", \"yyyy.MM.dd HH:mm\")", "2024-03-01 09:30:00")]
    [InlineData("parseDate(\" 2024-03-01 \", \"yyyy-MM-dd\")", "2024-03-01 00:00:00")]
    public void A_date_written_in_a_pattern_is_read_back(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Fact]
    public void A_date_is_read_and_written_round_the_same_pattern()
        => Assert.Equal("2024-03-01",
            Text("formatDate(parseDate(\"01/03/2024\", \"dd/MM/yyyy\"), \"yyyy-MM-dd\")"));

    [Fact]
    public void A_pattern_that_only_names_a_time_counts_as_today()
    {
        var today = Expression.Evaluate("today()").AsText();
        Assert.Equal(today + " 09:30:00", Text("parseDate(\"09:30\", \"HH:mm\")"));
    }

    [Fact]
    public void Text_that_does_not_match_the_pattern_is_refused()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch,
            Failure("parseDate(\"half past nine\", \"yyyy-MM-dd\")").Code);

    [Fact]
    public void Text_that_is_not_a_date_is_refused()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch, Failure("year(\"half past nine\")").Code);

    [Theory]
    [InlineData("jsonGet('{\"a\":{\"b\":[1,2,3]}}', 'a.b[1]')", "2")]
    [InlineData("jsonGet('{\"a\":{\"b\":[1,2,3]}}', '$.a.b[0]')", "1")]
    [InlineData("jsonGet('{\"a\":{\"b\":[1,2,3]}}', 'a.b')", "1, 2, 3")]
    [InlineData("jsonGet('{\"a\":{\"b\":1}}', 'a')", "{\"b\":1}")]
    [InlineData("jsonGet('{\"name\":\"ada\",\"ok\":true}', 'name')", "ada")]
    [InlineData("jsonGet('{\"name\":\"ada\",\"ok\":true}', 'ok')", "true")]
    [InlineData("jsonGet('{\"a\":1}', 'missing')", "")]
    [InlineData("jsonGet('', 'a')", "")]
    [InlineData("jsonKeys('{\"a\":1,\"b\":2}')", "a, b")]
    [InlineData("jsonHas('{\"a\":1}', 'a')", "true")]
    [InlineData("jsonHas('{\"a\":1}', 'b')", "false")]
    public void Json_is_read_by_path(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Theory]
    [InlineData("jsonSet('', 'a.b', 1)", "{\"a\":{\"b\":1}}")]
    [InlineData("jsonSet('{}', 'name', \"ada\")", "{\"name\":\"ada\"}")]
    [InlineData("jsonSet('{}', 'a', list(1, 2))", "{\"a\":[1,2]}")]
    [InlineData("jsonSet('{}', 'tags[0]', 1)", "{\"tags\":[1]}")]
    [InlineData("jsonSet(jsonSet('{}', 'tags[0]', 1), 'tags[1]', 2)", "{\"tags\":[1,2]}")]
    public void Json_can_be_grown_by_a_path(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Fact]
    public void A_value_is_written_as_json()
        => Assert.Equal("[1,\"a\",true]", Text("toJson(list(1, \"a\", true))"));

    [Theory]
    [InlineData("jsonOf(\"a\", 1, \"b\", 2)", "{\"a\":1,\"b\":2}")]
    [InlineData("jsonOf(\"name\", \"ada\")", "{\"name\":\"ada\"}")]
    [InlineData("jsonOf(\"ok\", true)", "{\"ok\":true}")]
    [InlineData("jsonOf(\"xs\", list(1, 2))", "{\"xs\":[1,2]}")]
    [InlineData("jsonGet(jsonOf(\"name\", \"ada\", \"age\", 36), \"age\")", "36")]
    [InlineData("jsonKeys(jsonOf(\"a\", 1, \"b\", 2))", "a, b")]
    public void An_object_is_put_together_from_names_and_values(string source, string expected)
        => Assert.Equal(expected, Text(source));

    [Fact]
    public void An_odd_number_of_arguments_cannot_make_an_object()
        => Assert.Equal(ExpressionErrorCode.ArgumentCount,
            Failure("jsonOf(\"a\", 1, \"b\")").Code);

    [Fact]
    public void A_field_of_an_object_has_to_be_named()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch,
            Failure("jsonOf(\"  \", 1)").Code);

    [Fact]
    public void Text_that_is_not_json_is_refused()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch, Failure("jsonGet('{oops}', 'a')").Code);

    [Fact]
    public void Numbers_are_written_without_a_trailing_zero()
    {
        Assert.Equal("3", Value.FromNumber(3).AsText());
        Assert.Equal("2.5", Value.FromNumber(2.5).AsText());
        Assert.Equal("0.3", Expression.Evaluate("0.1 + 0.2").AsText());
    }

    [Theory]
    [InlineData("$count + 1", "count")]
    [InlineData("upper($name)", "name")]
    [InlineData("append($names, $item)", "names,item")]
    [InlineData("$sys.date + \"-\" + $name", "sys.date,name")]
    [InlineData("$count - 1", "count")]
    [InlineData("$a and $a", "a")]
    [InlineData("nothing here", "")]
    [InlineData("cost is $5", "")]
    public void Referenced_names_are_found_inside_expressions(string source, string expected)
        => Assert.Equal(expected, string.Join(",", Expression.ReferencedNames(source)));

    [Fact]
    public void A_hyphen_reads_as_a_minus_rather_than_part_of_a_name()
    {
        var bag = new VariableBag();
        bag.SetNumber("count", 5);

        Assert.Equal("4", Expression.Evaluate("$count-1", bag).AsText());
        Assert.Equal(["count"], Expression.ReferencedNames("$count-1"));
    }
}
