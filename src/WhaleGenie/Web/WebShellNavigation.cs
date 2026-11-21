using System;
using System.IO;

namespace WhaleGenie.Web;

/// <summary>
/// What the shell may navigate to. The page is a local file or the debug dev server; anything else
/// would turn the interface into a browser. This is a pure decision so the allow and refuse cases
/// can be checked without a window or a WebView.
/// </summary>
internal static class WebShellNavigation
{
    /// <summary>
    /// Whether <paramref name="request"/> may be loaded. The root is the webui folder; a file is
    /// allowed only when it resolves inside that folder, not when its path merely starts with the
    /// same characters. The dev server is matched by origin, not by a string prefix, so a host such
    /// as localhost.evil is a different origin.
    /// </summary>
    public static bool IsAllowed(Uri request, Uri? devServer, string root)
    {
        try
        {
            if (request.Scheme == "about")
            {
                return string.Equals(request.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase);
            }

            if (devServer is not null && SameOrigin(request, devServer))
            {
                return true;
            }

            if (!request.IsFile)
            {
                return false;
            }

            var rootPath = Path.GetFullPath(root);
            var local = Path.GetFullPath(request.LocalPath);
            var relative = Path.GetRelativePath(rootPath, local);
            return relative.Length > 0
                && !relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SameOrigin(Uri request, Uri allowed)
        => string.Equals(request.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(request.Host, allowed.Host, StringComparison.OrdinalIgnoreCase)
           && request.Port == allowed.Port;
}
