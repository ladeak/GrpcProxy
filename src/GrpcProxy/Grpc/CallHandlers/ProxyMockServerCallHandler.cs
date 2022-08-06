using System.Buffers;
using System.Text.Json;
using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Data;

namespace GrpcProxy.Grpc.CallHandlers;

internal class ProxyMockServerCallHandler<TRequest, TResponse> : ProxyServerCallHandlerBase<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly IProxyMessageMediator _messageMediator;
    private readonly string _serializedMockResponse;
    private readonly ArrayBufferWriter<byte> _mockedMessageBuffer;

    public ProxyMockServerCallHandler(
        MethodOptions options,
        Method<TRequest, TResponse> method,
        IProxyMessageMediator messageMediator,
        TResponse mockResponse)
        : base(options, method, messageMediator)
    {
        _messageMediator = messageMediator ?? throw new ArgumentNullException(nameof(messageMediator));
        _ = mockResponse ?? throw new ArgumentNullException(nameof(mockResponse));
        _serializedMockResponse = JsonSerializer.Serialize(mockResponse);

        _mockedMessageBuffer = new ArrayBufferWriter<byte>();
        var serializationContext = new DefaultSerializationContext(_mockedMessageBuffer);
        _method.ResponseMarshaller.ContextualSerializer(mockResponse, serializationContext);
    }

    protected override async Task HandleCallAsyncCore(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
    {
        BodySizeFeatureHelper.DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);

        var readingRequest = DeserializingRequestsAsync(httpContext, serverCallContext);

        // Must call StartAsync before the first pipeWriter.GetSpan() in WriteHeader
        var httpResponse = serverCallContext.HttpContext.Response;
        if (!httpResponse.HasStarted)
        {
            await httpResponse.StartAsync();
        }
        await _messageMediator.AddResponseAsync(EmptyHttpResponseMessage.Instance, "127.0.0.1", serverCallContext.ProxyCallId, httpContext.Request.Path, _method.Type, _serializedMockResponse);
        await httpResponse.BodyWriter.WriteAsync(_mockedMessageBuffer.WrittenMemory, serverCallContext.CancellationToken);
        await readingRequest;
    }

    private async ValueTask DeserializingRequestsAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
    {
        while (true)
        {
            var message = await httpContext.Request.BodyReader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Request, CancellationToken.None);
            if (message == null)
                break;
            await _messageMediator.AddRequestAsync(httpContext, serverCallContext.ProxyCallId, _method.Type, message?.ToString() ?? string.Empty);
        }
    }

    private class EmptyHttpResponseMessage : HttpResponseMessage
    {
        public static readonly EmptyHttpResponseMessage Instance = new EmptyHttpResponseMessage();

        private EmptyHttpResponseMessage()
        {
            StatusCode = System.Net.HttpStatusCode.OK;
        }
    }
}
