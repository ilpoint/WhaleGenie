using System;
using System.Linq;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// What this suite may not drag in. It exists so a document check costs a second rather than a window
/// toolkit: a single reference to Avalonia — or to the window assembly — would put the interface back
/// in the path of every check here, and the reason to split the suites would quietly disappear.
/// </summary>
public class SuiteLayeringTests
{
    [Fact]
    public void The_suite_references_no_window_toolkit_and_no_window_assembly()
    {
        var referenced = typeof(SuiteLayeringTests).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
        Assert.DoesNotContain("WhaleGenie", referenced);
    }

    [Fact]
    public void The_application_layer_it_runs_is_the_one_without_a_window_toolkit()
    {
        var referenced = typeof(WhaleGenie.AppServices).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty);

        Assert.DoesNotContain(referenced, name => name.StartsWith("Avalonia", StringComparison.Ordinal));
    }
}
