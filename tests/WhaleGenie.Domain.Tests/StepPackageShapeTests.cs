using System;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// The package is the copy of the work that outlives the program. A region that names a variable
/// and a window rule held in one have to come back as the shapes they were, not as the lines they
/// happen to read as.
/// </summary>
public class StepPackageShapeTests
{
    [Fact]
    public void A_package_keeps_the_shapes_a_region_and_a_window_rule_were_written_as()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "shapes.wgmacro");
            var region = new StepParameter
            {
                Name = "region",
                Kind = ActionParameterKind.Region,
                Rows =
                [
                    Row(("text", Formula.Name("区域")),
                        ("x", Formula.Binary("+", Formula.Name("x"), Formula.Number(10))),
                        ("y", Formula.Number(20)),
                        ("width", Formula.Number(30)),
                        ("height", Formula.Number(40))),
                ],
            };
            var window = new StepParameter
            {
                Name = "window",
                Kind = ActionParameterKind.WindowRule,
                Written = Formula.Name("窗口"),
            };
            var images = new StepParameter
            {
                Name = "images",
                Kind = ActionParameterKind.Images,
                Rows = [Row(("image", Formula.Name("picture")))],
            };
            var macro = new MacroItem { Name = "shapes" };
            macro.Steps.Add(new MacroStep
            {
                Type = "vision.findImage",
                Parameters = [region, window, images],
            });

            MacroPackage.Save(path, [macro], new ProjectTriggers(), [], "test");
            var loaded = MacroPackage.Load(path).Macros.Single().Steps.Single();

            var regionRow = loaded.Parameters.First(parameter => parameter.Name == "region")
                .Rows.Single();
            Assert.IsType<VariableNode>(regionRow.Cell("text"));
            Assert.IsType<BinaryNode>(regionRow.Cell("x"));

            Assert.IsType<VariableNode>(
                loaded.Parameters.First(parameter => parameter.Name == "window").Written);
            Assert.IsType<VariableNode>(
                loaded.Parameters.First(parameter => parameter.Name == "images").Rows.Single()
                    .Cell("image"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static StepParameterRow Row(params (string Column, ExpressionNode Value)[] cells)
    {
        var row = new StepParameterRow();
        foreach (var (column, value) in cells)
        {
            row.Set(column, value);
        }

        return row;
    }
}
