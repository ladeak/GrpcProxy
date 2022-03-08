using System.Buffers;
using System.IO.Pipelines;
using System.Net;

namespace GrpcProxy.Forwarder;

internal enum StreamCopyResult
{
    Success,
    InputError,
    OutputError,
    Canceled
}

internal sealed class StreamCopyHttpContent : HttpContent
{
    private readonly Stream _source;
    private readonly bool _autoFlushHttpClientOutgoingStream;
    private readonly PipeWriter _pipe;
    private readonly CancellationToken _token;
    private readonly TaskCompletionSource<(StreamCopyResult, Exception?)> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    public StreamCopyHttpContent(Stream source, bool autoFlushHttpClientOutgoingStream, PipeWriter pipe, CancellationToken token)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _autoFlushHttpClientOutgoingStream = autoFlushHttpClientOutgoingStream;
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _token = token;
    }

    /// <summary>
    /// Gets a <see cref="System.Threading.Tasks.Task"/> that completes in successful or failed state
    /// mimicking the result of SerializeToStreamAsync.
    /// </summary>
    public Task<(StreamCopyResult, Exception?)> ConsumptionTask => _tcs.Task;

    /// <summary>
    /// Gets a value indicating whether consumption of this content has begun.
    /// Property <see cref="ConsumptionTask"/> can be used to track the asynchronous outcome of the operation.
    /// </summary>
    /// <remarks>
    /// When used as an outgoing request content with <see cref="HttpClient"/>,
    /// this should always be true by the time the task returned by
    /// <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>
    /// completes, even when using <see cref="HttpCompletionOption.ResponseHeadersRead"/>.
    /// </remarks>
    public bool Started => Volatile.Read(ref _started) == 1;

    public bool InProgress => Started && !ConsumptionTask.IsCompleted;

    /// <summary>
    /// Copies bytes from the stream provided in our constructor into the target <paramref name="stream"/>.
    /// </summary>
    /// <remarks>
    /// This is used internally by HttpClient.SendAsync to send the request body.
    /// Here's the sequence of events as of commit 17300169760c61a90cab8d913636c1058a30a8c1 (https://github.com/dotnet/corefx -- tag v3.1.1).
    ///
    /// <code>
    /// HttpClient.SendAsync -->
    /// HttpMessageInvoker.SendAsync -->
    /// HttpClientHandler.SendAsync -->
    /// SocketsHttpHandler.SendAsync -->
    /// HttpConnectionHandler.SendAsync -->
    /// HttpConnectionPoolManager.SendAsync -->
    /// HttpConnectionPool.SendAsync --> ... -->
    /// {
    ///     HTTP/1.1: HttpConnection.SendAsync -->
    ///               HttpConnection.SendAsyncCore -->
    ///               HttpConnection.SendRequestContentAsync -->
    ///               HttpContent.CopyToAsync
    ///
    ///     HTTP/2:   Http2Connection.SendAsync -->
    ///               Http2Stream.SendRequestBodyAsync -->
    ///               HttpContent.CopyToAsync
    ///
    ///     /* Only in .NET 5:
    ///     HTTP/3:   Http3Connection.SendAsync -->
    ///               Http3Connection.SendWithoutWaitingAsync -->
    ///               Http3RequestStream.SendAsync -->
    ///               Http3RequestStream.SendContentAsync -->
    ///               HttpContent.CopyToAsync
    ///     */
    /// }
    ///
    /// HttpContent.CopyToAsync -->
    /// HttpContent.SerializeToStreamAsync (bingo!)
    /// </code>
    ///
    /// Conclusion: by overriding HttpContent.SerializeToStreamAsync,
    /// we have full control over pumping bytes to the target stream for all protocols
    /// (except Web Sockets, which is handled separately).
    /// </remarks>
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            throw new InvalidOperationException("Stream was already consumed.");
        }

        // The cancellationToken that is passed to this method is:
        // On HTTP/1.1: Linked HttpContext.RequestAborted + Request Timeout
        // On HTTP/2.0: SocketsHttpHandler error / the server wants us to stop sending content / H2 connection closed
        // _cancellation will be the same as cancellationToken for HTTP/1.1, so we can avoid the overhead of linking them
        CancellationTokenSource? linkedCts = null;

        if (_token != cancellationToken)
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_token, cancellationToken);
            cancellationToken = linkedCts.Token;
        }

        try
        {
            if (_autoFlushHttpClientOutgoingStream)
            {
                // HttpClient's machinery keeps an internal buffer that doesn't get flushed to the socket on every write.
                // Some protocols (e.g. gRPC) may rely on specific bytes being sent, and HttpClient's buffering would prevent it.
                // AutoFlushingStream delegates to the provided stream, adding calls to FlushAsync on every WriteAsync.
                // Note that HttpClient does NOT call Flush on the underlying socket, so the perf impact of this is expected to be small.
                // This statement is based on current knowledge as of .NET Core 3.1.201.
                stream = new AutoFlushingStream(stream);
            }

            // Immediately flush request stream to send headers
            // https://github.com/dotnet/corefx/issues/39586#issuecomment-516210081
            try
            {
                await stream.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException oex)
            {
                _tcs.TrySetResult((StreamCopyResult.Canceled, oex));
                return;
            }
            catch (Exception ex)
            {
                _tcs.TrySetResult((StreamCopyResult.OutputError, ex));
                return;
            }

            // Check that the content-length matches the request body size. This can be removed in .NET 7 now that SocketsHttpHandler enforces this: https://github.com/dotnet/runtime/issues/62258.
            var (result, error) = await StreamCopier.CopyAsync(_source, stream, Headers.ContentLength ?? StreamCopier.UnknownLength, _pipe, cancellationToken);
            _tcs.TrySetResult((result, error));

            // Check for errors that weren't the result of the destination failing.
            // We have to throw something here so the transport knows the body is incomplete.
            // We can't re-throw the original exception since that would cause concurrency issues.
            // We need to wrap it.
            if (result == StreamCopyResult.InputError)
            {
                throw new IOException("An error occurred when reading the request body from the client.", error);
            }
            if (result == StreamCopyResult.Canceled)
            {
                throw new OperationCanceledException("The request body copy was canceled.", error);
            }
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }

    // this is used internally by HttpContent.ReadAsStreamAsync(...)
    protected override Task<Stream> CreateContentReadStreamAsync()
    {
        // Nobody should be calling this...
        throw new NotImplementedException();
    }

    protected override bool TryComputeLength(out long length)
    {
        // We can't know the length of the content being pushed to the output stream.
        length = -1;
        return false;
    }
}


