using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The door the window holds and the bridge a client's process becomes. A client can only start a
/// program and talk down its standard input and output, so reaching the window that is already
/// running means forwarding through a pipe — which is what makes an agent's edits land in the
/// project the person is looking at.
/// </summary>
public class McpPipeTests
{
    [Fact]
    public async Task A_client_reaches_the_window_through_the_pipe()
    {
        McpPipe.Name = "whalegenie-mcp-test-" + Guid.NewGuid().ToString("N");
        using var answered = new ManualResetEventSlim();
        using var close = new ManualResetEventSlim();

        McpPipe.Serve(async pipe =>
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };

            var line = await reader.ReadLineAsync();
            await writer.WriteLineAsync("answer:" + line);
            answered.Set();
        });

        var answer = new MemoryStream();
        var bridging = Task.Run(() => McpPipe.TryBridge(new OneLineStream("hello", close), answer));

        Assert.True(answered.Wait(TimeSpan.FromSeconds(10)), "the window never answered");
        close.Set();

        await bridging.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("answer:hello", Encoding.UTF8.GetString(answer.ToArray()).Trim());
    }

    /// <summary>
    /// With no window running there is nothing to bridge to, and the caller has to serve the project
    /// itself. The lookup gives up quickly rather than leaving a client waiting on a door that is
    /// not there.
    /// </summary>
    [Fact]
    public void A_bridge_with_no_window_behind_it_gives_up()
    {
        McpPipe.Name = "whalegenie-mcp-none-" + Guid.NewGuid().ToString("N");

        var started = DateTime.UtcNow;
        var bridged = McpPipe.TryBridge(new MemoryStream(), new MemoryStream());

        Assert.False(bridged);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5), "the lookup did not give up");
    }

    /// <summary>
    /// One line, then nothing until the check says the session is over. A plain memory stream would
    /// end the input at once, which closes the session before the answer can be read.
    /// </summary>
    private sealed class OneLineStream(string text, ManualResetEventSlim close) : Stream
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(text + "\n");

        private int _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= _bytes.Length)
            {
                close.Wait(TimeSpan.FromSeconds(10));
                return 0;
            }

            var take = Math.Min(count, _bytes.Length - _read);
            Array.Copy(_bytes, _read, buffer, offset, take);
            _read += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
