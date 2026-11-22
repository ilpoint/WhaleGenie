using System;
using System.IO;
using WhaleGenie.ContractsGen;

namespace WhaleGenie.Tests;

/// <summary>
/// The page's copy of the wire contract is generated from the C# records. This case is what keeps
/// the two from drifting: a record edited without regenerating fails here, with the command that
/// fixes it.
/// </summary>
public class ContractGenerationTests
{
    [Fact]
    public void The_generated_contract_is_current()
    {
        var expected = ContractGenerator.Generate();
        var path = Path.Combine(RepoPaths.Root,
            "webui", "src", "shared", "bridge", "generated", "types.ts");
        Assert.True(File.Exists(path), $"the generated contract is missing: {path}");

        var actual = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(expected == actual,
            "webui/src/shared/bridge/generated/types.ts is stale; run "
            + "dotnet run --project tools/WhaleGenie.ContractsGen");
    }
}
