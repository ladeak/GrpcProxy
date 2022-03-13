using Grpc.Net.Client;
using Super;

namespace NameClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Select and press Enter to send message: u - Unary, c - Client Streaming");
        var key = Console.ReadLine()?.FirstOrDefault() ?? 'n';
        SuperService.SuperServiceClient client;
        GrpcChannel channel;
        channel = GrpcChannel.ForAddress("https://localhost:7012");
        client = new SuperService.SuperServiceClient(channel);
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

    private static async Task UnaryMessageAsync(SuperService.SuperServiceClient client)
    {
        var request = new RequestData() { Message = "Pocak" };
        var response = await client.DoWorkAsync(request);
        Console.WriteLine(response.Message);
    }

    private static async Task ClientStreamingAsync(SuperService.SuperServiceClient client)
    {
        using var streamOfWork = client.StreamWork();
        for (int i = 0; i < 5; i++)
        {
            var request = new RequestData() { Message = "Pocak" };
            await streamOfWork.RequestStream.WriteAsync(request);
        }
        await streamOfWork.RequestStream.CompleteAsync();
        Console.WriteLine((await streamOfWork).Message);
    }
}

