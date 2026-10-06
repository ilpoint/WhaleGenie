using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Viktor.Core.Devices;

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
