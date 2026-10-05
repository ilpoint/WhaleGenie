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

    private static void Wanted(List<string> missing, string key)
    {
        if (!ActionStrings.Chinese.ContainsKey(key))
        {
            missing.Add(key);
        }
    }
}
