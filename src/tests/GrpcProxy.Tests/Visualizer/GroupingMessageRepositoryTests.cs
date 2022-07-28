using GrpcProxy.Grpc;
using GrpcProxy.Visualizer;

namespace GrpcProxy.Tests.Visualizer
{
    public class GroupingMessageRepositoryTests
    {
        [Fact]
        public void Creating_GroupingMessageRepository_DoesNotThrow()
        {
            var sut = new GroupingMessageRepository();
            Assert.Empty(sut.Messages);
        }

        [Fact]
        public async Task AddingProxyMessage_Messages_ReturnIt()
        {
            var sut = new GroupingMessageRepository();
            ProxyMessage message = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(message);
            Assert.Contains(message, sut.Messages.SelectMany(x => x.Chain));
        }

        [Fact]
        public async Task AddingIndependentProxyMessages_Messages_ReturnBoth()
        {
            var sut = new GroupingMessageRepository();
            ProxyMessage message0 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            ProxyMessage message1 = GetMessage("69121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(message0);
            await sut.AddAsync(message1);
            Assert.Equal(2, sut.Messages.Count);
            Assert.Contains(message1, sut.Messages.First().Chain);
            Assert.Contains(message0, sut.Messages.Last().Chain);
        }

        [Fact]
        public async Task AddingDependentProxyMessages_Messages_ReturnSingle()
        {
            var sut = new GroupingMessageRepository();
            ProxyMessage message0 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            ProxyMessage message1 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(message0);
            await sut.AddAsync(message1);
            Assert.Equal(1, sut.Messages.Count);
            Assert.Contains(message0, sut.Messages.First().Chain);
            Assert.Contains(message1, sut.Messages.First().Chain);
        }

        [Fact]
        public async Task AddingMaxMessages_Removes_First()
        {
            var sut = new GroupingMessageRepository();
            ProxyMessage first = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(first);
            Assert.Contains(sut.Messages, x => x.Id == first.ProxyCallId);
            for (int i = 0; i < GroupingMessageRepository.MaxSize; i++)
            {
                ProxyMessage message = GetMessage(Guid.NewGuid());
                await sut.AddAsync(message);
            }

            Assert.DoesNotContain(sut.Messages, x => x.Id == first.ProxyCallId);
        }

        [Fact]
        public async Task AddingDependentProxyMessages_Messages_MovesLatestToFirst()
        {
            var sut = new GroupingMessageRepository();
            ProxyMessage message0 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            ProxyMessage message1 = GetMessage("69121320-561F-4042-BF78-F31F456F81E3");
            ProxyMessage message2 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(message0);
            await sut.AddAsync(message1);
            await sut.AddAsync(message2);
            Assert.Equal(2, sut.Messages.Count);
            Assert.Contains(message0, sut.Messages.First().Chain);
            Assert.Contains(message1, sut.Messages.Last().Chain);
            Assert.Equal(2, sut.Messages.First().Chain.Length);
        }

        private ProxyMessage GetMessage(string id) => GetMessage(Guid.Parse(id));

        private ProxyMessage GetMessage(Guid id) => new ProxyMessage(id, MessageDirection.Request, new DateTime(2022, 07, 27), string.Empty, new List<string>(), string.Empty, string.Empty, string.Empty, false);
    }
}
