using System.Threading.Channels;
using Grpc.Core;
using GrpcProxy.Data;

namespace GrpcProxy.Grpc;

public class ProxyMessageMediator : IProxyMessageMediator
{
    private Channel<ProxyMessage> _channel = Channel.CreateBounded<ProxyMessage>(new BoundedChannelOptions(100) { AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });

    public ChannelReader<ProxyMessage> ChannelReader => _channel.Reader;

    public ValueTask AddRequestAsync(HttpContext context, Guid proxyCallId, MethodType methodType, string data)
    {
        var message = new ProxyMessage(
            proxyCallId,
            MessageDirection.Request,
            DateTime.UtcNow, $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}",
            context.Request.Headers.Select(x => $"{x.Key}: {x.Value}").ToList(),
            context.Request.Path,
            data,
            methodType.ToString(),
            IsCancelled: false);
        return _channel.Writer.WriteAsync(message);
    }

    public ValueTask AddResponseAsync(HttpResponseMessage response, string serviceAddress, Guid proxyCallId, string path, MethodType methodType, string data)
    {
        var message = new ProxyMessage(
            proxyCallId,
            MessageDirection.Response,
            DateTime.UtcNow, serviceAddress,
            response.Headers.Select(x => $"{x.Key}: {string.Join(',', x.Value)}").ToList(),
            path,
            data,
            methodType.ToString(),
            IsCancelled: false,
            response.StatusCode);
        return _channel.Writer.WriteAsync(message);
    }

    public ValueTask AddCancellationAsync(HttpContext context, Guid proxyCallId, MethodType methodType)
    {
        var message = new ProxyMessage(
            proxyCallId,
            MessageDirection.Request,
            DateTime.UtcNow, $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}",
            new List<string>(),
            context.Request.Path,
            string.Empty,
            methodType.ToString(),
            IsCancelled: true);
        return _channel.Writer.WriteAsync(message);
    }

    public ValueTask AddErrorAsync(HttpContext context, Guid proxyCallId, Exception exception, MethodType methodType)
    {
        var message = new ProxyMessage(
            proxyCallId,
            MessageDirection.None,
            DateTime.UtcNow,
            $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}",
            new List<string>(),
            context.Request.Path,
            string.Empty,
            methodType.ToString(),
            false,
            StatusCode: null,
            ProxyError: exception);
        return _channel.Writer.WriteAsync(message);
    }
}
