using Grpc.Core;
using Grpc.Net.Client;
using Super;

namespace NameClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Select and press Enter to send message: u - Unary, c - Client Streaming, s - Server Streaming, d - Duplex Streaming, f - Duplex Synced Streaming");
        var key = Console.ReadLine()?.FirstOrDefault() ?? 'n';
        GrpcChannel channel = GrpcChannel.ForAddress("https://localhost:7012");
        SuperService.SuperServiceClient client = new SuperService.SuperServiceClient(channel);
        while (key != 'e')
        {
            try
            {
                switch (key)
                {
                    case 'u':
                        await UnaryMessageAsync(client);
                        break;
                    case 'c':
                        await ClientStreamingAsync(client);
                        break;
                    case 's':
                        await ServerStreamingAsync(client);
                        break;
                    case 'd':
                        await DuplexStreamingAsync(client);
                        break;
                    case 'f':
                        await DuplexSyncStreamingAsync(client);
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                key = Console.ReadLine()?.FirstOrDefault() ?? 'n';
            }
        }
        channel.Dispose();
    }

    private static async Task DuplexSyncStreamingAsync(SuperService.SuperServiceClient client)
    {
        bool hasNext = true;
        using var streamOfWork = client.DuplexSyncStreaming();
        for (int i = 0; i < 3; i++)
        {
            var request = new RequestData() { Message = "Pocak" };
            await streamOfWork.RequestStream.WriteAsync(request);
            if (hasNext)
            {
                hasNext = await streamOfWork.ResponseStream.MoveNext();
                Console.WriteLine(streamOfWork.ResponseStream.Current.Message);
            }
        }
        await streamOfWork.RequestStream.CompleteAsync();
    }

    private const int Timeout = 30000;

    private static async Task DuplexStreamingAsync(SuperService.SuperServiceClient client)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));
        using var streamOfWork = client.DuplexStreaming(cancellationToken: cts.Token);
        await Task.WhenAll(SendingAsync(streamOfWork.RequestStream), ReceivingAsync(streamOfWork.ResponseStream));
    }

    private static async Task ServerStreamingAsync(SuperService.SuperServiceClient client)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));
        var streamOfWork = client.StreamResult(new RequestData() { Message = "Requesting Streamed Data" }, cancellationToken: cts.Token);
        await ReceivingAsync(streamOfWork.ResponseStream);
    }

    private static async Task ClientStreamingAsync(SuperService.SuperServiceClient client)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));
        using var streamOfWork = client.StreamWork(cancellationToken: cts.Token);
        await SendingAsync(streamOfWork.RequestStream);
        Console.WriteLine((await streamOfWork).Message);
    }

    private static async Task UnaryMessageAsync(SuperService.SuperServiceClient client)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));
        var request = new RequestData() { Message = "Pocak" };
        var response = await client.DoWorkAsync(request, new CallOptions(cancellationToken: cts.Token));
        Console.WriteLine(response.Message);
    }

    private static async Task SendingAsync(IClientStreamWriter<RequestData> requestStream)
    {
        for (int i = 0; i < 3; i++)
        {
            var request = new RequestData() { Message = "Pocak" };
            await requestStream.WriteAsync(request);
            await Task.Delay(20);
        }
        await requestStream.CompleteAsync();
    }

    private static async Task ReceivingAsync(IAsyncStreamReader<ResponseData> responseStream)
    {
        await foreach (var response in responseStream.ReadAllAsync())
            Console.WriteLine(response.Message);
    }
}

