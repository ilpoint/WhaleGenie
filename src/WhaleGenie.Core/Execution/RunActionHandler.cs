using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One action, answered by a family that has left the runner: the run it belongs to, the step, how
/// deep it sits, and the token that ends it. It is a delegate because a handler is a piece of work
/// rather than an object with a life of its own.
/// </summary>
internal delegate Task<Signal> RunActionHandler(IRunContext run, ExecutableStep step, int depth,
    CancellationToken token);
