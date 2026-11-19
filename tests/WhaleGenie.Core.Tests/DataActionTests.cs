using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The data family on its own: text turned into Base64 and back, and a checksum worked out from what
/// a step holds — no machine is touched, so the answer is the same on every computer.
/// </summary>
public class DataActionTests
{
    [Fact]
    public async Task Text_is_encoded_as_base64_and_come_back_the_same()
    {
        var context = new FakeRunContext();

        await Run(context, "data.base64Encode", Step("data.base64Encode",
            Fields.Text("dataText", "你好"),
            SampleFiles.Returns("encoded")));
        var encoded = context.Written["encoded"].AsText();
        Assert.Equal("5L2g5aW9", encoded);

        await Run(context, "data.base64Decode", Step("data.base64Decode",
            Fields.Text("dataText", encoded),
            SampleFiles.Returns("decoded")));

        Assert.Equal("你好", context.Written["decoded"].AsText());
    }

    [Fact]
    public async Task A_checksum_is_worked_out_from_the_text_the_step_holds()
    {
        var context = new FakeRunContext();

        await Run(context, "data.hash", Step("data.hash",
            Fields.Text("hashSource", "text"),
            Fields.Text("hashAlgorithm", "sha256"),
            Fields.Text("dataText", "abc"),
            SampleFiles.Returns("digest")));

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            context.Written["digest"].AsText());
    }

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => DataActions.Map[type](context, step, 0, CancellationToken.None);

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
