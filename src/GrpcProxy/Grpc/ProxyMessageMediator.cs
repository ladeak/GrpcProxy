using System.Net;
using System.Threading.Channels;
using Grpc.Core;

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
            data, methodType.ToString());
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
            response.StatusCode);
        return _channel.Writer.WriteAsync(message);
    }
}

public record ProxyMessage(Guid ProxyCallId, MessageDirection Direction, DateTime Timestamp, string Endpoint, List<string> Headers, string Path, string Message, string MethodType, HttpStatusCode? StatusCode = null)
{
    public bool Contains(string text)
    {
        if (string.IsNullOrEmpty(text))
            return true;

        return Endpoint == text
            || Path == text
            || MethodType == text
            || (text == "Request" && Direction == MessageDirection.Request)
            || (text == "Response" && Direction == MessageDirection.Response)
            || Message.Contains(text);
    }
}

public enum MessageDirection
{
    Request = 1,
    Response = 2,
}
