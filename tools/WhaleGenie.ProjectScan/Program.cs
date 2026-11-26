using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Storage;

if (args.Length == 0)
{
    Console.Error.WriteLine("用法：dotnet run --project tools/WhaleGenie.ProjectScan -- <项目包>.wgmacro [...]");
    return 2;
}

var scanned = 0;
var suspects = 0;
foreach (var path in args)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine("找不到：" + path);
        continue;
    }

    try
    {
        var contents = MacroPackage.Preview(path);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in contents.Globals)
        {
            names.Add(variable.Name);
        }

        foreach (var macro in contents.Macros.Concat(contents.Library.Select(entry => entry.Macro)))
        {
            ScanMacro(path, macro, names, ref scanned, ref suspects);
        }
    }
    catch (Exception error)
    {
        Console.Error.WriteLine(path + "：" + error.Message);
    }
}

Console.WriteLine($"扫描 {scanned} 个宏，{suspects} 处可疑");
return 0;

static void ScanMacro(string path, MacroItem macro, HashSet<string> names, ref int scanned,
    ref int suspects)
{
    foreach (var local in macro.Locals)
    {
        names.Add(local.Name);
    }

    foreach (var parameter in macro.Parameters)
    {
        names.Add(parameter.Name);
    }

    foreach (var result in macro.Results)
    {
        names.Add(result.Name);
    }

    scanned++;
    Walk(path, macro.Name, macro.Steps, names, ref suspects);
    foreach (var child in macro.SubMacros)
    {
        ScanMacro(path, child, names, ref scanned, ref suspects);
    }
}

static void Walk(string path, string macro, IReadOnlyList<MacroStep> steps, HashSet<string> names,
    ref int suspects)
{
    foreach (var step in steps)
    {
        foreach (var parameter in step.Parameters)
        {
            if (parameter.Kind == ActionParameterKind.Steps)
            {
                Walk(path, macro, parameter.Steps, names, ref suspects);
                continue;
            }

            if (parameter.Kind == ActionParameterKind.Region)
            {
                for (var index = 0; index < parameter.Rows.Count; index++)
                {
                    var row = parameter.Rows[index];
                    var text = row.Cell("text");
                    if (text is TextNode { Text.Length: > 0 } written
                        && !LooksLikeRectangle(written.Text))
                    {
                        Report(path, macro, step, parameter.Name, index, "text", written.Text,
                            names.Contains(written.Text.Trim())
                                ? "看起来是变量名，但存成了文本"
                                : "区域的名字存成了文本；引擎会把它当矩形读");
                        suspects++;
                    }

                    foreach (var column in new[] { "x", "y", "width", "height" })
                    {
                        if (row.Cell(column) is TextNode { Text.Length: > 0 } cell
                            && !double.TryParse(cell.Text, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out _))
                        {
                            Report(path, macro, step, parameter.Name, index, column, cell.Text,
                                "数字格不是数字，公式或变量可能被压成了文本");
                            suspects++;
                        }
                    }
                }

                continue;
            }

            if (parameter.Kind is ActionParameterKind.Arguments or ActionParameterKind.Images
                or ActionParameterKind.Narrow)
            {
                var column = parameter.Kind switch
                {
                    ActionParameterKind.Arguments => FieldNames.Value,
                    ActionParameterKind.Images => "image",
                    _ => FieldNames.Value,
                };
                for (var index = 0; index < parameter.Rows.Count; index++)
                {
                    if (parameter.Rows[index].Cell(column) is TextNode { Text.Length: > 0 } cell
                        && names.Contains(cell.Text.Trim()))
                    {
                        Report(path, macro, step, parameter.Name, index, column, cell.Text,
                            "值看起来是变量名，但存成了文本");
                        suspects++;
                    }
                }
            }
        }
    }
}

static void Report(string path, string macro, MacroStep step, string parameter, int index,
    string column, string text, string reason)
    => Console.WriteLine(
        $"{Path.GetFileName(path)} | {macro} | {step.Type} | {parameter}[{index}].{column}"
        + $" | {text} | {reason}");

static bool LooksLikeRectangle(string text)
{
    var places = text.Split([';', '\n', '\r'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return places.Length > 0 && places.All(place =>
    {
        var cells = place.Split(',', StringSplitOptions.TrimEntries);
        return cells.Length == 4 && cells.All(cell =>
            double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _));
    });
}
