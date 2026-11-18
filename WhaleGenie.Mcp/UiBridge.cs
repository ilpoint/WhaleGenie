using System;
using System.Threading.Tasks;

namespace WhaleGenie.Mcp;

/// <summary>
/// The one thing a tool needs from the interface thread: run a function there and bring the answer
/// back. The windowed program uses Avalonia's dispatcher; a server with no window has nothing to
/// marshal to and answers on the calling thread.
/// </summary>
public interface IUiDispatcher
{
    Task<T> InvokeAsync<T>(Func<T> work);
}

/// <summary>The dispatcher of a run with no window: the call is the work.</summary>
public sealed class DirectUiDispatcher : IUiDispatcher
{
    public static DirectUiDispatcher Instance { get; } = new();

    public Task<T> InvokeAsync<T>(Func<T> work) => Task.FromResult(work());
}

/// <summary>
/// Putting this program's own windows out of the way for the length of a screen capture. A run with
/// no window has none to move, so its answer is to take the picture as it stands.
/// </summary>
public interface IWindowAside
{
    Task<T> WithoutOwnWindows<T>(Func<T> work);
}

/// <summary>The aside of a run with no window: there is nothing to hide.</summary>
public sealed class NoWindowAside : IWindowAside
{
    public static NoWindowAside Instance { get; } = new();

    public Task<T> WithoutOwnWindows<T>(Func<T> work) => Task.FromResult(work());
}
