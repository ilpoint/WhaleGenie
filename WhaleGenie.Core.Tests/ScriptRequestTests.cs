using System;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What a script and the bridge between it and the macro say to each other, as a thing of its own:
/// the file a call is written in, the words the macro is asked in, the answer written back, and what
/// a value the macro holds comes to in a language that has one kind of value for everything. The
/// other end of it is AutoHotkey, which no check can drive from here, so what it does either side of
/// the writing is checked on its own.
/// </summary>
public class ScriptRequestTests
{
    /// <summary>
    /// A call arrives as a file a script wrote: what it is doing, the name or the step identity it
    /// is about, whether the value it hands over is a number, and then the value.
    /// </summary>
    [Fact]
    public void What_a_script_wrote_is_read_as_the_call_it_was()
    {
        var asked = ScriptRequest.Read("get\nwho\nt\n");
        Assert.Equal("get", asked.Verb);
        Assert.Equal("who", asked.Name);
        Assert.False(asked.Number);
        Assert.Equal(string.Empty, asked.Value);

        // The value is the last thing in the file, so it carries line breaks of its own without
        // anything having to be escaped on the way.
        Assert.Equal("one\ntwo", ScriptRequest.Read("print\n\nt\none\ntwo\n").Value);

        // A number is said to be one as the call is written: a language where everything is text
        // has no other way of saying so afterwards.
        var handed = ScriptRequest.Read("result\n\nn\n2.5");
        Assert.True(handed.Number);
        Assert.Equal("2.5", handed.Value);

        // A mark at the front is what a file this program wrote starts with.
        Assert.Equal("step", ScriptRequest.Read("\uFEFFstep\na7k2\nt\n").Verb);

        // A file with only half a call in it is refused rather than answered: it means the script
        // stopped in the middle of writing one.
        Assert.Throws<InvalidOperationException>(() => ScriptRequest.Read("get\nwho\n"));
    }

    /// <summary>
    /// What the macro is asked is what a script of one of the languages this program runs itself
    /// would have asked: the two ends of the bridge say the same thing in the same words.
    /// </summary>
    [Fact]
    public void What_a_script_asks_is_put_in_the_words_the_macro_answers_in()
    {
        var reading = ScriptRequest.Read("get\nwho\nt\n").Message();
        Assert.Equal("get", reading["call"]!.GetValue<string>());
        Assert.Equal("who", reading["name"]!.GetValue<string>());
        Assert.Null(reading["value"]);

        var ran = ScriptRequest.Read("step\na7k2\nt\n").Message();
        Assert.Equal("step", ran["call"]!.GetValue<string>());
        Assert.Equal("a7k2", ran["id"]!.GetValue<string>());

        var said = ScriptRequest.Read("print\n\nt\na line\n").Message();
        Assert.Equal("print", said["call"]!.GetValue<string>());
        Assert.Equal("a line", said["text"]!.GetValue<string>());

        // A number handed over is a number the macro holds, and the same digits written as text are
        // the text they were written as.
        Assert.Equal(2.5, ScriptRequest.Read("set\nn\nn\n2.5").Message()["value"]!.GetValue<double>());
        Assert.Equal("2.5", ScriptRequest.Read("set\nn\nt\n2.5").Message()["value"]!.GetValue<string>());

        // Anything else is a script asking for something there is no answer to, and it is said
        // rather than passed on as a call the macro would have to make sense of.
        Assert.Throws<InvalidOperationException>(() => ScriptRequest.Read("sing\n\nt\n").Message());
    }

    /// <summary>
    /// A value the macro holds, as the text of one line: what a script of this language can hold.
    /// A list or a record has no shape of its own in it, so it comes over as its own writing, which
    /// the script can read or hand straight back.
    /// </summary>
    [Fact]
    public void What_the_macro_holds_comes_over_as_text_this_language_can_keep()
    {
        Assert.Equal("Ann", ScriptRequest.Plain(JsonNode.Parse("\"Ann\"")));
        Assert.Equal("1", ScriptRequest.Plain(JsonNode.Parse("true")));
        Assert.Equal("0", ScriptRequest.Plain(JsonNode.Parse("false")));
        Assert.Equal("12", ScriptRequest.Plain(JsonNode.Parse("12")));
        Assert.Equal("2.5", ScriptRequest.Plain(JsonNode.Parse("2.5")));
        Assert.Equal(string.Empty, ScriptRequest.Plain(null));

        Assert.Equal("[\"a\",1]", ScriptRequest.Plain(JsonNode.Parse("[\"a\",1]")));
        Assert.Equal("{\"x\":1}", ScriptRequest.Plain(JsonNode.Parse("{\"x\":1}")));
    }

    /// <summary>What a script reads back: whether the macro took the call, and what it said.</summary>
    [Fact]
    public void What_a_script_reads_back_says_whether_it_was_taken()
    {
        Assert.Equal("ok\nAnn", ScriptRequest.Answered(true, "Ann"));
        Assert.Equal("no\nthis macro has no step with the identity zzzz",
            ScriptRequest.Answered(false, "this macro has no step with the identity zzzz"));
    }
}
