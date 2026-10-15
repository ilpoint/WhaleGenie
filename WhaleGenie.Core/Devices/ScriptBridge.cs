using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The other end of a script's bridge for a language that cannot open a named pipe of its own.
/// AutoHotkey is that language: its file functions do not work on a pipe, so the script talks to a
/// second copy of this program started with <see cref="Flag"/>, and that copy does the talking.
/// </summary>
/// <remarks>
/// What the two sides say to each other travels through two files rather than through the pipe
/// between them: one holds what the script asks, the other what the macro answered, and a single
/// byte on the bridge's own output says the answer is there to read. Text with a comma, a quote or
/// a line break of its own needs no escaping that way, and neither side has to agree on an encoding
/// for a pipe it cannot write to plainly.
/// </remarks>
public static class ScriptBridge
{
    /// <summary>What the program is asked with when it is a script's way back into the macro.</summary>
    public const string Flag = "--script-bridge";

    /// <summary>How long the bridge waits for the macro's pipe before giving up on it.</summary>
    private const int ConnectWaitMs = 10000;

    /// <summary>Whether this run of the program is a bridge rather than the program itself.</summary>
    public static bool Asked(IReadOnlyList<string> arguments)
        => arguments.Count > 0 && string.Equals(arguments[0], Flag, StringComparison.Ordinal);

    /// <summary>
    /// Serves one script until it goes away: everything it asks is asked of the macro in the macro's
    /// own words, and the end of the script's input is the end of the bridge. The two files it was
    /// pointed at are the only place either side reads or writes besides the macro's pipe.
    /// </summary>
    public static int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            if (arguments.Count < 3)
            {
                Console.Error.WriteLine($"{Flag} needs the file a script asks in and the file it "
                    + "is answered in, after it.");
                return 1;
            }

            var name = Environment.GetEnvironmentVariable(ScriptHost.PipeVariable) ?? string.Empty;
            if (name.Length == 0)
            {
                Console.Error.WriteLine("whalegenie: no pipe to the macro to answer on.");
                return 1;
            }

            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            pipe.Connect(ConnectWaitMs);
            using var hearing = new StreamReader(pipe, NoMark, false, 4096, leaveOpen: true);
            using var saying = new StreamWriter(pipe, NoMark, 4096, leaveOpen: true)
            {
                AutoFlush = true,
            };

            var asked = arguments[1];
            var answered = arguments[2];
            using var input = new StreamReader(Console.OpenStandardInput(), NoMark);
            var output = Console.OpenStandardOutput();
            while (input.ReadLine() is not null)
            {
                var answer = Serve(hearing, saying, asked, answered);
                foreach (var letter in Done)
                {
                    output.WriteByte((byte)letter);
                }

                output.Flush();
                if (answer.Length > 0)
                {
                    Console.Error.WriteLine(answer);
                }
            }

            return 0;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"whalegenie: {failure.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Answers one request out of the file a script wrote it in, leaving the answer where the script
    /// will look for it. Anything that went wrong is said rather than thrown: a script that cannot
    /// reach the macro has to be told so, and the bridge is the one that has to say it.
    /// </summary>
    private static string Serve(StreamReader hearing, StreamWriter saying, string asked,
        string answered)
    {
        ScriptRequest request;
        try
        {
            request = ScriptRequest.Read(File.ReadAllText(asked, NoMark));
        }
        catch (Exception failure)
        {
            File.WriteAllText(answered, ScriptRequest.Answered(false, failure.Message), Mark);
            return failure.Message;
        }

        try
        {
            saying.WriteLine(request.Message().ToJsonString());
            var line = hearing.ReadLine()
                ?? throw new IOException("the macro is no longer listening for this script");
            var reply = JsonNode.Parse(line) as JsonObject
                ?? throw new IOException("the macro answered with something that is not an answer");
            var ok = reply["ok"]?.GetValue<bool>() ?? false;
            var value = ok
                ? ScriptRequest.Plain(reply["value"])
                : reply["error"]?.GetValue<string>() ?? "the macro refused that";

            // A call that hands something over is answered with nothing but whether it was taken;
            // only the ones that ask something have a value to read.
            File.WriteAllText(answered, ScriptRequest.Answered(ok, value), Mark);
            return ok ? string.Empty : value;
        }
        catch (Exception failure)
        {
            File.WriteAllText(answered, ScriptRequest.Answered(false, failure.Message), Mark);
            return failure.Message;
        }
    }

