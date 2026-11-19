using System;
using System.Linq;
using WhaleGenie.Core.Execution;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A trial look is the editor's "test" button beside a vision or OCR step. It is a second table of
/// action keys beside the execution dispatch, so the two can drift apart without anything failing:
/// a new action would run in a macro and show nothing when tried out. The table is held against the
/// catalogue here instead.
/// </summary>
public class ActionPreviewTests
{
    [Fact]
    public void Every_vision_and_OCR_action_can_be_previewed()
    {
        var catalog = ActionSemanticsCatalog.All
            .Where(action => action.Category is ActionCategory.Vision or ActionCategory.Ocr)
            .Select(action => action.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var previewed = MacroRunner.PreviewedActions
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(catalog, previewed);
    }
}
