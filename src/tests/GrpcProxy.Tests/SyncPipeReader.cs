using System.IO.Pipelines;

namespace GrpcProxy.Tests;

public class SyncPipeReader : PipeReader
{
    private readonly PipeReader _reader;
    private TaskCompletionSource _readCompleted = new TaskCompletionSource();

    public SyncPipeReader(PipeReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public override void AdvanceTo(SequencePosition consumed)
    {
        _reader.AdvanceTo(consumed);
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        _reader.AdvanceTo(consumed, examined);
    }

    public override void CancelPendingRead()
    {
        _reader.CancelPendingRead();
        _readCompleted.SetCanceled();
    }

    public override void Complete(Exception? exception = null)
    {
        _reader.Complete(exception);
    }

    public async override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _reader.ReadAsync(cancellationToken);
        _readCompleted.TrySetResult();
        return result;
    }

    public override bool TryRead(out ReadResult result)
    {
        var readResult = _reader.TryRead(out result);
        _readCompleted.TrySetResult();
        return readResult;
    }

    public void SetNextRead(TaskCompletionSource tcs) => _readCompleted = tcs;
}
