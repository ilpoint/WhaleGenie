using WhaleGenie.Core.Expressions;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A picture is a value of its own: the handle it is known by, not the line that describes it. A
/// capture handed to a later step is a picture even though nothing but its name travels with it.
/// </summary>
public class ValueImageTests
{
    [Fact]
    public void An_image_is_not_a_number()
    {
        Assert.Throws<ExpressionException>(() => Value.FromImage("<image 800x600>").AsNumber());
    }

    [Fact]
    public void An_image_is_not_the_text_that_names_it()
    {
        var picture = Value.FromImage("<image 800x600>");
        var line = Value.FromText("<image 800x600>");

        Assert.NotEqual(picture, line);
        Assert.Equal(picture, Value.FromImage("<image 800x600>"));
    }

    [Fact]
    public void A_picture_survives_a_round_trip_through_json()
    {
        var picture = Value.FromImage("<image 800x600>");

        var read = JsonValues.FromJson(JsonValues.Node(picture));

        Assert.True(read.IsImage);
        Assert.Equal("<image 800x600>", read.AsText());
    }
}
