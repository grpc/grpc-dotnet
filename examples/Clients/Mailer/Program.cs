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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Mail;
using Common;
using Grpc.Core;

namespace Sample.Clients
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var mailboxName = GetMailboxName(args);

            // Server will only support Https on Windows and Linux
            var credentials = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ChannelCredentials.Insecure : ClientResources.SslCredentials;

            var channel = new Channel("localhost:50051", credentials);

            Console.WriteLine($"Connecting to mailbox '{mailboxName}'");
            Console.WriteLine();

            await channel.ConnectAsync();

            Console.WriteLine("Connected");
            Console.WriteLine("Press escape to disconnect. Press any other key to forward mail.");

            var client = new Mailer.MailerClient(channel);
            using (var mailbox = client.Mailbox(headers: new Metadata { new Metadata.Entry("mailbox-name", mailboxName) }))
            {
                _ = Task.Run(async () =>
                {
                    while (await mailbox.ResponseStream.MoveNext(CancellationToken.None))
                    {
                        var response = mailbox.ResponseStream.Current;

                        Console.ForegroundColor = response.Reason == MailboxMessage.Types.Reason.Received ? ConsoleColor.White : ConsoleColor.Green;
                        Console.WriteLine();
                        Console.WriteLine(response.Reason == MailboxMessage.Types.Reason.Received ? "Mail received" : "Mail forwarded");
                        Console.WriteLine($"New mail: {response.New}, Forwarded mail: {response.Forwarded}");
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

                    await mailbox.RequestStream.WriteAsync(new ForwardMailMessage());
                }

                await mailbox.RequestStream.CompleteAsync();
            }

            Console.WriteLine("Disconnecting");
            await channel.ShutdownAsync();

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
