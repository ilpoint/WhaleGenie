using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Mcp;

/// <summary>
/// The local HTTP MCP server. It exists only while the MCP switch is on, binds one loopback port
/// and has no token: it is a helper on this machine, not a service for the network.
/// </summary>
public sealed class McpServerHost : IAsyncDisposable
{
    private readonly MainViewModel _project;
    private readonly IServiceProvider _services;
    private readonly bool _ownsServices;
    private WebApplication? _app;
    private string _endpoint = string.Empty;

    /// <summary>
    /// A server over a project, wiring up the few things it needs itself. This is the constructor a
    /// check uses: it hands in a project and gets the same object graph the program builds.
    /// </summary>
    public McpServerHost(MainViewModel project)
        : this(project, AppServices.Minimal(project), ownsServices: true)
    {
    }

    /// <summary>
    /// A server that takes what it needs from the run's own container, so a tool and the window
    /// share one draft store rather than one each.
    /// </summary>
    internal McpServerHost(MainViewModel project, IServiceProvider services, bool ownsServices = false)
    {
        _project = project;
        _services = services;
        _ownsServices = ownsServices;
    }

    /// <summary>The address an MCP client connects to, or empty while the server is stopped.</summary>
    public string Endpoint => _endpoint;

    public bool IsRunning => _app is not null;

    public async Task StartAsync(int preferredPort = 3917)
    {
        if (_app is not null)
        {
            return;
        }

        WebApplication app;
        try
        {
            app = await StartOnAsync(preferredPort);
        }
        catch (IOException)
        {
            // The preferred port is taken by something else. The system can pick one instead; the
            // window shows the address it got.
            app = await StartOnAsync(0);
        }

        // The address has to be the one the listener is really holding. Asking for port 0 and then
        // reporting port 0 would hand out a client config that nobody can connect to.
        var address = AddressOf(app);
        if (address.Length == 0)
        {
            await StopAppAsync(app);
            throw new InvalidOperationException("The MCP server started but reported no address.");
        }

        _app = app;
        _endpoint = address.TrimEnd('/') + "/mcp";
    }

    public async Task StopAsync()
    {
        if (_app is not { } app)
        {
            return;
        }

        _app = null;
        _endpoint = string.Empty;
        await StopAppAsync(app);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_ownsServices && _services is IDisposable services)
        {
            services.Dispose();
        }
    }

    private static string AddressOf(WebApplication app)
        => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?.Addresses.FirstOrDefault(address =>
                address.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        ?? app.Urls.FirstOrDefault()
        ?? string.Empty;

    private static async Task StopAppAsync(WebApplication app)
    {
        // A connected MCP client can keep a stream open, and a graceful stop would wait for it.
        // The server belongs to this process, so two seconds is enough to let requests finish and
        // then the socket is closed; the process must not stay alive because a client went quiet.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await app.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // The listener and its client streams are disposed below.
        }
        catch
        {
            // A stop that failed still has to release the socket; the process is leaving.
        }

        try
        {
            await app.DisposeAsync();
        }
        catch
        {
            // Nothing else can be done with a host that failed to release its own socket.
        }
    }

    /// <summary>
    /// Every tool this server offers, each wrapped so a wrongly shaped argument is answered with a
    /// coded refusal instead of the runtime's message about the call failing. The owners come from
    /// the run's container, so a new tool class is registered once instead of being listed here as
    /// well — a list would go stale without saying anything.
    /// </summary>
    private IReadOnlyList<McpServerTool> Tools()
    {
        object[] owners =
        [
            _services.GetRequiredService<McpTools>(),
            _services.GetRequiredService<McpControlTools>(),
            _services.GetRequiredService<McpRunTools>(),
        ];

        var tools = new List<McpServerTool>();
        foreach (var owner in owners)
        {
            foreach (var method in owner.GetType().GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
                {
                    continue;
                }

                tools.Add(new McpTolerantTool(McpServerTool.Create(method, owner, null)));
            }
        }

        return tools;
    }

    private async Task<WebApplication> StartOnAsync(int port)
    {
        // The draft store and the bridge to the project belong to the run rather than to this
        // server: a tool and the window have to see the same drafts, and the project is one object.
        var drafts = _services.GetRequiredService<McpDraftStore>();
        var live = _services.GetRequiredService<McpLiveProject>();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });

        var loopback = IPAddress.Loopback.ToString();
        builder.WebHost.UseUrls($"http://{loopback}:{port}");
        builder.Configuration["AllowedHosts"] = $"{loopback};localhost";
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(options =>
            options.ShutdownTimeout = TimeSpan.FromSeconds(2));
        builder.Services.AddSingleton(drafts);
        builder.Services.AddSingleton(live);
        builder.Services.AddSingleton(_project);
        builder.Services
            .AddMcpServer(options => options.ServerInstructions = McpPrompts.ServerInstructions)
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools(Tools())
            .WithResources<McpResources>()
            .WithPrompts<McpPrompts>();

        var app = builder.Build();
        app.MapMcp("/mcp");
        try
        {
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }
}
