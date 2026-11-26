using System;
using System.Collections.Generic;
using System.Linq;

using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The order a folder listing is handed over in: one key sorted on, the name settling every tie, and
/// the whole order turned round when the step asks for the other way — ties included, which is what
/// keeps two files that agree on the key in the same order every run.
/// </summary>
public class FileOrderTests
{
    private static FileEntry File(string name, long size = 0, int day = 1, int modified = 1) => new(
        "dir\\" + name, name, "dir", size,
        new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, modified, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private static string[] Names(IEnumerable<FileEntry> files) => [.. files.Select(entry => entry.Name)];

    private static List<FileEntry> Listing() =>
    [
        File("delta.md", size: 30, day: 3, modified: 2),
        File("alpha.txt", size: 10, day: 1, modified: 4),
        File("charlie.md", size: 20, day: 2, modified: 1),
        File("bravo.txt", size: 10, day: 4, modified: 3),
    ];

    [Fact]
    public void A_key_sorts_and_the_name_settles_every_tie()
    {
        Assert.Equal(["alpha.txt", "bravo.txt", "charlie.md", "delta.md"],
            Names(FileOrder.Of(Listing(), "size", descending: false)));

        Assert.Equal(["charlie.md", "delta.md", "bravo.txt", "alpha.txt"],
            Names(FileOrder.Of(Listing(), "modified", descending: false)));

        Assert.Equal(["alpha.txt", "charlie.md", "delta.md", "bravo.txt"],
            Names(FileOrder.Of(Listing(), "created", descending: false)));
    }

    [Fact]
    public void A_key_nobody_writes_is_the_name()
        => Assert.Equal(["alpha.txt", "bravo.txt", "charlie.md", "delta.md"],
            Names(FileOrder.Of(Listing(), "nobody", descending: false)));

    [Fact]
    public void The_extension_is_compared_without_case()
    {
        var files = new List<FileEntry> { File("a.TXT"), File("b.md"), File("c.Md") };

        Assert.Equal(["b.md", "c.Md", "a.TXT"],
            Names(FileOrder.Of(files, "extension", descending: false)));
    }

    [Fact]
    public void Going_the_other_way_turns_the_whole_order_round_ties_included()
    {
        // The two files of size 10 agree on the key, so the name that settled them is turned round
        // as well: the listing is the same one read backwards, and reads the same way every run.
        Assert.Equal(["delta.md", "charlie.md", "bravo.txt", "alpha.txt"],
            Names(FileOrder.Of(Listing(), "nobody", descending: true)));
        Assert.Equal(["delta.md", "charlie.md", "bravo.txt", "alpha.txt"],
            Names(FileOrder.Of(Listing(), "size", descending: true)));
    }

    [Fact]
    public void The_same_listing_comes_out_the_same_way_twice()
    {
        var files = Listing();

        Assert.Equal(
            Names(FileOrder.Of(files, "size", descending: false)),
            Names(FileOrder.Of(files, "size", descending: false)));
    }
}
