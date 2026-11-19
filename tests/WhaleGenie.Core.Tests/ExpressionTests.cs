using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

public class ExpressionTests
{
    private static string Text(ExpressionNode source) => Expression.Evaluate(source).AsText();

    private static bool Flag(ExpressionNode source) => Expression.Evaluate(source).AsBool();

    private static ExpressionException Failure(ExpressionNode source)
    {
        Assert.False(Expression.TryEvaluate(source, null, out _, out var error));
        return Assert.IsType<ExpressionException>(error);
    }

    [Theory]
    [MemberData(nameof(Arithmetic_follows_the_usual_precedenceCases))]
    public void Arithmetic_follows_the_usual_precedence(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> Arithmetic_follows_the_usual_precedenceCases() => new()
    {
        { Formula.Binary("+", Formula.Number("1", 1d), Formula.Number("2", 2d)), "3" },
        { Formula.Binary("+", Formula.Binary("*", Formula.Number("2", 2d), Formula.Number("3", 3d)), Formula.Number("1", 1d)), "7" },
        { Formula.Binary("*", Formula.Number("2", 2d), Formula.Binary("+", Formula.Number("3", 3d), Formula.Number("1", 1d))), "8" },
        { Formula.Binary("/", Formula.Number("10", 10d), Formula.Number("4", 4d)), "2.5" },
        { Formula.Binary("%", Formula.Number("7", 7d), Formula.Number("3", 3d)), "1" },
        { Formula.Binary("+", Formula.Unary("-", Formula.Number("5", 5d)), Formula.Number("2", 2d)), "-3" },
        { Formula.Binary("+", Formula.Number("1.5", 1.5d), Formula.Number("1.5", 1.5d)), "3" },
        { Formula.Binary("+", Formula.Number("2", 2d), Formula.Binary("*", Formula.Number("3", 3d), Formula.Number("4", 4d))), "14" },
    };

    [Theory]
    [MemberData(nameof(Plus_glues_text_togetherCases))]
    public void Plus_glues_text_together(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> Plus_glues_text_togetherCases() => new()
    {
        { Formula.Binary("+", Formula.Text("a"), Formula.Text("b")), "ab" },
        { Formula.Binary("+", Formula.Text("count="), Formula.Number("3", 3d)), "count=3" },
        { Formula.Binary("+", Formula.Binary("+", Formula.Number("1", 1d), Formula.Number("2", 2d)), Formula.Text("x")), "3x" },
    };

    [Theory]
    [MemberData(nameof(Comparisons_work_on_numbers_and_textCases))]
    public void Comparisons_work_on_numbers_and_text(ExpressionNode source, bool expected)
        => Assert.Equal(expected, Flag(source));

    public static TheoryData<ExpressionNode, bool> Comparisons_work_on_numbers_and_textCases() => new()
    {
        { Formula.Binary("<", Formula.Number("1", 1d), Formula.Number("2", 2d)), true },
        { Formula.Binary("<=", Formula.Number("2", 2d), Formula.Number("2", 2d)), true },
        { Formula.Binary(">", Formula.Number("3", 3d), Formula.Number("4", 4d)), false },
        { Formula.Binary(">", Formula.Text("10"), Formula.Text("9")), true },
        { Formula.Binary("<", Formula.Text("abc"), Formula.Text("abd")), true },
        { Formula.Binary("==", Formula.Number("2", 2d), Formula.Number("2.0", 2d)), true },
        { Formula.Binary("!=", Formula.Number("1", 1d), Formula.Number("2", 2d)), true },
        { Formula.Binary("==", Formula.Text("a"), Formula.Text("A")), true },
    };

    [Theory]
    [MemberData(nameof(Logic_combines_flagsCases))]
    public void Logic_combines_flags(ExpressionNode source, bool expected)
        => Assert.Equal(expected, Flag(source));

    public static TheoryData<ExpressionNode, bool> Logic_combines_flagsCases() => new()
    {
        { Formula.Binary("and", Formula.Constant("true"), Formula.Constant("true")), true },
        { Formula.Binary("and", Formula.Constant("true"), Formula.Constant("false")), false },
        { Formula.Binary("or", Formula.Constant("false"), Formula.Constant("true")), true },
        { Formula.Unary("not", Formula.Constant("false")), true },
        { Formula.Unary("not", Formula.Number("0", 0d)), true },
        { Formula.Binary("and", Formula.Binary("==", Formula.Number("1", 1d), Formula.Number("1", 1d)), Formula.Binary(">", Formula.Number("2", 2d), Formula.Number("1", 1d))), true },
    };

    [Fact]
    public void And_skips_the_second_side_when_the_first_is_false()
        => Assert.False(Flag(Formula.Binary("and", Formula.Constant("false"), Formula.Binary("==", Formula.Binary("/", Formula.Number("1", 1d), Formula.Number("0", 0d)), Formula.Number("0", 0d)))));

    [Fact]
    public void Or_skips_the_second_side_when_the_first_is_true()
        => Assert.True(Flag(Formula.Binary("or", Formula.Constant("true"), Formula.Binary("==", Formula.Binary("/", Formula.Number("1", 1d), Formula.Number("0", 0d)), Formula.Number("0", 0d)))));

    [Fact]
    public void If_only_runs_the_branch_it_picks()
    {
        Assert.Equal("1", Text(Formula.Call("if", Formula.Constant("true"), Formula.Number("1", 1d), Formula.Binary("/", Formula.Number("1", 1d), Formula.Number("0", 0d)))));
        Assert.Equal("2", Text(Formula.Call("if", Formula.Constant("false"), Formula.Binary("/", Formula.Number("1", 1d), Formula.Number("0", 0d)), Formula.Number("2", 2d))));
    }

    [Theory]
    [MemberData(nameof(Text_functions_shape_valuesCases))]
    public void Text_functions_shape_values(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> Text_functions_shape_valuesCases() => new()
    {
        { Formula.Call("upper", Formula.Text("ada")), "ADA" },
        { Formula.Call("lower", Formula.Text("ADA")), "ada" },
        { Formula.Call("trim", Formula.Text("  ada  ")), "ada" },
        { Formula.Call("length", Formula.Text("hello")), "5" },
        { Formula.Call("substring", Formula.Text("abcdef"), Formula.Number("1", 1d), Formula.Number("3", 3d)), "bcd" },
        { Formula.Call("substring", Formula.Text("abcdef"), Formula.Unary("-", Formula.Number("2", 2d))), "ef" },
        { Formula.Call("replace", Formula.Text("a-b-c"), Formula.Text("-"), Formula.Text("+")), "a+b+c" },
        { Formula.Call("replaceRegex", Formula.Text("a1 b22"), Formula.Text("[0-9]+"), Formula.Text("#")), "a# b#" },
        { Formula.Call("replaceRegex", Formula.Text("2024-03-01"), Formula.Text("([0-9]+)-([0-9]+)-([0-9]+)"), Formula.Text("$3/$2/$1")), "01/03/2024" },
        { Formula.Call("replaceRegex", Formula.Text("AbC"), Formula.Text("(?i)b"), Formula.Text("x")), "AxC" },
        { Formula.Call("join", Formula.Call("split", Formula.Text("a,b"), Formula.Text(",")), Formula.Text("-")), "a-b" },
        { Formula.Call("concat", Formula.Text("a"), Formula.Number("1", 1d), Formula.Constant("true")), "a1true" },
        { Formula.Call("repeat", Formula.Text("ab"), Formula.Number("3", 3d)), "ababab" },
        { Formula.Call("format", Formula.Text("{0}-{1}"), Formula.Text("a"), Formula.Number("2", 2d)), "a-2" },
        { Formula.Call("extract", Formula.Text("abc123"), Formula.Text("[0-9]+")), "123" },
        { Formula.Call("length", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d))), "3" },
    };

    [Theory]
    [MemberData(nameof(Search_functions_answer_questionsCases))]
    public void Search_functions_answer_questions(ExpressionNode source, bool expected)
        => Assert.Equal(expected, Flag(source));

    public static TheoryData<ExpressionNode, bool> Search_functions_answer_questionsCases() => new()
    {
        { Formula.Call("contains", Formula.Text("hello"), Formula.Text("ELL")), true },
        { Formula.Call("contains", Formula.Call("list", Formula.Text("a"), Formula.Text("b")), Formula.Text("B")), true },
        { Formula.Call("startsWith", Formula.Text("Hello"), Formula.Text("he")), true },
        { Formula.Call("endsWith", Formula.Text("Hello"), Formula.Text("LO")), true },
        { Formula.Call("match", Formula.Text("abc123"), Formula.Text("[0-9]+")), true },
        { Formula.Call("match", Formula.Text("abc"), Formula.Text("[0-9]+")), false },
        { Formula.Binary("==", Formula.Call("indexOf", Formula.Text("hello"), Formula.Text("ll")), Formula.Number("2", 2d)), true },
        { Formula.Binary("==", Formula.Call("indexOf", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d)), Formula.Text("3")), Formula.Number("2", 2d)), true },
        { Formula.Binary("==", Formula.Call("indexOf", Formula.Text("hello"), Formula.Text("z")), Formula.Unary("-", Formula.Number("1", 1d))), true },
    };

    [Theory]
    [MemberData(nameof(Maths_functions_computeCases))]
    public void Maths_functions_compute(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> Maths_functions_computeCases() => new()
    {
        { Formula.Call("abs", Formula.Unary("-", Formula.Number("3", 3d))), "3" },
        { Formula.Call("round", Formula.Number("2.5", 2.5d)), "3" },
        { Formula.Call("round", Formula.Number("3.14159", 3.14159d), Formula.Number("2", 2d)), "3.14" },
        { Formula.Call("floor", Formula.Number("2.9", 2.9d)), "2" },
        { Formula.Call("ceil", Formula.Number("2.1", 2.1d)), "3" },
        { Formula.Call("min", Formula.Number("3", 3d), Formula.Number("1", 1d), Formula.Number("2", 2d)), "1" },
        { Formula.Call("max", Formula.Number("3", 3d), Formula.Number("1", 1d), Formula.Number("2", 2d)), "3" },
        { Formula.Call("pow", Formula.Number("2", 2d), Formula.Number("10", 10d)), "1024" },
        { Formula.Call("sqrt", Formula.Number("9", 9d)), "3" },
        { Formula.Call("sum", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d))), "6" },
    };

    [Fact]
    public void Random_between_bounds_stays_inside_them()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var value = Expression.Evaluate(Formula.Call("random", Formula.Number("5", 5d), Formula.Number("8", 8d))).AsNumber();
            Assert.InRange(value, 5.0, 8.0);
            Assert.Equal(value, System.Math.Floor(value));
        }
    }

    [Theory]
    [MemberData(nameof(List_functions_build_and_reshape_listsCases))]
    public void List_functions_build_and_reshape_lists(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> List_functions_build_and_reshape_listsCases() => new()
    {
        { Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d)), "1, 2, 3" },
        { Formula.List(Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d)), "1, 2, 3" },
        { Formula.Call("append", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d)), Formula.Number("3", 3d), Formula.Number("4", 4d)), "1, 2, 3, 4" },
        { Formula.Call("insertAt", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("3", 3d)), Formula.Number("1", 1d), Formula.Number("2", 2d)), "1, 2, 3" },
        { Formula.Call("removeAt", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d)), Formula.Number("0", 0d)), "2, 3" },
        { Formula.Call("get", Formula.Call("list", Formula.Text("a"), Formula.Text("b")), Formula.Number("1", 1d)), "b" },
        { Formula.Call("get", Formula.Call("list", Formula.Text("a"), Formula.Text("b")), Formula.Unary("-", Formula.Number("1", 1d))), "b" },
        { Formula.Call("first", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d))), "1" },
        { Formula.Call("last", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d))), "3" },
        { Formula.Call("sort", Formula.Call("list", Formula.Number("3", 3d), Formula.Number("1", 1d), Formula.Number("2", 2d))), "1, 2, 3" },
        { Formula.Call("sort", Formula.Call("list", Formula.Text("b"), Formula.Text("a"))), "a, b" },
        { Formula.Call("sortDesc", Formula.Call("list", Formula.Number("3", 3d), Formula.Number("1", 1d), Formula.Number("2", 2d))), "3, 2, 1" },
        { Formula.Call("reverse", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d))), "3, 2, 1" },
        { Formula.Call("unique", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("1", 1d), Formula.Number("2", 2d))), "1, 2" },
        { Formula.Call("slice", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d), Formula.Number("4", 4d)), Formula.Number("1", 1d), Formula.Number("2", 2d)), "2, 3" },
        { Formula.Call("count", Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d), Formula.Number("3", 3d))), "3" },
    };

    [Theory]
    [MemberData(nameof(Conversion_functions_switch_kindCases))]
    public void Conversion_functions_switch_kind(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> Conversion_functions_switch_kindCases() => new()
    {
        { Formula.Call("number", Formula.Text("42")), "42" },
        { Formula.Call("text", Formula.Number("42", 42d)), "42" },
        { Formula.Call("bool", Formula.Nothing), "false" },
        { Formula.Call("bool", Formula.Text("0")), "false" },
        { Formula.Call("bool", Formula.Text("yes")), "true" },
        { Formula.Call("bool", Formula.Call("list", Formula.Number("1", 1d))), "true" },
        { Formula.Call("bool", Formula.Call("list")), "false" },
    };

    [Fact]
    public void Unknown_variable_is_reported()
        => Assert.Equal(ExpressionErrorCode.UnknownVariable, Failure(Formula.Binary("+", Formula.Name("missing"), Formula.Number("1", 1d))).Code);

    [Fact]
    public void Unknown_function_is_reported()
        => Assert.Equal(ExpressionErrorCode.UnknownFunction, Failure(Formula.Call("nope", Formula.Number(1))).Code);

    [Fact]
    public void Wrong_argument_count_is_reported()
        => Assert.Equal(ExpressionErrorCode.ArgumentCount, Failure(Formula.Call("upper")).Code);

    [Fact]
    public void Divide_by_zero_is_reported()
        => Assert.Equal(ExpressionErrorCode.DivideByZero, Failure(Formula.Binary("/", Formula.Number("1", 1d), Formula.Number("0", 0d))).Code);

    [Fact]
    public void A_missing_bracket_is_reported()
        => Assert.Equal(ExpressionErrorCode.Syntax, Failure(Formula.Hole).Code);

    [Fact]
    public void An_index_off_the_end_is_reported()
        => Assert.Equal(ExpressionErrorCode.IndexOutOfRange, Failure(Formula.Call("get", Formula.Call("list", Formula.Number("1", 1d)), Formula.Number("5", 5d))).Code);

    [Fact]
    public void Variables_are_read_by_name()
    {
        var bag = new VariableBag();
        bag.SetText("name", "ada");
        bag.SetNumber("count", 3);

        Assert.Equal("ADA-3", Expression.Evaluate(Formula.Binary("+", Formula.Binary("+", Formula.Call("upper", Formula.Name("name")), Formula.Text("-")), Formula.Name("count")), bag).AsText());
        Assert.True(Expression.Evaluate(Formula.Binary("and", Formula.Binary(">=", Formula.Name("count"), Formula.Number("3", 3d)), Formula.Binary("==", Formula.Name("name"), Formula.Text("ADA"))), bag).AsBool());
        Assert.Equal("1", Expression.Evaluate(Formula.Binary("-", Formula.Number("123", 123d), Formula.Number("122", 122d)), bag).AsText());
    }

    [Fact]
    public void List_variables_can_be_grown_by_an_expression()
    {
        var bag = new VariableBag();
        bag.Set("names", Value.FromList([Value.FromText("Ada"), Value.FromText("Grace")]));

        var grown = Expression.Evaluate(Formula.Call("append", Formula.Name("names"), Formula.Text("Alan")), bag);
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
    [MemberData(nameof(A_date_is_read_apart_piece_by_pieceCases))]
    public void A_date_is_read_apart_piece_by_piece(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> A_date_is_read_apart_piece_by_pieceCases() => new()
    {
        { Formula.Call("year", Formula.Text("2024-03-01")), "2024" },
        { Formula.Call("month", Formula.Text("2024-03-01")), "3" },
        { Formula.Call("day", Formula.Text("2024-03-01")), "1" },
        { Formula.Call("hour", Formula.Text("2024-03-01 09:30:15")), "9" },
        { Formula.Call("minute", Formula.Text("2024-03-01 09:30:15")), "30" },
        { Formula.Call("second", Formula.Text("2024-03-01 09:30:15")), "15" },
        { Formula.Call("weekday", Formula.Text("2024-03-01")), "5" },
        { Formula.Call("weekday", Formula.Text("2024-03-03")), "7" },
    };

    [Theory]
    [MemberData(nameof(A_date_can_be_moved_forward_or_backCases))]
    public void A_date_can_be_moved_forward_or_back(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> A_date_can_be_moved_forward_or_backCases() => new()
    {
        { Formula.Call("dateAdd", Formula.Text("2024-03-01"), Formula.Number("45", 45d), Formula.Text("days")), "2024-04-15" },
        { Formula.Call("dateAdd", Formula.Text("2024-01-31"), Formula.Number("1", 1d), Formula.Text("months")), "2024-02-29" },
        { Formula.Call("dateAdd", Formula.Text("2024-03-01 09:00:00"), Formula.Unary("-", Formula.Number("90", 90d)), Formula.Text("minutes")), "2024-03-01 07:30:00" },
        { Formula.Call("dateAdd", Formula.Text("2024-03-01"), Formula.Number("2", 2d), Formula.Text("weeks")), "2024-03-15" },
    };

    [Theory]
    [MemberData(nameof(The_gap_between_two_dates_is_counted_in_the_unitCases))]
    public void The_gap_between_two_dates_is_counted_in_the_unit(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> The_gap_between_two_dates_is_counted_in_the_unitCases() => new()
    {
        { Formula.Call("dateDiff", Formula.Text("2024-01-01"), Formula.Text("2024-03-01"), Formula.Text("days")), "60" },
        { Formula.Call("dateDiff", Formula.Text("2024-01-01"), Formula.Text("2025-01-01"), Formula.Text("years")), "1" },
        { Formula.Call("dateDiff", Formula.Text("2024-03-01"), Formula.Text("2024-01-01"), Formula.Text("days")), "-60" },
        { Formula.Call("dateDiff", Formula.Text("2024-01-06"), Formula.Text("2024-01-01"), Formula.Text("weeks")), "-0.714286" },
    };

    [Fact]
    public void A_date_is_written_out_in_a_pattern()
        => Assert.Equal("2024/03/01 09:30",
            Text(Formula.Call("formatDate", Formula.Text("2024-03-01 09:30:00"), Formula.Text("yyyy/MM/dd HH:mm"))));

    [Fact]
    public void A_time_on_its_own_counts_as_today()
    {
        var bag = new VariableBag();
        bag.SetText("clock", "09:30:00");

        var today = Expression.Evaluate(Formula.Call("today")).AsText();
        Assert.Equal(today + " 09:30:00",
            Expression.Evaluate(Formula.Call("formatDate", Formula.Name("clock"), Formula.Text("yyyy-MM-dd HH:mm:ss")), bag).AsText());
    }

    [Theory]
    [MemberData(nameof(A_date_written_in_a_pattern_is_read_backCases))]
    public void A_date_written_in_a_pattern_is_read_back(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> A_date_written_in_a_pattern_is_read_backCases() => new()
    {
        { Formula.Call("parseDate", Formula.Text("01/03/2024"), Formula.Text("dd/MM/yyyy")), "2024-03-01" },
        { Formula.Call("parseDate", Formula.Text("2024.03.01 09:30"), Formula.Text("yyyy.MM.dd HH:mm")), "2024-03-01 09:30:00" },
        { Formula.Call("parseDate", Formula.Text(" 2024-03-01 "), Formula.Text("yyyy-MM-dd")), "2024-03-01" },
    };

    [Theory]
    [MemberData(nameof(Dates_are_compared_and_ordered_as_momentsCases))]
    /// <summary>
    /// A date is its own kind, so two of them have an order before anybody asks for one.
    /// The text a date is written as still compares equal to it.
    /// </summary>
    public void Dates_are_compared_and_ordered_as_moments(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    [Fact]
    public void A_list_of_dates_sorts_in_time_order()
        => Assert.Equal("2024-03-01, 2024-03-05",
            Text(Formula.Call("sort", Formula.List(Formula.Call("parseDate", Formula.Text("2024-03-05"), Formula.Text("yyyy-MM-dd")), Formula.Call("parseDate", Formula.Text("2024-03-01"), Formula.Text("yyyy-MM-dd"))))));

    [Fact]
    public void A_date_is_read_and_written_round_the_same_pattern()
        => Assert.Equal("2024-03-01",
            Text(Formula.Call("formatDate", Formula.Call("parseDate", Formula.Text("01/03/2024"), Formula.Text("dd/MM/yyyy")), Formula.Text("yyyy-MM-dd"))));

    [Fact]
    public void A_pattern_that_only_names_a_time_counts_as_today()
    {
        var today = Expression.Evaluate(Formula.Call("today")).AsText();
        Assert.Equal(today + " 09:30:00", Text(Formula.Call("parseDate", Formula.Text("09:30"), Formula.Text("HH:mm"))));
    }

    [Fact]
    public void Text_that_does_not_match_the_pattern_is_refused()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch,
            Failure(Formula.Call("parseDate", Formula.Text("half past nine"), Formula.Text("yyyy-MM-dd"))).Code);

    [Fact]
    public void Text_that_is_not_a_date_is_refused()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch, Failure(Formula.Call("year", Formula.Text("half past nine"))).Code);

    [Theory]
    [MemberData(nameof(Json_is_read_by_pathCases))]
    public void Json_is_read_by_path(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> Dates_are_compared_and_ordered_as_momentsCases() => new()
    {
        { Formula.Binary("<", Formula.Call("parseDate", Formula.Text("2024-03-01"), Formula.Text("yyyy-MM-dd")), Formula.Call("parseDate", Formula.Text("2024-03-02"), Formula.Text("yyyy-MM-dd"))), "true" },
        { Formula.Binary(">", Formula.Call("parseDate", Formula.Text("2024-03-02"), Formula.Text("yyyy-MM-dd")), Formula.Text("2024-03-01")), "true" },
        { Formula.Binary("==", Formula.Call("parseDate", Formula.Text("2024-03-01"), Formula.Text("yyyy-MM-dd")), Formula.Text("2024-03-01")), "true" },
        { Formula.Binary("==", Formula.Call("parseDate", Formula.Text("2024-03-01"), Formula.Text("yyyy-MM-dd")), Formula.Call("parseDate", Formula.Text("2024-03-01"), Formula.Text("yyyy-MM-dd"))), "true" },
        { Formula.Call("min", Formula.Call("parseDate", Formula.Text("2024-03-05"), Formula.Text("yyyy-MM-dd")), Formula.Call("parseDate", Formula.Text("2024-03-01"), Formula.Text("yyyy-MM-dd"))), "2024-03-01" },
        { Formula.Call("max", Formula.Call("parseDate", Formula.Text("2024-03-05"), Formula.Text("yyyy-MM-dd")), Formula.Call("parseDate", Formula.Text("2024-03-01"), Formula.Text("yyyy-MM-dd"))), "2024-03-05" },
    };

    public static TheoryData<ExpressionNode, string> Json_is_read_by_pathCases() => new()
    {
        { Formula.Call("jsonGet", Formula.Text("{\"a\":{\"b\":[1,2,3]}}"), Formula.Text("a.b[1]")), "2" },
        { Formula.Call("jsonGet", Formula.Text("{\"a\":{\"b\":[1,2,3]}}"), Formula.Text("$.a.b[0]")), "1" },
        { Formula.Call("jsonGet", Formula.Text("{\"a\":{\"b\":[1,2,3]}}"), Formula.Text("a.b")), "1, 2, 3" },
        { Formula.Call("jsonGet", Formula.Text("{\"a\":{\"b\":1}}"), Formula.Text("a")), "{\"b\":1}" },
        { Formula.Call("jsonGet", Formula.Text("{\"name\":\"ada\",\"ok\":true}"), Formula.Text("name")), "ada" },
        { Formula.Call("jsonGet", Formula.Text("{\"name\":\"ada\",\"ok\":true}"), Formula.Text("ok")), "true" },
        { Formula.Call("jsonGet", Formula.Text("{\"a\":1}"), Formula.Text("missing")), "" },
        { Formula.Call("jsonGet", Formula.Nothing, Formula.Text("a")), "" },
        { Formula.Call("jsonKeys", Formula.Text("{\"a\":1,\"b\":2}")), "a, b" },
        { Formula.Call("jsonHas", Formula.Text("{\"a\":1}"), Formula.Text("a")), "true" },
        { Formula.Call("jsonHas", Formula.Text("{\"a\":1}"), Formula.Text("b")), "false" },
    };

    [Theory]
    [MemberData(nameof(Json_can_be_grown_by_a_pathCases))]
    public void Json_can_be_grown_by_a_path(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> Json_can_be_grown_by_a_pathCases() => new()
    {
        { Formula.Call("jsonSet", Formula.Nothing, Formula.Text("a.b"), Formula.Number("1", 1d)), "{\"a\":{\"b\":1}}" },
        { Formula.Call("jsonSet", Formula.Text("{}"), Formula.Text("name"), Formula.Text("ada")), "{\"name\":\"ada\"}" },
        { Formula.Call("jsonSet", Formula.Text("{}"), Formula.Text("a"), Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d))), "{\"a\":[1,2]}" },
        { Formula.Call("jsonSet", Formula.Text("{}"), Formula.Text("tags[0]"), Formula.Number("1", 1d)), "{\"tags\":[1]}" },
        { Formula.Call("jsonSet", Formula.Call("jsonSet", Formula.Text("{}"), Formula.Text("tags[0]"), Formula.Number("1", 1d)), Formula.Text("tags[1]"), Formula.Number("2", 2d)), "{\"tags\":[1,2]}" },
    };

    [Fact]
    public void A_value_is_written_as_json()
        => Assert.Equal("[1,\"a\",true]", Text(Formula.Call("toJson", Formula.Call("list", Formula.Number("1", 1d), Formula.Text("a"), Formula.Constant("true")))));

    [Theory]
    [MemberData(nameof(An_object_is_put_together_from_names_and_valuesCases))]
    public void An_object_is_put_together_from_names_and_values(ExpressionNode source, string expected)
        => Assert.Equal(expected, Text(source));

    public static TheoryData<ExpressionNode, string> An_object_is_put_together_from_names_and_valuesCases() => new()
    {
        { Formula.Call("jsonOf", Formula.Text("a"), Formula.Number("1", 1d), Formula.Text("b"), Formula.Number("2", 2d)), "{\"a\":1,\"b\":2}" },
        { Formula.Call("jsonOf", Formula.Text("name"), Formula.Text("ada")), "{\"name\":\"ada\"}" },
        { Formula.Call("jsonOf", Formula.Text("ok"), Formula.Constant("true")), "{\"ok\":true}" },
        { Formula.Call("jsonOf", Formula.Text("xs"), Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d))), "{\"xs\":[1,2]}" },
        { Formula.Call("jsonGet", Formula.Call("jsonOf", Formula.Text("name"), Formula.Text("ada"), Formula.Text("age"), Formula.Number("36", 36d)), Formula.Text("age")), "36" },
        { Formula.Call("jsonKeys", Formula.Call("jsonOf", Formula.Text("a"), Formula.Number("1", 1d), Formula.Text("b"), Formula.Number("2", 2d))), "a, b" },
    };

    [Fact]
    public void An_odd_number_of_arguments_cannot_make_an_object()
        => Assert.Equal(ExpressionErrorCode.ArgumentCount,
            Failure(Formula.Call("jsonOf", Formula.Text("a"), Formula.Number("1", 1d), Formula.Text("b"))).Code);

    [Fact]
    public void A_field_of_an_object_has_to_be_named()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch,
            Failure(Formula.Call("jsonOf", Formula.Text("  "), Formula.Number("1", 1d))).Code);

    [Fact]
    public void Text_that_is_not_json_is_refused()
        => Assert.Equal(ExpressionErrorCode.TypeMismatch, Failure(Formula.Call("jsonGet", Formula.Text("{oops}"), Formula.Text("a"))).Code);

    [Fact]
    public void Numbers_are_written_without_a_trailing_zero()
    {
        Assert.Equal("3", Value.FromNumber(3).AsText());
        Assert.Equal("2.5", Value.FromNumber(2.5).AsText());
        Assert.Equal("0.3", Expression.Evaluate(Formula.Binary("+", Formula.Number("0.1", 0.1d), Formula.Number("0.2", 0.2d))).AsText());
    }
}
