using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A snapshot from an older format is not restored.
public partial class RecoveryTests
{

    /// <summary>The time the snapshot says it was taken, which a rewrite moves and nothing else does.</summary>
    private static string? TakenAt(string path)
        => (JsonNode.Parse(File.ReadAllText(path)) as JsonObject)?["savedUtc"]?.GetValue<string>();

    private static MacroItem Named(string name) => new() { Name = name, BindKey = "F5" };

    /// <summary>Older recovery files may carry superseded trigger shapes and must not be read.</summary>
    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    public void A_recovery_snapshot_from_an_older_format_is_not_restored(int version)
    {
        InOwnFile(path =>
        {
            File.WriteAllText(path,
                "{\"formatVersion\":" + version
                + ",\"project\":{\"macros\":[],\"variables\":[]}}");

            Assert.Null(RecoveryStore.Load());
            Assert.False(File.Exists(path));
        });
    }

    /// <summary>A macro with a step, so a round trip carries more than the name.</summary>
    private static MacroItem Sample()
    {
        var macro = new MacroItem { Name = "login", BindKey = "F5" };
        macro.Steps.Add(new MacroStep
        {
            Type = "input.keyPress",
            Parameters = [new StepParameter { Name = "key", Kind = ActionParameterKind.Text, Value = "A" }],
        });
        return macro;
    }

    /// <summary>
    /// Points the snapshot at a file of its own under the temporary directory, so a check never
    /// reads the one this machine keeps and never leaves anything behind.
    /// </summary>
    private static void InOwnFile(Action<string> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "recovery.json");
        var previous = RecoveryStore.FilePath;
        RecoveryStore.FilePath = path;
        try
        {
            body(path);
        }
        finally
        {
            RecoveryStore.FilePath = previous;
            Directory.Delete(folder, true);
        }
    }
}
