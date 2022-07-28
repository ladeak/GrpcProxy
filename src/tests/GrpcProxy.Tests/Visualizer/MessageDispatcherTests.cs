using System.Threading.Channels;
using GrpcProxy.Grpc;
using GrpcProxy.Visualizer;
using NSubstitute;

namespace GrpcProxy.Tests.Visualizer
{
    public class MessageDispatcherTests
    {
        [Fact]
        public void NoDependencies_Construction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MessageDispatcher(null!, null!));
        }

        [Fact]
        public void WithDependencies_Construction_DoesNotThrow()
        {
            _ = new MessageDispatcher(Substitute.For<IProxyMessageMediator>(), Enumerable.Empty<IMessageRepositoryIngress>());
        }

        [Fact]
        public void NewMessage_Dispatched_ToRepository()
        {
            var repo = Substitute.For<IMessageRepositoryIngress>();
            var mediator = Substitute.For<IProxyMessageMediator>();
            var channel = Channel.CreateUnbounded<ProxyMessage>();
            mediator.ChannelReader.Returns(channel.Reader);
            var sut = new MessageDispatcher(mediator, new[] { repo });
        }
    }
}
