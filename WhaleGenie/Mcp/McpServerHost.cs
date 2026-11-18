using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Mcp;

/// <summary>
/// The MCP server, speaking over the two streams it is handed: standard input and output when the
/// program is started by a client, a loopback pair of in-memory pipes in a check. There is no port
/// and no listener — a client owns the pipe, launches this program, and closes the pipe to end it.
/// That is what keeps a local helper local: nothing starts listening unless somebody asked.
/// </summary>
public sealed class McpServerHost : IAsyncDisposable, IDisposable
{
    private readonly MainViewModel _project;
    private readonly IServiceProvider _services;
    private readonly bool _ownsServices;
    private IHost? _host;
    private Task? _session;

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

    public bool IsRunning => _host is not null;

    /// <summary>
    /// Starts speaking over <paramref name="input"/> and <paramref name="output"/>. A one-session
    /// server runs for as long as the session does, so starting it cannot be awaited: the caller has
    /// a client to create, and the client would be waiting for a server that is waiting for it to
    /// say something. This hands back as soon as the graph is built and the session is under way.
    /// </summary>
    public void Start(Stream input, Stream output)
    {
        if (_host is not null)
        {
            return;
        }

        var host = Build(input, output);
        _host = host;
        _session = host.StartAsync();
    }

    /// <summary>
    /// Completes when the client closed its end of the pipe or the server was asked to stop — the
    /// one signal a stdio server has that nobody is listening any more. A server that failed to
    /// start says so here rather than at the moment it was handed the streams.
    /// </summary>
    public async Task WaitForShutdownAsync()
    {
        if (_session is { } session)
        {
            await session.ConfigureAwait(false);
        }

        if (_host is { } host)
        {
            await host.WaitForShutdownAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAsync()
    {
        if (_host is not { } host)
        {
            return;
        }

        _host = null;
        await StopHostAsync(host).ConfigureAwait(false);

        // The session task ends with the host; observing it here keeps a stopped server from
        // surfacing as an unobserved task exception later.
        if (_session is { } session)
        {
            _session = null;
            try
            {
                await session.ConfigureAwait(false);
            }
            catch
            {
                // A session that ended because it was stopped has nothing to report.
            }
        }
    }

    /// <summary>
    /// Stops the server without awaiting. The container disposes what it built synchronously, and a
    /// singleton that answered only <see cref="IAsyncDisposable"/> would make that throw — and the
    /// throw would cut the exit short before Program takes the virtual keyboard and mouse off the
    /// machine, which is what leaves the pointer stuttering.
    /// </summary>
    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        if (_ownsServices && _services is IDisposable services)
        {
            services.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_ownsServices && _services is IDisposable services)
        {
            services.Dispose();
        }
    }

    private static async Task StopHostAsync(IHost host)
    {
        // A client that went quiet still holds the pipe open. Two seconds lets the session finish
        // what it was doing; the process must not stay alive because somebody's editor crashed.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await host.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The streams and the session are disposed below either way.
        }
        catch
        {
            // A stop that failed still has to release the session; the process is leaving.
        }

        try
        {
            host.Dispose();
        }
        catch
        {
            // Nothing else can be done with a host that failed to release its own session.
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

    private IHost Build(Stream input, Stream output)
    {
        // The draft store and the bridge to the project belong to the run rather than to this
        // server: a tool and the window have to see the same drafts, and the project is one object.
        var drafts = _services.GetRequiredService<McpDraftStore>();
        var live = _services.GetRequiredService<McpLiveProject>();

        // A plain builder, not the console one: this host must not install a console lifetime that
        // handles Ctrl+C and ends the process, because it is not the process — the window or the
        // command line that owns these streams is. It also must not print: on these streams stdout
        // is the protocol, and a log line there reads as a malformed message.
        var builder = new HostBuilder();
        builder.ConfigureServices((_, services) =>
        {
            services.Configure<HostOptions>(options =>
                options.ShutdownTimeout = TimeSpan.FromSeconds(2));
            services.AddSingleton(drafts);
            services.AddSingleton(live);
            services.AddSingleton(_project);
            services.AddLogging(logging => logging.ClearProviders());
            services
                .AddMcpServer(options => options.ServerInstructions = McpPrompts.ServerInstructions)
                .WithStreamServerTransport(input, output)
                .WithTools(Tools())
                .WithResources<McpResources>()
                .WithPrompts<McpPrompts>();
        });

        return builder.Build();
    }
}
