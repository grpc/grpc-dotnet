using System;
using System.Threading.Tasks;
using Common;
using Grpc.Core;
using Count;

namespace Sample.Clients
{
    class Program
    {
        static Random RNG = new Random();

        static async Task Main(string[] args)
        {
            var channel = new Channel("localhost:50051", ClientResources.SslCredentials);
            var client = new Counter.CounterClient(channel);

            var reply = client.IncrementCount(new Google.Protobuf.WellKnownTypes.Empty());
            Console.WriteLine("Count: " + reply.Count);

            using (var call = client.AccumulateCount())
            {
                for (int i = 0; i < 3; i++)
                {
                    var count = RNG.Next(5);
                    Console.WriteLine($"Accumulating with {count}");
                    await call.RequestStream.WriteAsync(new CounterRequest { Count = count });
                    await Task.Delay(1000);
                }

                await call.RequestStream.CompleteAsync();
                Console.WriteLine($"Count: {(await call.ResponseAsync).Count}");
            }

            Console.WriteLine("Shutting down");
            channel.ShutdownAsync().Wait();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
