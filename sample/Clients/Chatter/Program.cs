using System;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Grpc.Core;
using Chat;

namespace Sample.Clients
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var name = args[0];
            var channel = new Channel("localhost:50051", ClientResources.SslCredentials);
            var client = new Chatter.ChatterClient(channel);


            using (var chat = client.Chat())
            {
                Console.WriteLine($"Connected as {name}. Send empty message to quit.");

                // Dispatch, this could be racy
                var responseTask = Task.Run(async () =>
                {
                    while (await chat.ResponseStream.MoveNext(CancellationToken.None))
                    {
                        Console.WriteLine($"{chat.ResponseStream.Current.Name}: {chat.ResponseStream.Current.Message}");
                    }
                });

                var line = Console.ReadLine();
                while (!string.IsNullOrEmpty(line))
                {
                    await chat.RequestStream.WriteAsync(new ChatMessage { Name = name, Message = line });
                    line = Console.ReadLine();
                }
                await chat.RequestStream.CompleteAsync();
            }

            Console.WriteLine("Shutting down");
            channel.ShutdownAsync().Wait();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
