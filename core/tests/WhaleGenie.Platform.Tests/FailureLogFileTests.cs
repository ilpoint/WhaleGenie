using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;
using WhaleGenie.Platform.Windows;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Platform.Tests;

/// <summary>
/// The log of a failed run through the real file device: what it claims about its encoding has to
/// be what lands on disk, because a person double-clicking the file reads that copy.
/// </summary>
public class FailureLogFileTests
{
    /// <summary>
    /// The log goes through the real file device as well, so what it claims about its encoding is
    /// what lands on disk: a person double-clicking the file must read the macro's name and the
    /// steps it took, not a page of question marks where the writing was.
    /// </summary>
    [Fact]
    public async Task The_log_of_a_failed_run_is_a_text_file_a_person_can_open()
    {
        var folder = Directory.CreateTempSubdirectory("whalegenie-log-");
        try
        {
            var devices = new FakeDeviceLayer(new LocalFileDevice(folder.FullName));
            var result = await new MacroRunner(new VariableStore(), new SilentRunHost(), devices)
            {
                FailureLogFolder = folder.FullName,
                RunName = "订单导出",
            }.RunAsync([SampleFiles.Step("nope.unknown")]);
            Assert.Equal(RunStatus.Failed, result.Status);
            var which = Assert.Single(Directory.GetFiles(folder.FullName, "*.log"));
            Assert.Contains("failure-订单导出-", Path.GetFileName(which), StringComparison.Ordinal);
            // UTF-8 with a byte-order mark, which is what a double-click has to be able to read.
            var bytes = File.ReadAllBytes(which);
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
            var text = Encoding.UTF8.GetString(bytes[3..]);
            Assert.StartsWith("Log.LogFileHeader 订单导出 ", text, StringComparison.Ordinal);
            Assert.Contains("Log.Aborted", text, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
