using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Forwarder;

namespace GrpcProxy.Grpc.CallHandlers
{
    internal class ProxyServerStreamingServerCallHandler<TRequest, TResponse> : ProxyServerCallHandlerBase<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProxyMessageMediator _messageMediator;
        private readonly string _serviceAddress;
        private readonly HttpForwarder _httpForwarder;

        public ProxyServerStreamingServerCallHandler(
            MethodOptions options,
            Method<TRequest, TResponse> method,
            IHttpClientFactory httpClientFactory,
            IProxyMessageMediator messageMediator,
            ILoggerFactory loggerFactory,
            string serviceAddress) : base(options, method, loggerFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _messageMediator = messageMediator;
            _serviceAddress = serviceAddress ?? throw new ArgumentNullException(nameof(serviceAddress));
            _httpForwarder = new HttpForwarder();

        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            BodySizeFeatureHelper.DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);

            var proxyCallId = Guid.NewGuid();
            var sending = await _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, serverCallContext);
            var requestData = await serverCallContext.RequestPipe.Reader.ReadSingleMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Request);
            await _messageMediator.AddRequestAsync(httpContext, proxyCallId, _method.Type, requestData?.ToString() ?? string.Empty);

            await _httpForwarder.ReturnResponseAsync(httpContext, sending.StreamCopyContent, HttpTransformer.Empty, serverCallContext);
            while (!serverCallContext.CancellationToken.IsCancellationRequested)
            {
                var message = await serverCallContext.ResponsePipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Response, serverCallContext.CancellationToken);
                if (message == null)
                    break;
                await _messageMediator.AddResponseAsync(sending.ResponseMessage, _serviceAddress, proxyCallId, httpContext.Request.Path, _method.Type, message?.ToString() ?? string.Empty);
            }
        }
    }
}
