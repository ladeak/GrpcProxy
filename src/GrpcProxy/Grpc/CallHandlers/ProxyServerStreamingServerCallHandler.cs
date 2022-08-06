using Grpc.Core;
using Grpc.Shared.Server;
using GrpcProxy.AspNetCore;
using GrpcProxy.Data;
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
            var sending = await ForwardRequestAsync(httpContext, serverCallContext);
            await ForwardResponseAsync(sending, httpContext, serverCallContext);
        }

        private async Task<ForwardingContext> ForwardRequestAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            try
            {
                var sendingTask = _httpForwarder.SendRequestAsync(httpContext, _serviceAddress, _httpClientFactory.CreateClient(), HttpTransformer.Empty, serverCallContext);
                var deserializationTask = DeserializeRequestAsync(httpContext, serverCallContext);
                await Task.WhenAll(sendingTask, deserializationTask);
                return sendingTask.Result;
            }
            catch (OperationCanceledException)
            {
                await _messageMediator.AddCancellationAsync(httpContext, serverCallContext.ProxyCallId, _method.Type);
                throw;
            }
        }

        private async Task ForwardResponseAsync(ForwardingContext sending, HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            try
            {
                var responseTask = _httpForwarder.ReturnResponseAsync(httpContext, sending.StreamCopyContent, HttpTransformer.Empty, serverCallContext);
                var deserializationTask = DeserializingResponseAsync(sending, httpContext, serverCallContext);
                await Task.WhenAll(responseTask, deserializationTask);
            }
            catch (OperationCanceledException)
            {
                await _messageMediator.AddCancellationAsync(httpContext, serverCallContext.ProxyCallId, _method.Type);
                throw;
            }
        }

        private async Task DeserializeRequestAsync(HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            var requestData = await serverCallContext.RequestPipe.Reader.ReadSingleMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Request);
            await _messageMediator.AddRequestAsync(httpContext, serverCallContext.ProxyCallId, _method.Type, requestData?.ToString() ?? string.Empty);
        }

        private async Task DeserializingResponseAsync(ForwardingContext sending, HttpContext httpContext, ProxyHttpContextServerCallContext serverCallContext)
        {
            while (!serverCallContext.CancellationToken.IsCancellationRequested)
            {
                var message = await serverCallContext.ResponsePipe.Reader.ReadStreamMessageAsync(serverCallContext, _method.RequestMarshaller.ContextualDeserializer, MessageDirection.Response, serverCallContext.CancellationToken);
                if (message == null)
                    break;
                await _messageMediator.AddResponseAsync(sending.ResponseMessage, _serviceAddress, serverCallContext.ProxyCallId, httpContext.Request.Path, _method.Type, message?.ToString() ?? string.Empty);
            }
        }
    }
}
