using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;

namespace WhaleGenie.Tests;

/// <summary>
/// The page's commands and the program's registered methods are one list. A handler that is gone
/// from the program but still called by a page is a dead button; a handler the program added and
/// nobody calls is a door with no sign. The page may also use bridge.ready, which is the client
/// handshake and not a registered method.
/// </summary>
public class BridgeMethodInventoryTests
{
    private static readonly HashSet<string> Expected = [.. BridgeMethods.All];

    [Fact]
    public void The_registered_methods_are_the_page_inventory()
    {
        using var services = WindowedServices.Build();
        var server = services.GetRequiredService<BridgeServer>();

        var missing = Expected.Except(server.Methods).Order(StringComparer.Ordinal).ToList();
        var extra = server.Methods.Except(Expected).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            "missing: " + string.Join(", ", missing) + " | extra: " + string.Join(", ", extra));
    }

    [Fact]
    public void The_page_calls_only_registered_methods()
    {
        var called = new HashSet<string>(StringComparer.Ordinal);
        // The old pattern only saw .call('method'); it missed .call<T>('method') and the method
        // names handed to the editor's command helpers. Scan call sites instead of every dotted
        // literal so action-catalogue keys such as window.activate do not look like bridge methods.
        var callPattern = new Regex(@"\.call(?:\s*<[^>]*>)?\s*\(\s*['""]([a-zA-Z.]+)['""]",
            RegexOptions.CultureInvariant);
        var helperPattern = new Regex(
            @"\.(?:snapshotCommand|actionCommand|declaration|trigger)\(\s*['""]([a-zA-Z.]+)['""]",
            RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(RepoPaths.Root, "webui", "src"), "*.ts", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in new[] { callPattern, helperPattern })
            {
                foreach (Match match in pattern.Matches(text))
                {
                    var method = match.Groups[1].Value;
                    if (method != "bridge.ready")
                    {
                        called.Add(method);
                    }
                }
            }
        }

        Assert.Empty(called.Except(Expected));
    }
}
