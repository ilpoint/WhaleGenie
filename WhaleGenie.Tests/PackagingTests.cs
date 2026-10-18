using System.Xml.Linq;

namespace WhaleGenie.Tests;

/// <summary>
/// The solution, the project and the packaging script decide what a released copy looks like.
/// These checks keep the two decisions a user sees — x64 only, and one executable with a lib
/// folder — from drifting apart between the three files.
/// </summary>
public class PackagingTests
{
    [Fact]
    public void The_solution_offers_only_x64()
    {
        var solution = XDocument.Load(Path.Combine(Repository(), "WhaleGenie.slnx"));
        var platforms = solution.Root!
            .Element("Configurations")!
            .Elements("Platform")
            .Select(platform => platform.Attribute("Name")!.Value)
            .ToArray();

        Assert.Equal(new[] { "x64" }, platforms);
        Assert.DoesNotContain("Any CPU", File.ReadAllText(Path.Combine(Repository(), "WhaleGenie.slnx")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_application_is_published_as_one_executable()
    {
        var project = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "WhaleGenie.csproj"));

        Assert.Contains("<PublishSingleFile", project, StringComparison.Ordinal);
        Assert.Contains("<SatelliteResourceLanguages>zh-Hans</SatelliteResourceLanguages>", project,
            StringComparison.Ordinal);
        Assert.Contains("DropPublishSymbols", project, StringComparison.Ordinal);
        Assert.Contains("MoveNativeLibrariesIntoLib", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_portable_package_keeps_only_native_libraries_in_lib()
    {
        var script = File.ReadAllText(Path.Combine(Repository(), "build", "package.ps1"));

        Assert.Contains("PublishSingleFile=true", script, StringComparison.Ordinal);
        Assert.Contains("Get-ChildItem $app -File -Filter *.dll | Move-Item -Destination $lib", script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("WhaleGenie.deps.json", script, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("the repository root was not found above the test output");
    }
}
