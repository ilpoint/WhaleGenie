using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Tests;

// Tables and files: the four tables, where a file step comes from, and what the manual lists.
public partial class ActionContractTests
{
    /// <summary>
    /// A table says so rather than listing columns it cannot know: the names come from the data,
    /// so a catalogue that wrote them down would be wrong the first time a sheet had other columns.
    /// The four actions whose result is a table are named here, so adding one is a decision.
    /// </summary>
    [Fact]
    public void A_table_is_a_list_and_the_four_tables_say_so()
    {
        string[] expected =
        [
            "browser.readTable.table", "file.readCsv.rows", "ocr.recognize.table", "uia.readTable.table",
        ];

        var found = new List<string>();
        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var result in action.Returns)
            {
                if (!result.Table)
                {
                    continue;
                }

                Assert.Equal(ActionReturnType.List, result.Type);
                Assert.Empty(result.Fields);
                found.Add($"{action.Key}.{result.Key}");
            }
        }

        Assert.Equal(expected, found.Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// The file family asks the same three questions in the same order, so a step that copies a
    /// file reads like one that writes one: where it comes from, where it goes, and what to do when
    /// the name is already there. The third question has one block of its own rather than a second
    /// name for the same idea, which is what a reader compares two file steps by.
    /// </summary>
    [Fact]
    public void A_file_step_says_where_it_comes_from_and_where_it_goes()
    {
        var wrong = new List<string>();
        foreach (var action in ActionSemanticsCatalog.All
                     .Where(action => action.Category is ActionCategory.File))
        {
            foreach (var parameter in action.Parameters)
            {
                var field = $"{action.Key}.{parameter.Name}";
                if (parameter.Group is "from" or "to")
                {
                    wrong.Add($"{field} stands in the {parameter.Group} block");
                }

                // A place is said in one of the two blocks the family shares: where the file comes
                // from, or where it goes. A path outside them reads as an ungrouped oddity.
                if (parameter.Name is "path" or "folder"
                    && parameter.Group is not ("" or "source" or "save"))
                {
                    wrong.Add($"{field} is a place, in the {parameter.Group} block");
                }

                if (parameter.Name is "writeMode" or "ifExists" or "overwrite"
                    && parameter.Group != "clash")
                {
                    wrong.Add($"{field} answers the existing file, in the {parameter.Group} block");
                }
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// Two steps that write a file look the same where it matters: one group of words for what to do
    /// with a file that is already there, and one encoding everywhere, in the reading or the writing
    /// block. A reader who learned one of them has learned the other.
    /// </summary>
    [Fact]
    public void The_file_family_answers_one_question_about_an_existing_file_the_same_way()
    {
        var files = ActionCatalog.Definitions
            .Where(definition => definition.Category is ActionCategory.File)
            .ToDictionary(definition => definition.Key, StringComparer.Ordinal);

        var text = files["file.writeText"].Parameters
            .First(parameter => parameter.Name == "writeMode");
        var csv = files["file.writeCsv"].Parameters
            .First(parameter => parameter.Name == "writeMode");
        Assert.Equal(
            text.OptionChoices.Select(option => option.Display).ToList(),
            csv.OptionChoices.Select(option => option.Display).ToList());

        var encodings = files.Values
            .SelectMany(definition => definition.Parameters
                .Where(parameter => parameter.Name == "encoding")
                .Select(parameter => (definition.Key, Parameter: parameter)))
            .ToList();
        Assert.NotEmpty(encodings);

        var first = encodings[0].Parameter.Semantics;
        foreach (var (key, parameter) in encodings)
        {
            Assert.Equal(first.Options, parameter.Semantics.Options);
            Assert.Equal(first.DefaultValue, parameter.Semantics.DefaultValue);
            Assert.True(parameter.Group is "reading" or "writing",
                $"{key}.encoding stands in the {parameter.Group} block");
        }
    }

    /// <summary>
    /// The manual's record table is what a user reads a field name off, so it has to name exactly
    /// the fields the catalogue declares — no field the engine writes is missing, and no field the
    /// manual promises is gone.
    /// </summary>
    [Fact]
    public void The_manual_lists_exactly_the_record_fields_the_catalogue_declares()
    {
        var declared = ActionSemanticsCatalog.All
            .SelectMany(action => action.Returns)
            .SelectMany(result => result.Fields)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

        var manual = ManualDocument.AllLines();
        var documented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in manual.Where(line =>
                     line.StartsWith("| 命中（", StringComparison.Ordinal)
                     || line.StartsWith("| 截图（", StringComparison.Ordinal)
                     || line.StartsWith("| 窗口（", StringComparison.Ordinal)
                     || line.StartsWith("| 显示器（", StringComparison.Ordinal)
                     || line.StartsWith("| 程序（", StringComparison.Ordinal)
                     || line.StartsWith("| 元素（", StringComparison.Ordinal)
                     || line.StartsWith("| 点（", StringComparison.Ordinal)
                     || line.StartsWith("| 运行句柄（", StringComparison.Ordinal)))
        {
            var cells = line.Split('|');
            if (cells.Length < 3)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(cells[2], "`([^`]+)`"))
            {
                documented.Add(match.Groups[1].Value);
            }
        }

        Assert.NotEmpty(documented);
        Assert.Equal(declared.Order(StringComparer.Ordinal),
            documented.Order(StringComparer.Ordinal));
    }
}
