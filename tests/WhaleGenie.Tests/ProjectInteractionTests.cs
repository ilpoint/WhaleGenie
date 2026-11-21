using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The import report is the one piece of the shared interaction that is pure text: both the page
/// and the native file menu show it, so a change here must be visible to the test rather than only
/// to a person clicking through a package import.
/// </summary>
public class ProjectInteractionTests
{
    [Fact]
    public void An_empty_import_says_nothing_changed()
    {
        var report = new ImportReport([], [], [], [], [], [], [], []);

        Assert.Equal(Strings.Get("Import.ReportNothing"), ProjectInteraction.ReportText(report));
    }

    [Fact]
    public void An_import_report_names_what_arrived_and_what_was_renamed()
    {
        var report = new ImportReport(
            [new MacroItem { Name = "arrived" }],
            [("old", "renamed")],
            ["global"],
            [],
            [],
            [],
            [],
            []);

        var text = ProjectInteraction.ReportText(report);

        Assert.Contains("arrived", text, StringComparison.Ordinal);
        Assert.Contains("old", text, StringComparison.Ordinal);
        Assert.Contains("renamed", text, StringComparison.Ordinal);
        Assert.Contains("global", text, StringComparison.Ordinal);
    }
}