    /// <summary>UTF-8 with a mark, which is what a script opens a file of this program's as.</summary>
    private static readonly UTF8Encoding Mark = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>What the bridge says once the answer is written where the script will look.</summary>
    private const string Done = "done\n";

    /// <summary>UTF-8 that leaves the byte-order mark off, which is how a pipe is written to.</summary>
    private static readonly UTF8Encoding NoMark = new(encoderShouldEmitUTF8Identifier: false);
}

/// <summary>
/// One question a script asks of the macro, as it was written down: which of the five things it is
/// doing, the name or the step identity it is about, and the value it is handing over — with
/// whether that value is a number, because a scripting language with one kind of value for
/// everything cannot say so afterwards.
/// </summary>
public sealed record ScriptRequest(string Verb, string Name, bool Number, string Value)
{
    /// <summary>Reads what a script wrote: the verb, the name, the kind of value, then the value.</summary>
    public static ScriptRequest Read(string text)
    {
        var written = text.TrimStart('\uFEFF');
        var first = written.IndexOf('\n');
        var second = first < 0 ? -1 : written.IndexOf('\n', first + 1);
        var third = second < 0 ? -1 : written.IndexOf('\n', second + 1);
        if (third < 0)
        {
            throw new InvalidOperationException("a bridge call needs a verb, a name and a kind");
        }

        return new ScriptRequest(
            Line(written, 0, first),
            Line(written, first + 1, second),
            Line(written, second + 1, third) is "n",
            written[(third + 1)..].TrimEnd('\r', '\n'));
    }

    /// <summary>What the macro is asked, in the words its own scripts are answered in.</summary>
    public JsonObject Message() => Verb switch
    {
        "get" => new JsonObject { ["call"] = "get", ["name"] = Name },
        "set" => Valued("set"),
        "step" => new JsonObject { ["call"] = "step", ["id"] = Name },
        "print" => new JsonObject { ["call"] = "print", ["text"] = Value },
        "result" => Valued("result"),
        _ => throw new InvalidOperationException($"a script asked for something there is no "
            + $"answer to: {Verb}"),
    };

    private JsonObject Valued(string call)
    {
        var node = Number
            && double.TryParse(Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out var number)
            ? JsonValue.Create(number)
            : JsonValue.Create(Value);
        return new JsonObject { ["call"] = call, ["name"] = Name, ["value"] = node };
    }

    /// <summary>
    /// A value the macro holds as the text of one line, which is all a scripting language without
    /// records has: what it wrote, the digits of a number, one or nothing for a flag, and the JSON
    /// of a list or a record, which the script can read or hand straight back.
    /// </summary>
    public static string Plain(JsonNode? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value is JsonArray or JsonObject)
        {
            return value.ToJsonString();
        }

        var element = value.GetValue<JsonElement>();
        return element.ValueKind switch
        {
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            _ => element.GetString() ?? string.Empty,
        };
    }

    /// <summary>One line of what a script wrote, or nothing when it wrote fewer lines than that.</summary>
    private static string Line(string text, int from, int to)
        => to < 0 ? string.Empty : text[from..to].TrimEnd('\r');

    /// <summary>What a script reads back: whether it went well, and what the macro said.</summary>
    public static string Answered(bool ok, string value)
        => (ok ? "ok" : "no") + "\n" + value;
}
