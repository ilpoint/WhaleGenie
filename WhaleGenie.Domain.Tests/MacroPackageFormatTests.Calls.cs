using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Domain.Tests;

// What a call passes, and the fields a record argument declares.
public partial class MacroPackageFormatTests
{
    /// <summary>
    /// What a call passes is written as rows, so a saved package has to bring those rows back: the
    /// values, and the mark that says an empty one is meant rather than left out. Losing either
    /// would change what the call does without saying anything.
    /// </summary>
    [Fact]
    public void A_call_keeps_what_it_passes_through_a_saved_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "call.wgmacro");
            var call = new MacroStep
            {
                Type = "control.runMacro",
                Id = "aaaa",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = FieldNames.Macro,
                        Kind = ActionParameterKind.Macro,
                        Written = Fields.Shape("整理"),
                    },
                    new StepParameter
                    {
                        Name = FieldNames.Arguments,
                        Kind = ActionParameterKind.Arguments,
                        Rows =
                        [
                            Row(("name", "次数"), ("value", "n")),
                            Row(("name", "标签"), ("value", string.Empty), ("given", "true")),
                        ],
                    },
                ],
            };

            var macro = new MacroItem { Name = "主" };
            macro.Steps.Add(call);
            MacroPackage.Save(path, [macro], new ProjectTriggers(), [], "test");

            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var again = Assert.Single(loaded.Steps);
            var rows = Assert.Single(again.Parameters,
                parameter => parameter.Name == FieldNames.Arguments).Rows;
            Assert.Equal(2, rows.Count);
            Assert.Equal("次数", rows[0].Text(FieldNames.Name));
            Assert.Equal("n", rows[0].Text(FieldNames.Value));
            Assert.Equal("标签", rows[1].Text(FieldNames.Name));
            Assert.Equal(string.Empty, rows[1].Text(FieldNames.Value));
            Assert.Equal("true", rows[1].Text(FieldNames.Given));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// A record argument may say which fields it holds, and a package has to bring them back: the
    /// names are what `record.field` is checked against, so losing them would make a checked macro
    /// an unchecked one after a save.
    /// </summary>
    [Fact]
    public void A_record_argument_keeps_the_fields_it_declares()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "record.wgmacro");
            var macro = new MacroItem { Name = "主" };
            macro.Parameters.Add(new MacroParameter
            {
                Name = "点",
                Type = ValueKind.Record,
                Required = true,
                Fields =
                [
                    new MacroField { Name = "x", Type = ValueKind.Number },
                    new MacroField { Name = "y", Type = ValueKind.Number },
                ],
            });
            macro.Results.Add(new MacroResult
            {
                Name = "框",
                Type = ValueKind.Record,
                Fields = [new MacroField { Name = "left", Type = ValueKind.Number }],
            });

            MacroPackage.Save(path, [macro], new ProjectTriggers(), [], "test");

            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var argument = Assert.Single(loaded.Parameters);
            Assert.Equal(["x", "y"], argument.Fields.Select(field => field.Name));
            Assert.All(argument.Fields, field => Assert.Equal(ValueKind.Number, field.Type));

            var result = Assert.Single(loaded.Results);
            Assert.Equal(["left"], result.Fields.Select(field => field.Name));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
