using System;
using System.IO;
using WhaleGenie.ContractsGen;

var output = args.Length > 0
    ? args[0]
    : Path.Combine("webui", "src", "shared", "bridge", "generated", "types.ts");
var full = Path.GetFullPath(output);
Directory.CreateDirectory(Path.GetDirectoryName(full)!);
File.WriteAllText(full, ContractGenerator.Generate());
Console.WriteLine($"wrote {full}");
