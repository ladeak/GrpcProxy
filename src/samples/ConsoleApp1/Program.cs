using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Super;

namespace NameClient;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Press enter to send message");
        Console.ReadLine();
        SuperService.SuperServiceClient client;
        GrpcChannel channel;
        channel = GrpcChannel.ForAddress("https://localhost:7012");
        client = new SuperService.SuperServiceClient(channel);
        char key = (char)0;
        while (key != 'e')
        {
            try
            {
                RequestData request;
                request = new RequestData() { Message = "Pocak" };
                var response = await client.DoWorkAsync(request);
                Console.WriteLine(response.Message);

                //using var streamOfWork = client.StreamWork();
                //for (int i = 0; i < 5; i++)
                //{
                //    request = new RequestData() { Message = "Pocak" };
                //    await streamOfWork.RequestStream.WriteAsync(request);
                //}
                //await streamOfWork.RequestStream.CompleteAsync();
                //Console.WriteLine((await streamOfWork).Message);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                key = Console.ReadKey().KeyChar;
            }
        }
        channel.Dispose();
    }
}

