using System;
using System.IO;
using System.Linq;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// The restore itself. A harness that silently does nothing is worse than none — it looks like
/// isolation and explains nothing — so the tool is checked the way the code it protects is: change
/// what it watches, put it back, and read it.
/// </summary>
[RestoreSharedState]
public class SharedStateTests
{
    [Fact]
    public void Restoring_puts_back_the_language_the_settings_path_and_the_catalogue()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-state-" + Guid.NewGuid().ToString("N"));

        Ui.Run(() =>
        {
            var before = SharedState.Capture();
            try
            {
                Strings.Current.Language = Other(before.Language);
                LocalSettings.FilePath = Path.Combine(folder, "settings.json");
                VariableCatalog.Globals.Add(new VariableDefinition { Name = "leakedByAccident" });

                before.Restore();

                Assert.Equal(before.Language, Strings.Current.Language);
                Assert.Equal(before.SettingsPath, LocalSettings.FilePath);
                Assert.Equal(
                    before.Globals.Select(definition => definition.Name),
                    VariableCatalog.Globals.Select(definition => definition.Name));
            }
            finally
            {
                before.Restore();
            }
        });
    }

    [Fact]
    public void A_change_made_between_two_checks_is_not_there_for_the_second_one()
    {
        var before = Ui.Run(() =>
        {
            var snapshot = SharedState.Capture();
            Strings.Current.Language = Other(snapshot.Language);
            Assert.NotEqual(snapshot.Language, Strings.Current.Language);

            // What the attribute does between two checks, done here by hand. Without it, the next
            // check reads a world the previous one changed.
            snapshot.Restore();
            return snapshot.Language;
        });

        Assert.Equal(before, Ui.Run(() => Strings.Current.Language));
    }

    private static Language Other(Language language)
        => language == Language.Chinese ? Language.English : Language.Chinese;
}