internal class StreamCopier
{
    private const int DefaultBufferSize = 65536;
    public const long UnknownLength = -1;

    internal static async ValueTask<(StreamCopyResult, Exception?)> CopyAsync(Stream input, Stream output, long promisedContentLength, PipeWriter pipe, CancellationToken cancellation)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
        var read = 0;
        long contentLength = 0;
        try
        {
            while (true)
            {
                read = 0;

                // Issue a zero-byte read to the input stream to defer buffer allocation until data is available.
                // Note that if the underlying stream does not supporting blocking on zero byte reads, then this will
                // complete immediately and won't save any memory, but will still function correctly.
                var zeroByteReadTask = input.ReadAsync(Memory<byte>.Empty, cancellation);
                if (zeroByteReadTask.IsCompletedSuccessfully)
                {
                    // Consume the ValueTask's result in case it is backed by an IValueTaskSource
                    _ = zeroByteReadTask.Result;
                }
                else
                {
                    // Take care not to return the same buffer to the pool twice in case zeroByteReadTask throws
                    var bufferToReturn = buffer;
                    buffer = null;
                    ArrayPool<byte>.Shared.Return(bufferToReturn);

                    await zeroByteReadTask;

                    buffer = ArrayPool<byte>.Shared.Rent(DefaultBufferSize);
                }

                read = await input.ReadAsync(buffer.AsMemory(), cancellation);
                contentLength += read;
                // Normally this is enforced by the server, but it could get out of sync if something in the proxy modified the body.
                if (promisedContentLength != UnknownLength && contentLength > promisedContentLength)
                {
                    var exception = new InvalidOperationException("More bytes received than the specified Content-Length.");
                    await pipe.CompleteAsync(exception);
                    return (StreamCopyResult.InputError, exception);
                }

                // End of the source stream.
                if (read == 0)
                {
                    if (promisedContentLength == UnknownLength || contentLength == promisedContentLength)
                    {
                        await pipe.CompleteAsync();
                        return (StreamCopyResult.Success, null);
                    }
                    else
                    {
                        var exception = new InvalidOperationException($"Sent {contentLength} request content bytes, but Content-Length promised {promisedContentLength}.");
                        await pipe.CompleteAsync(exception);
                        // This can happen if something in the proxy consumes or modifies part or all of the request body before proxying.
                        return (StreamCopyResult.InputError, exception);
                    }
                }

                await pipe.WriteAsync(buffer.AsMemory(0, read), cancellation);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
            }
        }
        catch (Exception ex)
        {
            var result = ex is OperationCanceledException ? StreamCopyResult.Canceled :
                (read == 0 ? StreamCopyResult.InputError : StreamCopyResult.OutputError);

            return (result, ex);
        }
        finally
        {
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}