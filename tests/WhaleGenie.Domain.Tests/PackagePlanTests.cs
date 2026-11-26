using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using Xunit;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// The package plan is what the archive will contain, decided before a zip is opened. These cases
/// use a dictionary instead of the file system: a source that does not exist on disk is enough.
/// </summary>
public class PackagePlanTests
{
    [Fact]
    public void A_plan_rewrites_and_collects_an_asset_without_reading_the_file()
    {
        var saved = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var readme = new DateTime(2026, 1, 2, 4, 4, 5, DateTimeKind.Local);
        var plan = MacroPackage.BuildPlan("test", saved, readme,
            [Picture("assets/ok.png")], new ProjectTriggers(), [], true, null,
            value => value == "assets/ok.png" ? @"C:\not-there\ok.png" : null);

        var asset = Assert.Single(plan.Assets);
        Assert.Equal("assets/ok.png", asset.EntryName);
        Assert.Equal(@"C:\not-there\ok.png", asset.SourcePath);

        var node = Assert.Single(plan.Entries,
            entry => entry.Name.StartsWith("nodes/", StringComparison.Ordinal));
        Assert.Contains("assets/ok.png", node.Content);
        Assert.DoesNotContain(@"C:\not-there", node.Content);

        var manifest = Assert.Single(plan.Entries, entry => entry.Name == "manifest.json");
        var manifestJson = JsonNode.Parse(manifest.Content)!.AsObject();
        Assert.Contains("assets/ok.png",
            manifestJson["assets"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal(saved.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            manifestJson["savedUtc"]!.GetValue<string>());

        var readmeEntry = Assert.Single(plan.Entries, entry => entry.Name == "README.md");
        Assert.Contains("assets/ok.png", readmeEntry.Content);
    }

    [Fact]
    public void The_same_picture_is_collected_once_and_two_with_one_name_are_kept_apart()
    {
        var same = Picture("assets/a.png", "look-a");
        var again = Picture("a.png", "look-b");
        var apart = Picture("assets/b.png", "look-c");
        var plan = MacroPackage.BuildPlan("test", DateTime.UtcNow, DateTime.Now,
            [same, again, apart], new ProjectTriggers(), [], true, null, value => value switch
            {
                "assets/a.png" or "a.png" => @"C:\one\a.png",
                "assets/b.png" => @"C:\two\a.png",
                _ => null,
            });

        Assert.Equal(["assets/a.png", "assets/a-2.png"],
            plan.Assets.Select(asset => asset.EntryName));
    }

    private static MacroItem Picture(string picture, string name = "look-test")
    {
        var macro = new MacroItem { Name = name };
        macro.Steps.Add(new MacroStep
        {
            Type = "vision.findImage",
            Parameters =
            [
                new StepParameter
                {
                    Name = "images",
                    Kind = ActionParameterKind.Images,
                    Rows = [StepParameterRow.Of("image", Formula.Text(picture))],
                },
            ],
        });
        return macro;
    }
}
