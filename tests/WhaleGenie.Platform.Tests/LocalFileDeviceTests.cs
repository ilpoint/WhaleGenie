using System.IO;

using WhaleGenie.Platform.Windows;

namespace WhaleGenie.Platform.Tests;

/// <summary>
/// The file device's own packing. Whether the folder goes into the archive under its own name is a
/// question about what lands on disk, so it is asked of the real device rather than the stand-in.
/// </summary>
public class LocalFileDeviceTests
{
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
}
