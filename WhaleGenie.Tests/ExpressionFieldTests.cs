using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// Which fields are handed the expression editor, and which are not. A field the engine reads as
/// one value may hold a variable or a formula, so the editor belongs beside it; a field that is a
/// path, a selector, a picture, a name or a list of its own is not that, and putting the editor
/// there would offer something the engine never looks at.
///
/// The two together are the rule: the list below is every field the engine reads as one value, and
/// the second check is every field it does not, so a field added later has to be decided on rather
/// than falling in by accident. The note the step carries (<c>MetaComment</c>) is not a parameter
/// at all, so it is out of reach of both.
/// </summary>
public class ExpressionFieldTests
{
    private static readonly Dictionary<string, string[]> ValueFields = new(StringComparer.Ordinal)
    {
        ["control.switch"] = ["value"],
        ["control.stop"] = ["reason"],
        ["control.log"] = ["message"],
        ["file.writeText"] = ["text"],
        ["file.appendLog"] = ["text"],
        ["file.readJson"] = ["query"],
        ["file.writeJson"] = ["query", "value"],
        ["file.readCsv"] = ["matchValue"],
        ["file.writeCsv"] = ["rows"],
        ["data.base64Encode"] = ["text"],
        ["data.base64Decode"] = ["text"],
        ["data.hash"] = ["text"],
        ["clipboard.writeText"] = ["text"],
        ["clipboard.writeFiles"] = ["files"],
        ["clipboard.paste"] = ["text"],
        ["process.start"] = ["arguments"],
        ["process.waitFor"] = ["name"],
        ["process.waitExit"] = ["id"],
        ["process.exists"] = ["name"],
        ["process.info"] = ["target"],
        ["process.kill"] = ["target"],
        ["command.run"] = ["arguments"],
        ["system.environment"] = ["name"],
        ["system.notify"] = ["heading", "message"],
        ["system.ime"] = ["layout"],
        ["window.list"] = ["filter"],
        ["input.typeText"] = ["text"],
        ["vision.findImage"] = ["ignoreColor"],
        ["vision.waitImage"] = ["ignoreColor"],
        ["vision.clickImage"] = ["ignoreColor"],
        ["vision.waitColor"] = ["color"],
        ["vision.findColor"] = ["color"],
        ["vision.findColors"] = ["color"],
        ["ocr.recognize"] = ["colorFilter"],
        ["ocr.findText"] = ["text", "colorFilter"],
        ["ocr.clickText"] = ["text", "colorFilter"],
        ["uia.setText"] = ["text"],
        ["uia.select"] = ["item"],
        ["browser.open"] = ["url"],
        ["browser.goTo"] = ["url"],
        ["browser.fill"] = ["text"],
        ["browser.switchTab"] = ["match"],
        ["condition.textExists"] = ["text"],
        ["condition.textNotExists"] = ["text"],
        ["condition.colorEquals"] = ["color"],
        ["condition.colorsExist"] = ["color"],
        ["condition.colorCoverage"] = ["color"],
        ["condition.compare"] = ["value"],
        ["condition.listContains"] = ["list", "value"],
        ["condition.processRunning"] = ["name"],
        ["condition.windowExists"] = ["value"],
        ["condition.valueInRange"] = ["value", "min", "max"],
        ["condition.dateCompare"] = ["left", "right"],
    };

    [Fact]
    public void The_fields_that_hold_one_value_offer_the_expression_editor()
    {
        var missing = new List<string>();
        var undecided = new List<string>();

        foreach (var action in ActionCatalog.Definitions)
        {
            foreach (var parameter in action.Parameters)
            {
                var listed = ValueFields.TryGetValue(action.Key, out var names)
                    && names.Contains(parameter.Name, StringComparer.Ordinal);

                if (listed && !parameter.AcceptsFormula)
                {
                    missing.Add($"{action.Key}.{parameter.Name}");
                }
                else if (!listed && parameter.AcceptsFormula)
                {
                    undecided.Add($"{action.Key}.{parameter.Name}");
                }
            }
        }

        Assert.Empty(missing);
        Assert.Empty(undecided);
    }

    [Fact]
    public void A_field_that_is_not_one_value_offers_no_expression_editor()
    {
        var wrong = new List<string>();
        var matched = new Dictionary<string, int>(StringComparer.Ordinal);

        void Count(string rule, bool hit)
        {
            if (hit)
            {
                matched[rule] = matched.GetValueOrDefault(rule) + 1;
            }
        }

        foreach (var action in ActionCatalog.Definitions)
        {
            foreach (var parameter in action.Parameters)
            {
                // A path is picked from a dialog; a selector is taken off the screen; a picture is
                // browsed for or grabbed; a key is pressed on the on-screen keyboard; a rectangle
                // is dragged out and its four numbers are dialled in one page over.
                var isPath = parameter.PathIntent is not PathIntent.None;
                var isForeign = parameter.ForeignText;
                var isName = parameter.NamesVariable;
                var isKey = parameter.Kind
                    is ActionParameterKind.Key or ActionParameterKind.Keys
                    or ActionParameterKind.KeySequence;
                var isPicture = parameter.Kind
                    is ActionParameterKind.Image or ActionParameterKind.Images;
                var isArea = parameter.Kind is ActionParameterKind.Region;
                var isSelector = parameter.Name is "selector" or "anchorSelector"
                    || (parameter.Name == "target"
                        && parameter.OwnerKey.StartsWith("browser.", StringComparison.Ordinal));

                Count("path", isPath);
                Count("foreign", isForeign);
                Count("name", isName);
                Count("key", isKey);
                Count("picture", isPicture);
                Count("area", isArea);
                Count("selector", isSelector);

                if ((isPath || isForeign || isName || isKey || isPicture || isArea || isSelector)
                    && parameter.AcceptsFormula)
                {
                    wrong.Add($"{action.Key}.{parameter.Name}");
                }
            }
        }

        Assert.Empty(wrong);

        // A rule that nothing in the catalogue fits proves nothing about the catalogue.
        Assert.Equal(
            ["area", "foreign", "key", "name", "path", "picture", "selector"],
            matched.Keys.Order(StringComparer.Ordinal));
    }
}
