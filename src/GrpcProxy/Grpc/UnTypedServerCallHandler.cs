using System.IO.Pipelines;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Compilation;
using GrpcProxy.Forwarder;

namespace GrpcProxy.Grpc;

internal class UnTypedServerCallHandler : ProxyServerCallHandlerBase<string, string>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProxyMessageMediator _messageMediator;
    private readonly string _serviceAddress;
    private readonly HttpForwarder _httpForwarder;

    public UnTypedServerCallHandler(
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IProxyMessageMediator messageMediator,
        string serviceAddress)
        : base(MethodOptions.Create(new[] { new GrpcServiceOptions() }),
            new Method<string, string>(MethodType.DuplexStreaming, "untyped", "untyped", Marshallers.Create((_, __) => { }, Deserialize), Marshallers.Create((_, __) => { }, Deserialize)),
            loggerFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _messageMediator = messageMediator ?? throw new ArgumentNullException(nameof(messageMediator));
        _serviceAddress = serviceAddress ?? throw new ArgumentNullException(nameof(serviceAddress));
        _httpForwarder = new HttpForwarder();
    }

    protected override async Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext)
    {
        BodySizeFeatureHelper.DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);

        var proxyCallId = Guid.NewGuid();
        var requestPipe = new Pipe();
        var responsePipe = new Pipe();

        var sending = await _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, requestPipe.Writer, serverCallContext.CancellationToken);
        var deserializingRequestTask = DeserializingRequestsAsync(httpContext, serverCallContext, proxyCallId, requestPipe);

        var returningResponseTask = _httpForwarder.ReturnResponseAsync(httpContext, sending.Item1, sending.Item2, HttpTransformer.Empty, responsePipe.Writer, serverCallContext.CancellationToken);
        var deserializingResponseTask = DeserializingResponseAsync(httpContext, serverCallContext, proxyCallId, sending.Item1, responsePipe);

        await deserializingRequestTask;
        await returningResponseTask;
        await deserializingResponseTask;
    }

    private async ValueTask DeserializingRequestsAsync(HttpContext httpContext, HttpContextServerCallContext serverCallContext, Guid proxyCallId, Pipe requestPipe)
    {
        while (true)
        {
            var message = await requestPipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, CancellationToken.None);
            if (message == null)
                break;
            await _messageMediator.AddRequestAsync(httpContext, proxyCallId, _method.Type, message?.ToString() ?? string.Empty);
        }
    }

    private async ValueTask DeserializingResponseAsync(HttpContext httpContext, HttpContextServerCallContext serverCallContext, Guid proxyCallId, HttpResponseMessage response, Pipe responsePipe)
    {
        while (true)
        {
            var message = await responsePipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.ResponseMarshaller.ContextualDeserializer, CancellationToken.None);
            if (message == null)
                break;
            await _messageMediator.AddResponseAsync(response, _serviceAddress, proxyCallId, httpContext.Request.Path, _method.Type, message?.ToString() ?? string.Empty);
        }
    }

    private static string Deserialize(DeserializationContext context) => ProtoCompiler.Deserialize(context.PayloadAsReadOnlySequence());
}
