using System.IO.Pipelines;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared.Server;
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
            new Method<string, string>(MethodType.Unary, "untyped", "untyped", Marshallers.Create((_, __) => { }, Deserialize), Marshallers.Create((_, __) => { }, Deserialize)),
            loggerFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _messageMediator = messageMediator ?? throw new ArgumentNullException(nameof(messageMediator));
        _serviceAddress = serviceAddress ?? throw new ArgumentNullException(nameof(serviceAddress));
        _httpForwarder = new HttpForwarder();
    }

    protected override async Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext)
    {
        var proxyCallId = Guid.NewGuid();
        var requestPipe = new Pipe();
        var sending = await _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, requestPipe.Writer, serverCallContext.CancellationToken);
        var requestData = await requestPipe.Reader.ReadSingleMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer);
        await _messageMediator.AddRequest(httpContext, proxyCallId, _method.Type, requestData);

        var responsePipe = new Pipe();
        await _httpForwarder.ReturnResponseAsync(httpContext, sending.Item1, sending.Item2, HttpTransformer.Empty, responsePipe.Writer, serverCallContext.CancellationToken);
        var responseData = await responsePipe.Reader.ReadSingleMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer);
        await _messageMediator.AddResponse(sending.Item1, _serviceAddress, proxyCallId, httpContext.Request.Path, _method.Type, responseData);
    }

    private static string Deserialize(DeserializationContext context) => ProtoCompiler.Deserialize(context.PayloadAsReadOnlySequence());
}
