using System.IO.Pipelines;

namespace GrpcProxy.Tests;

public class SyncPipeWriter : PipeWriter
{
    private readonly PipeWriter _writer;
    private readonly SyncPipeReader _reader;
    private TaskCompletionSource? _readCompleted;

    public SyncPipeWriter(PipeWriter writer, SyncPipeReader reader)
    {
        _writer = writer;
        _reader = reader;
    }

    public async ValueTask<FlushResult> WaitReadAndWriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        if (_readCompleted != null)
            await _readCompleted.Task;
        _readCompleted = new TaskCompletionSource();
        _reader.SetNextRead(_readCompleted);
        var result = await base.WriteAsync(source, cancellationToken);
        return result;
    }

    public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        return base.WriteAsync(source, cancellationToken);
    }

    public override void Advance(int bytes)
    {
        _writer.Advance(bytes);
    }

    public override void CancelPendingFlush()
    {
        _writer.CancelPendingFlush();
    }

    public override void Complete(Exception? exception = null)
    {
        _writer.Complete(exception);
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        return _writer.FlushAsync(cancellationToken);
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        return _writer.GetMemory(sizeHint);
    }

    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        return _writer.GetSpan(sizeHint);
    }
}
