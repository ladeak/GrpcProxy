using System.IO.Pipelines;
using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Forwarder;

namespace GrpcProxy.Grpc
{
    internal class ProxyClientStreamingServerCallHandler<TRequest, TResponse> : ProxyServerCallHandlerBase<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProxyMessageMediator _messageMediator;
        private readonly string _serviceAddress;
        private readonly HttpForwarder _httpForwarder;

        public ProxyClientStreamingServerCallHandler(
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
            var requestPipe = new Pipe();
            var sending = await _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, requestPipe.Writer, serverCallContext.CancellationToken);
            while (!serverCallContext.CancellationToken.IsCancellationRequested)
            {
                var message = await requestPipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Request, serverCallContext.CancellationToken);
                if (message == null)
                    break;
                await _messageMediator.AddRequestAsync(httpContext, proxyCallId, _method.Type, message?.ToString() ?? string.Empty);
            }

            var responsePipe = new Pipe();
            await _httpForwarder.ReturnResponseAsync(httpContext, sending.ResponseMessage, sending.StreamCopyContent, HttpTransformer.Empty, responsePipe.Writer, serverCallContext.CancellationToken);
            serverCallContext.SetProxiedResponse(sending.ResponseMessage);
            var responseData = await responsePipe.Reader.ReadSingleMessageAsync(serverCallContext, _method.ResponseMarshaller.ContextualDeserializer, MessageDirection.Response);
            await _messageMediator.AddResponseAsync(sending.ResponseMessage, _serviceAddress, proxyCallId, httpContext.Request.Path, _method.Type, responseData?.ToString() ?? string.Empty);
        }
    }
}
