using System.IO.Pipelines;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.Forwarder;

namespace GrpcProxy.Grpc;

internal class ProxyUnaryServerCallHandler<TRequest, TResponse> : ProxyServerCallHandlerBase<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IProxyMessageMediator _messageMediator;
    private readonly string _serviceAddress;
    private readonly HttpForwarder _httpForwarder;

    public ProxyUnaryServerCallHandler(
        MethodOptions options,
        Method<TRequest, TResponse> method,
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IProxyMessageMediator messageMediator,
        string serviceAddress)
        : base(options, method, loggerFactory)
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
        var requestData = await requestPipe.Reader.ReadSingleMessageAsync<TRequest>(serverCallContext, _method.RequestMarshaller.ContextualDeserializer);
        await _messageMediator.AddRequest(httpContext, proxyCallId, _method.Type, requestData?.ToString() ?? string.Empty);

        var responsePipe = new Pipe();
        await _httpForwarder.ReturnResponseAsync(httpContext, sending.Item1, sending.Item2, HttpTransformer.Empty, responsePipe.Writer, serverCallContext.CancellationToken);
        var responseData = await responsePipe.Reader.ReadSingleMessageAsync<TResponse>(serverCallContext, _method.ResponseMarshaller.ContextualDeserializer);
        await _messageMediator.AddResponse(sending.Item1, _serviceAddress, proxyCallId, httpContext.Request.Path, _method.Type, responseData?.ToString() ?? string.Empty);
    }
}
