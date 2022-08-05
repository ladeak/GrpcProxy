using System.Threading.Channels;
using GrpcProxy.Grpc;
using GrpcProxy.Visualizer;
using NSubstitute;

namespace GrpcProxy.Tests.Visualizer
{
    public class MessageMultiplexerTests
    {
        [Fact]
        public void NoDependencies_Construction_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MessageMultiplexer(null!, null!));
        }

        [Fact]
        public void WithDependencies_Construction_DoesNotThrow()
        {
            _ = new MessageMultiplexer(Substitute.For<IProxyMessageMediator>(), Enumerable.Empty<IMessageRepositoryIngress>());
        }

        [Fact]
        public async Task NewMessage_Dispatched_ToRepository()
        {
            var repo = new FakeRepo();
            var mediator = Substitute.For<IProxyMessageMediator>();
            var channel = Channel.CreateUnbounded<ProxyMessage>();
            mediator.ChannelReader.Returns(channel.Reader);
            var sut = new MessageMultiplexer(mediator, new[] { repo });
            await sut.StartAsync(CancellationToken.None);
            var message = GetMessage("69121320-561F-4042-BF78-F31F456F81E3");
            await channel.Writer.WriteAsync(message);
            await sut.StopAsync(CancellationToken.None);
            Assert.Contains(message, repo.Messages);
        }

        [Fact]
        public async Task MultipleRepos_MessageDispatched_ToAllRepositories()
        {
            var repo0 = new FakeRepo();
            var repo1 = new FakeRepo();
            var mediator = Substitute.For<IProxyMessageMediator>();
            var channel = Channel.CreateUnbounded<ProxyMessage>();
            mediator.ChannelReader.Returns(channel.Reader);
            var sut = new MessageMultiplexer(mediator, new[] { repo0, repo1 });
            await sut.StartAsync(CancellationToken.None);
            var message = GetMessage("69121320-561F-4042-BF78-F31F456F81E3");
            await channel.Writer.WriteAsync(message);
            await sut.StopAsync(CancellationToken.None);
            Assert.Contains(message, repo0.Messages);
            Assert.Contains(message, repo1.Messages);
        }

        [Fact]
        public async Task MultipleMessages_AllDispatched_ToRepository()
        {
            var repo = new FakeRepo();
            var mediator = Substitute.For<IProxyMessageMediator>();
            var channel = Channel.CreateUnbounded<ProxyMessage>();
            mediator.ChannelReader.Returns(channel.Reader);
            var sut = new MessageMultiplexer(mediator, new[] { repo });
            await sut.StartAsync(CancellationToken.None);
            var message0 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            var message1 = GetMessage("69121320-561F-4042-BF78-F31F456F81E3");
            await channel.Writer.WriteAsync(message0);
            await channel.Writer.WriteAsync(message1);
            await sut.StopAsync(CancellationToken.None);
            Assert.Contains(message0, repo.Messages);
            Assert.Contains(message1, repo.Messages);
        }

        [Fact]
        public async Task ChannelCompletes_RemainingMessages_Dispatched()
        {
            var repo = new FakeRepo();
            var mediator = Substitute.For<IProxyMessageMediator>();
            var channel = Channel.CreateUnbounded<ProxyMessage>();
            mediator.ChannelReader.Returns(channel.Reader);
            var sut = new MessageMultiplexer(mediator, new[] { repo });
            await sut.StartAsync(CancellationToken.None);
            var message = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await channel.Writer.WriteAsync(message);
            channel.Writer.Complete();
            await sut.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(message, repo.Messages);
        }

        private ProxyMessage GetMessage(string id) => GetMessage(Guid.Parse(id));

        private ProxyMessage GetMessage(Guid id) => new ProxyMessage(id, MessageDirection.Request, new DateTime(2022, 07, 27), string.Empty, new List<string>(), string.Empty, string.Empty, string.Empty, false);

        private class FakeRepo : IMessageRepositoryIngress
        {
            private List<ProxyMessage> _messages = new();

            public IEnumerable<ProxyMessage> Messages => _messages;

            public Task AddAsync(ProxyMessage item)
            {
                _messages.Add(item);
                return Task.CompletedTask;
            }
        }
    }
}
