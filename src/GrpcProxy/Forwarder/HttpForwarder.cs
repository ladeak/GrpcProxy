using System.IO.Pipelines;
using System.Net;
using GrpcProxy.AspNetCore;
using GrpcProxy.Grpc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace GrpcProxy.Forwarder;

internal sealed class HttpForwarder
{
    private static readonly Version DefaultVersion = HttpVersion.Version20;

    public async ValueTask<(HttpResponseMessage ResponseMessage, StreamCopyHttpContent? StreamCopyContent)> SendRequestAsync(
        HttpContext context,
        string destinationPrefix,
        HttpClient httpClient,
        HttpTransformer transformer,
        ProxyHttpContextServerCallContext proxyContext)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        _ = destinationPrefix ?? throw new ArgumentNullException(nameof(destinationPrefix));
        _ = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ = transformer ?? throw new ArgumentNullException(nameof(transformer));

        // It is http2 and GRPC request
        var isStreamingRequest = true;

        // Step 1-3: Create outgoing HttpRequestMessage
        var (destinationRequest, requestContent) = await CreateRequestMessageAsync(context, destinationPrefix, transformer, isStreamingRequest, proxyContext.RequestPipe.Writer, proxyContext.CancellationToken);

        // Step 4: Send the outgoing request using HttpClient
        HttpResponseMessage destinationResponse;
        try
        {
            destinationResponse = await httpClient.SendAsync(destinationRequest, HttpCompletionOption.ResponseHeadersRead, proxyContext.CancellationToken);
        }
        catch (Exception)
        {
            throw;
            //await HandleRequestFailureAsync(context, requestContent, requestException, transformer, token);
        }
        proxyContext.ProxiedResponseMessage = destinationResponse;
        return (destinationResponse, requestContent);
    }

    public async ValueTask<ForwarderError> ReturnResponseAsync(
        HttpContext context,
        StreamCopyHttpContent? requestContent,
        HttpTransformer transformer,
        ProxyHttpContextServerCallContext proxyContext)
    {
        var isClientHttp2 = true;
        var isStreamingRequest = true;

        if (proxyContext.ProxiedResponseMessage == null)
            throw new ArgumentException("ProxyContext has no Http response message");

        var pipeWriter = proxyContext.ResponsePipe.Writer;

        // Detect connection downgrade, which may be problematic for e.g. gRPC.
        if (isClientHttp2 && proxyContext.ProxiedResponseMessage.Version.Major != 2)
            throw new InvalidOperationException("Downgrade");

        try
        {
            // Step 5: Copy response status line Client ◄-- Proxy ◄-- Destination
            // Step 6: Copy response headers Client ◄-- Proxy ◄-- Destination
            var copyBody = await CopyResponseStatusAndHeadersAsync(proxyContext.ProxiedResponseMessage, context, transformer);

            if (!copyBody)
            {
                // The transforms callback decided that the response body should be discarded.
                proxyContext.ProxiedResponseMessage.Dispose();

                if (requestContent is not null && requestContent.InProgress)
                    await requestContent.ConsumptionTask;

                return ForwarderError.None;
            }
        }
        catch (Exception)
        {
            proxyContext.ProxiedResponseMessage.Dispose();
            if (requestContent is not null && requestContent.InProgress)
                await requestContent.ConsumptionTask;

            // Clear the response since status code, reason and some headers might have already been copied and we want clean 502 response.
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await pipeWriter.CompleteAsync(new Exception("Bad gateway"));
            return ForwarderError.ResponseHeaders;
        }

        // Step 7-A: Check for a 101 upgrade response, this takes care of WebSockets as well as any other upgradeable protocol.
        if (proxyContext.ProxiedResponseMessage.StatusCode == HttpStatusCode.SwitchingProtocols)
            throw new InvalidOperationException("Upgrade protocol is not supported.");

        // NOTE: it may *seem* wise to call `context.Response.StartAsync()` at this point
        // since it looks like we are ready to send back response headers
        // (and this might help reduce extra delays while we wait to receive the body from the destination).
        // HOWEVER, this would produce the wrong result if it turns out that there is no content
        // from the destination -- instead of sending headers and terminating the stream at once,
        // we would send headers thinking a body may be coming, and there is none.
        // This is problematic on gRPC connections when the destination server encounters an error,
        // in which case it immediately returns the response headers and trailing headers, but no content,
        // and clients misbehave if the initial headers response does not indicate stream end.

        // Step 7-B: Copy response body Client ◄-- Proxy ◄-- Destination
        var (responseBodyCopyResult, responseBodyException) = await CopyResponseBodyAsync(proxyContext.ProxiedResponseMessage.Content, context.Response.Body, pipeWriter, proxyContext.CancellationToken);

        if (responseBodyCopyResult != StreamCopyResult.Success)
        {
            return await HandleResponseBodyErrorAsync(context, requestContent, responseBodyCopyResult, responseBodyException!);
        }

        // Step 8: Copy response trailer headers and finish response Client ◄-- Proxy ◄-- Destination
        await CopyResponseTrailingHeadersAsync(proxyContext.ProxiedResponseMessage, context, transformer);

        if (isStreamingRequest)
        {
            // NOTE: We must call `CompleteAsync` so that Kestrel will flush all bytes to the client.
            // In the case where there was no response body,
            // this is also when headers and trailing headers are sent to the client.
            // Without this, the client might wait forever waiting for response bytes,
            // while we might wait forever waiting for request bytes,
            // leading to a stuck connection and no way to make progress.

            // GRPC will take care of response header trailers here, hence not completing response.
            // await context.Response.CompleteAsync();
            await pipeWriter.CompleteAsync();
        }

        // Step 9: Wait for completion of step 2: copying request body Client --► Proxy --► Destination
        // NOTE: It is possible for the request body to NOT be copied even when there was an incoming requet body,
        // e.g. when the request includes header `Expect: 100-continue` and the destination produced a non-1xx response.
        // We must only wait for the request body to complete if it actually started,
        // otherwise we run the risk of waiting indefinitely for a task that will never complete.
        if (requestContent is not null && requestContent.Started)
        {
            var (requestBodyCopyResult, requestBodyException) = await requestContent.ConsumptionTask;

            if (requestBodyCopyResult != StreamCopyResult.Success)
            {
                // The response succeeded. If there was a request body error then it was probably because the client or destination decided
                // to cancel it. Report as low severity.
                var error = requestBodyCopyResult switch
                {
                    StreamCopyResult.InputError => ForwarderError.RequestBodyClient,
                    StreamCopyResult.OutputError => ForwarderError.RequestBodyDestination,
                    StreamCopyResult.Canceled => ForwarderError.RequestBodyCanceled,
                    _ => throw new NotImplementedException(requestBodyCopyResult.ToString())
                };
                return error;
            }
        }

        return ForwarderError.None;
    }

    private async ValueTask<(HttpRequestMessage, StreamCopyHttpContent?)> CreateRequestMessageAsync(HttpContext context, string destinationPrefix,
        HttpTransformer transformer, bool isStreamingRequest, PipeWriter pipeWriter, CancellationToken token)
    {
        // "http://a".Length = 8
        if (destinationPrefix == null || destinationPrefix.Length < 8)
            throw new ArgumentException("Invalid destination prefix.", nameof(destinationPrefix));

        var destinationRequest = new HttpRequestMessage();

        destinationRequest.Method = RequestUtilities.GetHttpMethod(context.Request.Method);

        // No support for upgrade requests
        //var upgradeFeature = context.Features.Get<IHttpUpgradeFeature>();
        //var upgradeHeader = context.Request.Headers[HeaderNames.Upgrade].ToString();
        //var isUpgradeRequest = (upgradeFeature?.IsUpgradableRequest ?? false)
        //    // Mitigate https://github.com/microsoft/reverse-proxy/issues/255, IIS considers all requests upgradeable.
        //    && (string.Equals("WebSocket", upgradeHeader, StringComparison.OrdinalIgnoreCase)
        //        // https://github.com/microsoft/reverse-proxy/issues/467 for kubernetes APIs
        //        || upgradeHeader.StartsWith("SPDY/", StringComparison.OrdinalIgnoreCase));

        // Default to HTTP/1.1 for proxying upgradeable requests. This is already the default as of .NET Core 3.1
        // Otherwise request what's set in proxyOptions (e.g. default HTTP/2) and let HttpClient negotiate the protocol
        // based on VersionPolicy (for .NET 5 and higher). For example, downgrading to HTTP/1.1 if it cannot establish HTTP/2 with the target.
        // This is done without extra round-trips thanks to ALPN. We can detect a downgrade after calling HttpClient.SendAsync
        // (see Step 3 below). TBD how this will change when HTTP/3 is supported.
        destinationRequest.Version = DefaultVersion;
        destinationRequest.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        // Step 2: Setup copy of request body (background) Client --► Proxy --► Destination
        // Note that we must do this before step (3) because step (3) may also add headers to the HttpContent that we set up here.
        var requestContent = SetupRequestBodyCopy(context.Request, isStreamingRequest, pipeWriter, token);
        destinationRequest.Content = requestContent;

        // Step 3: Copy request headers Client --► Proxy --► Destination
        await transformer.TransformRequestAsync(context, destinationRequest, destinationPrefix);

        //if (isUpgradeRequest)
        //{
        //    RestoreUpgradeHeaders(context, destinationRequest);
        //}

        // Allow someone to custom build the request uri, otherwise provide a default for them.
        var request = context.Request;
        destinationRequest.RequestUri ??= RequestUtilities.MakeDestinationAddress(destinationPrefix, request.Path, request.QueryString);

        return (destinationRequest, requestContent);
    }

    private StreamCopyHttpContent? SetupRequestBodyCopy(HttpRequest request, bool isStreamingRequest, PipeWriter pipeWriter, CancellationToken token)
    {
        // If we generate an HttpContent without a Content-Length then for HTTP/1.1 HttpClient will add a Transfer-Encoding: chunked header
        // even if it's a GET request. Some servers reject requests containing a Transfer-Encoding header if they're not expecting a body.
        // Try to be as specific as possible about the client's intent to send a body. The one thing we don't want to do is to start
        // reading the body early because that has side-effects like 100-continue.
        var hasBody = true;
        var contentLength = request.Headers.ContentLength;
        var method = request.Method;

        var canHaveBodyFeature = request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>();
        if (canHaveBodyFeature != null)
        {
            hasBody = canHaveBodyFeature.CanHaveBody;
        }
        else
        // https://tools.ietf.org/html/rfc7230#section-3.3.3
        // All HTTP/1.1 requests should have Transfer-Encoding or Content-Length.
        // Http.Sys/IIS will even add a Transfer-Encoding header to HTTP/2 requests with bodies for back-compat.
        // HTTP/1.0 Connection: close bodies are only allowed on responses, not requests.
        // https://tools.ietf.org/html/rfc1945#section-7.2.2
        //
        // Transfer-Encoding overrides Content-Length per spec
        if (request.Headers.TryGetValue(HeaderNames.TransferEncoding, out var transferEncoding)
            && transferEncoding.Count == 1
            && string.Equals("chunked", transferEncoding.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            hasBody = true;
        }
        else if (contentLength.HasValue)
        {
            hasBody = contentLength > 0;
        }
        // Kestrel HTTP/2: There are no required headers that indicate if there is a request body so we need to sniff other fields.
        else if (!ProtocolHelper.IsHttp2OrGreater(request.Protocol))
        {
            hasBody = false;
        }
        // https://tools.ietf.org/html/rfc7231#section-4.3.1
        // A payload within a GET/HEAD/DELETE/CONNECT request message has no defined semantics; sending a payload body on a
        // GET/HEAD/DELETE/CONNECT request might cause some existing implementations to reject the request.
        // https://tools.ietf.org/html/rfc7231#section-4.3.8
        // A client MUST NOT send a message body in a TRACE request.
        else if (HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsDelete(method)
            || HttpMethods.IsConnect(method)
            || HttpMethods.IsTrace(method))
        {
            hasBody = false;
        }
        // else hasBody defaults to true

        if (hasBody)
        {
            if (isStreamingRequest)
            {
                BodySizeFeatureHelper.DisableMinRequestBodyDataRateAndMaxRequestBodySize(request.HttpContext);
            }

            // Note on `autoFlushHttpClientOutgoingStream: isStreamingRequest`:
            // The.NET Core HttpClient stack keeps its own buffers on top of the underlying outgoing connection socket.
            // We flush those buffers down to the socket on every write when this is set,
            // but it does NOT result in calls to flush on the underlying socket.
            // This is necessary because we proxy http2 transparently,
            // and we are deliberately unaware of packet structure used e.g. in gRPC duplex channels.
            // Because the sockets aren't flushed, the perf impact of this choice is expected to be small.
            // Future: It may be wise to set this to true for *all* http2 incoming requests,
            // but for now, out of an abundance of caution, we only do it for requests that look like gRPC.
            return new StreamCopyHttpContent(
                source: request.Body,
                autoFlushHttpClientOutgoingStream: isStreamingRequest,
                pipeWriter,
                token);
        }

        return null;
    }

    private ForwarderError HandleRequestBodyFailure(HttpContext context, StreamCopyResult requestBodyCopyResult, Exception requestBodyException, Exception additionalException)
    {
        ForwarderError requestBodyError;
        int statusCode;
        switch (requestBodyCopyResult)
        {
            // Failed while trying to copy the request body from the client. It's ambiguous if the request or response failed first.
            case StreamCopyResult.InputError:
                requestBodyError = ForwarderError.RequestBodyClient;
                statusCode = StatusCodes.Status400BadRequest;
                break;
            // Failed while trying to copy the request body to the destination. It's ambiguous if the request or response failed first.
            case StreamCopyResult.OutputError:
                requestBodyError = ForwarderError.RequestBodyDestination;
                statusCode = StatusCodes.Status502BadGateway;
                break;
            // Canceled while trying to copy the request body, either due to a client disconnect or a timeout. This probably caused the response to fail as a secondary error.
            case StreamCopyResult.Canceled:
                requestBodyError = ForwarderError.RequestBodyCanceled;
                // Timeouts (504s) are handled at the SendAsync call site.
                // The request body should only be canceled by the RequestAborted token.
                statusCode = StatusCodes.Status502BadGateway;
                break;
            default:
                throw new NotImplementedException(requestBodyCopyResult.ToString());
        }

        // We don't know if the client is still around to see this error, but set it for diagnostics to see.
        if (!context.Response.HasStarted)
        {
            // Nothing has been sent to the client yet, we can still send a good error response.
            context.Response.Clear();
            context.Response.StatusCode = statusCode;
            return requestBodyError;
        }

        ResetOrAbort(context, isCancelled: requestBodyCopyResult == StreamCopyResult.Canceled);

        return requestBodyError;
    }

    private async ValueTask<ForwarderError> HandleRequestFailureAsync(HttpContext context, StreamCopyHttpContent? requestContent, Exception requestException, HttpTransformer transformer, CancellationToken token)
    {
        if (requestException is OperationCanceledException)
        {
            if (!context.RequestAborted.IsCancellationRequested && token.IsCancellationRequested)
            {
                return await ReportErrorAsync(ForwarderError.RequestTimedOut, StatusCodes.Status504GatewayTimeout);
            }
            else
            {
                return await ReportErrorAsync(ForwarderError.RequestCanceled, StatusCodes.Status502BadGateway);
            }
        }

        // Check for request body errors, these may have triggered the response error.
        if (requestContent?.ConsumptionTask.IsCompleted == true)
        {
            var (requestBodyCopyResult, requestBodyException) = requestContent.ConsumptionTask.Result;

            if (requestBodyCopyResult != StreamCopyResult.Success)
            {
                var error = HandleRequestBodyFailure(context, requestBodyCopyResult, requestBodyException!, requestException);
                await transformer.TransformResponseAsync(context, proxyResponse: null);
                return error;
            }
        }

        // We couldn't communicate with the destination.
        return await ReportErrorAsync(ForwarderError.Request, StatusCodes.Status502BadGateway);

        async ValueTask<ForwarderError> ReportErrorAsync(ForwarderError error, int statusCode)
        {
            context.Response.StatusCode = statusCode;

            if (requestContent is not null && requestContent.InProgress)
            {
                await requestContent.ConsumptionTask;
            }

            await transformer.TransformResponseAsync(context, null);
            return error;
        }
    }
    private static ValueTask<bool> CopyResponseStatusAndHeadersAsync(HttpResponseMessage source, HttpContext context, HttpTransformer transformer)
    {
        context.Response.StatusCode = (int)source.StatusCode;

        if (!ProtocolHelper.IsHttp2OrGreater(context.Request.Protocol))
        {
            // Don't explicitly set the field if the default reason phrase is used
            if (source.ReasonPhrase != ReasonPhrases.GetReasonPhrase((int)source.StatusCode))
            {
                context.Features.Get<IHttpResponseFeature>()!.ReasonPhrase = source.ReasonPhrase;
            }
        }

        // Copies headers
        return transformer.TransformResponseAsync(context, source);
    }


    private async ValueTask<(StreamCopyResult, Exception?)> CopyResponseBodyAsync(HttpContent destinationResponseContent, Stream clientResponseStream, PipeWriter pipeWriter,
        CancellationToken token)
    {
        // SocketHttpHandler and similar transports always provide an HttpContent object, even if it's empty.
        // In 3.1 this is only likely to return null in tests.
        // As of 5.0 HttpResponse.Content never returns null.
        // https://github.com/dotnet/runtime/blame/8fc68f626a11d646109a758cb0fc70a0aa7826f1/src/libraries/System.Net.Http/src/System/Net/Http/HttpResponseMessage.cs#L46
        if (destinationResponseContent != null)
        {
            using var destinationResponseStream = await destinationResponseContent.ReadAsStreamAsync();
            // The response content-length is enforced by the server.
            return await StreamCopier.CopyAsync(destinationResponseStream, clientResponseStream, StreamCopier.UnknownLength, pipeWriter, token);
        }

        return (StreamCopyResult.Success, null);
    }

    private async ValueTask<ForwarderError> HandleResponseBodyErrorAsync(HttpContext context, StreamCopyHttpContent? requestContent, StreamCopyResult responseBodyCopyResult, Exception responseBodyException)
    {
        if (requestContent is not null && requestContent.Started)
        {
            var alreadyFinished = requestContent.ConsumptionTask.IsCompleted == true;

            if (!alreadyFinished)
            {
            }

            var (requestBodyCopyResult, requestBodyError) = await requestContent.ConsumptionTask;

            // Check for request body errors, these may have triggered the response error.
            if (alreadyFinished && requestBodyCopyResult != StreamCopyResult.Success)
            {
                return HandleRequestBodyFailure(context, requestBodyCopyResult, requestBodyError!, responseBodyException);
            }
        }

        var error = responseBodyCopyResult switch
        {
            StreamCopyResult.InputError => ForwarderError.ResponseBodyDestination,
            StreamCopyResult.OutputError => ForwarderError.ResponseBodyClient,
            StreamCopyResult.Canceled => ForwarderError.ResponseBodyCanceled,
            _ => throw new NotImplementedException(responseBodyCopyResult.ToString()),
        };

        if (!context.Response.HasStarted)
        {
            // Nothing has been sent to the client yet, we can still send a good error response.
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            return error;
        }

        // The response has already started, we must forcefully terminate it so the client doesn't get the
        // the mistaken impression that the truncated response is complete.
        ResetOrAbort(context, isCancelled: responseBodyCopyResult == StreamCopyResult.Canceled);

        return error;
    }

    private static ValueTask CopyResponseTrailingHeadersAsync(HttpResponseMessage source, HttpContext context, HttpTransformer transformer)
    {
        // Copies trailers
        return transformer.TransformResponseTrailersAsync(context, source);
    }


    private static void ResetOrAbort(HttpContext context, bool isCancelled)
    {
        var resetFeature = context.Features.Get<IHttpResetFeature>();
        if (resetFeature != null)
        {
            // https://tools.ietf.org/html/rfc7540#section-7
            const int Cancelled = 2;
            const int InternalError = 8;
            resetFeature.Reset(isCancelled ? Cancelled : InternalError);
            return;
        }

        context.Abort();
    }

}
