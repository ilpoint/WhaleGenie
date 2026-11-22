using System;
using System.IO;
using WhaleGenie.ContractsGen;

var target = args.Length > 0
    ? args[0]
    : Path.Combine("webui", "src", "shared", "bridge", "generated");
var full = Path.GetFullPath(target);
var directory = Path.HasExtension(full) ? Path.GetDirectoryName(full)! : full;
Directory.CreateDirectory(directory);
File.WriteAllText(Path.Combine(directory, "types.ts"), ContractGenerator.Generate());
File.WriteAllText(Path.Combine(directory, "methods.ts"), ContractGenerator.GenerateMethods());
Console.WriteLine($"wrote {directory}");
