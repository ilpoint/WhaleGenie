using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Mcp;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

/// <summary>Marshals a tool's work to the window's thread.</summary>
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public async Task<T> InvokeAsync<T>(Func<T> work)
        => await Dispatcher.UIThread.InvokeAsync(work);
}

/// <summary>
/// Steps this program's windows aside for one capture, on the thread that owns them. The aside is
/// released before the answer comes back, which is the same shape every picker uses.
/// </summary>
internal sealed class AvaloniaWindowAside : IWindowAside
{
    public async Task<T> WithoutOwnWindows<T>(Func<T> work)
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var aside = AppWindows.StepAside();
            return work();
        });
}
