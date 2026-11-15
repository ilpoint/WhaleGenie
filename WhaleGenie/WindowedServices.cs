using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie;

/// <summary>
/// The one registration a run with a window needs and a run without one must not compile into its
/// path: the window itself. Keeping it in its own method is what lets the headless entry point build
/// the same graph without touching Avalonia.
/// </summary>
internal static class WindowedServices
{
    internal static void Add(ServiceCollection services)
        =>
        // The view model the window shows is the same instance the MCP server reads.
        services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<TriggerRuntime>())
        {
            DataContext = sp.GetRequiredService<MainViewModel>(),
        });
}
