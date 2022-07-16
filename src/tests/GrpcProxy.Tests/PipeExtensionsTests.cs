using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using GrpcProxy.Grpc;

namespace GrpcProxy.Tests;

public class PipeExtensionsTests
{
    private static readonly Marshaller<TestData> TestDataMarshaller = new Marshaller<TestData>(
           (TestData data, SerializationContext c) =>
           {
               throw new NotImplementedException();
           },
           (DeserializationContext c) =>
           {
               var sequence = c.PayloadAsReadOnlySequence();
               if (sequence.IsSingleSegment)
               {
                   return new TestData(sequence.First);
               }
               return new TestData(sequence.ToArray());
           });

    [Fact]
    public async Task ReadSingleMessageAsync_EmptyMessage_ReturnNoData()
    {
        // Arrange
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x00 // length = 0
});

        var pipeReader = PipeReader.Create(ms);

        // Act
        var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request);

        // Assert
        Assert.Equal(0, messageData.Span.Length);
    }

    [Fact]
    public async Task ReadSingleMessageAsync_OneByteMessage_ReturnData()
    {
        // Arrange
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x01, // length = 1
                0x10
            });

        var pipeReader = PipeReader.Create(ms);

        // Act
        var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request);

        // Assert
        Assert.Equal(1, messageData.Span.Length);
        Assert.Equal(0x10, messageData.Span[0]);
    }

    [Fact]
    public async Task ReadSingleMessageAsync_UnderReceiveSize_ReturnData()
    {
        // Arrange
        var context = HttpContextServerCallContextHelper.CreateServerCallContext(maxSendMessageSize: 1);
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x01, // length = 1
                0x10
            });
        var pipeReader = PipeReader.Create(ms);

        // Act
        var messageData = await pipeReader.ReadSingleMessageAsync(context, TestDataMarshaller.ContextualDeserializer, MessageDirection.Request);

        // Assert
        Assert.Equal(1, messageData.Span.Length);
        Assert.Equal(0x10, messageData.Span[0]);
    }

    [Fact]
    public async Task ReadSingleMessageAsync_ExceedReceiveSize_ReturnData()
    {
        // Arrange
        var context = HttpContextServerCallContextHelper.CreateServerCallContext(maxReceiveMessageSize: 1);
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x02, // length = 1
                0x10,
                0x10
            });

        var pipeReader = PipeReader.Create(ms);

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeReader.ReadSingleMessageAsync(context, TestDataMarshaller.ContextualDeserializer, MessageDirection.Request)).WaitAsync(TimeSpan.FromSeconds(30));

        // Assert
        Assert.Equal("Received message exceeds the maximum configured message size.", ex.Message);
    }

    [Fact]
    public async Task ReadSingleMessageAsync_LongMessage_ReturnData()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Nullam varius nibh a blandit mollis. "
            + "In hac habitasse platea dictumst. Proin non quam nec neque convallis commodo. Orci varius natoque penatibus et magnis dis "
            + "parturient montes, nascetur ridiculus mus. Mauris commodo est vehicula, semper arcu eu, ornare urna. Mauris malesuada nisl "
            + "nisl, vitae tincidunt purus vestibulum sit amet. Interdum et malesuada fames ac ante ipsum primis in faucibus.");

        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x01,
                0xC1 // length = 449
            }.Concat(content).ToArray());

        var pipeReader = PipeReader.Create(ms);

        // Act
        var messageData = await pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request);

        // Assert
        Assert.Equal(449, messageData.Span.Length);
        Assert.Equal(content, messageData.Span.ToArray());
    }

    [Fact]
    public async Task ReadSingleMessageAsync_HeaderIncomplete_ThrowError()
    {
        // Arrange
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00
            });

        var pipeReader = PipeReader.Create(ms);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request)).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ReadSingleMessageAsync_MessageDataIncomplete_ThrowError()
    {
        // Arrange
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x02, // length = 2
                0x10
            });

        var pipeReader = PipeReader.Create(ms);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request)).WaitAsync(TimeSpan.FromSeconds(30));

    }

    [Fact]
    public async Task ReadSingleMessageAsync_AdditionalData_ThrowError()
    {
        // Arrange
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x01, // length = 1
                0x10,
                0x10 // additional data
            });

        var pipeReader = PipeReader.Create(ms);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request)).WaitAsync(TimeSpan.FromSeconds(30));

    }

    [Fact]
    public async Task ReadSingleMessageAsync_AdditionalDataInSeparatePipeRead_ThrowError()
    {
        // Arrange
        var pipe = new Pipe();

        var pipeReader = pipe.Reader;

        // Act
        var readTask = pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request);

        // Assert
        Assert.False(readTask.IsCompleted, "Still waiting for data");

        await pipe.Writer.WriteAsync(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00,
                0x00,
                0x01, // length = 1
                0x10
            });

        //Assert.False(readTask.IsCompleted, "Still waiting for data");

        //await pipe.Writer.WriteAsync(new byte[] { 0x00 });
        //await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => readTask).WaitAsync(TimeSpan.FromSeconds(30));
    }

    //[Fact]
    //public async Task ReadSingleMessageAsync_MessageInMultiplePipeReads_ReadMessageData()
    //{
    //    // Arrange
    //    var messageData = new byte[]
    //        {
    //            0x00, // compression = 0
    //            0x00,
    //            0x00,
    //            0x00,
    //            0x01, // length = 1
    //            0x10
    //        };

    //    // Run continuations without async so ReadSingleMessageAsync immediately consumes added data
    //    var requestStream = new SyncPointMemoryStream(runContinuationsAsynchronously: false);

    //    var pipeReader = PipeReader.Create(requestStream);

    //    // Act
    //    var readTask = pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request);

    //    // Assert
    //    for (var i = 0; i < messageData.Length; i++)
    //    {
    //        var b = messageData[i];
    //        var isLast = i == messageData.Length - 1;

    //        Assert.False(readTask.IsCompleted, "Still waiting for data");

    //        await requestStream.AddDataAndWait(new[] { b }).WaitAsync(TimeSpan.FromSeconds(30));
    //    }

    //    await requestStream.AddDataAndWait(Array.Empty<byte>()).WaitAsync(TimeSpan.FromSeconds(30));

    //    var readMessageData = await readTask.WaitAsync(TimeSpan.FromSeconds(30));

    //    // Assert
    //    Assert.Equal(new byte[] { 0x10 }, readMessageData.Span.ToArray());
    //}

    [Fact]
    public async Task ReadMessageStreamAsync_HeaderIncomplete_ThrowError()
    {
        // Arrange
        var ms = new MemoryStream(new byte[]
            {
                0x00, // compression = 0
                0x00,
                0x00
            });

        var pipeReader = PipeReader.Create(ms);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeReader.ReadSingleMessageAsync(HttpContextServerCallContextHelper.CreateServerCallContext(), TestDataMarshaller.ContextualDeserializer, MessageDirection.Request)).WaitAsync(TimeSpan.FromSeconds(30));

    }


}