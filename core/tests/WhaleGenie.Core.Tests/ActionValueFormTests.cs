using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A picture is a whole value — a file path or the name of a picture an earlier step saved — not
/// text with names standing between its pieces. The editor reads this form to decide which value
/// window to open, so the catalogue has to say it.
/// </summary>
public class ActionValueFormTests
{
    [Fact]
    public void Picture_fields_hold_whole_values()
    {
        var pictures = ActionSemanticsCatalog.All
            .SelectMany(action => action.Parameters.Select(field => new { action.Key, Field = field }))
            .Where(entry => entry.Field.Shape == ActionValueShape.Image
                || entry.Field.Picker == ActionPickerKind.Image)
            .ToList();

        Assert.NotEmpty(pictures);
        Assert.All(pictures, entry => Assert.Equal(ActionValueForm.Value, entry.Field.ValueForm));
    }
}
