using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Data;
using GrpcProxy.Forwarder;

namespace GrpcProxy.Grpc.CallHandlers
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
            string serviceAddress) : base(options, method, messageMediator)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _messageMediator = messageMediator;
            _serviceAddress = serviceAddress ?? throw new ArgumentNullException(nameof(serviceAddress));
            _httpForwarder = new HttpForwarder();

        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            BodySizeFeatureHelper.DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);
            try
            {
                var sending = await _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, serverCallContext);
                var deserializingRequestTask = DeserializingRequestsAsync(httpContext, serverCallContext);

                var returningResponseTask = _httpForwarder.ReturnResponseAsync(httpContext, sending.StreamCopyContent, HttpTransformer.Empty, serverCallContext);
                var deserializingResponseTask = DeserializingResponseAsync(httpContext, serverCallContext, sending.ResponseMessage);

                await deserializingRequestTask;
                await deserializingResponseTask;
                await returningResponseTask;
            }
            catch (OperationCanceledException)
            {
                await _messageMediator.AddCancellationAsync(httpContext,  serverCallContext.ProxyCallId, _method.Type);
                throw;
            }
        }

        private async ValueTask DeserializingRequestsAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            while (!serverCallContext.CancellationToken.IsCancellationRequested)
            {
                var message = await serverCallContext.RequestPipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Request, CancellationToken.None);
                if (message == null)
                    break;
                await _messageMediator.AddRequestAsync(httpContext, serverCallContext.ProxyCallId, _method.Type, message?.ToString() ?? string.Empty);
            }
        }

        private async ValueTask DeserializingResponseAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext, HttpResponseMessage response)
        {
            while (!serverCallContext.CancellationToken.IsCancellationRequested)
            {
                var message = await serverCallContext.ResponsePipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.ResponseMarshaller.ContextualDeserializer, MessageDirection.Response, CancellationToken.None);
                if (message == null)
                    break;
                await _messageMediator.AddResponseAsync(response, _serviceAddress, serverCallContext.ProxyCallId, httpContext.Request.Path, _method.Type, message?.ToString() ?? string.Empty);
            }
        }
    }
}
