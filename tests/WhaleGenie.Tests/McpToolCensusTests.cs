using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using WhaleGenie.Mcp;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The census of tools is taken from the code rather than from a list kept beside it. Every method
/// marked as a tool declares exactly one audit behaviour, has one unique name, and is exactly what
/// a client is offered — the three ways a tool could go missing, be recorded by nobody, or be listed
/// twice.
/// </summary>
public class McpToolCensusTests
{
    [Fact]
    public void What_the_code_declares_is_what_the_server_offers()
    {
        Ui.RunAsync(async () =>
        {
            var methods = Marked();
            Assert.NotEmpty(methods);

            // One declaration each. Of() is the reader the server itself uses, so a tool that says
            // nothing or says two things stops the server rather than shipping an unrecorded call.
            foreach (var method in methods)
            {
                Assert.NotNull(McpToolDeclaration.Of(method));
            }

            var named = methods.Select(Name).ToList();
            Assert.Equal(named.Count, named.Distinct(StringComparer.Ordinal).Count());

            await using var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();
            var offered = (await client.ListToolsAsync()).Select(tool => tool.Name).ToList();

            Assert.Equal(named.OrderBy(name => name, StringComparer.Ordinal),
                offered.OrderBy(name => name, StringComparer.Ordinal));

            // Both scanned assemblies are in the census, not only the one this check owns.
            Assert.Contains("whalegenie_open_project", offered);
            Assert.Contains("whalegenie_mouse_click", offered);
            Assert.Contains("whalegenie_run_macro", offered);

            await loop.StopAsync();
            return true;
        });
    }

    /// <summary>
    /// Every method the server would find: the same two assemblies and the same filter, because a
    /// census taken another way would agree with a server that had stopped finding something.
    /// </summary>
    private static IReadOnlyList<MethodInfo> Marked()
        => new[] { typeof(McpTools).Assembly, typeof(McpRunTools).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

    /// <summary>The name a client sees; a tool without one is a tool named after its method, which
    /// is not what a client was promised, so it fails here rather than being quietly accepted.</summary>
    private static string Name(MethodInfo method)
    {
        var name = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
        Assert.False(string.IsNullOrEmpty(name),
            $"{method.DeclaringType?.Name}.{method.Name} is marked as a tool without a name.");
        return name!;
    }
}
