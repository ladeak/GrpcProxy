using GrpcProxy.Data;
using GrpcProxy.Visualizer;

namespace GrpcProxy.Tests.Visualizer
{
    public class LimitedMessageRepositoryTests
    {
        [Fact]
        public void Creating_LimitedMessageRepository_DoesNotThrow()
        {
            var sut = new LimitedMessageRepository();
            Assert.Empty(sut.Messages);
        }

        [Fact]
        public async Task AddingProxyMessage_Messages_ReturnIt()
        {
            var sut = new LimitedMessageRepository();
            ProxyMessage message = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(message);
            Assert.Contains(message, sut.Messages);
        }

        [Fact]
        public async Task AddingIndependentProxyMessages_Messages_ReturnBoth()
        {
            var sut = new LimitedMessageRepository();
            ProxyMessage message0 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            ProxyMessage message1 = GetMessage("69121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(message0);
            await sut.AddAsync(message1);
            Assert.Equal(2, sut.Messages.Count);
            Assert.Contains(message0, sut.Messages);
            Assert.Contains(message1, sut.Messages);
        }

        [Fact]
        public async Task AddingMaxMessages_Removes_First()
        {
            var sut = new LimitedMessageRepository();
            ProxyMessage first = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(first);
            Assert.Contains(first, sut.Messages);
            for (int i = 0; i < GroupingMessageRepository.MaxSize; i++)
            {
                ProxyMessage message = GetMessage(Guid.NewGuid());
                await sut.AddAsync(message);
            }

            Assert.DoesNotContain(first, sut.Messages);
        }

        [Fact]
        public async Task Disable_Disables_MessagesProcessing()
        {
            var sut = new LimitedMessageRepository();
            ProxyMessage message0 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            ProxyMessage message1 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            await sut.AddAsync(message0);
            sut.Disable();
            await sut.AddAsync(message1);
            Assert.Equal(1, sut.Messages.Count);
            Assert.Contains(message0, sut.Messages);
            Assert.DoesNotContain(message1, sut.Messages);
        }

        [Fact]
        public async Task Enable_Enables_MessagesProcessing()
        {
            var sut = new LimitedMessageRepository();
            ProxyMessage message0 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            ProxyMessage message1 = GetMessage("59121320-561F-4042-BF78-F31F456F81E3");
            sut.Disable();
            await sut.AddAsync(message0);
            sut.Enable();
            await sut.AddAsync(message1);
            Assert.Equal(1, sut.Messages.Count);
            Assert.DoesNotContain(message0, sut.Messages);
            Assert.Contains(message1, sut.Messages);
        }

        [Fact]
        public void WhenDisabled_EnabledReturns_False()
        {
            var sut = new LimitedMessageRepository();
            sut.Disable();
            Assert.False(sut.IsEnabled);
        }

        [Fact]
        public void WhenEnabled_EnabledReturns_True()
        {
            var sut = new LimitedMessageRepository();
            sut.Disable();
            sut.Enable();
            Assert.True(sut.IsEnabled);
        }

        private ProxyMessage GetMessage(string id) => GetMessage(Guid.Parse(id));

        private ProxyMessage GetMessage(Guid id) => new ProxyMessage(id, MessageDirection.Request, new DateTime(2022, 07, 27), string.Empty, new List<string>(), string.Empty, string.Empty, string.Empty, false);
    }
}
