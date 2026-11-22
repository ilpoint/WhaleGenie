using System;
using System.Threading.Tasks;

namespace WhaleGenie.Runtime;

/// <summary>The interface-thread hop the run model needs; a check runs inline.</summary>
public interface IUiPost
{
    void Post(Action action);

    Task<T> InvokeAsync<T>(Func<Task<T>> work);
}

/// <summary>Runs posted work on the calling thread; the default for a model built without a window.</summary>
public sealed class InlineUiPost : IUiPost
{
    public static InlineUiPost Instance { get; } = new();

    public void Post(Action action) => action();

    public Task<T> InvokeAsync<T>(Func<Task<T>> work) => work();
}
