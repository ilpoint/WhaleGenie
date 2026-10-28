using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Mcp;

namespace WhaleGenie.Tests;

/// <summary>
/// The server has to be disposable the way a container disposes what it built: synchronously. A
/// service that answered only <c>IAsyncDisposable</c> made the container throw while the program was
/// leaving, and that throw cut the exit short before Program took the virtual keyboard and mouse off
/// the machine — which showed up as a pointer that stutters for a few seconds after the window has
/// gone.
/// </summary>
public class McpLifecycleTests
{
    [Fact]
    public void The_container_disposes_the_server_it_built()
    {
        var services = AppServices.Build(windowed: false);
        _ = services.GetRequiredService<McpServerHost>();

        // Throwing here is what the exit cannot afford: the device layer is released after this, and
        // Program's driver stop comes after that.
        services.Dispose();
    }
}
