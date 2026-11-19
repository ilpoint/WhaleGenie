namespace WhaleGenie.GraphicsCapture;

/// <summary>
/// One capture session kept on the thread that opened it. A Windows Runtime capture session is
/// apartment-bound: reading it or closing it from the interface thread raises
/// <c>RPC_E_WRONG_THREAD</c>. That exception during shutdown used to end the process before the
/// virtual keyboard and mouse could be taken off the machine, which left the pointer stuttering.
/// </summary>
internal sealed class OneThreadPictureSource : IPictureSource
{
    private readonly object _gate = new();

    private readonly IPictureSource _source;
    private readonly OneThread _thread;

    private bool _closed;

    public OneThreadPictureSource(IPictureSource source, OneThread thread)
    {
        _source = source;
        _thread = thread;
    }

    public Picture Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return _thread.Run(_source.Read);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _thread.Run(() =>
            {
                _source.Dispose();
                return true;
            });
        }
    }
}
