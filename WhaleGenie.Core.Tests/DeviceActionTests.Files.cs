using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

// Text files, folders, archives and listings, as the file device is asked for them.
public partial class DeviceActionTests
{
    [Fact]
    public async Task Reading_a_text_file_fills_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["notes.txt"] = "hello world";
        var (result, _, store) = await RunAsync(
            [Step("file.readText", Param("path", "notes.txt"), Returns("text=text"))],
            devices);

        Assert.True(result.Succeeded);
        Assert.Equal("hello world", store.Local.Values["text"].AsText());
        Assert.Contains("readFile notes.txt utf8", devices.Calls);
    }

    [Fact]
    public async Task A_text_file_can_be_read_one_line_at_a_time()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["names.txt"] = "alpha\r\nbeta\ngamma\r";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readText", Param("path", "names.txt"), Returns("lines=names")),
        ], devices);

        var names = store.Local.Values["names"];
        Assert.True(names.IsList);
        Assert.Equal(["alpha", "beta", "gamma"], names.Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task Blank_lines_and_padding_can_be_left_out_of_a_line_list()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["names.txt"] = "alpha\n\n  beta  \n";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readText", Param("path", "names.txt"),
                Param("skipBlankLines", "true"), Param("trimLines", "true"),
                Returns("lines=names")),
        ], devices);

        Assert.Equal(["alpha", "beta"],
            store.Local.Values["names"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task Capturing_to_a_file_hands_over_where_it_was_saved()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("vision.capture", Param("x", "0"), Param("y", "0"), Param("width", "2"),
                Param("height", "2"), Param("path", "shot.png"), Returns("path=saved")),
        ], devices);

        Assert.EndsWith("shot.png", store.Local.Values["saved"].AsText(), StringComparison.Ordinal);
        Assert.Contains("shot.png", devices.Files.Blobs.Keys);
    }

    [Fact]
    public async Task A_line_result_can_be_read_up_to_a_line()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["names.txt"] = "alpha\nbeta\ngamma\n";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readText", Param("path", "names.txt"), Param("limit", "2"),
                Returns("lines=head")),
        ], devices);

        // The limit belongs to the line result; the text result stays the whole file.
        Assert.Equal(["alpha", "beta"],
            store.Local.Values["head"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task The_text_result_is_the_whole_file_even_when_the_line_options_are_set()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["names.txt"] = "alpha\n\n  beta  \n";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readText", Param("path", "names.txt"), Param("limit", "1"),
                Param("skipBlankLines", "true"), Param("trimLines", "true"),
                Returns("text=whole")),
        ], devices);

        Assert.Equal("alpha\n\n  beta  \n", store.Local.Values["whole"].AsText());
    }

    [Fact]
    public async Task Reading_a_text_file_hands_nothing_over_until_a_result_is_asked_for()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["notes.txt"] = "kept";
        var (_, _, store) = await RunAsync(
            [Step("file.readText", Param("path", "notes.txt"))], devices);

        Assert.Empty(store.Local.Values);
    }

    [Fact]
    public async Task Text_files_can_name_the_encoding_they_are_written_in()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["old.csv"] = "a,b";
        var (result, _, _) = await RunAsync(
        [
            Step("file.writeText", Param("path", "notes.txt"), Param("fileContent", "你好"),
                Param("encoding", "gbk")),
            Step("file.readText", Param("path", "notes.txt"), Param("encoding", "gbk")),
            Step("file.readCsv", Param("path", "old.csv"), Param("encoding", "gbk")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("writeFile notes.txt 你好 False gbk", devices.Calls);
        Assert.Contains("readFile notes.txt gbk", devices.Calls);
        Assert.Contains("readFile old.csv gbk", devices.Calls);
    }

    [Fact]
    public async Task Writing_a_text_file_replaces_it_or_adds_to_the_end()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["log.txt"] = "first\n";
        await RunAsync(
        [
            Step("file.writeText", Param("path", "log.txt"), Param("fileContent", "second"),
                Param("writeMode", "append")),
            Step("file.writeText", Param("path", "other.txt"), Param("fileContent", "fresh"),
                Param("writeMode", "overwrite")),
        ], devices);

        Assert.Equal("first\nsecond", devices.Files.Store["log.txt"]);
        Assert.Equal("fresh", devices.Files.Store["other.txt"]);
    }

    [Fact]
    public async Task Writing_text_can_leave_a_line_break_after_it()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("file.writeText", Param("path", "notes.txt"), Param("fileContent", "done"),
                Param("newline", "end")),
        ], devices);

        Assert.Equal("done" + Environment.NewLine, devices.Files.Store["notes.txt"]);
    }

    [Fact]
    public async Task Adding_to_a_file_that_never_ended_its_line_starts_a_new_one()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["list.txt"] = "first";
        await RunAsync(
        [
            Step("file.writeText", Param("path", "list.txt"), Param("fileContent", "second"),
                Param("writeMode", "append"), Param("newline", "start")),
            Step("file.writeText", Param("path", "list.txt"), Param("fileContent", "third"),
                Param("writeMode", "append"), Param("newline", "start")),
        ], devices);

        // The second line gets a break in front of it because the file had none, and the third
        // does not because the second one left one behind.
        Assert.Equal("first" + Environment.NewLine + "second" + Environment.NewLine + "third",
            devices.Files.Store["list.txt"]);
    }

    [Fact]
    public async Task Adding_to_a_file_that_is_not_there_yet_does_not_open_with_a_blank_line()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("file.writeText", Param("path", "fresh.txt"), Param("fileContent", "first"),
                Param("writeMode", "append"), Param("newline", "start")),
        ], devices);

        Assert.Equal("first", devices.Files.Store["fresh.txt"]);
    }

    [Fact]
    public async Task A_log_line_never_lands_on_the_end_of_the_last_one()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["log.txt"] = "half a line";
        await RunAsync(
        [
            Step("file.appendLog", Param("path", "log.txt"), Param("logLine", "second"),
                Param("timestamp", "false")),
        ], devices);

        Assert.Equal("half a line" + Environment.NewLine + "second" + Environment.NewLine,
            devices.Files.Store["log.txt"]);
    }

    [Fact]
    public async Task A_log_line_is_added_to_the_end_of_the_file()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["log.txt"] = "first\n";
        await RunAsync(
        [
            Step("file.appendLog", Param("path", "log.txt"), Param("logLine", "second"),
                Param("timestamp", "false")),
        ], devices);

        // The line carries its own line break, which is what keeps the next one on its own line.
        Assert.Equal("first\nsecond" + Environment.NewLine, devices.Files.Store["log.txt"]);
    }

    [Fact]
    public async Task A_log_line_can_carry_the_time_it_was_written()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("file.appendLog", Param("path", "log.txt"), Param("logLine", "done"),
                Param("timestamp", "true")),
        ], devices);

        var text = devices.Files.Store["log.txt"];
        Assert.EndsWith(" done" + Environment.NewLine, text);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} done", text);
    }

    [Fact]
    public async Task Checking_for_a_file_leaves_true_or_false()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.PathExists = false;
        devices.Files.Store["there.txt"] = "";
        var (_, _, store) = await RunAsync(
        [
            Step("file.exists", Param("path", "there.txt"), Returns("exists=there")),
            Step("file.exists", Param("path", "gone.txt"), Returns("exists=gone")),
        ], devices);

        Assert.True(store.Local.Values["there"].Flag);
        Assert.False(store.Local.Values["gone"].Flag);
    }

    [Fact]
    public async Task Deleting_a_file_asks_the_file_device_to_remove_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["old.txt"] = "x";
        await RunAsync([Step("file.delete", Param("path", "old.txt"))], devices);

        Assert.DoesNotContain("old.txt", devices.Files.Store.Keys);
        Assert.Contains("deleteFile old.txt False", devices.Calls);
    }

    [Fact]
    public async Task A_file_can_be_put_in_the_recycle_bin_instead_of_removed_for_good()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["old.txt"] = "x";
        await RunAsync(
            [Step("file.delete", Param("path", "old.txt"), Param("toRecycleBin", "true"))],
            devices);

        Assert.Contains("deleteFile old.txt True", devices.Calls);
    }

    [Fact]
    public async Task Copying_a_file_passes_both_paths_to_the_device()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["a.txt"] = "data";
        var (_, _, store) = await RunAsync(
        [
            Step("file.copy", Param("from", "a.txt"), Param("to", "b.txt"),
                Param("ifExists", "overwrite"), Returns("path=landed")),
        ], devices);

        Assert.Equal("data", devices.Files.Store["b.txt"]);
        Assert.Contains("copyFile a.txt b.txt", devices.Calls);
        Assert.EndsWith("b.txt", store.Local.Values["landed"].AsText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_a_file_takes_it_away_from_where_it_was()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["report.csv"] = "rows";
        var (result, _, _) = await RunAsync(
        [
            Step("file.move", Param("from", "report.csv"), Param("to", @"archive\old.csv"),
                Param("ifExists", "overwrite")),
        ], devices);

        // A move is a rename as much as it is a change of folder: the old name is gone.
        Assert.True(result.Succeeded);
        Assert.Equal("rows", devices.Files.Store[@"archive\old.csv"]);
        Assert.False(devices.Files.Store.ContainsKey("report.csv"));
        Assert.Contains(@"moveFile report.csv archive\old.csv", devices.Calls);
    }

    [Fact]
    public async Task A_move_can_leave_a_file_that_is_already_there_alone()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["report.csv"] = "rows";
        devices.Files.Store["taken.csv"] = "keep me";
        var (result, _, store) = await RunAsync(
        [
            Step("file.move", Param("from", "report.csv"), Param("to", "taken.csv"),
                Param("ifExists", "skip"), Returns("path=landed")),
        ], devices);

        // Nothing was moved and nothing was overwritten: the file that was there is still there,
        // and the one that was to go there has not gone anywhere.
        Assert.True(result.Succeeded);
        Assert.Equal("keep me", devices.Files.Store["taken.csv"]);
        Assert.Equal("rows", devices.Files.Store["report.csv"]);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("moveFile", StringComparison.Ordinal));
        Assert.EndsWith("taken.csv", store.Local.Values["landed"].AsText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_copy_can_be_given_a_number_of_its_own_beside_a_taken_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["report.csv"] = "new";
        devices.Files.Store[@"backup\report.csv"] = "old";
        devices.Files.Store[@"backup\report (2).csv"] = "older";
        var (_, _, store) = await RunAsync(
        [
            Step("file.copy", Param("from", "report.csv"), Param("to", @"backup\report.csv"),
                Param("ifExists", "unique"), Returns("path=landed")),
        ], devices);

        Assert.Equal("old", devices.Files.Store[@"backup\report.csv"]);
        Assert.Equal("older", devices.Files.Store[@"backup\report (2).csv"]);
        Assert.Equal("new", devices.Files.Store[@"backup\report (3).csv"]);
        Assert.EndsWith(@"report (3).csv", store.Local.Values["landed"].AsText(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_folder_can_be_made()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("file.createFolder", Param("path", @"output\reports"))], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"createFolder output\reports", devices.Calls);
        Assert.Contains(@"output\reports", devices.Files.Folders);
    }

    [Fact]
    public async Task A_folder_that_still_holds_something_is_left_alone_unless_told_otherwise()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.FolderHasFiles = true;
        var (refused, _, _) = await RunAsync(
            [Step("file.deleteFolder", Param("path", "old"))], devices);

        Assert.False(refused.Succeeded);
        Assert.Equal("Run.FileFailed", refused.Key);

        var (agreed, _, _) = await RunAsync(
            [Step("file.deleteFolder", Param("path", "old"), Param("recurse", "true"))], devices);

        Assert.True(agreed.Succeeded);
        Assert.Contains("deleteFolder old True", devices.Calls);
    }

    [Fact]
    public async Task A_path_can_be_taken_apart_and_put_back_together()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, store) = await RunAsync(
        [
            Step("file.path", Param("operation", "combine"), Param("path", @"reports\"),
                Param("fileName", "day.csv"), Returns("path=joined")),
            Step("file.path", Param("operation", "folder"), Param("path", @"reports\day.csv"),
                Returns("path=where")),
            Step("file.path", Param("operation", "name"), Param("path", @"reports\day.csv"),
                Returns("path=what")),
            Step("file.path", Param("operation", "baseName"), Param("path", @"reports\day.csv"),
                Returns("path=stem")),
            Step("file.path", Param("operation", "extension"), Param("path", @"reports\day.csv"),
                Returns("path=tail")),
            Step("file.path", Param("operation", "full"), Param("path", @"reports\day.csv"),
                Returns("path=whole")),
            Step("file.path", Param("operation", "macros"), Returns("path=home")),
            Step("file.path", Param("operation", "temp"), Returns("path=scratch")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal(@"reports\day.csv", store.Local.Values["joined"].AsText());
        Assert.Equal("reports", store.Local.Values["where"].AsText());
        Assert.Equal("day.csv", store.Local.Values["what"].AsText());
        Assert.Equal("day", store.Local.Values["stem"].AsText());
        Assert.Equal(".csv", store.Local.Values["tail"].AsText());

        // A relative path points inside the macros folder, which is where the file actions look too.
        Assert.Equal(Path.Combine(@"G:\fake", @"reports\day.csv"), store.Local.Values["whole"].AsText());
        Assert.Equal(@"G:\fake", store.Local.Values["home"].AsText());
        Assert.True(Path.IsPathRooted(store.Local.Values["scratch"].AsText()));
    }

    [Fact]
    public async Task A_zip_file_can_be_unpacked()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("file.unzip", Param("from", "download.zip"), Param("folder", "unpacked"),
                Param("overwrite", "true")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"unzip download.zip unpacked True", devices.Calls);
        Assert.Equal("unpacked", devices.Files.Store[Path.Combine("unpacked", "readme.txt")]);
    }

    [Fact]
    public async Task A_folder_can_be_packed_and_unpack_back_to_what_was_inside_it()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
            [Step("file.zip", Param("folder", "reports"), Param("to", "reports.zip"))],
            devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"zip reports reports.zip False", devices.Calls);
    }

    [Fact]
    public async Task A_folder_can_be_packed_with_its_own_name_kept_as_a_top_entry()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("file.zip", Param("folder", "photos"), Param("to", "photos.zip"),
                Param("includeTopFolder", "true")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains(@"zip photos photos.zip True", devices.Calls);
    }

    /// <summary>
    /// The two answers to "does the folder go in as well" have to differ on disk, not only in the
    /// call that was made. This packs the same folder both ways, unpacks both, and writes over an
    /// archive an earlier run left behind, which is what a macro on its second run does.
    /// </summary>
    [Fact]
    public void A_packed_folder_keeps_its_own_name_only_when_it_is_asked_to()
    {
        var folder = Directory.CreateTempSubdirectory("whalegenie-zip-");
        try
        {
            Directory.CreateDirectory(Path.Combine(folder.FullName, "photos"));
            File.WriteAllText(Path.Combine(folder.FullName, "photos", "one.txt"), "one");
            var files = new LocalFileDevice(folder.FullName);

            files.Zip("photos", "inside.zip", includeTopFolder: false);
            files.Unzip("inside.zip", "inside", overwrite: true);

            files.Zip("photos", "outside.zip", includeTopFolder: true);
            files.Unzip("outside.zip", "outside", overwrite: true);

            // The same archive written a second time: the packer refuses a file that is there.
            files.Zip("photos", "inside.zip", includeTopFolder: false);

            Assert.True(File.Exists(Path.Combine(folder.FullName, "inside", "one.txt")));
            Assert.True(File.Exists(Path.Combine(folder.FullName, "outside", "photos", "one.txt")));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_zip_file_that_is_not_one_reports_what_the_device_said()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.UnzipWorks = false;
        var (result, _, _) = await RunAsync(
            [Step("file.unzip", Param("from", "notes.txt"), Param("folder", "unpacked"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileFailed", result.Key);
    }

    [Fact]
    public async Task Listing_a_folder_gives_a_list_of_full_paths()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.FolderEntries.Add(@"C:\fake\a.txt");
        devices.Files.FolderEntries.Add(@"C:\fake\b.txt");
        var (_, _, store) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("pattern", "*.txt"),
                Param("recurse", "true"), Returns("files=files")),
        ], devices);

        var files = store.Local.Values["files"];
        Assert.True(files.IsList);
        Assert.Equal(2, files.Items.Count);
        Assert.Equal(@"C:\fake\b.txt", files.Items[1].AsText());
        Assert.Contains(@"listFiles . *.txt True -1", devices.Calls);
    }

    /// <summary>
    /// 列文件夹：要"给每个子文件夹各做一遍"，先得拿得到它们的名字。默认仍是只列文件，老宏照旧；
    /// 说一声 folders 就只回来文件夹，both 是两样一起。
    /// </summary>
    [Fact]
    public async Task A_listing_can_be_asked_for_the_folders_rather_than_the_files()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.FolderEntries.Add(@"C:/fake/a.txt");
        devices.Files.Folders.Add(@"C:/fake/2024");
        devices.Files.Folders.Add(@"C:/fake/2025");

        var (_, _, store) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("kind", "folders"),
                Returns("files=files")),
        ], devices);

        var listed = store.Local.Values["files"];
        Assert.True(listed.IsList);
        Assert.Equal([@"C:/fake/2024", @"C:/fake/2025"],
            listed.Items.Select(item => item.AsText()).ToArray());

        // 说 both 时两样都在，文件不会因为要了文件夹而消失。
        var (_, _, both) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("kind", "both"),
                Returns("files=files")),
        ], devices);

        Assert.Equal(3, both.Local.Values["files"].Items.Count);
    }

    [Fact]
    public async Task A_listing_can_ask_for_what_changed_recently()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.FolderEntries.Add(@"C:\fake\today.txt");
        devices.Files.FolderEntries.Add(@"C:\fake\last_month.txt");
        devices.Files.Facts[@"C:\fake\today.txt"] = (12, DateTimeOffset.Now.AddHours(-2));
        devices.Files.Facts[@"C:\fake\last_month.txt"] = (12, DateTimeOffset.Now.AddDays(-30));

        var (_, _, store) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("pattern", "*"),
                Param("withinCount", "1"), Param("withinUnit", "days"),
                Returns("files=files")),
        ], devices);

        var files = store.Local.Values["files"];
        Assert.Single(files.Items);
        Assert.Equal(@"C:\fake\today.txt", files.Items[0].AsText());
    }

    [Fact]
    public async Task A_listing_can_be_ordered_and_weeded_by_size()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.FolderEntries.Add(@"C:\fake\small.txt");
        devices.Files.FolderEntries.Add(@"C:\fake\big.txt");
        devices.Files.Facts[@"C:\fake\small.txt"] = (100, DateTimeOffset.Now);
        devices.Files.Facts[@"C:\fake\big.txt"] = (5_000_000, DateTimeOffset.Now);

        var (_, _, store) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("pattern", "*"),
                Param("sortBy", "size"), Param("descending", "true"),
                Param("minKb", "2"), Returns("files=files")),
        ], devices);

        var files = store.Local.Values["files"];
        Assert.Single(files.Items);
        Assert.Equal(@"C:\fake\big.txt", files.Items[0].AsText());

        var (_, _, notSorted) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("pattern", "*"),
                Param("sortBy", "size"), Returns("files=files")),
        ], devices);

        Assert.Equal(@"C:\fake\small.txt", notSorted.Local.Values["files"].Items[0].AsText());
    }

    [Fact]
    public async Task A_listing_can_be_given_as_paths_inside_the_folder()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.FolderEntries.Add(@"G:\fake\reports\a.txt");
        var (_, _, store) = await RunAsync(
        [
            Step("file.listFiles", Param("folder", "reports"), Param("pattern", "*"),
                Param("relative", "true"), Returns("files=files")),
        ], devices);

        Assert.Equal("a.txt", store.Local.Values["files"].Items[0].AsText());
    }

    [Fact]
    public async Task A_listing_looks_no_deeper_than_it_was_told_to()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("pattern", "*"),
                Param("recurse", "true"), Param("depth", "2"), Returns("files=files")),
        ], devices);

        Assert.Contains(@"listFiles . * True 2", devices.Calls);
    }

    [Fact]
    public async Task A_listing_can_ask_for_several_kinds_of_file_at_once()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("file.listFiles", Param("folder", "."), Param("pattern", "*.txt; report?.csv"),
                Returns("files=files")),
        ], devices);

        Assert.Contains(@"listFiles . *.txt;report?.csv False 0", devices.Calls);
    }
}
