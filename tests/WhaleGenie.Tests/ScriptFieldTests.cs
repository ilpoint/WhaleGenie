using System;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Tests.Support;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// A script field starts from the skeleton its language names. The example is the language's
/// documentation as much as it is a default, so an empty field is a regression, not a preference.
/// </summary>
public class ScriptFieldTests
{
    [Theory]
    [InlineData("script.python", "WhaleGenie.Print")]
    [InlineData("script.csharp", "class Program")]
    [InlineData("script.ahk", "Print(\"Hello World\")")]
    public void A_script_field_starts_from_its_language_example(string action, string marker)
    {
        Ui.Run(() =>
        {
            var viewModel = new AddActionViewModel(ActionCatalog.Definitions, [], []);
            viewModel.SelectAction(action);
            var field = viewModel.Parameters.First(parameter => parameter.Definition.Name == "script");

            Assert.Contains(marker, field.DisplayLine, StringComparison.Ordinal);
            Assert.Equal(field.Definition.LocalDefault,
                Assert.IsType<TextNode>(field.CurrentShape).Text);
        });
    }
}
