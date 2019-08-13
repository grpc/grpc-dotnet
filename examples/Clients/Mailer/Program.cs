#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Mail;

namespace Sample.Clients
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var mailboxName = GetMailboxName(args);

            Console.WriteLine($"Creating client to mailbox '{mailboxName}'");
            Console.WriteLine();

            var channel = GrpcChannel.ForAddress("https://localhost:50051");
            var client = new Mailer.MailerClient(channel);

            Console.WriteLine("Client created");
            Console.WriteLine("Press escape to disconnect. Press any other key to forward mail.");

            using (var call = client.Mailbox(headers: new Metadata { new Metadata.Entry("mailbox-name", mailboxName) }))
            {
                var responseTask = Task.Run(async () =>
                {
                    await foreach (var message in call.ResponseStream.ReadAllAsync())
                    {
                        Console.ForegroundColor = message.Reason == MailboxMessage.Types.Reason.Received ? ConsoleColor.White : ConsoleColor.Green;
                        Console.WriteLine();
                        Console.WriteLine(message.Reason == MailboxMessage.Types.Reason.Received ? "Mail received" : "Mail forwarded");
                        Console.WriteLine($"New mail: {message.New}, Forwarded mail: {message.Forwarded}");
                        Console.ResetColor();
                    }
                });

                while (true)
                {
                    var result = Console.ReadKey(intercept: true);
                    if (result.Key == ConsoleKey.Escape)
                    {
                        break;
                    }

                    await call.RequestStream.WriteAsync(new ForwardMailMessage());
                }

                Console.WriteLine("Disconnecting");
                await call.RequestStream.CompleteAsync();
                await responseTask;
            }

            Console.WriteLine("Disconnected. Press any key to exit.");
            Console.ReadKey();
        }

        private static string GetMailboxName(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("No mailbox name provided. Using default name. Usage: dotnet run <name>.");
                return "DefaultMailbox";
            }

            return args[0];
        }
    }
}
