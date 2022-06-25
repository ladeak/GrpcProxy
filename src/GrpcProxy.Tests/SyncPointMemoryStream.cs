namespace GrpcProxy.Tests;

public class SyncPointMemoryStream : Stream
{
    private readonly bool _runContinuationsAsynchronously;

    private SyncPoint _syncPoint;
    private Func<Task> _awaiter;
    private byte[] _currentData;
    private Exception? _exception;

    public SyncPointMemoryStream(bool runContinuationsAsynchronously = true)
    {
        _runContinuationsAsynchronously = runContinuationsAsynchronously;
        _currentData = Array.Empty<byte>();
        _awaiter = SyncPoint.Create(out _syncPoint, _runContinuationsAsynchronously);
    }

    /// <summary>
    /// Give the stream more data and wait until it is all read.
    /// </summary>
    public Task AddDataAndWait(byte[] data)
    {
        AddDataCore(data);
        return _awaiter();
    }

    /// <summary>
    /// Give the stream more data.
    /// </summary>
    public void AddData(byte[] data)
    {
        AddDataCore(data);
        _ = _awaiter();
    }

    public Task AddExceptionAndWait(Exception ex)
    {
        _exception = ex;
        return _awaiter();
    }

    private void AddDataCore(byte[] data)
    {
        if (_currentData.Length != 0)
        {
            throw new Exception("Memory stream still has data to read.");
        }

        _currentData = data;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // Still have leftover data?
        if (_currentData.Length > 0)
        {
            return ReadInternalBuffer(buffer, offset, count);
        }

        cancellationToken.Register(() =>
        {
            _syncPoint.CancelWaitForSyncPoint(cancellationToken);
        });

        // Wait until data is provided by AddDataAndWait
        await _syncPoint.WaitForSyncPoint();

        return ReadInternalBuffer(buffer, offset, count);
    }

    private int ReadInternalBuffer(byte[] buffer, int offset, int count)
    {
        if (_exception != null)
        {
            var ex = _exception;
            _exception = null;

            ResetSyncPointAndContinuePrevious();

            throw ex;
        }

        var readBytes = Math.Min(count, _currentData.Length);
        if (readBytes > 0)
        {
            Array.Copy(_currentData, 0, buffer, offset, readBytes);
            _currentData = _currentData.AsSpan(readBytes, _currentData.Length - readBytes).ToArray();
        }

        if (_currentData.Length == 0)
        {
            ResetSyncPointAndContinuePrevious();
        }

        return readBytes;
    }

    private void ResetSyncPointAndContinuePrevious()
    {
        // We have read all data
        // Signal AddDataAndWait to continue
        // Reset sync point for next read
        var syncPoint = _syncPoint;

        ResetSyncPoint();

        syncPoint.Continue();
    }

    private void ResetSyncPoint()
    {
        _awaiter = SyncPoint.Create(out _syncPoint, _runContinuationsAsynchronously);
    }

    #region Stream implementation
    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => throw new NotImplementedException();

    public override long Position { get; set; }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return 0;
    }

    public override void SetLength(long value)
    {
        throw new NotImplementedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotImplementedException();
    }
    #endregion
}
