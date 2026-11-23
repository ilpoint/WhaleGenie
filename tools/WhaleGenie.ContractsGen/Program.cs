using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using WhaleGenie.ContractsGen;
using WhaleGenie.Localization;

var target = args.Length > 0
    ? args[0]
    : Path.Combine("webui", "src", "shared", "bridge", "generated");
var full = Path.GetFullPath(target);
var directory = Path.HasExtension(full) ? Path.GetDirectoryName(full)! : full;
Directory.CreateDirectory(directory);
File.WriteAllText(Path.Combine(directory, "types.ts"), ContractGenerator.Generate());
File.WriteAllText(Path.Combine(directory, "methods.ts"), ContractGenerator.GenerateMethods());

// The browser mock shows the same words the program does. Reading the one table keeps a copied
// word list from drifting; only the words the page actually names are emitted, so the production
// bundle does not carry the whole table.
var webSrc = Path.Combine(directory, "..", "..", "..");
var used = UsedKeys(Path.GetFullPath(webSrc));
var languages = new
{
    en = Filtered(Strings.English, used),
    zh = Filtered(Strings.Chinese, used),
};
File.WriteAllText(Path.Combine(directory, "strings.json"), JsonSerializer.Serialize(languages,
    new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }) + "\n");
Console.WriteLine($"wrote {directory}");

static SortedDictionary<string, string> Filtered(Dictionary<string, string> table, HashSet<string> used)
{
    var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var (key, value) in table)
    {
        if (used.Contains(key))
        {
            copy[key] = value;
        }
    }

    return copy;
}

// Every dotted name the page mentions, however it is quoted. The mock only has to answer the
// words the page asks for; the real host still sends the whole table.
static HashSet<string> UsedKeys(string sourceFolder)
{
    var keys = new HashSet<string>(StringComparer.Ordinal);
    if (!Directory.Exists(sourceFolder))
    {
        return keys;
    }

    foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.ts", SearchOption.AllDirectories))
    {
        if (file.Contains(Path.Combine("bridge", "generated"), StringComparison.Ordinal))
        {
            continue;
        }

        var text = File.ReadAllText(file);
        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(text,
                     @"[""']([A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)[""']"))
        {
            keys.Add(match.Groups[1].Value);
        }
    }

    return keys;
}
