using System.Text.RegularExpressions;
using Viktor.Localization;
using Viktor.Models;

namespace Viktor.Tests;

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

        // The other way round is deliberate for the system variables: their English text sits
        // next to the variable definition and only the Chinese translation lives in the table.
        Assert.Empty(chinese.Except(english)
            .Where(key => !key.StartsWith("Variable.", StringComparison.Ordinal))
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
            }

            return missing;
        });

        Assert.Empty(gaps);
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

        foreach (var project in new[] { "Viktor.Core", "Viktor" })
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
    /// The repository root, found by walking up from the test binaries until the solution file is
    /// there. The tests run from inside <c>bin</c>, so the source they read is found rather than
    /// assumed.
    /// </summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "Viktor.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("Viktor.slnx was not found above the test binaries.");
    }

    private static void Wanted(List<string> missing, string key)
    {
        if (!ActionStrings.Chinese.ContainsKey(key))
        {
            missing.Add(key);
        }
    }
}
