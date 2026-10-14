using System.Text.RegularExpressions;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The text tables are written by hand, so a key only one of them has is easy to miss: the
/// interface then shows English in the middle of Chinese, or the other way round. The action
/// catalogue is checked the same way against the Chinese names the interface needs.
/// </summary>
public class LocalizationTests
{
    [Fact]
    public void Both_languages_carry_the_same_keys()
    {
        var english = Strings.English.Keys.ToHashSet(StringComparer.Ordinal);
        var chinese = Strings.Chinese.Keys.ToHashSet(StringComparer.Ordinal);

        // English is the fallback language, so an English key without a Chinese one would show
        // English text in the middle of the Chinese interface.
        Assert.Empty(english.Except(chinese).Order());

        // The other way round is deliberate for the system variables and the expression functions:
        // their English text sits next to the definition — in the variable catalogue and in the
        // function table of the engine — and only the Chinese translation lives in the table.
        Assert.Empty(chinese.Except(english)
            .Where(key => !key.StartsWith("Variable.", StringComparison.Ordinal)
                && !key.StartsWith("Expr.Fn.", StringComparison.Ordinal))
            .Order());
    }

    [Fact]
    public void No_text_is_left_empty()
    {
        foreach (var (key, text) in Strings.English)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"English {key} is empty");
        }

        foreach (var (key, text) in Strings.Chinese)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"Chinese {key} is empty");
        }
    }

    [Fact]
    public void Every_action_and_parameter_is_named_in_chinese()
    {
        var gaps = Ui.Run(() =>
        {
            var missing = new List<string>();
            foreach (var definition in ActionCatalog.Definitions)
            {
                Wanted(missing, $"{definition.Key}.name");
                Wanted(missing, $"{definition.Key}.desc");

                foreach (var parameter in definition.Parameters)
                {
                    Wanted(missing, $"{definition.Key}.{parameter.Name}.label");

                    foreach (var choice in parameter.OptionChoices)
                    {
                        Wanted(missing, $"{definition.Key}.{parameter.Name}.option.{choice.Value}");
                    }
                }

                // What a step hands over is named and explained on its returns page, so those lines
                // are user-facing text like any other and have to exist in Chinese too.
                foreach (var result in definition.Returns)
                {
                    Wanted(missing, $"{definition.Key}.return.{result.Key}.label");
                    Wanted(missing, $"{definition.Key}.return.{result.Key}.hint");
                }
            }

            return missing;
        });

        Assert.Empty(gaps);
    }

    /// <summary>
    /// The fields that ask how writing is compared offer the same ways, in the same order, and each
    /// way has a word in Chinese. One question with one list of answers is the whole point: "starts
    /// with" may not read as one thing in a search and another in a condition about writing.
    /// </summary>
    [Fact]
    public void The_fields_that_compare_writing_offer_the_same_ways()
    {
        string[] textMatchFields =
        [
            "ocr.findText", "ocr.clickText", "condition.textExists", "condition.textNotExists",
        ];

        Ui.Run(() =>
        {
            foreach (var action in textMatchFields)
            {
                var parameter = ActionCatalog.Find(action)!
                    .Parameters.Single(item => item.Name == "matchMode");

                Assert.Equal(TextCompare.Values, parameter.Options);

                foreach (var choice in parameter.OptionChoices)
                {
                    var key = $"{action}.matchMode.option.{choice.Value}";
                    Assert.True(ActionStrings.Chinese.ContainsKey(key), key);
                }
            }

            // A branch and a table's rows are compared with operators, and the ways of comparing text
            // among them are the same words rather than a second set that could drift apart.
            foreach (var action in new[] { "control.switch", "file.readCsv" })
            {
                var options = ActionCatalog.Find(action)!
                    .Parameters.Single(item => item.Name == "matchMode").Options;

                Assert.All(TextCompare.Values, way => Assert.Contains(way, options));
            }
        });
    }

    [Fact]
    public void Every_list_that_names_its_own_add_button_is_named_in_both_languages()
    {
        // The label of an "add to this list" button is a key the catalogue carries, so renaming
        // the text would otherwise leave the button showing the raw key with nothing to notice it.
        var gaps = ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters)
            .Select(parameter => parameter.AddLabelKey)
            .Where(key => key.Length > 0)
            .Where(key => !Strings.English.ContainsKey(key) || !Strings.Chinese.ContainsKey(key))
            .Order()
            .ToList();

        Assert.Empty(gaps);
    }

    [Fact]
    public void Every_chinese_key_belongs_to_an_action()
    {
        var orphans = Ui.Run(() =>
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in ActionCatalog.Definitions)
            {
                known.Add($"{definition.Key}.name");
                known.Add($"{definition.Key}.desc");

                foreach (var parameter in definition.Parameters)
                {
                    known.Add($"{definition.Key}.{parameter.Name}.label");
                    known.Add($"{definition.Key}.{parameter.Name}.hint");

                    foreach (var choice in parameter.OptionChoices)
                    {
                        known.Add($"{definition.Key}.{parameter.Name}.option.{choice.Value}");
                    }
                }

                foreach (var result in definition.Returns)
                {
                    known.Add($"{definition.Key}.return.{result.Key}.label");
                    known.Add($"{definition.Key}.return.{result.Key}.hint");
                }
            }

            return ActionStrings.Chinese.Keys.Where(key => !known.Contains(key)).Order().ToList();
        });

        Assert.Empty(orphans);
    }

    /// <summary>
    /// Every word the run reports a failure or a step with is a key the interface has to know, and
    /// the engine names them as plain string literals — a key nobody wrote down shows up as itself,
    /// so a timeout reads "Run.Timeout" on screen. The engine's own source is the list of keys, so
    /// this reads it rather than keeping a second copy that can fall out of step.
    /// </summary>
    [Fact]
    public void Every_word_the_engine_reports_a_run_with_is_written_down()
    {
        var words = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in new[] { "WhaleGenie.Core", "WhaleGenie" })
        {
            var folder = Path.Combine(Repository(), project);
            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"(Run\\.[A-Za-z]+)\""))
                {
                    words.Add(match.Groups[1].Value);
                }
            }
        }

        Assert.NotEmpty(words);

        var gaps = words
            .Where(word => !Strings.English.ContainsKey(word) || !Strings.Chinese.ContainsKey(word))
            .Order()
            .ToList();

        Assert.Empty(gaps);
    }

    /// <summary>
    /// A field with nothing under it leaves the reader to guess three things at once: what it takes,
    /// what an empty box means, and what happens if they leave it. Only the labels, the options and
    /// the returns were ever asked for, which is how a dozen fields came to have no line at all —
    /// half of them with no English one either, so the Chinese interface fell back to English.
    /// </summary>
    [Fact]
    public void Every_field_says_something_about_itself_in_both_languages()
    {
        var gaps = Ui.Run(() =>
        {
            var missing = new List<string>();
            foreach (var definition in ActionCatalog.Definitions)
            {
                foreach (var parameter in definition.Parameters)
                {
                    if (parameter.Hint.Length == 0)
                    {
                        missing.Add($"{definition.Key}.{parameter.Name} (English)");
                    }

                    var key = $"{definition.Key}.{parameter.Name}.hint";
                    if (!ActionStrings.Chinese.ContainsKey(key))
                    {
                        missing.Add(key);
                    }
                }
            }

            return missing;
        });

        Assert.Empty(gaps.Order());
    }

    /// <summary>
    /// A number that is already filled in is one the reader does not have to answer, so the one thing
    /// its line has to say is what the filled-in value is. Numbers are what this is about: a choice
    /// says which option it starts on in the option's own words.
    /// </summary>
    [Fact]
    public void A_number_that_starts_filled_in_says_what_it_starts_at()
    {
        var gaps = Ui.Run(() =>
        {
            var missing = new List<string>();
            foreach (var definition in ActionCatalog.Definitions)
            {
                foreach (var parameter in definition.Parameters)
                {
                    if (parameter.Kind != ActionParameterKind.Number
                        || parameter.DefaultValue.Length == 0)
                    {
                        continue;
                    }

                    if (!parameter.Hint.Contains("default", StringComparison.OrdinalIgnoreCase))
                    {
                        missing.Add($"{definition.Key}.{parameter.Name} (English)");
                    }

                    var key = $"{definition.Key}.{parameter.Name}.hint";
                    if (!ActionStrings.Chinese.TryGetValue(key, out var chinese)
                        || !chinese.Contains("默认"))
                    {
                        missing.Add(key);
                    }
                }
            }

            return missing;
        });

        Assert.Empty(gaps.Order());
    }

    /// <summary>
    /// A field that has to be filled in may not say in its line that it comes filled in. It is the
    /// one place such a line does real harm: the reader leaves the field, takes the line at its
    /// word, and the step will not save. What counts is a value being named as what is already
    /// there — a line saying that an empty box is the default, or that nothing is required, is
    /// saying what an empty box means, which is the opposite.
    /// </summary>
    [Fact]
    public void A_field_that_has_to_be_filled_in_does_not_say_that_it_comes_filled_in()
    {
        // Two lines say what a length of time already is, that being the step's own timeout rather
        // than the field: a step that waits has one filled in, and the number is worth reading.
        string[] aTimeout = ["browser.waitFor.target", "control.waitUntil.condition"];

        var gaps = Ui.Run(() =>
        {
            var missing = new List<string>();
            foreach (var definition in ActionCatalog.Definitions)
            {
                foreach (var parameter in definition.Parameters)
                {
                    if (!parameter.Required || parameter.DefaultValue.Length > 0
                        || aTimeout.Contains($"{definition.Key}.{parameter.Name}"))
                    {
                        continue;
                    }

                    // A value written as the default: "10 by default", "*.txt by default",
                    // "默认 2026-01-01", "默认 10 毫秒".
                    if (ValueSaidAsDefault.IsMatch(parameter.Hint))
                    {
                        missing.Add($"{definition.Key}.{parameter.Name} (English)");
                    }

                    var key = $"{definition.Key}.{parameter.Name}.hint";
                    if (ActionStrings.Chinese.TryGetValue(key, out var chinese)
                        && ChineseValueSaidAsDefault.IsMatch(chinese))
                    {
                        missing.Add(key);
                    }
                }
            }

            return missing;
        });

        Assert.Empty(gaps.Order());
    }

    /// <summary>A value named as what is already in the box, in either language.</summary>
    private static readonly Regex ValueSaidAsDefault = new(@"[\d*][^\s]*\s+by default",
        RegexOptions.IgnoreCase);

    private static readonly Regex ChineseValueSaidAsDefault = new(@"默认\s*[\d*]");

    /// <summary>
    /// Text the user reads is written in the tables rather than on a page, so the marks a note is
    /// written in are not part of it: a dash or a backtick left in one reads as a slip of the pen,
    /// and it lands where the reader is deciding what to type or which button to press. Everything
    /// the interface can show is read here, in both languages, because a dash usually comes in
    /// with a sentence translated in a hurry.
    /// </summary>
    [Fact]
    public void No_text_a_user_reads_carries_a_dash_or_a_backtick()
    {
        var marked = Ui.Run(() =>
        {
            var found = new List<string>();
            void Look(string where, string text)
            {
                if (text.Contains('—') || text.Contains('`'))
                {
                    found.Add(where);
                }
            }

            foreach (var (key, text) in Strings.English)
            {
                Look($"Strings.English[{key}]", text);
            }

            foreach (var (key, text) in Strings.Chinese)
            {
                Look($"Strings.Chinese[{key}]", text);
            }

            foreach (var (key, text) in ActionStrings.Chinese)
            {
                Look($"ActionStrings.Chinese[{key}]", text);
            }

            foreach (var definition in ActionCatalog.Definitions)
            {
                Look($"{definition.Key}.Description", definition.Description);
                Look($"{definition.Key}.DisplayName", definition.DisplayName);
                foreach (var parameter in definition.Parameters)
                {
                    Look($"{definition.Key}.{parameter.Name}", parameter.Hint);
                    Look($"{definition.Key}.{parameter.Name}.label", parameter.Label);
                    Look($"{definition.Key}.{parameter.Name}.placeholder", parameter.Placeholder);
                    foreach (var option in parameter.OptionLabels)
                    {
                        Look($"{definition.Key}.{parameter.Name}.option", option);
                    }
                }

                foreach (var result in definition.Returns)
                {
                    Look($"{definition.Key}.return.{result.Key}.label", result.Label);
                    Look($"{definition.Key}.return.{result.Key}.hint", result.Hint);
                }
            }

            return found;
        });

        Assert.Empty(marked.Order());
    }

    /// <summary>
    /// The repository root, found by walking up from the test binaries until the solution file is
    /// there. The tests run from inside <c>bin</c>, so the source they read is found rather than
    /// assumed.
    /// </summary>
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

    private static void Wanted(List<string> missing, string key)
    {
        if (!ActionStrings.Chinese.ContainsKey(key))
        {
            missing.Add(key);
        }
    }
}
