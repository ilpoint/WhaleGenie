using System;
using System.IO;
using WhaleGenie.Web;

namespace WhaleGenie.Tests;

/// <summary>
/// The shell may load its own local page or the debug dev server, and nothing else. The cases that
/// look almost like an allow — a sibling folder with the same prefix, a look-alike host — are the
/// ones this check exists for.
/// </summary>
public class WebShellNavigationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "whalegenie-webui");

    [Fact]
    public void The_local_page_and_its_subpaths_are_allowed()
    {
        Assert.True(WebShellNavigation.IsAllowed(new Uri(Path.Combine(Root, "index.html")), null, Root));
        Assert.True(WebShellNavigation.IsAllowed(new Uri(Path.Combine(Root, "assets", "x.js")), null, Root));
        Assert.True(WebShellNavigation.IsAllowed(new Uri("about:blank"), null, Root));
    }

    [Fact]
    public void A_sibling_folder_that_shares_the_prefix_is_not_inside()
    {
        Assert.False(WebShellNavigation.IsAllowed(new Uri(Path.Combine(Root + "-evil", "index.html")), null, Root));
        Assert.False(WebShellNavigation.IsAllowed(
            new Uri(Path.Combine(Path.GetDirectoryName(Root)!, "elsewhere", "index.html")), null, Root));
    }

    [Fact]
    public void Other_schemes_and_about_pages_are_not_the_page()
    {
        Assert.False(WebShellNavigation.IsAllowed(new Uri("https://example.com/"), null, Root));
        Assert.False(WebShellNavigation.IsAllowed(new Uri("about:config"), null, Root));
    }

    [Fact]
    public void The_dev_server_matches_an_origin_not_a_string_prefix()
    {
        var dev = new Uri("http://localhost:5173");
        Assert.True(WebShellNavigation.IsAllowed(new Uri("http://localhost:5173/index.html"), dev, Root));
        Assert.False(WebShellNavigation.IsAllowed(new Uri("http://localhost:5174/index.html"), dev, Root));
        Assert.False(WebShellNavigation.IsAllowed(new Uri("https://localhost:5173/index.html"), dev, Root));
        Assert.False(WebShellNavigation.IsAllowed(new Uri("http://127.0.0.1:5173/index.html"), dev, Root));
        Assert.False(WebShellNavigation.IsAllowed(new Uri("http://localhost.evil.example:5173/"), dev, Root));
    }
}
