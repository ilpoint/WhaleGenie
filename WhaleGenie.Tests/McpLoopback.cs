using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Mcp;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// A server and a client talking over a pair of in-memory pipes, which is how every MCP check runs
/// now that the transport is stdio: the pipe <c>--mcp</c> is given on the command line is the same
/// kind of thing, only attached to a real process. Nothing here listens on a port, so checks run
/// side by side without ports to pick or conflicts to avoid.
/// </summary>
internal sealed class McpLoopback : IAsyncDisposable
{
    private readonly LoopbackStream _toClient;
    private readonly LoopbackStream _toServer;

    private McpLoopback(McpServerHost host, LoopbackStream toServer, LoopbackStream toClient)
    {
        Host = host;
        _toServer = toServer;
        _toClient = toClient;
    }

    public McpServerHost Host { get; }

    /// <summary>What the report calls this run: there is no address to name any more.</summary>
    public string Label => "stdio";

    public static McpLoopback Start(MainViewModel project)
    {
        var (serverIn, clientOut) = LoopbackStream.Pair();
        var (clientIn, serverOut) = LoopbackStream.Pair();
        var host = new McpServerHost(project);
        host.Start(serverIn, serverOut);
        return new McpLoopback(host, clientOut, clientIn);
    }

    /// <summary>The official client, on the other end of the same two pipes.</summary>
    public async Task<McpClient> ConnectAsync(CancellationToken token = default)
        => await McpClient.CreateAsync(
            new StreamClientTransport(_toServer, _toClient, null), cancellationToken: token);

    /// <summary>
    /// Closes the client's end of both pipes, which is what a client does when it exits: the server
    /// reads it as end-of-input, the one signal a stdio server gets that nobody is listening.
    /// </summary>
    public void CloseClientEnd()
    {
        _toServer.Dispose();
        _toClient.Dispose();
    }

    public async Task StopAsync() => await Host.StopAsync();

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    /// <summary>
    /// One end of a pipe that keeps what is written until the other end reads it. Reads block, the
    /// way a pipe does; closing one end makes the other read as finished.
    /// </summary>
    private sealed class LoopbackStream : Stream
    {
        private readonly BlockingCollection<byte[]> _incoming = [];
        private LoopbackStream? _peer;
        private byte[]? _chunk;
        private int _offset;

        public static (LoopbackStream Reading, LoopbackStream Writing) Pair()
        {
            var left = new LoopbackStream();
            var right = new LoopbackStream();
            left._peer = right;
            right._peer = left;
            return (left, right);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            while (_chunk is null || _offset >= _chunk.Length)
            {
                byte[]? next;
                try
                {
                    next = _incoming.Take(cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    return 0;
                }

                _chunk = next;
                _offset = 0;
            }

            var take = Math.Min(buffer.Length, _chunk.Length - _offset);
            _chunk.AsMemory(_offset, take).CopyTo(buffer);
            _offset += take;
            return take;
        }

        public override void Write(byte[] buffer, int offset, int count)
            => _peer?.Deliver(buffer.AsSpan(offset, count).ToArray());

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _peer?.Deliver(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // A pipe ends at both ends when either is closed: whoever was waiting on the other
                // side reads end-of-input rather than waiting for something that cannot come.
                _incoming.CompleteAdding();
                _peer?._incoming.CompleteAdding();
            }

            base.Dispose(disposing);
        }

        private void Deliver(byte[] data)
        {
            try
            {
                _incoming.Add(data);
            }
            catch (InvalidOperationException)
            {
                // The reading end is gone; a pipe that nobody reads is the same as a closed one.
            }
        }
    }
}
