using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The line under a formula says what the formula is doing in plain Chinese: it is the reading a
/// reader who does not follow the formula checks their own logic against before running anything, so
/// every case here is read off a formula the language itself reads back rather than off a pattern
/// written out a second time.
/// </summary>
[RestoreSharedState]
public class ExpressionPhraseTests
{
    /// <summary>A formula said out loud, with the names WhaleGenie's own variables go by.</summary>
    private static string Say(ExpressionNode shape)
        => ExpressionPhrase.Say(shape, VariableCatalog.Spoken);

    /// <summary>
    /// Reads the sentences with the interface in Chinese, on the one thread that may change the
    /// language at all: a window on screen binds to the text table, and the table says it has changed
    /// by raising an event on whichever thread is writing.
    /// </summary>
    private static void InChinese(Action check) => Ui.Run(() => InterfaceLanguage.InChinese(check));

    [Fact]
    public void The_line_reads_a_formula_built_out_of_formulas()
    {
        InChinese(() => Assert.Equal("取【剪贴板】里的【第1项】的【月份】字段",
            Say(Formula.Call("field", Formula.Call("get", Formula.Name("sys.clipboard"), Formula.Number("0", 0d)), Formula.Text("月份")))));
    }

    [Fact]
    public void A_place_still_to_fill_in_is_said_as_well()
    {
        InChinese(() => Assert.Equal("待填", ExpressionPhrase.Say(HoleNode.Instance)));
    }

    /// <summary>
    /// A place is counted the way a person counts places: the formula writes 0 for the first item and
    /// a person says 第1项. Only a number written into the formula is counted that way, because only
    /// then is the number a thing the writer chose rather than a value that arrives later.
    /// </summary>
    [Fact]
    public void A_place_written_as_a_number_is_counted_from_one()
    {
        InChinese(() =>
        {
            Assert.Equal("取【rows】里的【第1项】", Say(Formula.Call("get", Formula.Name("rows"), Formula.Number("0", 0d))));
            Assert.Equal("取【rows】从第【3】项开始", Say(Formula.Call("slice", Formula.Name("rows"), Formula.Number("2", 2d))));
            Assert.Equal("取【text】从第【3】个字开始", Say(Formula.Call("substring", Formula.Name("text"), Formula.Number("2", 2d))));
        });
    }

    [Fact]
    public void A_place_written_as_a_variable_is_read_out_as_it_stands()
    {
        InChinese(() => Assert.Equal("取【rows】里的【第i项】", Say(Formula.Call("get", Formula.Name("rows"), Formula.Name("i")))));
    }

    /// <summary>
    /// One sentence to a function has to read well when an argument that may be left out has been
    /// left out, which is why the part about it is written in square brackets and dropped whole.
    /// </summary>
    [Fact]
    public void An_argument_left_out_is_not_said_at_all()
    {
        InChinese(() =>
        {
            Assert.Equal("取【text】从第【2】个字开始", Say(Formula.Call("substring", Formula.Name("text"), Formula.Number("1", 1d))));
            Assert.Equal("取【text】从第【2】个字开始，要【3】个字", Say(Formula.Call("substring", Formula.Name("text"), Formula.Number("1", 1d), Formula.Number("3", 3d))));
            Assert.Equal("随机取一个数", Say(Formula.Call("random")));
            Assert.Equal("随机取一个数，在【1】到【10】之间", Say(Formula.Call("random", Formula.Number("1", 1d), Formula.Number("10", 10d))));
            Assert.Equal("现在的时间", Say(Formula.Call("now")));
        });
    }

    [Fact]
    public void The_words_for_true_false_and_nothing_are_read_out()
    {
        InChinese(() =>
        {
            Assert.Equal("真", Say(Formula.Constant("true")));
            Assert.Equal("假", Say(Formula.Constant("false")));
            Assert.Equal("空", Say(Formula.Constant("null")));
        });
    }

