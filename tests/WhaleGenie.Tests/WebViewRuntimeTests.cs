using WhaleGenie.Web;

namespace WhaleGenie.Tests;

/// <summary>
/// A missing WebView2 runtime must be a clear answer before a window is built, not a white frame
/// after one. The probe itself is one registry read; the reader is replaceable for the check.
/// </summary>
public class WebViewRuntimeTests
{
    [Fact]
    public void A_version_means_the_runtime_is_there()
    {
        var saved = WebViewRuntime.VersionReader;
        try
        {
            WebViewRuntime.VersionReader = () => "120.0.2210.91";
            Assert.True(WebViewRuntime.IsInstalled);

            WebViewRuntime.VersionReader = () => null;
            Assert.False(WebViewRuntime.IsInstalled);

            WebViewRuntime.VersionReader = () => "   ";
            Assert.False(WebViewRuntime.IsInstalled);
        }
        finally
        {
            WebViewRuntime.VersionReader = saved;
        }
    }
}
