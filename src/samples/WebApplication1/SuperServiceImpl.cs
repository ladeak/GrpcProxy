using System.Diagnostics;
using Grpc.Core;
using Super;

namespace Service
{
    public class SuperServiceImpl : SuperService.SuperServiceBase
    {
        public SuperServiceImpl()
        {

        }

        private static readonly DiagnosticListener _diagnostics = new DiagnosticListener(nameof(Service));

        public override async Task<ResponseData> DoWork(RequestData request, ServerCallContext context)
        {
            await Task.Delay(10);
            Console.WriteLine("Processing");
            return new ResponseData { Message = $"Hello {request.Message}" };
        }

        public override async Task<ResponseData> StreamWork(IAsyncStreamReader<RequestData> requestStream, ServerCallContext context)
        {
            int i = 0;
            await foreach (var item in requestStream.ReadAllAsync())
                if (!string.IsNullOrWhiteSpace(item.Message))
                    Console.WriteLine($"Processing Stream {i++}");
            return new ResponseData { Message = $"Received {i}" };
        }
    }
}