    /// <summary>
    /// Operators are said as words rather than as signs, and brackets go where a person reading the
    /// sentence would need them: around an operand of an arithmetic sign that is a sentence of its
    /// own, and nowhere around a condition, which reads as one clause after another.
    /// </summary>
    [Fact]
    public void Operators_are_said_in_words_with_brackets_where_they_are_needed()
    {
        InChinese(() =>
        {
            Assert.Equal("count加上1", Say(Formula.Binary("+", Formula.Name("count"), Formula.Number("1", 1d))));
            Assert.Equal("（1加上2）乘上3", Say(Formula.Binary("*", Formula.Binary("+", Formula.Number("1", 1d), Formula.Number("2", 2d)), Formula.Number("3", 3d))));
            Assert.Equal("a大于或等于3并且并非b小于2", Say(Formula.Binary("and", Formula.Binary(">=", Formula.Name("a"), Formula.Number("3", 3d)), Formula.Unary("not", Formula.Binary("<", Formula.Name("b"), Formula.Number("2", 2d))))));
            Assert.Equal("text等于ab", Say(Formula.Binary("==", Formula.Name("text"), Formula.Text("ab"))));
            Assert.Equal("负的5", Say(Formula.Unary("-", Formula.Number("5", 5d))));
        });
    }

    /// <summary>
    /// A list and the functions that take any number of arguments read as one run of things, which is
    /// what a reader counting them expects: one list rather than one phrase per argument.
    /// </summary>
    [Fact]
    public void A_list_and_the_functions_that_take_many_arguments_read_as_one_run_of_things()
    {
        InChinese(() =>
        {
            Assert.Equal("由1、a组成的列表", Say(Formula.List(Formula.Number("1", 1d), Formula.Text("a"))));
            Assert.Equal("造一张列表", Say(Formula.Call("list")));
            Assert.Equal("造一张列表，里面有1、2", Say(Formula.Call("list", Formula.Number("1", 1d), Formula.Number("2", 2d))));
            Assert.Equal("把a、和、b接成一段文字", Say(Formula.Call("concat", Formula.Name("a"), Formula.Text("和"), Formula.Name("b"))));
            Assert.Equal("取1、2里最小的", Say(Formula.Call("min", Formula.Number("1", 1d), Formula.Number("2", 2d))));
            Assert.Equal("造一条记录，字段名和值依次是a、1", Say(Formula.Call("jsonOf", Formula.Text("a"), Formula.Number("1", 1d))));
        });
    }

    [Fact]
    public void A_function_with_many_arguments_of_its_own_is_read_in_its_own_words()
    {
        InChinese(() =>
        {
            Assert.Equal("把【name】写成大写", Say(Formula.Call("upper", Formula.Name("name"))));
            Assert.Equal("如果count大于3，就取【多】，不然取【少】", Say(Formula.Call("if", Formula.Binary(">", Formula.Name("count"), Formula.Number("3", 3d)), Formula.Text("多"), Formula.Text("少"))));
            Assert.Equal("把【rows】复制一份", Say(Formula.Call("append", Formula.Name("rows"))));
            Assert.Equal("把【rows】复制一份，末尾加上4、5", Say(Formula.Call("append", Formula.Name("rows"), Formula.Number("4", 4d), Formula.Number("5", 5d))));
            Assert.Equal("把【原样】插到【rows】的第【2】项前面", Say(Formula.Call("insertAt", Formula.Name("rows"), Formula.Number("1", 1d), Formula.Text("原样"))));
        });
    }

    /// <summary>
    /// Every sentence in the table is a whole sentence: what a call of that function says has no
    /// piece of the pattern left in it and is not empty. A sentence naming a place the function does
    /// not have, or leaving a bracket of its own open, is caught here rather than by a reader meeting
    /// a stray brace in the middle of a line.
    /// </summary>
    [Fact]
    public void Every_function_says_a_whole_sentence()
    {
        InChinese(() =>
        {
            var wrong = new List<string>();
            foreach (var function in Expression.Functions)
            {
                var places = Enumerable.Range(0, function.Parameters.Count)
                    .Select(_ => (ExpressionNode)HoleNode.Instance)
                    .ToList();
                var said = ExpressionPhrase.Say(new CallNode(function.Name, places));
                if (said.Length == 0 || said.IndexOfAny(['{', '}', '[', ']']) >= 0)
                {
                    wrong.Add($"{function.Name}: \"{said}\"");
                }
            }

            Assert.Empty(wrong.Order());
        });
    }
}
