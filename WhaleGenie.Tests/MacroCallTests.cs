using System;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// What a macro says about being called: the values it asks a call to fill in, and the names it
/// offers to hand back. The call site, the design-time check and the engine all read this, so it
/// has to survive the file exactly as it was declared.
/// </summary>
public class MacroCallTests
{
    private static MacroItem Declaring()
    {
        var macro = new MacroItem { Name = "整理一下" };
        macro.Parameters.Add(new MacroParameter
        {
            Name = "次数",
            Default = "3",
            Required = false,
            Description = "要做多少遍。",
        });
        macro.Parameters.Add(new MacroParameter
        {
            Name = "标签",
            Required = true,
            Description = "写在每一项前面的文字。",
        });
        macro.Results.Add(new MacroResult
        {
            Name = "答案",
            Description = "整理完留下的数。",
            Type = ActionReturnType.Number,
        });
        return macro;
    }

    [Fact]
    public void A_declared_parameter_keeps_its_default_and_whether_it_is_required()
    {
        var callable = MacroItem.FromJson(Declaring().ToJson()).Callable;

        var optional = Assert.Single(callable.Parameters, parameter => parameter.Name == "次数");
        Assert.Equal("3", optional.Default);
        Assert.False(optional.Required);
        Assert.Equal("要做多少遍。", optional.Description);

        var wanted = Assert.Single(callable.Parameters, parameter => parameter.Name == "标签");
        Assert.True(wanted.Required);
        Assert.Equal(string.Empty, wanted.Default);
    }

    [Fact]
    public void A_declared_result_keeps_what_it_is()
    {
        var callable = MacroItem.FromJson(Declaring().ToJson()).Callable;

        var result = Assert.Single(callable.Results);
        Assert.Equal("答案", result.Name);
        Assert.Equal("整理完留下的数。", result.Description);
        Assert.Equal(ActionReturnType.Number, result.Type);
    }

    [Fact]
    public void A_macro_that_declares_nothing_asks_for_nothing()
    {
        var callable = MacroItem.FromJson(new MacroItem { Name = "空手" }.ToJson()).Callable;

        Assert.Empty(callable.Parameters);
        Assert.Empty(callable.Results);
        Assert.Empty(callable.Signature.Arguments);
        Assert.Empty(callable.Signature.Results);
    }

    [Fact]
    public void The_declaration_survives_a_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "macros" + MacroPackage.Extension);

        try
        {
            MacroPackage.Save(path, [Declaring()], [], "0.0.0");
            var read = MacroPackage.Load(path).Macros.Single();

            Assert.Equal("次数", read.Parameters[0].Name);
            Assert.False(read.Parameters[0].Required);
            Assert.Equal("答案", read.Results[0].Name);
            Assert.Equal(ActionReturnType.Number, read.Results[0].Type);
            Assert.Equal(["次数", "标签"], read.Callable.Signature.Arguments.Select(a => a.Name));
            Assert.Equal(["答案"], read.Callable.Signature.Results);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
