using System.Diagnostics;
using Grpc.Core;
using Super;

namespace Service
{
    public class SuperServiceImpl : SuperService.SuperServiceBase
    {
        private static readonly DiagnosticListener _diagnostics = new DiagnosticListener(nameof(Service));

        public override async Task<ResponseData> DoWork(RequestData request, ServerCallContext context)
        {
            await Task.Delay(10);
            Console.WriteLine("Processing");
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
            //await Task.WhenAll(SendingStreamAsync(responseStream), ReceivingStreamAsync(requestStream))
            int i = 0;
            await foreach (var item in requestStream.ReadAllAsync())
                if (!string.IsNullOrWhiteSpace(item.Message))
                {
                    Console.WriteLine($"Processing Stream {i++}");
                    await responseStream.WriteAsync(new ResponseData { Message = $"Response part {i}" });
                }
        }

        private static async Task<int> ReceivingStreamAsync(IAsyncStreamReader<RequestData> requestStream)
        {
            int i = 0;
            await foreach (var item in requestStream.ReadAllAsync())
                if (!string.IsNullOrWhiteSpace(item.Message))
                    Console.WriteLine($"Processing Stream {i++}");
            return i;
        }

        private static async Task SendingStreamAsync(IServerStreamWriter<ResponseData> responseStream)
        {
            Console.WriteLine("Streaming Responses");
            for (int i = 0; i < 3; i++)
                await responseStream.WriteAsync(new ResponseData { Message = $"Response part {i}" });
        }
    }
}
