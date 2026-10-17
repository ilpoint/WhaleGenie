using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// What the interface says about the variables a macro may not change. A system variable is one
/// of those, and the answer to "why can I not store into it" is a sentence the user has to notice
/// rather than a grey line they scroll past — so it is written in red, and only when there really
/// is something to fix.
/// </summary>
public class VariableWarningTests
{
    [Fact]
    public void Answering_why_a_step_cannot_be_saved_is_a_problem_not_a_note()
    {
        Ui.Run(() =>
        {
            var viewModel = new AddActionViewModel(ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("count"), []);

            // Nothing picked yet is the state the dialog opens in, and nobody has done anything
            // wrong, so the line is a plain note.
            Assert.Equal(Strings.Get("Add.SelectFirst"), viewModel.ValidationMessage);
            Assert.False(viewModel.ValidationIsProblem);

            viewModel.SelectAction("control.setVariable");
            viewModel.Parameters.First(parameter => parameter.Definition.Name == "name").Text =
                "sys.clipboard";

            // A name the macro may only read is a mistake to fix.
            Assert.Equal(Strings.Format("Add.SystemReadOnly", "sys.clipboard"),
                viewModel.ValidationMessage);
            Assert.True(viewModel.ValidationIsProblem);
            Assert.False(viewModel.CanSave);
        });
    }

    [Fact]
    public void A_name_a_macro_may_not_write_is_a_problem_wherever_it_is_named()
    {
        Ui.Run(() =>
        {
            // Naming a variable to write is the same question in every action that does it, so the
            // answer may not depend on which one the user happens to be in: the counter of a loop
            // and the item of "for each" are asked the same way "Set Variable" is.
            foreach (var (key, field) in new[]
                     {
                         ("control.setVariable", "name"),
                         ("control.for", "name"),
                         ("control.forEach", "itemVariable"),
                         ("control.forEach", "indexVariable"),
                     })
            {
                var viewModel = new AddActionViewModel(ActionCatalog.Definitions,
                    VariableChoicesForChecks.Named("count"), []);
                viewModel.SelectAction(key);
                viewModel.Parameters.First(parameter => parameter.Definition.Name == field).Text =
                    "sys.date";

                Assert.False(viewModel.CanSave, $"{key}.{field} lets a macro write what it only reads");
                Assert.Equal(Strings.Format("Add.SystemReadOnly", "sys.date"),
                    viewModel.ValidationMessage);
            }

            // Reading one is not: that is what those values are for.
            var reading = new AddActionViewModel(ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("count"), []);
            reading.SelectAction("condition.dateCompare");
            reading.Parameters.First(parameter => parameter.Definition.Name == "left").Text =
                "$sys.dateTime";
            reading.Parameters.First(parameter => parameter.Definition.Name == "right").Text =
                "2026-01-01";

            Assert.True(reading.CanSave, reading.ValidationMessage);
        });
    }

    [Fact]
    public void The_read_only_lines_are_written_in_red()
    {
        // The dialog that refuses to store into one, and the panel that lists them.
        AssertRed(Path.Combine("WhaleGenie", "Views", "AddActionWindow.axaml"),
            "Text=\"{Binding ValidationMessage}\"");

        // The lines that list the variables a macro may not change live in the run-and-debug window.
        AssertRed(Path.Combine("WhaleGenie", "Views", "RunWindow.axaml"),
            "Variable.SystemHint");
    }

    /// <summary>
    /// Asserts that the element holding <paramref name="binding"/> wears the red the rest of the
    /// interface uses for something that went wrong.
    /// </summary>
    private static void AssertRed(string path, string binding)
    {
        var markup = File.ReadAllText(Path.Combine(Repository(), path));

        var red = 0;
        while ((red = markup.IndexOf("Foreground=\"#E06C75\"", red, StringComparison.Ordinal)) >= 0)
        {
            var window = markup[red..Math.Min(markup.Length, red + 200)];
            if (window.Contains(binding, StringComparison.Ordinal))
            {
                return;
            }

            red += 1;
        }

        Assert.Fail($"{path} no longer says {binding} in red");
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
