using System.Diagnostics;
using Grpc.Core;
using Super;

namespace Service
{
    public class SuperServiceImpl : SuperService.SuperServiceBase
    {
        private static readonly DiagnosticListener _diagnostics = new DiagnosticListener(nameof(Service));
        private const int WorkDuration = 100;

        public override async Task<ResponseData> DoWork(RequestData request, ServerCallContext context)
        {
            Console.WriteLine("Processing");
            await Task.Delay(WorkDuration);
            return new ResponseData { Message = $"Hello {request.Message}" };
        }

        public override async Task<ResponseData> StreamWork(IAsyncStreamReader<RequestData> requestStream, ServerCallContext context)
        {
            int i = await ReceivingStreamAsync(requestStream);
            return new ResponseData { Message = $"Received {i}" };
        }

        public override async Task StreamResult(RequestData request, IServerStreamWriter<ResponseData> responseStream, ServerCallContext context)
        {
            await SendingStreamAsync(responseStream);
        }

        public override async Task DuplexStreaming(IAsyncStreamReader<RequestData> requestStream, IServerStreamWriter<ResponseData> responseStream, ServerCallContext context)
        {
            await Task.WhenAll(SendingStreamAsync(responseStream), ReceivingStreamAsync(requestStream));
        }

        public override async Task DuplexSyncStreaming(IAsyncStreamReader<RequestData> requestStream, IServerStreamWriter<ResponseData> responseStream, ServerCallContext context)
        {
            try
            {
                int i = 0;
                await foreach (var item in requestStream.ReadAllAsync(context.CancellationToken))
                    if (!string.IsNullOrWhiteSpace(item.Message))
                    {
                        Console.WriteLine($"Processing Stream {i++}");
                        await responseStream.WriteAsync(new ResponseData { Message = $"Response part {i}" });
                        await Task.Delay(WorkDuration);
                    }
            }
            catch (OperationCanceledException)
            {
                // This is normal operation
            }
        }

        private static async Task<int> ReceivingStreamAsync(IAsyncStreamReader<RequestData> requestStream)
        {
            int i = 0;
            await foreach (var item in requestStream.ReadAllAsync())
            {
                await Task.Delay(WorkDuration);
                if (!string.IsNullOrWhiteSpace(item.Message))
                    Console.WriteLine($"Processing Stream {i++}");
            }
            return i;
        }

        private static async Task SendingStreamAsync(IServerStreamWriter<ResponseData> responseStream)
        {
            Console.WriteLine("Streaming Responses");
            for (int i = 0; i < 3; i++)
            {
                await responseStream.WriteAsync(new ResponseData { Message = $"Response part {i}" });
                await Task.Delay(WorkDuration);
            }
        }
    }
}
