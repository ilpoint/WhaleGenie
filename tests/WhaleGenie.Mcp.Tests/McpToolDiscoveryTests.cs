using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Devices;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Mcp.Tests;

/// <summary>
/// The server finds its tools by scanning the assemblies it was given rather than by being handed a
/// list of instances. A list was a second place to register a tool, and the one that was left out
/// disappeared without a word — the checks that call tools stayed green because none of them asked
/// what the server offers.
/// </summary>
public class McpToolDiscoveryTests
{
    [Fact]
    public void A_tool_class_the_container_never_registered_is_still_offered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new McpDraftStore());
        services.AddSingleton<IProjectGateway>(new UnusedGateway());
        services.AddSingleton<IWindowAside>(NoWindowAside.Instance);
        services.AddSingleton<Func<IDeviceLayer>>(_ => NoDevice);
        using var container = services.BuildServiceProvider();

        // No tool class is registered: the scan finds the classes, and each is built from what the
        // container does hold. ActivatorUtilities is the path that makes that work for a tool whose
        // constructor takes a factory or a window aside rather than a registered instance.
        var host = new McpServerHost(container, [typeof(McpTools).Assembly]);
        var offered = host.Tools()
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var written = typeof(McpTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Every marked method, once: a tool class that contributed one of its methods and not the
        // rest, or the same class twice, shows up as a missing or repeated name.
        Assert.Equal(written, offered);
        Assert.Contains("whalegenie_open_project", offered);
        Assert.Contains("whalegenie_mouse_click", offered);
        Assert.DoesNotContain("whalegenie_run_macro", offered);
    }

    /// <summary>A factory that is never called: this check builds the tools, it does not run them.</summary>
    private static IDeviceLayer NoDevice()
        => throw new InvalidOperationException("This check never reaches the machine.");

    /// <summary>The project side, answering nothing: the tool classes are built, never called.</summary>
    private sealed class UnusedGateway : IProjectGateway
    {
        public Task<McpProjectStatus> StatusAsync() => throw new NotSupportedException();

        public Task<McpProjectSnapshot> SnapshotAsync() => throw new NotSupportedException();

        public Task<(McpProjectSnapshot Snapshot, bool Dirty)> ReadAsync()
            => throw new NotSupportedException();

        public Task<int> ApplyAsync(McpProjectSnapshot basis, IReadOnlyList<MacroItem> macros,
            IReadOnlyList<VariableDefinition> globals) => throw new NotSupportedException();

        public Task<int> ApplyAsync(McpProjectSnapshot basis, IReadOnlyList<MacroItem> macros,
            ProjectTriggers triggers, IReadOnlyList<VariableDefinition> globals)
            => throw new NotSupportedException();

        public Task<string> SaveAsync() => throw new NotSupportedException();
    }
}
