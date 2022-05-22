using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Forwarder;

namespace GrpcProxy.Grpc.CallHandlers
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
            string serviceAddress) : base(options, method)
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
            var sending = await ForwardRequestAsync(proxyCallId, httpContext, serverCallContext);
            await ForwardResponseAsync(proxyCallId, sending, httpContext, serverCallContext);
        }

        private async Task<ForwardingContext> ForwardRequestAsync(Guid proxyCallId, HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            try
            {
                var sendingTask = _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, serverCallContext);
                var deserializationTask = DeserializeRequestAsync(httpContext, serverCallContext, proxyCallId);
                await Task.WhenAll(sendingTask, deserializationTask);
                return sendingTask.Result;
            }
            catch (TaskCanceledException)
            {
                await _messageMediator.AddCancellationAsync(httpContext, proxyCallId, _method.Type);
                throw;
            }
        }

        private async Task ForwardResponseAsync(Guid proxyCallId, ForwardingContext sending, HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            try
            {
                var responseTask = _httpForwarder.ReturnResponseAsync(httpContext, sending.StreamCopyContent, HttpTransformer.Empty, serverCallContext);
                var deserializationTask = DeserializingResponseAsync(proxyCallId, sending, httpContext, serverCallContext);
                await Task.WhenAll(responseTask, deserializationTask);
            }
            catch (TaskCanceledException)
            {
                await _messageMediator.AddCancellationAsync(httpContext, proxyCallId, _method.Type);
                throw;
            }
        }

        private async Task DeserializeRequestAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext, Guid proxyCallId)
        {
            while (!serverCallContext.CancellationToken.IsCancellationRequested)
            {
                var message = await serverCallContext.RequestPipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Request, CancellationToken.None);
                if (message == null)
                    break;
                await _messageMediator.AddRequestAsync(httpContext, proxyCallId, _method.Type, message?.ToString() ?? string.Empty);
            }
        }

        private async Task DeserializingResponseAsync(Guid proxyCallId, ForwardingContext sending, HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            var responseData = await serverCallContext.ResponsePipe.Reader.ReadSingleMessageAsync(serverCallContext, _method.ResponseMarshaller.ContextualDeserializer, MessageDirection.Response);
            await _messageMediator.AddResponseAsync(sending.ResponseMessage, _serviceAddress, proxyCallId, httpContext.Request.Path, _method.Type, responseData?.ToString() ?? string.Empty);
        }
    }
}
