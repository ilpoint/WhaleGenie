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
        ["control.switch"] = ["switchValue"],
        ["control.stop"] = ["reason"],
        ["control.fail"] = ["reason"],
        ["control.log"] = ["message"],
        ["control.ask"] = ["message", "detail"],
        ["control.input"] = ["message", "default"],
        ["file.writeText"] = ["fileContent"],
        ["file.appendLog"] = ["logLine"],
        ["file.readJson"] = ["query"],
        ["file.writeJson"] = ["query", "writeValue"],
        ["file.readCsv"] = ["matchValue"],
        ["file.writeCsv"] = ["rows"],
        ["data.base64Encode"] = ["dataText"],
        ["data.base64Decode"] = ["dataText"],
        ["data.hash"] = ["dataText"],
        ["clipboard.writeText"] = ["clipboardText"],
        ["clipboard.writeFiles"] = ["files"],
        ["clipboard.paste"] = ["pasteText"],
        ["process.start"] = ["commandArguments"],
        ["process.waitFor"] = ["processName"],
        ["process.waitExit"] = ["id"],
        ["process.exists"] = ["processName"],
        ["process.info"] = ["processTarget"],
        ["process.kill"] = ["processTarget"],
        ["command.run"] = ["commandArguments"],
        ["system.environment"] = ["environmentName"],
        ["system.notify"] = ["heading", "notificationText"],
        ["system.ime"] = ["layout"],
        ["input.typeText"] = ["typedText"],
        ["input.mouseMove"] = ["place"],
        ["input.mouseClick"] = ["place"],
        ["input.mouseDoubleClick"] = ["place"],
        ["input.mouseDown"] = ["place"],
        ["input.mouseUp"] = ["place"],
        ["input.mouseScroll"] = ["place"],
        ["input.mouseDrag"] = ["startPlace", "endPlace"],
        ["vision.waitColor"] = ["place"],
        ["vision.getPixel"] = ["place"],
        ["vision.findColor"] = ["place"],
        ["vision.findColors"] = ["place"],
        ["vision.capture"] = ["rect"],
        ["ocr.recognize"] = ["rect"],
        ["window.move"] = ["rect"],
        ["ocr.findText"] = ["queryText"],
        ["ocr.clickText"] = ["queryText"],
        ["uia.setText"] = ["valueText"],
        ["uia.select"] = ["item"],
        ["browser.open"] = ["url"],
        ["browser.goTo"] = ["url"],
        ["browser.fill"] = ["fillText"],
        ["browser.select"] = ["optionValue"],
        ["browser.switchTab"] = ["tabMatch"],
        ["browser.waitForUrl"] = ["urlMatch"],
        ["browser.dialog"] = ["dialogText"],
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
                // A number, an expression and a colour have editors of their own that already
                // write the whole value as a formula; asking the catalogue to offer the builder
                // again would draw the same question twice.
                if (parameter.Kind is ActionParameterKind.Number
                    or ActionParameterKind.Expression or ActionParameterKind.Color)
                {
                    Assert.Equal(WhaleGenie.Core.Execution.ActionReadMode.WholeValue,
                        parameter.ReadMode);
                    continue;
                }

                var listed = ValueFields.TryGetValue(action.Key, out var names)
                    && names.Contains(parameter.Name, StringComparer.Ordinal);

                if (parameter.Kind is ActionParameterKind.WindowRule)
                {
                    if (listed)
                    {
                        undecided.Add($"{action.Key}.{parameter.Name} is a window rule");
                    }

                    continue;
                }

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
                // A single path is picked from a dialog; a selector is taken off the screen; a
                // picture is browsed for or grabbed; a key is pressed on the on-screen keyboard;
                // a rectangle is dragged out and its four numbers are dialled in one page over.
                // A list of files is not one path: it is a whole value that may be a list of paths
                // or a variable holding one, so the expression editor stays available beside the
                // multi-file picker.
                var isPath = parameter.PathIntent
                    is PathIntent.Read or PathIntent.Write or PathIntent.Folder;
                var isForeign = parameter.ForeignText;
                var isName = parameter.NamesVariable;
                var isKey = parameter.Kind
                    is ActionParameterKind.Key or ActionParameterKind.Keys
                    or ActionParameterKind.KeySequence;
                var isPicture = parameter.Kind
                    is ActionParameterKind.Image or ActionParameterKind.Images;
                var isArea = parameter.Kind is ActionParameterKind.Region;
                var isWindowRule = parameter.Kind is ActionParameterKind.WindowRule;
                var isSelector = parameter.Name is "uiSelector"
                    || (parameter.Name == "pageSelector"
                        && parameter.OwnerKey.StartsWith("browser.", StringComparison.Ordinal));

                Count("path", isPath);
                Count("foreign", isForeign);
                Count("name", isName);
                Count("key", isKey);
                Count("picture", isPicture);
                Count("area", isArea);
                Count("selector", isSelector);
                Count("windowRule", isWindowRule);

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
            ["area", "foreign", "key", "name", "path", "picture", "selector", "windowRule"],
            matched.Keys.Order(StringComparer.Ordinal));
    }
}
