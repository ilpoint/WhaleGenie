using System.Text.RegularExpressions;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

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

        // The other way round is deliberate for the system variables, the expression functions and
        // the arguments they take: their English text sits next to the definition — in the variable
        // catalogue and in the function table of the engine — and only the Chinese translation lives
        // in the table. The formula said in plain Chinese is the same: it is a reading of a formula
        // for someone whose interface is Chinese, and an English interface has no line at all.
        Assert.Empty(chinese.Except(english)
            .Where(key => !key.StartsWith("Variable.", StringComparison.Ordinal)
                && !key.StartsWith("Expr.Fn.", StringComparison.Ordinal)
                && !key.StartsWith("Expr.Arg.", StringComparison.Ordinal)
                && !key.StartsWith("Expr.FnName.", StringComparison.Ordinal)
                && !key.StartsWith("Expr.Say.", StringComparison.Ordinal))
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
    /// Every variable WhaleGenie provides has a short Chinese name to go with the code name, and
    /// nothing holds a name for a variable that is gone. A macro's own variables are left alone:
    /// they are called whatever their author called them, and a Chinese name would be a name nobody
    /// chose.
    /// </summary>
    [Fact]
    public void Every_system_variable_is_named_in_chinese()
    {
        var wanted = VariableCatalog.SystemVariables
            .Select(variable => $"Variable.{variable.Name}.label")
            .ToHashSet(StringComparer.Ordinal);
        var held = Strings.Chinese.Keys
            .Where(key => key.StartsWith("Variable.", StringComparison.Ordinal)
                && key.EndsWith(".label", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(wanted.Except(held).Order());
        Assert.Empty(held.Except(wanted).Order());
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

    /// <summary>
    /// Every way of narrowing a page down is offered under a name the text tables carry, and the
    /// dropdown says which questions a row can ask in the reader's own language. A way added to the
    /// engine without a word for it would show the person filling the form a raw name instead.
    /// </summary>
    [Fact]
    public void Every_way_of_narrowing_a_page_down_is_named_in_both_languages()
    {
        Ui.Run(() =>
        {
            foreach (var way in Enum.GetValues<PageFilter>())
            {
                foreach (var prefix in new[] { "Add.Narrow.", "Add.NarrowValue." })
                {
                    Assert.True(Strings.English.ContainsKey(prefix + way), prefix + way);
                    Assert.True(Strings.Chinese.ContainsKey(prefix + way), prefix + way);
                }
            }

            // The order they are offered in is the order they read in: where the element sits
            // first, then what it writes, then what it holds, and last the one that asks nothing
            // beside it. Every way is in the list, read from the table rather than from the engine's
            // own names.
            Assert.Equal(
                [
                    nameof(PageFilter.Inside), nameof(PageFilter.HasText),
                    nameof(PageFilter.HasNotText), nameof(PageFilter.Has),
                    nameof(PageFilter.HasNot), nameof(PageFilter.Visible),
                ],
                NarrowRowViewModel.Ways.Select(choice => choice.Value));
            Assert.All(NarrowRowViewModel.Ways,
                choice => Assert.NotEqual(choice.Value, choice.Display));
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

                    // A field whose starting text is prose of its own — the example a script step
                    // is written from — says it in both languages the way a label does.
                    if (parameter.DefaultValue.Length > 0
                        || Strings.English.ContainsKey($"{definition.Key}.{parameter.Name}.default"))
                    {
                        known.Add($"{definition.Key}.{parameter.Name}.default");
                    }

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
    /// The engine records a run as a key and its arguments, so the text of every line — the ones a
    /// failed run writes into its own file included — comes from the wording table. A host that
    /// dropped the arguments would leave a log line saying "Run.Start" where the person expects the
    /// macro's own story.
    /// </summary>
    [Fact]
    public void A_line_of_a_run_log_reads_the_way_the_window_shows_it()
    {
        var entry = new LogEntry(DateTimeOffset.Now, LogLevel.Info, 0, "control.delay", "Run.Start", [3]);

        var worded = RunLogText.Of(entry);
        Assert.NotEqual(entry.Key, worded);
        Assert.Contains("3", worded, StringComparison.Ordinal);

        // A run a trigger started has no window to show its lines in, and words them the same way:
        // the file it leaves behind has to read like the window would have.
        Assert.Equal(worded, new AskRunHost(null).Text(entry));

        // The header of that file goes through the same table, so both languages carry its parts.
        Assert.Contains("{0}", Strings.Chinese["Run.LogFileHeader"], StringComparison.Ordinal);
        Assert.Contains("{1}", Strings.Chinese["Run.LogFileHeader"], StringComparison.Ordinal);
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
        // Three lines say what a length of time already is, that being the step's own timeout rather
        // than the field: a step that waits has one filled in, and the number is worth reading.
        string[] aTimeout =
            ["browser.waitFor.target", "browser.goTo.url", "control.waitUntil.condition"];

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
