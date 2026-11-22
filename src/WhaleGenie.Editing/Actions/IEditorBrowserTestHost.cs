using System.Threading.Tasks;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Editing;

/// <summary>The native page highlight a browser test uses; the editor only needs the answer.</summary>
public interface IEditorBrowserTestHost
{
    Task<HighlightResult> HighlightAsync(IBrowserDevice browser, PageTarget target);
}
