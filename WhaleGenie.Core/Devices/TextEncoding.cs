using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The encodings a macro can name when it reads or writes a text file. Plain UTF-8 is what
/// everything new uses, but a Chinese Windows writes its own files in GBK and often puts a
/// byte-order mark on UTF-8 ones, so a macro that has to work with the files a machine already
/// has needs to be able to say which is which.
/// </summary>
public static class TextEncoding
{
    /// <summary>The name used when a step says nothing: UTF-8 without a byte-order mark.</summary>
    public const string Default = "utf8";

    /// <summary>
    /// The name for the code page this machine's own console programs write in, which is what a
    /// macro needs when it reads what cmd.exe or Windows PowerShell printed.
    /// </summary>
    public const string System = "system";

    /// <summary>The code page a Chinese Windows writes its own text files in.</summary>
    private const int CodePageGbk = 936;

    /// <summary>The code page of plain UTF-8, which is what everything new is written in.</summary>
    private const int CodePageUtf8 = 65001;

    /// <summary>
    /// UTF-8 that refuses what it cannot read, which is how the bytes of a file are asked whether
    /// they are UTF-8 at all. The ordinary one hands back a question mark for every byte it cannot
    /// make sense of, which is exactly the answer that must not be settled for here.
    /// </summary>
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Registers the code pages a machine's own files may be written in. This is a static
    /// constructor rather than a field initializer on purpose: the runtime is allowed to leave a
    /// field initializer alone until something reads a field, so a macro whose first call on this
    /// class was <see cref="Resolve"/> — writing a GBK file before the action editor had ever been
    /// opened — would find the code pages missing. A static constructor runs before the first use
    /// of the type, and exactly once however many threads arrive at the same moment.
    /// </summary>
    static TextEncoding() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The names the action editor offers, in the order they are shown.</summary>
    public static IReadOnlyList<string> Names { get; } = ["utf8", "utf8bom", "gbk", "utf16"];

    /// <summary>
    /// The .NET encoding behind a name. The code pages are registered on the way in: .NET leaves
    /// GBK and its neighbours out unless it is asked for them, and a macro on a Chinese Windows is
    /// expected to read the files that machine already has.
    /// </summary>
    public static Encoding Resolve(string name)
    {
        switch (Canonical(name))
        {
            case "utf8bom":
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            case "gbk":
                try
                {
                    return Encoding.GetEncoding(CodePageGbk);
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException)
                {
                    throw new DeviceUnavailableException("the GBK encoding");
                }

            case "utf16":
                return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            case "system":
                return SystemCodePage();
            default:
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }

    /// <summary>
    /// The text of a file's bytes, read the way a person opening the file would: a byte-order mark
    /// says what the file is and beats whatever the step named, because a mark is something the
    /// file itself says. With no mark, UTF-8 is what is tried first, and bytes that cannot be UTF-8
    /// at all are read in this machine's own code page.
    /// </summary>
    /// <remarks>
    /// That last step is the one that matters. A Chinese Excel writes a CSV in GBK, and reading
    /// those bytes as UTF-8 turns every character into a question mark — a file nothing can be done
    /// with, rather than one that reads slightly wrong. The bytes have to be looked at to know, and
    /// they can only be looked at here.
    /// </remarks>
    public static string Read(byte[] bytes, string name)
    {
        if (Mark(bytes) is { } mark)
        {
            return mark.Text.GetString(bytes, mark.Length, bytes.Length - mark.Length);
        }

        var named = Resolve(name);
        if (named.CodePage != CodePageUtf8)
        {
            return named.GetString(bytes);
        }

        return Utf8(bytes) ? named.GetString(bytes) : SystemCodePage().GetString(bytes);
    }

    /// <summary>
    /// The mark at the front of the bytes and how long it is, or nothing when there is none. Which
    /// marks are looked for is the list the runtime itself looks for when it reads a file.
    /// </summary>
    private static (Encoding Text, int Length)? Mark(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 3);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 2);
        }

        return bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
            ? (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), 2)
            : null;
    }

    /// <summary>Whether these bytes are UTF-8, asked by decoding them and seeing if it goes.</summary>
    private static bool Utf8(byte[] bytes)
    {
        try
        {
            Strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whatever code page this machine's console programs write in. Windows calls it the ANSI code
    /// page: GBK on a Chinese Windows, 1252 on an English one, and the same one a console program
    /// switches to when its output goes into a pipe rather than to a screen.
    /// </summary>
    private static Encoding SystemCodePage()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            // A code page the runtime will not hand over is not worth failing a macro over: reading
            // the output as UTF-8 is what a macro that says nothing was read as before.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }

    /// <summary>
    /// Reads a name the way a person might have written it. Anything unrecognised is taken as
    /// plain UTF-8 rather than being refused, which is what an older macro that says nothing means.
    /// </summary>
    private static string Canonical(string name) => (name ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "utf8bom" or "utf-8-bom" or "utf8-bom" or "bom" => "utf8bom",
        "gbk" or "gb2312" or "gb18030" or "936" => "gbk",
        "system" or "ansi" or "default" => "system",
        "utf16" or "utf-16" or "unicode" or "utf16le" => "utf16",
        _ => "utf8",
    };
}
