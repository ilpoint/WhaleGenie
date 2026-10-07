using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Avalonia;

namespace Viktor;

sealed class Program
{
    /// <summary>
    /// The folder a published copy keeps its assemblies in, so the folder the user unzips holds
    /// the exe and one folder of dlls instead of sixty files side by side.
    /// </summary>
    private const string LibraryFolder = "lib";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Hooking has to stay in its own method: the moment Main itself names a type from
        // Avalonia, compiling Main needs Avalonia loaded, and the hook would be too late.
        HookLibraryFolder();
        Run(args);
    }

    private static void Run(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Points assembly and native library lookups at <c>lib</c> when it is there. A source
    /// checkout and the flat layout have no such folder and go straight through as before.
    /// </summary>
    private static void HookLibraryFolder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, LibraryFolder);
        if (!Directory.Exists(folder))
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, name) => ResolveAssembly(folder, name);
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) => ResolveLibrary(folder, name);

        // Skia, HarfBuzz and SharpHook ask Windows for their library by name and never go through
        // the managed resolver, so the folder also has to be in the search path Windows uses.
        AddDllDirectory(folder);
    }

    private static Assembly? ResolveAssembly(string folder, AssemblyName name)
    {
        var path = Path.Combine(folder, $"{name.Name}.dll");
        return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
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
