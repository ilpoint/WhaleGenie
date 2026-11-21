using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WhaleGenie.Tests;

/// <summary>
/// Every page source is UTF-8. A file saved in the system code page can still build while its
/// comments are mojibake, and a Chinese sentence hidden in it would get past the page-text check;
/// this is the check that makes the encoding itself part of the contract.
/// </summary>
public class SourceEncodingTests
{
    [Fact]
    public void The_web_sources_are_valid_utf8()
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var offenders = new List<string>();

        foreach (var folder in new[] { "src", "tests", "e2e" })
        {
            var path = Path.Combine(RepoPaths.Root, "webui", folder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.ts", SearchOption.AllDirectories))
            {
                try
                {
                    _ = strict.GetString(File.ReadAllBytes(file));
                }
                catch (DecoderFallbackException)
                {
                    offenders.Add(Path.GetRelativePath(RepoPaths.Root, file));
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "these page sources are not UTF-8: " + string.Join(", ", offenders));
    }
}
