using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace WhaleGenie.Runtime;

/// <summary>
/// The door the window keeps open for a client, and the bridge a client's own process becomes.
///
/// A client can only start a program and talk down that program's standard input and output, so it
/// can never reach a window that is already running — and the window is where the project the person
/// is editing lives. The window therefore holds a named pipe open; the program the client starts
/// finds it, forwards both directions unchanged, and lets the window answer. Nothing is filtered in
/// the middle: what the client sends is what the tools see.
/// </summary>
internal static class McpPipe
{
    /// <summary>
    /// The pipe's name. It carries the user name so two people on one machine do not meet, and the
    /// pipe itself is created for its owner only.
    /// </summary>
    internal static string Name { get; set; } = "whalegenie-mcp-" + Environment.UserName;

    /// <summary>How long a bridge looks for a window before deciding there is none to talk to.</summary>
    internal static readonly TimeSpan Lookup = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Takes one client at a time for as long as the window lives. A second client is not refused —
    /// it waits until the first is done, because one window holds one project and two sessions
    /// writing it at once is the conflict this whole arrangement exists to avoid.
    /// </summary>
    internal static void Serve(Func<Stream, Task> session)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(
                        Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // A door that cannot be built is not a reason to take the window down.
                    return;
                }

                try
                {
                    await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                    await session(pipe).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException
                    or ObjectDisposedException)
                {
                    // The client went away, or the window is closing: the next round waits again.
                }
                finally
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }
            }
        });
    }

    /// <summary>
    /// Becomes the bridge, if there is a window to bridge to. Returns false when nothing is
    /// listening, which is the case a client that started this program with no window open is in:
    /// the caller then serves the project itself, the way it did before.
    /// </summary>
    internal static bool TryBridge(Stream input, Stream output)
    {
        NamedPipeClientStream pipe;
        try
        {
            pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect((int)Lookup.TotalMilliseconds);
        }
        catch (Exception error) when (error is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        using (pipe)
        {
            var up = Task.Run(async () =>
            {
                try
                {
                    await input.CopyToAsync(pipe).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                {
                    // The window closed the door while a request was on its way.
                }
                finally
                {
                    // The client has no more to say: closing this side is what tells the window the
                    // session is over, and the window's tools are released for the next client.
                    try
                    {
                        pipe.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Already gone.
                    }
                }
            });

            var down = Task.Run(async () =>
            {
                try
                {
                    await pipe.CopyToAsync(output).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                {
                    // The window went away; there is nobody left to answer.
                }
            });

            Task.WaitAny(up, down);
        }

        return true;
    }
}
