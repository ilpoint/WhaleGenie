using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>The Avalonia interface thread behind the run model's dispatcher port.</summary>
internal sealed class AvaloniaUiPost : IUiPost
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public async Task<T> InvokeAsync<T>(Func<Task<T>> work)
        => await Dispatcher.UIThread.InvokeAsync(work);
}
