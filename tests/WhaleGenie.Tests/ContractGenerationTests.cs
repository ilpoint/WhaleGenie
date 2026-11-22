using System;
using System.IO;
using System.Text.Json;
using WhaleGenie.Contracts;
using WhaleGenie.ContractsGen;

namespace WhaleGenie.Tests;

/// <summary>
/// The page's copy of the wire contract is generated from the C# records and method list. This
/// case is what keeps the two from drifting: an edit without regenerating fails here, with the
/// command that fixes it.
/// </summary>
public class ContractGenerationTests
{
    private const string Regenerate =
        "dotnet run --project tools/WhaleGenie.ContractsGen -- webui/src/shared/bridge/generated";

    [Fact]
    public void The_generated_contract_is_current()
    {
        AssertCurrent("types.ts", ContractGenerator.Generate());
        AssertCurrent("methods.ts", ContractGenerator.GenerateMethods());
    }

    [Fact]
    public void A_polymorphic_command_round_trips_with_the_discriminator()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var json = JsonSerializer.Serialize<EditorValueCommand>(
            new EditorValueOpenCommand("s1", "name"), options);
        Assert.Contains("\"kind\":\"open\"", json, StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<EditorValueCommand>(json, options);
        var open = Assert.IsType<EditorValueOpenCommand>(back);
        Assert.Equal("s1", open.Step);
        Assert.Equal("name", open.Parameter);
    }

    private static void AssertCurrent(string name, string expected)
    {
        var path = Path.Combine(RepoPaths.Root,
            "webui", "src", "shared", "bridge", "generated", name);
        Assert.True(File.Exists(path), $"the generated contract is missing: {path}");

        var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected == actual, $"{name} is stale; run {Regenerate}");
    }
}
