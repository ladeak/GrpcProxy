using System.IO.Pipelines;
using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Forwarder;

namespace GrpcProxy.Grpc
{
    internal class ProxyDuplexStreamingServerCallHandler<TRequest, TResponse> : ProxyServerCallHandlerBase<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IProxyMessageMediator _messageMediator;
        private readonly string _serviceAddress;
        private readonly HttpForwarder _httpForwarder;

        public ProxyDuplexStreamingServerCallHandler(
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
            var responsePipe = new Pipe();

            serverCallContext.CancellationToken.Register(_ => { Console.WriteLine("cancellation requested"); }, null, false);

            var sending = await _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, requestPipe.Writer, serverCallContext.CancellationToken);
            var deserializingRequestTask = DeserializingRequestsAsync(httpContext, serverCallContext, proxyCallId, requestPipe);

            var returningResponseTask = _httpForwarder.ReturnResponseAsync(httpContext, sending.ResponseMessage, sending.StreamCopyContent, HttpTransformer.Empty, responsePipe.Writer, serverCallContext.CancellationToken);
            var deserializingResponseTask = DeserializingResponseAsync(httpContext, serverCallContext, proxyCallId, sending.ResponseMessage, responsePipe);

            await deserializingRequestTask;
            await deserializingResponseTask;
            await returningResponseTask;
        }

        private async ValueTask DeserializingRequestsAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext, Guid proxyCallId, Pipe requestPipe)
        {
            while (true)
            {
                var message = await requestPipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Request, CancellationToken.None);
                if (message == null)
                    break;
                await _messageMediator.AddRequestAsync(httpContext, proxyCallId, _method.Type, message?.ToString() ?? string.Empty);
            }
        }

        private async ValueTask DeserializingResponseAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext, Guid proxyCallId, HttpResponseMessage response, Pipe responsePipe)
        {
            serverCallContext.SetProxiedResponse(response);
            while (true)
            {
                var message = await responsePipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.ResponseMarshaller.ContextualDeserializer, MessageDirection.Response, CancellationToken.None);
                if (message == null)
                    break;
                await _messageMediator.AddResponseAsync(response, _serviceAddress, proxyCallId, httpContext.Request.Path, _method.Type, message?.ToString() ?? string.Empty);
            }
        }
    }
}
