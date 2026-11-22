using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Editing;
using WhaleGenie.Views;

namespace WhaleGenie.Editing;

/// <summary>The native page highlight behind the editor bridge's browser test port.</summary>
internal sealed class EditorBrowserTestHost : IEditorBrowserTestHost
{
    public Task<HighlightResult> HighlightAsync(IBrowserDevice browser, PageTarget target)
        => BrowserPicker.HighlightAsync(browser, target);
}
