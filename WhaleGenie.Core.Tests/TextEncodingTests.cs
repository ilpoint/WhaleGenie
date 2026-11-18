using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The encodings a macro can name. Most of what matters here is that the code pages a Chinese
/// Windows writes its own files in are there when a macro asks for them.
/// </summary>
public class TextEncodingTests
{
    [Fact]
    public void Gbk_works_even_when_it_is_the_first_thing_this_class_is_asked_for()
    {
        // .NET leaves GBK and its neighbours out unless it is asked for them, and registering is
        // something the class itself does on the way in. A macro that writes a GBK file before the
        // action editor has ever been opened asks for exactly this and nothing else first.
        var bytes = TextEncoding.Resolve("gbk").GetBytes("中文");

        Assert.Equal([0xD6, 0xD0, 0xCE, 0xC4], bytes);
    }

    [Fact]
    public void Utf8_is_what_a_macro_says_nothing_means()
    {
        var plain = TextEncoding.Resolve(string.Empty);

        // No byte-order mark, which is what everything from Node to a JSON file expects, and what
        // an older macro that has no encoding field was written with.
        Assert.Equal([0xE4, 0xB8, 0xAD], plain.GetBytes("中"));
        Assert.Empty(plain.GetPreamble());
    }

    [Fact]
    public void A_mark_can_be_asked_for_and_so_can_a_wider_alphabet()
    {
        Assert.Equal([0xEF, 0xBB, 0xBF], TextEncoding.Resolve("utf8bom").GetPreamble());
        Assert.Equal([0xFF, 0xFE], TextEncoding.Resolve("utf16").GetPreamble());
    }

    [Fact]
    public void A_name_that_means_nothing_is_read_as_plain_utf8()
    {
        // Rather than refusing to run, which would break a macro that was written before the field
        // existed, or one whose author mistyped it.
        Assert.Empty(TextEncoding.Resolve("klingon").GetPreamble());
        Assert.Equal(TextEncoding.Default, TextEncoding.Names[0]);
    }

    [Fact]
    public void The_machines_own_code_page_can_be_asked_for_by_name()
    {
        // This is the one a macro needs when it reads what cmd.exe printed: on the machine this runs
        // on it is GBK, and on an English one it is 1252, so the name is asked for rather than the
        // number being written into every macro.
        var system = TextEncoding.Resolve(TextEncoding.System);
        var current = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

        Assert.Equal(current, system.CodePage);
    }

    [Fact]
    public void A_file_that_is_not_utf8_at_all_is_read_in_the_machines_own_code_page()
    {
        // A Chinese Excel writes a CSV in GBK and nothing in the file says so. Read as UTF-8, every
        // character of it becomes a question mark — a file there is nothing to be done with, rather
        // than one that reads slightly wrong. So bytes with no mark that cannot be UTF-8 are read
        // with the code page the machine itself uses.
        var bytes = TextEncoding.Resolve("gbk").GetBytes("任务,状态");

        var read = TextEncoding.Read(bytes, string.Empty);

        Assert.DoesNotContain("\uFFFD", read);
        Assert.Equal(TextEncoding.Resolve(TextEncoding.System).GetString(bytes), read);
    }

    [Fact]
    public void Utf8_is_read_as_utf8_and_a_mark_beats_what_the_step_guessed()
    {
        Assert.Equal("任务", TextEncoding.Read(TextEncoding.Resolve("utf8").GetBytes("任务"), string.Empty));

        // A mark is something the file itself says, so it wins over the name the step gave — which
        // is the case that matters, because a step that has no idea still has to read a file
        // Notepad wrote. Written out the way Notepad writes it: the mark, then the text.
        var marked = TextEncoding.Resolve("utf8bom").GetPreamble()
            .Concat(TextEncoding.Resolve("utf8").GetBytes("任务"))
            .ToArray();
        Assert.Equal("任务", TextEncoding.Read(marked, "gbk"));

        // No mark and a name to go on: an older program's own encoding, read as written.
        Assert.Equal("任务", TextEncoding.Read(TextEncoding.Resolve("utf16").GetBytes("任务"), "utf16"));
    }

    [Fact]
    public void A_file_on_disk_is_read_through_the_same_rule()
    {
        // The rule lives in one place and the file device calls it: a file read any other way would
        // be a second answer to the same question.
        var folder = Directory.CreateTempSubdirectory("whalegenie-encoding-");
        try
        {
            var bytes = TextEncoding.Resolve("gbk").GetBytes("任务,状态");
            File.WriteAllBytes(Path.Combine(folder.FullName, "state.csv"), bytes);

            var devices = new LocalFileDevice(folder.FullName);

            Assert.Equal(TextEncoding.Read(bytes, string.Empty), devices.ReadText("state.csv", string.Empty));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
