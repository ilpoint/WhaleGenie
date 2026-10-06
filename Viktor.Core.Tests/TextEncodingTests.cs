using Viktor.Core.Devices;

namespace Viktor.Core.Tests;

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
}
