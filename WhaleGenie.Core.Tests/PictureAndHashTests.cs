using System.IO;
using System.Text;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Two ways of putting something on disk in a shape a later step can read back: a picture written
/// out as a PNG while it is being taken, and a checksum that can be worked out over a file as well
/// as over a piece of text.
/// </summary>
public class PictureAndHashTests
{
    /// <summary>The eight bytes every PNG starts with.</summary>
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10];

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Id = "aaaa", Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>See <see cref="SampleFiles.Returns"/>: the results a step hands over.</summary>
    private static ExecutableParameter Returns(params string[] results)
        => SampleFiles.Returns(results);

    /// <summary>The results a step that reads a picture hands over, picture and place together.</summary>
    private static ExecutableParameter Parts(string key, string name, params string[] extra)
        => SampleFiles.Parts(key, name, extra);

    private static async Task<(RunResult Result, FakeDeviceLayer Devices, VariableStore Store)> RunAsync(
        ExecutableStep[] steps, FakeDeviceLayer? devices = null)
    {
        var layer = devices ?? new FakeDeviceLayer();
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), layer).RunAsync(steps);
        return (result, layer, store);
    }

    [Fact]
    public async Task A_picture_is_written_out_while_it_is_being_taken()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("vision.capture", Param("x", "0"), Param("y", "0"), Param("width", "10"),
                Param("height", "10"), Parts("image", "shot"), Param("path", @"shots\out.png")),
        ]);

        Assert.True(result.Succeeded);
        var written = Assert.Contains(@"shots\out.png", devices.Files.Blobs);
        Assert.Equal(Png, written[..8]);
    }

    /// <summary>
    /// A capture that names no file writes none: the picture goes to the variable and stays there,
    /// which is what a step that takes one to look at it right away wants.
    /// </summary>
    [Fact]
    public async Task A_capture_that_names_no_file_writes_none()
    {
        var (result, devices, _) = await RunAsync(
        [
            Step("vision.capture", Param("x", "0"), Param("y", "0"), Param("width", "10"),
                Param("height", "10"), Parts("image", "shot")),
        ]);

        Assert.True(result.Succeeded);
        Assert.Empty(devices.Files.Blobs);
    }

    /// <summary>
    /// A window is taken and kept the same way a rectangle is: the two capture steps ask the one
    /// question about where the file goes, so a macro that keeps what it captured does not have to
    /// remember which of them it used.
    /// </summary>
    [Fact]
    public async Task A_window_is_written_out_while_it_is_being_taken()
    {
        var devices = new FakeDeviceLayer();
        devices.Windows.Add(new WhaleGenie.Core.Devices.WindowInfo(1, "Notepad - notes.txt",
            new WhaleGenie.Core.Devices.ScreenPoint(1000, 500),
            new WhaleGenie.Core.Devices.ScreenSize(800, 600), false, false));

        var (result, _, _) = await RunAsync(
        [
            Step("vision.captureWindow",
                Param("window", new WindowRule(Title: "Notepad").ToJson().ToJsonString()),
                Parts("image", "shot"),
                Param("path", "window.png")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(Png, devices.Files.Blobs["window.png"][..8]);
    }

    /// <summary>
    /// The checksum of a file and the checksum of the text those bytes spell out are the same
    /// number, which is what makes the two ways of asking one step rather than two.
    /// </summary>
    [Fact]
    public async Task A_checksum_reads_a_file_the_same_way_a_piece_of_text()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Blobs["report.txt"] = Encoding.UTF8.GetBytes("hello");

        var (result, _, store) = await RunAsync(
        [
            Step("data.hash", Param("hashAlgorithm", "sha256"), Param("hashSource", "text"),
                Param("dataText", "hello"), Returns("digest=fromText")),
            Step("data.hash", Param("hashAlgorithm", "sha256"), Param("hashSource", "file"),
                Param("path", "report.txt"), Returns("digest=fromFile")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("openRead report.txt", devices.Calls);
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            store.Local.Values["fromText"].AsText());
        Assert.Equal(store.Local.Values["fromText"].AsText(), store.Local.Values["fromFile"].AsText());
    }

    /// <summary>
    /// The real file device is the one that has to read a file a piece at a time, so this goes
    /// through it: the checksum of a file on disk is the checksum of the text those bytes spell
    /// out, and a file that is not there is refused rather than hashed as nothing.
    /// </summary>
    [Fact]
    public async Task A_checksum_of_a_real_file_matches_the_bytes_in_it()
    {
        var folder = Directory.CreateTempSubdirectory("whalegenie-hash-");
        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "report.txt"),
                Encoding.UTF8.GetBytes("hello"));
            var devices = new FakeDeviceLayer(new LocalFileDevice(folder.FullName));

            var (result, _, store) = await RunAsync(
            [
                Step("data.hash", Param("hashAlgorithm", "sha256"), Param("hashSource", "file"),
                    Param("path", "report.txt"), Returns("digest=sum")),
            ], devices);

            Assert.True(result.Succeeded);
            Assert.Equal(
                "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
                store.Local.Values["sum"].AsText());

            var missing = await RunAsync(
            [
                Step("data.hash", Param("hashSource", "file"), Param("path", "absent.bin"),
                    Returns("digest=gone")),
            ], new FakeDeviceLayer(new LocalFileDevice(folder.FullName)));

            Assert.False(missing.Result.Succeeded);
            Assert.Equal("Run.FileNotFound", missing.Result.Key);
            Assert.Equal(string.Empty, missing.Store.Local.Values["gone"].AsText());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
