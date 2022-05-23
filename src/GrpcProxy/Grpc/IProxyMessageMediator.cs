using System.Threading.Channels;
using Grpc.Core;

namespace GrpcProxy.Grpc
{
    public interface IProxyMessageMediator
    {
        ChannelReader<ProxyMessage> ChannelReader { get; }

        ValueTask AddRequestAsync(HttpContext context, Guid proxyCallId, MethodType methodType, string data);

        ValueTask AddResponseAsync(HttpResponseMessage response, string serviceAddress, Guid proxyCallId, string path, MethodType methodType, string data);

        ValueTask AddCancellationAsync(HttpContext context, Guid proxyCallId, MethodType methodType);
    }
}