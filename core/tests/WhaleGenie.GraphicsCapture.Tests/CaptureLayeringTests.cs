using System;
using System.Linq;

namespace WhaleGenie.GraphicsCapture.Tests;

/// <summary>
/// Which direction this project is built in. The shapes it works in belong to the engine's device
/// contracts, so the engine must not be replaced by it and the program's own adapters must not be
/// pulled in behind it: a program that only takes pictures of the screen has no use for the input
/// hook or the browser, and that is the whole reason this is a package of its own.
/// </summary>
public class CaptureLayeringTests
{
    [Fact]
    public void The_capture_library_is_built_on_the_engine()
    {
        var referenced = typeof(ScreenGrabber).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        Assert.Contains("WhaleGenie.Core", referenced);
        Assert.DoesNotContain("WhaleGenie.Platform.Windows", referenced);
    }
}
