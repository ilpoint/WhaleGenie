using System;
using System.IO;
using WhaleGenie.Editing;
using Xunit;

namespace WhaleGenie.Tests;

public class ActionImagePreviewTests
{
    private const string OnePixel =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL5WQAAAABJRU5ErkJggg==";

    [Fact]
    public void A_picture_becomes_a_data_url_and_a_missing_file_explains_itself()
    {
        var folder = Directory.CreateTempSubdirectory("wg-preview-");
        try
        {
            var file = Path.Combine(folder.FullName, "shot.png");
            File.WriteAllBytes(file, Convert.FromBase64String(OnePixel));
            var previews = new ActionImagePreviews();

            var found = previews.For(file, folder.FullName);
            Assert.NotNull(found);
            Assert.StartsWith("data:image/png;base64,", found!.DataUrl);
            Assert.Equal("shot.png", found.FileName);
            Assert.Empty(found.Problem);

            var missing = previews.For("missing.png", folder.FullName);
            Assert.NotNull(missing);
            Assert.Empty(missing!.DataUrl);
            Assert.NotEmpty(missing.Problem);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
