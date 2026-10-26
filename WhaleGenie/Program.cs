using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.Mcp;

namespace WhaleGenie;

sealed class Program
{
    /// <summary>
    /// The folder a published copy keeps its native libraries in, so the folder the user unzips
    /// holds the exe, the driver it needs beside it, and one folder of libraries instead of sixty
    /// files side by side. The managed assemblies are inside the single-file exe.
    /// </summary>
    private const string LibraryFolder = "lib";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
            AppDiagnostics.Record("process.unhandled",
                error.ExceptionObject as Exception ?? new Exception(error.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, error) =>
        {
            AppDiagnostics.Record("process.task", error.Exception);
            error.SetObserved();
        };

        // Hooking has to stay in its own method: the moment Main itself names a type from
        // Avalonia, compiling Main needs Avalonia loaded, and the hook would be too late.
        HookLibraryFolder();

        // A script a macro compiled is run by this very program, started a second time: it gets
        // the folder of libraries the same way, because what the program itself carries is what a
        // script written beside it may name, and then it runs the script and ends. It never builds
        // a window, never takes a place in the notification area and never touches the devices.
        if (ScriptHost.Asked(args))
        {
            return ScriptHost.Run(args);
        }

        // A script of a language that cannot write to a pipe of its own is served by this program
        // started a second time, which does the talking for it. Like the script host it runs before
        // anything of the window is built and never touches it.
        if (ScriptBridge.Asked(args))
        {
            return ScriptBridge.Run(args);
        }

        // The doctor is a client, not a host: it talks to an MCP server over the loopback and
        // prints what it found, so it belongs here where nothing of the window has been built yet.
        if (McpCommandLine.DoctorAsked(args))
        {
            return McpDoctor.Run(args);
        }

        // And this one is a host with no window: the interface still has to exist, because the
        // project lives on its thread, but nothing is ever shown.
        if (McpCommandLine.HostAsked(args))
        {
            var options = McpCommandLine.ParseHost(args, out var error);
            if (options is null)
            {
                Console.Error.WriteLine($"whalegenie: {error}");
                Console.Error.WriteLine(McpCommandLine.Usage);
                return 2;
            }

            App.Headless = options;
            return RunHeadless();
        }

        Run(args);
        return 0;
    }

    /// <summary>
    /// Runs with no window. The lifetime is asked to end only when told to, because a lifetime that
    /// ends with its last window would end immediately when there is no window at all.
    /// </summary>
    private static int RunHeadless()
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);

    private static void Run(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // The virtual keyboard, mouse and controller this program put on the machine come off it
        // again, and the USB server it runs inside itself is closed: the user asked for driver-level
        // input while using WhaleGenie, not for virtual devices left plugged into their machine
        // afterwards. The program's own device layer is what knows about them, so that is where this
        // is asked.
        try
        {
            SharedDevices.Current.Driver.Stop();
        }
        finally
        {
            // The desktop lifetime has ended and every owned resource has had its turn. The MCP
            // server is stopped first by App.Exit; this last exit makes sure a Kestrel or client
            // thread that did not release its socket cannot leave a windowless WhaleGenie process
            // behind.
            Environment.Exit(0);
        }
    }

    /// <summary>
    /// Points native library lookups at <c>lib</c> when it is there. A source checkout and the
    /// flat layout have no such folder and go straight through as before.
    /// </summary>
    private static void HookLibraryFolder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, LibraryFolder);
        if (!Directory.Exists(folder))
        {
            return;
        }

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => ResolveLibrary(folder, name);

        // Skia, HarfBuzz and the library the input hook and the injector come from ask Windows for
        // their own native library by name and never go through the managed resolver, so the folder
        // also has to be in the search path Windows uses.
        AddDllDirectory(folder);
    }

    private static IntPtr ResolveLibrary(string folder, string name)
    {
        foreach (var candidate in new[] { name, $"{name}.dll" })
        {
            var path = Path.Combine(folder, candidate);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
